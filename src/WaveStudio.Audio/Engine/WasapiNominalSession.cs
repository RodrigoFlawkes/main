using System.Buffers.Binary;
using System.Text.Json.Serialization;

namespace WaveStudio.Audio.Engine;

internal sealed class WasapiNominalSession : IAsyncDisposable
{
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(5);
    private readonly WasapiSessionCore core;

    private WasapiNominalSession(WasapiSessionCore core) => this.core = core;

    public static WasapiNominalSession CreateSilentProof(TransportRoute route, TransportActivityGuard? guard = null) =>
        new(new(route, guard, nominal: new WasapiNominalControl()));

    public static WasapiNominalSession CreateFake(TransportRoute route, TransportActivityGuard guard,
        Func<TransportEndpoint, IWasapiPreparationDevice> open, WasapiDriftSimulation pacing, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(open);
        if (route.Capture.Evidence != FormatEvidence.SimulatedExact || route.Playback.Evidence != FormatEvidence.SimulatedExact)
            throw new ArgumentException("A escrita de PCM exige uma rota simulada.", nameof(route));
        return new(new(route, guard, open, timeout, simulation: pacing, nominal: new(new(route.Playback.Mix!))));
    }

    public async Task<WasapiNominalReport> RunAsync(TimeSpan duration, CancellationToken token = default) =>
        core.ReadNominalReport(await core.RunAsync(duration, token));
    public bool Running => core.NominalRunning;
    public void RequestStop() => core.RequestStop();
    public void Invalidate(long generation) => core.Invalidate(generation);
    public void Step() => core.StepNominal();
    public void SetFakeGain(float gain) => core.SetFakeGain(gain);
    public ValueTask DisposeAsync() => core.DisposeAsync();
}

internal sealed record WasapiNominalReport(WasapiSilentSnapshot Cleanup, bool NominalObserved, bool PacketRateConfirmed,
    bool RenderSilent, long ClockEvidenceUnavailable, long EndpointPrimed, long FifoPrimed, long PreTicketRender, long WarmupRender,
    int MarginFrames, long CoherentCuts, long BusyCuts, long IndependentCaptureObservations, long IndependentRenderObservations,
    long? MinimumQueued, long? MaximumQueued, double? MinimumMean, double? MaximumMean, long Covered100ns)
{
    public bool DriftPrecisionAvailable => false;
}

internal sealed class WasapiNominalControl(WasapiMonitorEncoder? encoder = null, WasapiAudibleConsent? audible = null,
    bool externalRoute = false)
{
    public WasapiMonitorEncoder? Encoder { get; } = encoder;
    public WasapiAudibleConsent? Audible { get; } = audible;
    private bool armed, bypass = true;
    private readonly object gainControl = new();
    public bool ExternalRoute => externalRoute;
    private int capacity, externalReserve;
    public int MarginFrames { get; private set; }
    private long origin, lastCut, coherent, busy, captureObservations, renderObservations, lastCapture, lastRender, covered;
    private long? minimum, maximum;
    private Int128 minimumArea, maximumArea;
    public WasapiCheckpoint? Baseline { get; private set; }

    public void Configure(int fifoCapacity, int inputRate, int outputRate, uint captureCapacity, uint renderCapacity)
    {
        int converted = checked((int)(((long)captureCapacity * outputRate + inputRate - 1) / inputRate)) + 1;
        MarginFrames = Math.Max(converted, (int)renderCapacity);
        if (MarginFrames >= fifoCapacity / 2) throw new NotSupportedException("FIFO sem margem nominal para um bloco.");
        capacity = fifoCapacity;
        // Cinco segundos a 500 ppm relativos, mais 2 ms de jitter no domínio de saída.
        externalReserve = (int)Math.Ceiling(outputRate * (.002 + .0005 * WasapiExternalRouteSession.MaximumDuration.TotalSeconds));
    }
    public void Start(long now) => origin = lastCut = now;

    public string? Observe(WasapiCheckpointLedger ledger, WasapiCheckpointResult result, long now)
    {
        // Cada confirmação individual é útil mesmo quando a outra operação impede um corte conjunto.
        Count(ledger.Capture, ref lastCapture, ref captureObservations);
        Count(ledger.Render, ref lastRender, ref renderObservations);
        if (result.Checkpoint is not { } cut)
        {
            if (result.Status is WasapiCheckpointStatus.OwnerBusy or WasapiCheckpointStatus.OwnerUnconfirmed) busy++;
            if (result.Status == WasapiCheckpointStatus.HistoryLost) return "Nominal/HistoryLost";
            return now - lastCut >= WasapiCheckpointLedger.MaxAcknowledgmentAge100ns ? "Nominal/CheckpointExpired" : null;
        }
        if (cut.Cut100ns - lastCut >= WasapiCheckpointLedger.MaxAcknowledgmentAge100ns) return "Nominal/CheckpointExpired";
        lastCut = cut.Cut100ns;
        Baseline = cut;
        coherent++;
        if (!armed && Audible is { } consent && !consent.StopToken.IsCancellationRequested && cut.Capture.Evidence.FifoPrimingFrames > 0)
        {
            if (externalRoute && Math.Min(cut.QueuedFrames - MarginFrames, capacity - MarginFrames - cut.QueuedFrames) < externalReserve)
                return "External/FifoReserve";
            lock (gainControl)
            {
                Encoder!.SetGain(externalRoute ? ExternalGain : WasapiAudibleProofSession.ProofGain);
                armed = true;
            }
        }
        minimum = Math.Min(minimum ?? cut.QueuedFrames, cut.QueuedFrames);
        maximum = Math.Max(maximum ?? cut.QueuedFrames, cut.QueuedFrames);
        if (cut.Mean is { } bounds)
        {
            covered += bounds.Duration100ns;
            minimumArea += bounds.MinimumFrameTicks;
            maximumArea += bounds.MaximumFrameTicks;
        }
        return null;
    }

    private float ExternalGain => bypass ? WasapiExternalRouteSession.BypassGain : WasapiExternalRouteSession.EffectGain;

    public void SetExternalBypass(bool value)
    {
        if (!externalRoute) throw new InvalidOperationException("Comparação disponível somente na rota externa.");
        lock (gainControl)
        {
            bypass = value;
            if (armed) Encoder!.SetGain(ExternalGain);
        }
    }

    private static void Count(WasapiCheckpointLedger.Journal journal, ref long previous, ref long count)
    {
        if (!journal.TryRead(out var state) || state.Evidence.Clock.Qpc100ns == 0) return;
        long qpc = (long)state.Evidence.Clock.Qpc100ns;
        if (qpc <= previous) return;
        previous = qpc;
        count++;
    }

    public WasapiNominalReport Report(WasapiSilentSnapshot result, bool packets, long unavailable,
        long fifoPrimed, long preTicket, long warmup, long observationEnd)
    {
        bool nominal = result.Completed && result.Failure is null && result.DurationReached && !result.ResourcesRetained &&
            result.Capture.Started && result.Playback.Started && result.Capture.StopConfirmed && result.Playback.StopConfirmed &&
            result.Capture.Released && result.Playback.Released && !result.Capture.BufferInFlight && !result.Playback.BufferInFlight &&
            result.Capture.MovementSamples >= 2 && result.Playback.MovementSamples >= 2 && packets &&
            coherent >= 2 && lastCut > origin && observationEnd - lastCut < WasapiCheckpointLedger.MaxAcknowledgmentAge100ns &&
            fifoPrimed > 0 && result.Playback.PrimedFrames == result.Playback.CapacityFrames &&
            result.Capture.PacketsAcquired == result.Capture.PacketsReleased &&
            result.Capture.PacketsAccepted + result.Capture.StartupDiscardedPackets == result.Capture.PacketsReleased &&
            result.Capture.FramesAcquired == result.Capture.FramesReleased &&
            result.Capture.FramesAccepted + result.Capture.StartupDiscardedFrames == result.Capture.FramesReleased &&
            result.Playback.PacketsAcquired == result.Playback.PacketsReleased &&
            result.Playback.FramesAcquired == result.Playback.FramesReleased &&
            result.Playback.FramesReleased == result.Playback.PrimedFrames + preTicket + warmup + result.ReadFrames &&
            result.ProducedFrames > 0 && result.ReadFrames > 0 && result.DroppedFrames == 0 && result.UnderrunFrames == 0;
        return new(result, nominal, packets, Encoder is null, unavailable, result.Playback.PrimedFrames, fifoPrimed,
            preTicket, warmup, MarginFrames, coherent, busy, captureObservations, renderObservations,
            minimum, maximum, covered == 0 ? null : (double)minimumArea / covered,
            covered == 0 ? null : (double)maximumArea / covered, covered);
    }
}

