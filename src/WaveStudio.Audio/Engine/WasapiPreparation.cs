namespace WaveStudio.Audio.Engine;

// HRESULT and UINT64 outputs are retained verbatim. Success/accuracy is not movement.
internal readonly record struct WasapiClockReading(int FrequencyHResult, ulong Frequency,
    int PositionHResult, ulong Position, ulong Qpc100ns)
{
    public NativeClockQuality Quality => FrequencyHResult != 0 || Frequency == 0 ? NativeClockQuality.Unavailable
        : PositionHResult == 0 ? NativeClockQuality.Accurate
        : PositionHResult == 1 ? NativeClockQuality.Uncertain : NativeClockQuality.Unavailable;
    public NativeClockSample ToSample(TransportEndpoint endpoint, TransportPacketFlags flags = 0) =>
        new(endpoint, Position, Frequency, Qpc100ns, NativePositionUnit.DeviceUnits, Quality, flags);
}

internal sealed class WasapiCallException(string stage, int result) : Exception($"{stage}: HRESULT 0x{result:X8}.")
{
    public string Stage { get; } = stage;
    public int Result { get; } = result;
    public static void RequireSuccess(int result, string stage)
    {
        if (result != 0) throw new WasapiCallException(stage, result);
    }
}

// Narrow preparation-only seam: deliberately has no Start, Stop or audio buffer methods.
internal interface IWasapiPreparationDevice : IDisposable
{
    void ValidateIdentity(TransportEndpoint endpoint);
    IWasapiPreparationClient Activate();
}
internal interface IWasapiPreparationClient : IDisposable
{
    WasapiPcm ReadMixFormat();
    void RequireExactSupport();
    (long Default100ns, long MinimumExclusive100ns) ReadPeriods();
    void Initialize();
    (uint Frames, long Latency100ns) ReadBufferInfo();
    IWasapiPreparationClock OpenClock();
}
internal interface IWasapiPreparationClock : IDisposable { WasapiClockReading Read(); }

internal sealed record WasapiPreparationResult(TransportEndpoint Endpoint, WasapiPcm? Format,
    long? DefaultPeriod100ns, long? MinimumExclusivePeriod100ns, uint? BufferFrames, long? StreamLatency100ns,
    bool Initialized, WasapiClockReading? Clock, string? FailedStage, int? HResult,
    string? CleanupError, bool ResourcesReleased, int OwnerThreadId)
{
    public bool Succeeded => FailedStage is null && ResourcesReleased;
    public bool AudioStarted => false; // This operation has no audio-start capability.
}

internal static class WasapiPreparation
{
    // An unconfirmed Release must not unlock another operation or allow a finalizer on another owner.
    // No forced retry on a pool thread; keep failed resources rooted. Restart/review is required.
    private static readonly List<PreparationLifetime> retained = [];

    public static Task<WasapiPreparationResult> PrepareAsync(TransportEndpoint endpoint, WasapiPcm expected,
        TransportActivityGuard guard, CancellationToken token = default,
        Func<TransportEndpoint, IWasapiPreparationDevice>? open = null)
    {
        endpoint.Validate(); expected.Validate(); ArgumentNullException.ThrowIfNull(guard);
        return Task.Run(() => Prepare(endpoint, expected, guard, token, open ?? NativeWasapiPreparationDevice.Open), token);
    }

    private static WasapiPreparationResult Prepare(TransportEndpoint endpoint, WasapiPcm expected,
        TransportActivityGuard guard, CancellationToken token, Func<TransportEndpoint, IWasapiPreparationDevice> open)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA) throw new InvalidOperationException("Preparação exige MTA.");
        token.ThrowIfCancellationRequested();
        using var lifetime = new PreparationLifetime(guard.TryAcquire(AudioActivity.NativePreparation)
            ?? throw new InvalidOperationException("Outra operação de áudio está ativa. Preparação recusada."));
        WasapiPcm? mix = null; long? period = null, minimum = null, latency = null; uint? frames = null;
        WasapiClockReading? reading = null; bool initialized = false; string? failed = null; int? hr = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var device = lifetime.Add(open(endpoint)); token.ThrowIfCancellationRequested();
            device.ValidateIdentity(endpoint); token.ThrowIfCancellationRequested();
            var client = lifetime.Add(device.Activate()); token.ThrowIfCancellationRequested();
            mix = client.ReadMixFormat(); mix.Validate(); token.ThrowIfCancellationRequested();
            if (mix != expected) throw new InvalidOperationException("Formato mudou; preparação recusada sem fallback.");
            client.RequireExactSupport(); token.ThrowIfCancellationRequested();
            (long p, long m) = client.ReadPeriods(); period = p; minimum = m;
            if (p <= 0 || m <= 0) throw new InvalidOperationException("Período consultado inválido.");
            token.ThrowIfCancellationRequested(); device.ValidateIdentity(endpoint); token.ThrowIfCancellationRequested();
            client.Initialize(); initialized = true; token.ThrowIfCancellationRequested();
            (uint f, long l) = client.ReadBufferInfo(); frames = f; latency = l;
            if (f == 0 || l < 0) throw new InvalidOperationException("Buffer/latência consultados inválidos.");
            token.ThrowIfCancellationRequested();
            var clock = lifetime.Add(client.OpenClock()); token.ThrowIfCancellationRequested();
            reading = clock.Read(); token.ThrowIfCancellationRequested();
            if (reading.Value.FrequencyHResult != 0) throw new WasapiCallException("IAudioClock.GetFrequency", reading.Value.FrequencyHResult);
            if (reading.Value.PositionHResult is not (0 or 1)) throw new WasapiCallException("IAudioClock.GetPosition", reading.Value.PositionHResult);
            if (reading.Value.Frequency == 0) throw new InvalidOperationException("Frequência de relógio inválida.");
            device.ValidateIdentity(endpoint); token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { failed = "Canceled"; }
        catch (WasapiCallException error) { failed = error.Stage; hr = error.Result; }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        { failed = error.GetType().Name; if (error is System.Runtime.InteropServices.COMException) hr = error.HResult; }
        finally { lifetime.Dispose(); }
        return new(endpoint, mix, period, minimum, frames, latency, initialized, reading, failed, hr,
            lifetime.CleanupError, lifetime.Released, Environment.CurrentManagedThreadId);
    }

    private sealed class PreparationLifetime(IDisposable lease) : IDisposable
    {
        private readonly List<IDisposable> resources = [];
        private bool attempted;
        public bool Released { get; private set; }
        public string? CleanupError { get; private set; }
        public T Add<T>(T resource) where T : IDisposable { resources.Add(resource); return resource; }
        public void Dispose()
        {
            if (attempted) return;
            attempted = true;
            for (int i = resources.Count - 1; i >= 0; i--)
            {
                try { resources[i].Dispose(); resources.RemoveAt(i); }
                catch (Exception error) { CleanupError ??= error.GetType().Name; }
                // A failed child release retains its parents too. Never release the owning client first.
                if (CleanupError is not null) { lock (retained) retained.Add(this); return; }
            }
            lease.Dispose(); Released = true;
        }
    }
}
