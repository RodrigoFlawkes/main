namespace WaveStudio.Audio.Engine;

// One-shot, always-SILENT proof. No UI, unmute, recovery, monitor level or simulated transport interface.
internal sealed class WasapiSilentSession : IAsyncDisposable
{
    private static readonly HashSet<WasapiSilentSession> live = [];
    private readonly TransportRoute route;
    private readonly Func<TransportEndpoint, IWasapiPreparationDevice> open;
    private readonly TransportActivityGuard guard;
    private readonly TimeSpan timeout;
    private readonly object control = new(); // Never acquired by sample processing.
    private readonly SemaphoreSlim finalizing = new(1);
    private readonly ManualResetEventSlim stop = new();
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DeliveryTicket ticket;
    private readonly Owner capture, playback;
    private IDisposable? lease;
    private CancellationTokenRegistration cancellation;
    private WasapiSilentPath? path;
    private Task<WasapiSilentSnapshot>? run;
    private bool completed;
    private string? failure;
    private int? hresult;
    private int faultWritten;
    private long observation100ns;
    private bool durationReached;

    public WasapiSilentSession(TransportRoute route, TransportActivityGuard? guard = null,
        Func<TransportEndpoint, IWasapiPreparationDevice>? open = null, TimeSpan? timeout = null)
    {
        route.Validate();
        if (open is null && (route.Capture.Evidence != FormatEvidence.ExactCapability ||
            route.Playback.Evidence != FormatEvidence.ExactCapability))
            throw new ArgumentException("A prova nativa requer formatos consultados no Windows.", nameof(route));
        if (route.Capture.Endpoint.Generation == long.MaxValue) throw new ArgumentException("Geração sem espaço para limpeza.");
        this.route = route; this.guard = guard ?? TransportActivityGuard.Shared;
        this.open = open ?? NativeWasapiPreparationDevice.Open; this.timeout = timeout ?? TimeSpan.FromSeconds(3);
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromSeconds(5)) throw new ArgumentOutOfRangeException(nameof(timeout));
        ticket = new(route); capture = new(this, route.Capture, true); playback = new(this, route.Playback, false);
    }
    public Task<WasapiSilentSnapshot> RunAsync(TimeSpan duration, CancellationToken token = default)
    {
        if (duration < TimeSpan.FromMilliseconds(50) || duration > TimeSpan.FromSeconds(10)) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (control)
        {
            if (run is not null || stop.IsSet) throw new InvalidOperationException("Sessão de prova é de uso único.");
            token.ThrowIfCancellationRequested();
            lease = guard.TryAcquire(AudioActivity.IsolatedTransport) ?? throw new InvalidOperationException("Outra operação de áudio está ativa.");
            lock (live) live.Add(this); // Root COM owners/lease even if a caller abandons a timed-out operation.
            cancellation = token.Register(RequestStop);
            try { capture.Launch(); playback.Launch(); }
            catch (Exception error) { Fail("OwnerLaunch", error); capture.NotLaunched(); playback.NotLaunched(); }
            return run = ControlAsync(duration);
        }
    }
    public void RequestStop()
    {
        ticket.Invalidate(); // Irreversible before any wait or native stop.
        stop.Set(); stopped.TrySetResult();
    }
    public void Invalidate(long generation)
    {
        if (generation == route.Capture.Endpoint.Generation && !stop.IsSet) Fail("SelectionInvalidated");
    }
    private void Fail(string stage, Exception? error = null)
    {
        if (Interlocked.CompareExchange(ref faultWritten, 1, 0) == 0)
        {
            failure = error is WasapiCallException call ? stage + "/" + call.Stage : stage;
            hresult = error is WasapiCallException result ? result.Result : error is System.Runtime.InteropServices.COMException ? error.HResult : null;
        }
        RequestStop();
    }
    private async Task<WasapiSilentSnapshot> ControlAsync(TimeSpan duration)
    {
        try
        {
            await Task.WhenAll(capture.Ready.Task, playback.Ready.Task).WaitAsync(timeout);
            if (!stop.IsSet)
            {
                path = new(route, capture.Capacity, playback.Capacity, capture.Period, playback.Period);
                playback.Go.Set(); await playback.Started.Task.WaitAsync(timeout);
            }
            if (!stop.IsSet) { capture.Go.Set(); await capture.Started.Task.WaitAsync(timeout); }
            if (!stop.IsSet)
            {
                long begin = WasapiTime.Now100ns();
                ticket.Enable();
                using var delayCancellation = new CancellationTokenSource();
                var limit = Task.Delay(duration, delayCancellation.Token);
                durationReached = await Task.WhenAny(limit, stopped.Task) == limit && !stop.IsSet;
                observation100ns = WasapiTime.Now100ns() - begin;
                delayCancellation.Cancel();
            }
        }
        catch (Exception error) { Fail("Control", error); }
        RequestStop(); return await FinishAsync(retryStop: false);
    }
    private async Task<WasapiSilentSnapshot> FinishAsync(bool retryStop)
    {
        await finalizing.WaitAsync();
        try
        {
        if (completed) return Snapshot();
        if (retryStop) { capture.Retry.Set(); playback.Retry.Set(); }
        try { await Task.WhenAll(capture.Done.Task, playback.Done.Task).WaitAsync(timeout); }
        catch (TimeoutException) { return Snapshot(); } // Keep owners/COM/scratch/lease; never abort a native call.
        if (!capture.Join() || !playback.Join()) return Snapshot();
            path?.Dispose();
            cancellation.Dispose();
            lease?.Dispose();
            lease = null;
            completed = true;
            lock (live) live.Remove(this);
            return Snapshot();
        }
        finally { finalizing.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        RequestStop();
        Task<WasapiSilentSnapshot>? pending; lock (control) pending = run;
        if (pending is null) return;
        await pending;
        var result = await FinishAsync(retryStop: true);
        if (result.ResourcesRetained) throw new InvalidOperationException("Encerramento nativo não confirmado; recursos e bloqueio preservados.");
    }
    private WasapiSilentSnapshot Snapshot() => new(capture.Snapshot(), playback.Snapshot(), completed, lease is not null,
        failure, hresult, path?.CapacityFrames ?? 0, path?.Produced ?? 0, path?.Read ?? 0, path?.Dropped ?? 0, path?.Underrun ?? 0,
        observation100ns, durationReached);

    private sealed class Owner
    {
        private readonly WasapiSilentSession session;
        private readonly WasapiEndpointInfo info;
        private readonly bool input;
        private readonly Thread thread;
        private readonly WasapiResourceScope resources = new();
        private IWasapiPreparationDevice? device;
        private IWasapiStreamClient? client;
        private IWasapiPreparationClock? clock;
        private IWasapiCapturePackets? packets;
        private IWasapiRenderPackets? render;
        private bool launched, initialized, startAttempted, started, stopConfirmed, bufferInFlight, bufferUncertain;
        private int ownerId;
        public uint Capacity;
        public long Period;
        private long latency, acquired, released, acquiredFrames, releasedFrames, primed;
        private long acceptedPackets, acceptedFrames, startupDiscardedPackets, startupDiscardedFrames;
        private uint padding, flags, startupDiscardFlags;
        private ulong packetPosition, packetQpc, startupDiscardPosition, startupDiscardQpc;
        private string? error, cleanup;
        private WasapiRunningClock? motion;
        private WasapiClockReading? lastClock;
        private int? startResult, stopResult, acquireResult, releaseResult;
        private long nextClock, startTime, startRequested100ns;
        private NativeClockResult? clockStatus;
        private const long StartupDiscardWindow100ns = 5_000_000; // Bound by the existing packet freshness budget.
        private const uint Discontinuity = (uint)TransportPacketFlags.Discontinuity;
        private const uint Silent = (uint)TransportPacketFlags.Silent;
        private const int BufferEmpty = 0x08890001;
        public readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Go = new(), Retry = new();
        public Owner(WasapiSilentSession session, WasapiEndpointInfo info, bool input)
        {
            this.session = session; this.info = info; this.input = input;
            thread = new Thread(Run) { IsBackground = true, Name = input ? "WaveStudio WASAPI capture owner" : "WaveStudio WASAPI silent render owner" };
            thread.SetApartmentState(ApartmentState.MTA);
        }
        public void Launch() { thread.Start(); launched = true; }
        public void NotLaunched() { if (!launched) { Ready.TrySetResult(); Started.TrySetResult(); Done.TrySetResult(); } }
        public bool Join() => !launched || thread.Join(TimeSpan.FromMilliseconds(200));
        private void Check() { if (session.stop.IsSet) throw new OperationCanceledException(); }
        private void Prepare()
        {
            Check(); device = resources.Add(session.open(info.Endpoint)); Check(); device.ValidateIdentity(info.Endpoint); Check();
            client = (IWasapiStreamClient)resources.Add(device.Activate()); Check();
            var mix = client.ReadMixFormat(); mix.Validate();
            if (mix != info.Mix) throw new InvalidOperationException("Formato divergente; sem fallback.");
            Check(); client.RequireExactSupport(); (Period, long minimum) = client.ReadPeriods();
            if (Period is <= 0 or > 1000000 || minimum <= 0) throw new NotSupportedException("Período fora do orçamento.");
            Check(); device.ValidateIdentity(info.Endpoint); Check(); client.Initialize(); initialized = true; Check();
            (Capacity, latency) = client.ReadBufferInfo();
            if (Capacity is 0 or > 4096 || latency < 0) throw new NotSupportedException("Capacidade/latência fora do orçamento.");
            padding = client.ReadPadding(); if (padding > Capacity) throw new InvalidOperationException("Padding inválido na preparação.");
            Check(); clock = resources.Add(client.OpenClock()); Check();
            if (input) packets = resources.Add(client.OpenCapture()); else render = resources.Add(client.OpenRender());
            device.ValidateIdentity(info.Endpoint); Check();
        }
        private void Run()
        {
            ownerId = Environment.CurrentManagedThreadId;
            try
            {
                Prepare(); Ready.TrySetResult();
                while (!Go.IsSet && !session.stop.Wait(2)) { }
                Check(); device!.ValidateIdentity(info.Endpoint); client!.RevalidateFormat(info.Mix!); Check();
                if (!input) { FillRender(prime: true); Check(); }
                startRequested100ns = WasapiTime.Now100ns();
                startAttempted = true; startResult = client!.Start(); WasapiCallException.RequireSuccess(startResult.Value, "IAudioClient.Start"); started = true;
                startTime = WasapiTime.Now100ns(); motion = new(info.Endpoint, info.Mix!.SampleRate, startTime); nextClock = startTime + 1000000;
                Started.TrySetResult(); Check();
                int pollMs = Math.Clamp((int)(Period / 20000), 1, 10);
                while (!session.stop.IsSet)
                {
                    if (input)
                    {
                        padding = client.ReadPadding(); if (padding > Capacity) throw new InvalidOperationException("Padding de captura inválido.");
                        if (session.ticket.IsValid) DrainCapture();
                    }
                    else FillRender(prime: false);
                    long now = WasapiTime.Now100ns();
                    if (session.ticket.IsValid && now >= nextClock) { ReadClock(); nextClock = now + 1000000; }
                    session.stop.Wait(pollMs);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception problem) { error = problem is WasapiCallException c ? c.Stage : problem.GetType().Name; session.Fail(input ? "Capture" : "Render", problem); }
            finally
            {
                Ready.TrySetResult(); Started.TrySetResult(); session.RequestStop();
                // Retry Stop only on an explicit disposal attempt. A failed Release is never blindly repeated.
                while (true)
                {
                    if (TryCleanup()) { Done.TrySetResult(); break; }
                    Retry.Wait(); Retry.Reset();
                }
            }
        }
        private void ReadClock()
        {
            lastClock = clock!.Read();
            long now = WasapiTime.Now100ns();
            var result = motion!.Observe(lastClock.Value, now);
            clockStatus = result;
            if (result == NativeClockResult.Stalled && lastClock.Value.Position == 0 && now - startTime <= 5000000) return;
            if (result != NativeClockResult.Valid) throw new InvalidOperationException("Clock inválido: " + result);
        }
        private unsafe void DrainCapture()
        {
            for (int count = 0; count < 32 && !session.stop.IsSet && session.ticket.IsValid; count++)
            {
                uint nextFrames = packets!.NextPacketSize();
                if (nextFrames == 0) return;
                Check();
                var buffer = packets.GetBuffer();
                acquireResult = buffer.HResult;
                if (buffer.HResult == BufferEmpty) return; // AUDCLNT_S_BUFFER_EMPTY: no packet acquired, no Release.
                WasapiCallException.RequireSuccess(buffer.HResult, "IAudioCaptureClient.GetBuffer");
                bufferInFlight = true; acquired++; acquiredFrames += buffer.Frames; uint consumed = 0;
                bool discardStartup = (buffer.Flags & Discontinuity) != 0;
                try
                {
                    flags |= buffer.Flags; packetPosition = buffer.Position; packetQpc = buffer.Qpc100ns;
                    long now = WasapiTime.Now100ns();
                    // BUFFER_EMPTY never reaches here. A valid first packet uses up the one-shot exception, too.
                    if (buffer.Frames != nextFrames || buffer.Frames > Capacity ||
                        (buffer.Flags & ~(Silent | Discontinuity)) != 0 ||
                        (discardStartup && (acquired != 1 || now - startRequested100ns > StartupDiscardWindow100ns)))
                        throw new InvalidOperationException("Packet inválido/descontínuo ou timestamp incerto.");
                    if (buffer.Qpc100ns == 0 || buffer.Qpc100ns > (ulong)now || (ulong)now - buffer.Qpc100ns > 5000000)
                        throw new InvalidOperationException("Timestamp do packet indisponível/obsoleto.");
                    bool silent = (buffer.Flags & Silent) != 0;
                    if (!silent && buffer.Data == 0) throw new InvalidOperationException("Packet sem dados e sem SILENT.");
                    Check();
                    var bytes = silent ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>((void*)buffer.Data, checked((int)buffer.Frames * info.Mix!.BlockAlign));
                    if (discardStartup)
                    {
                        // ReleaseBuffer(0) would return this same packet next time. Consume the whole packet
                        // before advancing, with no sample read for SILENT and no input to the FIR for either case.
                        if (!silent) session.path!.DiscardCapture(bytes, (int)buffer.Frames);
                    }
                    else session.path!.CopyCapture(bytes, (int)buffer.Frames, silent);
                    consumed = buffer.Frames;
                }
                finally
                {
                    int hr;
                    try { hr = packets.ReleaseBuffer(consumed); releaseResult = hr; }
                    catch { bufferUncertain = true; throw; }
                    if (hr != 0) { bufferUncertain = true; throw new WasapiCallException("IAudioCaptureClient.ReleaseBuffer", hr); }
                    bufferInFlight = false; released++; releasedFrames += consumed;
                }
                if (discardStartup)
                {
                    startupDiscardedPackets++;
                    startupDiscardedFrames += consumed;
                    startupDiscardFlags = buffer.Flags;
                    startupDiscardPosition = buffer.Position;
                    startupDiscardQpc = buffer.Qpc100ns;
                    continue;
                }
                acceptedPackets++;
                acceptedFrames += consumed;
                session.path!.Produce((int)consumed, session.ticket);
            }
        }
        private void FillRender(bool prime)
        {
            if (session.stop.IsSet) return;
            padding = client!.ReadPadding();
            if (padding > Capacity || (prime && padding != 0)) throw new InvalidOperationException("Padding nativo inválido.");
            uint frames = Capacity - padding;
            if (frames == 0 || session.stop.IsSet) return;
            nint data = render!.GetBuffer(frames); bufferInFlight = true; acquired++; acquiredFrames += frames;
            acquireResult = 0;
            try
            {
                if (data == 0) throw new InvalidOperationException("Buffer de render inválido.");
            }
            finally
            {
                // SILENT is unconditional, even if cancellation/selection/failure occurs after GetBuffer.
                int hr;
                try { hr = render.ReleaseBuffer(frames, Silent); releaseResult = hr; }
                catch { bufferUncertain = true; throw; }
                if (hr != 0) { bufferUncertain = true; throw new WasapiCallException("IAudioRenderClient.ReleaseBuffer", hr); }
                bufferInFlight = false; released++; releasedFrames += frames; if (prime) primed += frames;
            }
            if (!prime && session.ticket.IsValid) session.path!.Consume((int)frames, session.ticket);
        }
        private bool TryCleanup()
        {
            if (cleanup is not null) return false;
            if (startAttempted && !stopConfirmed)
            {
                try
                {
                    int hr = client!.Stop(); stopResult = hr;
                    if (hr is not (0 or 1)) throw new WasapiCallException("IAudioClient.Stop", hr);
                    stopConfirmed = true;
                }
                catch (Exception problem) { error ??= "Stop não confirmado"; session.Fail(input ? "CaptureStop" : "RenderStop", problem); return false; }
            }
            if (bufferUncertain) { cleanup = "ReleaseBuffer não confirmado"; return false; }
            resources.Dispose(); cleanup = resources.CleanupError;
            if (!resources.Released) { session.Fail(input ? "CaptureRelease" : "RenderRelease"); return false; }
            return true;
        }
        public WasapiRoleSnapshot Snapshot() => new(initialized, startAttempted, started, stopConfirmed, resources.Released, ownerId,
            Capacity, Period, latency, Interlocked.Read(ref acquired), Interlocked.Read(ref released), Interlocked.Read(ref acquiredFrames),
            Interlocked.Read(ref releasedFrames), Interlocked.Read(ref primed), padding, motion?.MovementSamples ?? 0,
            Done.Task.IsCompleted ? lastClock : null, flags, packetPosition, packetQpc, bufferInFlight, error, cleanup,
            startResult, stopResult, acquireResult, releaseResult, Done.Task.IsCompleted ? clockStatus : null,
            Interlocked.Read(ref acceptedPackets), Interlocked.Read(ref acceptedFrames),
            Interlocked.Read(ref startupDiscardedPackets), Interlocked.Read(ref startupDiscardedFrames),
            startupDiscardFlags, startupDiscardPosition, startupDiscardQpc);
    }
}