internal sealed class WasapiMonitorEncoder(WasapiPcm format, bool compareGains = false)
{
    public const float MaximumGain = 0.1f;
    private readonly PcmEncoding encoding = format.Validate();
    private readonly float slew = MaximumGain / (format.SampleRate * .005f);
    private float gain, target;

    public void SetGain(float value)
    {
        if (!float.IsFinite(value) || value is < 0 or > MaximumGain) throw new ArgumentOutOfRangeException(nameof(value));
        Volatile.Write(ref target, value);
    }

    public bool Encode(ReadOnlySpan<float> samples, Span<byte> destination) => Encode(samples, destination, out _);

    public bool Encode(ReadOnlySpan<float> samples, Span<byte> destination, out WasapiEncodedBlock block)
    {
        block = default;
        if (samples.Length % format.Channels != 0 || destination.Length != samples.Length / format.Channels * format.BlockAlign)
            throw new ArgumentException("O render exige frames completos.");
        float requested = Volatile.Read(ref target);
        if (requested == 0 && gain == 0) return false;
        int stride = encoding == PcmEncoding.Pcm16LittleEndian ? 2 : 4;
        float peak = 0, peakGain = 0, minimumGain = float.PositiveInfinity;
        int nonZero = 0;
        for (int index = 0; index < samples.Length; index += format.Channels)
        {
            gain += Math.Clamp(requested - gain, -slew, slew);
            peakGain = Math.Max(gain, peakGain);
            if (compareGains) minimumGain = Math.Min(gain, minimumGain);
            bool frameSignal = false;
            for (int channel = 0; channel < format.Channels; channel++)
            {
                int offset = index + channel;
                float sample = float.IsFinite(samples[offset]) ? Math.Clamp(samples[offset], -1, 1) * gain : 0;
                if (encoding == PcmEncoding.Pcm16LittleEndian)
                {
                    short quantized = (short)Math.Round(sample * 32768);
                    BinaryPrimitives.WriteInt16LittleEndian(destination[(offset * stride)..], quantized);
                    sample = quantized / 32768f;
                }
                else BinaryPrimitives.WriteSingleLittleEndian(destination[(offset * stride)..], sample);
                frameSignal |= sample != 0;
                peak = Math.Max(peak, Math.Abs(sample));
            }
            if (frameSignal) nonZero++;
        }
        block = new(nonZero, peak, peakGain, compareGains ? minimumGain : 0);
        return true;
    }
}
