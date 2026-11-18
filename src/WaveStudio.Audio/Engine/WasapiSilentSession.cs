namespace WaveStudio.Audio.Engine;

// Prova de execução única e saída sempre SILENT. Sem UI, reativação de áudio, recuperação, nível de monitoramento ou interface de transporte simulado.
internal sealed class WasapiSilentSession : IAsyncDisposable
{
    private static readonly HashSet<WasapiSilentSession> live = [];
    private readonly TransportRoute route;
    private readonly Func<TransportEndpoint, IWasapiPreparationDevice> open;
    private readonly TransportActivityGuard guard;
    private readonly TimeSpan timeout;
    private readonly bool verifyCaptureTiming, observeCheckpoints;
    private readonly object control = new(); // O processamento de amostras nunca adquire este lock.
    private readonly SemaphoreSlim finalizing = new(1);
    private readonly ManualResetEventSlim stop = new();
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DeliveryTicket ticket;
    private readonly Owner capture, playback;
    private IDisposable? lease;
    private CancellationTokenRegistration cancellation;
    private WasapiSilentPath? path;
    private WasapiCheckpointLedger? checkpoints;
    private Task<WasapiSilentSnapshot>? run;
    private bool completed;
    private string? failure;
    private int? hresult;
    private int faultWritten;
    private long observation100ns, observationEnd100ns;
    private bool durationReached;

    public WasapiSilentSession(TransportRoute route, TransportActivityGuard? guard = null,
        Func<TransportEndpoint, IWasapiPreparationDevice>? open = null, TimeSpan? timeout = null,
        bool simulateCaptureTiming = false, bool observeCheckpoints = false)
    {
        route.Validate();
        if (observeCheckpoints && open is null)
            throw new ArgumentException("O diário é habilitado apenas com backend injetado neste checkpoint.", nameof(observeCheckpoints));
        this.observeCheckpoints = observeCheckpoints;
        if (open is null && (route.Capture.Evidence != FormatEvidence.ExactCapability ||
            route.Playback.Evidence != FormatEvidence.ExactCapability))
            throw new ArgumentException("A prova nativa requer formatos consultados no Windows.", nameof(route));
        if (route.Capture.Endpoint.Generation == long.MaxValue) throw new ArgumentException("Geração sem espaço para limpeza.");
        this.route = route; this.guard = guard ?? TransportActivityGuard.Shared;
        verifyCaptureTiming = open is null || simulateCaptureTiming;
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
            lock (live) live.Add(this); // Mantém os proprietários COM e o lease referenciados mesmo se o chamador abandonar uma operação que excedeu o prazo.
            cancellation = token.Register(RequestStop);
            try { capture.Launch(); playback.Launch(); }
            catch (Exception error) { Fail("OwnerLaunch", error); capture.NotLaunched(); playback.NotLaunched(); }
            return run = ControlAsync(duration);
        }
    }
    internal WasapiCheckpointResult ReadCheckpoint(long generation, WasapiCheckpoint? baseline = null)
    {
        if (!observeCheckpoints) return new(WasapiCheckpointStatus.Disabled);
        var result = Volatile.Read(ref checkpoints)?.Read(generation, baseline) ?? new(WasapiCheckpointStatus.Inactive);
        if (result.Status is WasapiCheckpointStatus.OwnerStale or WasapiCheckpointStatus.InvalidTime or
            WasapiCheckpointStatus.InvalidAccounting or WasapiCheckpointStatus.InvalidSequence or
            WasapiCheckpointStatus.Overflow or WasapiCheckpointStatus.OwnerFault)
            Fail("Checkpoint/" + result.Status);
        return result;
    }
    public void RequestStop()
    {
        ticket.Invalidate(); // Invalidação irreversível antes de qualquer espera ou parada nativa.
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
                if (observeCheckpoints) Volatile.Write(ref checkpoints, new(ticket, path.CapacityFrames));
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
                observationEnd100ns = WasapiTime.Now100ns();
                observation100ns = observationEnd100ns - begin;
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
        catch (TimeoutException) { return Snapshot(); } // Preserva proprietários, COM, memória de trabalho e lease. Nunca interrompe uma chamada nativa.
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
    private WasapiSilentSnapshot Snapshot() => new(capture.Snapshot(completed), playback.Snapshot(completed), completed, lease is not null,
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
        private long latency, acquired, released, acquiredFrames, releasedFrames, primed, preTicketRenderFrames;
        private long acceptedPackets, acceptedFrames, startupDiscardedPackets, startupDiscardedFrames;
        private uint padding, flags, startupDiscardFlags;
        private ulong packetPosition, packetQpc, startupDiscardPosition, startupDiscardQpc;
        private string? error, cleanup;
        private WasapiRunningClock? motion;
        private WasapiCaptureTiming? captureTiming;
        private WasapiClockReading? lastClock;
        private int? startResult, stopResult, acquireResult, releaseResult;
        private long nextClock, startTime, startRequested100ns;
        private NativeClockResult? clockStatus;
        private const long StartupDiscardWindow100ns = 5_000_000; // Limitado pelo prazo de frescor já aplicado aos pacotes.
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
            if (input && session.verifyCaptureTiming) captureTiming = new(mix.SampleRate, Period, Capacity);
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
                if (!input) { ProcessBoundary(prime: true); Check(); }
                startRequested100ns = WasapiTime.Now100ns();
                startAttempted = true; startResult = client!.Start(); WasapiCallException.RequireSuccess(startResult.Value, "IAudioClient.Start"); started = true;
                startTime = WasapiTime.Now100ns(); motion = new(info.Endpoint, info.Mix!.SampleRate, startTime, captureTiming); nextClock = startTime + 1000000;
                Started.TrySetResult(); Check();
                int pollMs = Math.Clamp((int)(Period / 20000), 1, 10);
                while (!session.stop.IsSet)
                {
                    ProcessBoundary(prime: false);
                    long now = WasapiTime.Now100ns();
                    if (session.ticket.IsValid)
                    {
                        captureTiming?.CheckProgress(now, startTime);
                        if (now >= nextClock) { ReadClock(); nextClock = now + 1000000; }
                    }
                    session.stop.Wait(pollMs);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception problem) { error = problem is WasapiCallException c ? c.Stage : problem.GetType().Name; session.Fail(input ? "Capture" : "Render", problem); }
            finally
            {
                Ready.TrySetResult(); Started.TrySetResult(); session.RequestStop();
                // Só repete Stop em uma tentativa explícita de descarte. Não repete uma liberação malsucedida sem confirmação.
                while (true)
                {
                    if (TryCleanup()) { Done.TrySetResult(); break; }
                    Retry.Wait(); Retry.Reset();
                }
            }
        }
        private void ProcessBoundary(bool prime)
        {
            var ledger = session.checkpoints;
            var journal = input ? ledger?.Capture : ledger?.Render;
            if (journal is null) { ProcessAudio(prime); return; }

            journal.Begin();
            try { ProcessAudio(prime); }
            catch
            {
                // Uma operação sem confirmação de liberação não forma fronteira válida.
                session.ticket.Invalidate();
                journal.Abort();
                throw;
            }
            if (session.stop.IsSet) { journal.Abort(); return; }
            if (bufferInFlight || bufferUncertain)
            {
                journal.Abort();
                session.Fail("Checkpoint/OwnerFault");
                return;
            }

            var path = session.path!;
            var status = journal.Complete(new(acquired, released, acquiredFrames, releasedFrames,
                acceptedPackets, acceptedFrames, startupDiscardedPackets, startupDiscardedFrames,
                input ? path.Produced : path.Read, input ? path.Dropped : path.Underrun, primed, preTicketRenderFrames));
            if (status != WasapiCheckpointStatus.Available) session.Fail("Checkpoint/" + status);
        }
        private void ProcessAudio(bool prime)
        {
            if (!input) { FillRender(prime); return; }
            padding = client!.ReadPadding();
            if (padding > Capacity) throw new InvalidOperationException("Padding de captura inválido.");
            if (session.ticket.IsValid) DrainCapture();
        }
        private void ReadClock()
        {
            lastClock = clock!.Read();
            long now = WasapiTime.Now100ns();
            var result = motion!.Observe(lastClock.Value, now);
            clockStatus = result;
            if (result == NativeClockResult.Stalled && lastClock.Value.Position == 0 && now - startTime <= 5000000) return;
            if (result is not (NativeClockResult.Valid or NativeClockResult.Provisional))
                throw new InvalidOperationException("Clock inválido: " + result);
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
                if (buffer.HResult == BufferEmpty) return; // AUDCLNT_S_BUFFER_EMPTY: nenhum pacote adquirido, nenhuma liberação.
                WasapiCallException.RequireSuccess(buffer.HResult, "IAudioCaptureClient.GetBuffer");
                bufferInFlight = true; acquired++; acquiredFrames += buffer.Frames; uint consumed = 0;
                bool discardStartup = (buffer.Flags & Discontinuity) != 0;
                try
                {
                    flags |= buffer.Flags; packetPosition = buffer.Position; packetQpc = buffer.Qpc100ns;
                    long now = WasapiTime.Now100ns();
                    // BUFFER_EMPTY não chega a este ponto. Um primeiro pacote válido também esgota a exceção de uso único.
                    if (buffer.Frames != nextFrames || buffer.Frames > Capacity ||
                        (buffer.Flags & ~(Silent | Discontinuity)) != 0 ||
                        (discardStartup && (acquired != 1 || now - startRequested100ns > StartupDiscardWindow100ns)))
                        throw new InvalidOperationException("Packet inválido/descontínuo ou timestamp incerto.");
                    if (buffer.Qpc100ns == 0 || buffer.Qpc100ns > (ulong)now || (ulong)now - buffer.Qpc100ns > 5000000)
                        throw new InvalidOperationException("Timestamp do packet indisponível/obsoleto.");
                    bool silent = (buffer.Flags & Silent) != 0;
                    if (!silent && buffer.Data == 0) throw new InvalidOperationException("Packet sem dados e sem SILENT.");
                    Check();
                    if (!discardStartup) captureTiming?.ValidatePacket(buffer.Position, buffer.Frames, buffer.Qpc100ns);
                    var bytes = silent ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>((void*)buffer.Data, checked((int)buffer.Frames * info.Mix!.BlockAlign));
                    if (discardStartup)
                    {
                        // ReleaseBuffer(0) faria o mesmo pacote reaparecer. Consuma o pacote inteiro
                        // antes de avançar, sem ler amostras quando SILENT e sem alimentar o FIR em nenhum dos casos.
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
                captureTiming?.AcceptReleasedPacket(buffer.Position, consumed, buffer.Qpc100ns);
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
                // SILENT é incondicional, mesmo se houver cancelamento, mudança de seleção ou falha após GetBuffer.
                int hr;
                try { hr = render.ReleaseBuffer(frames, Silent); releaseResult = hr; }
                catch { bufferUncertain = true; throw; }
                if (hr != 0) { bufferUncertain = true; throw new WasapiCallException("IAudioRenderClient.ReleaseBuffer", hr); }
                bufferInFlight = false; released++; releasedFrames += frames; if (prime) primed += frames;
            }
            if (!prime)
            {
                if (session.ticket.IsValid) session.path!.Consume((int)frames, session.ticket);
                else if (session.observeCheckpoints) preTicketRenderFrames += frames;
            }
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
        public WasapiRoleSnapshot Snapshot(bool joined) => new(initialized, startAttempted, started, stopConfirmed, resources.Released, ownerId,
            Capacity, Period, latency, Interlocked.Read(ref acquired), Interlocked.Read(ref released), Interlocked.Read(ref acquiredFrames),
            Interlocked.Read(ref releasedFrames), Interlocked.Read(ref primed), padding, motion?.MovementSamples ?? 0,
            Done.Task.IsCompleted ? lastClock : null, flags, packetPosition, packetQpc, bufferInFlight, error, cleanup,
            startResult, stopResult, acquireResult, releaseResult, Done.Task.IsCompleted ? clockStatus : null,
            Interlocked.Read(ref acceptedPackets), Interlocked.Read(ref acceptedFrames),
            Interlocked.Read(ref startupDiscardedPackets), Interlocked.Read(ref startupDiscardedFrames),
            startupDiscardFlags, startupDiscardPosition, startupDiscardQpc, joined ? motion?.Snapshot() : null, joined ? captureTiming?.Snapshot(session.durationReached && session.failure is null ? session.observationEnd100ns : 0) : null);
    }
}
