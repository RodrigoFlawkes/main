using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace WaveStudio.Audio.Engine;

// Prova interna do contrato: aceita apenas ISimulatedAudioTransport, sem stream nativo.
internal sealed class IsolatedTransportSession : IAsyncDisposable
{
    private const long Frequency = 10000000;
    private readonly BlockingCollection<Action> commands = new(8);
    private readonly object posting = new(); // Apenas no plano de controle. Os callbacks nunca adquirem este lock.
    private readonly TransportActivityGuard guard;
    private readonly Func<SyntheticAudioPipeline, FrameBufferSnapshot?> readBuffer;
    private readonly Thread worker;
    private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TransportSnapshot snapshot = new(TransportState.Closed, 0, TransportFault.None, 0, 0, 0, 0, 0, 0, 0, 0, null, 0, 0, DriftControlState.Waiting, 0, false, false);
    private Epoch? active;
    private long requestedGeneration, stoppedThrough;
    private int disposing;
    private bool shutdown;
    public TransportSnapshot Snapshot => Volatile.Read(ref snapshot);

    public IsolatedTransportSession(TransportActivityGuard guard, Func<SyntheticAudioPipeline, FrameBufferSnapshot?>? readBuffer = null)
    {
        this.guard = guard;
        this.readBuffer = readBuffer ?? (static pipe => pipe.Snapshot().Buffer);
        worker = new Thread(Run) { IsBackground = true, Name = "WaveStudio isolated transport control" };
        worker.SetApartmentState(ApartmentState.MTA); worker.Start();
    }
    public Task<TransportSnapshot> StartAsync(TransportRoute route, ISimulatedAudioTransport backend,
        long origin100ns, bool recover = false, CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposing) != 0, this);
        ArgumentNullException.ThrowIfNull(backend); route.Validate();
        _ = route.CapacityFrames;
        if (origin100ns < 0) throw new ArgumentOutOfRangeException(nameof(origin100ns));
        long generation = route.Capture.Endpoint.Generation;
        while (true)
        {
            long previous = Volatile.Read(ref requestedGeneration);
            if (generation <= previous) throw new ArgumentException("Início/recuperação exige geração estritamente nova.");
            if (Interlocked.CompareExchange(ref requestedGeneration, generation, previous) == previous) break;
        }
        return Post(() => Start(route, backend, origin100ns, recover, cancellation));
    }
    public Task<TransportSnapshot> StopAsync()
    {
        Freeze(); // Mesmo se o worker ainda estiver em preparação ou houver um callback em andamento.
        return Post(() => StopCore(false));
    }
    public Task<TransportSnapshot> TickAsync(long closedThrough100ns) => Post(() => Tick(closedThrough100ns));
    public Task<TransportSnapshot> SetMonitorAsync(long generation, bool muted, double digitalLevel)
    {
        if (!double.IsFinite(digitalLevel) || digitalLevel is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(digitalLevel));
        return Post(() =>
        {
            var e = active;
            if (e is null || e.Generation != generation || !e.Ticket.IsValid) throw new InvalidOperationException("Sessão obsoleta/inativa.");
            Volatile.Write(ref e.Monitor, new(muted, (float)digitalLevel));
            return Publish(e, TransportState.Running);
        });
    }
    public void SignalInvalidation(long generation, TransportFault reason)
    {
        if (reason == TransportFault.None) throw new ArgumentOutOfRangeException(nameof(reason));
        var e = Volatile.Read(ref active);
        if (e?.Generation == generation) Fail(e, reason);
    }
    private void Freeze()
    {
        long requested = Volatile.Read(ref requestedGeneration);
        long old;
        do { old = Volatile.Read(ref stoppedThrough); if (old >= requested) break; }
        while (Interlocked.CompareExchange(ref stoppedThrough, requested, old) != old);
        Volatile.Read(ref active)?.Ticket.Invalidate();
    }
    private Task<TransportSnapshot> Post(Func<TransportSnapshot> action, bool duringDisposal = false)
    {
        lock (posting)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposing) != 0 && !duringDisposal, this);
            var completion = new TaskCompletionSource<TransportSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!commands.TryAdd(() =>
            {
                try { completion.SetResult(action()); } catch (Exception error) { completion.SetException(error); }
            })) completion.SetException(new InvalidOperationException("Fila de controle cheia; entrega já invalidada quando Stop foi solicitado."));
            return completion.Task;
        }
    }
    private void Run()
    {
        try
        {
            while (!shutdown)
            {
                if (commands.TryTake(out var command, 10)) command();
                var e = active;
                if (e is not null && (Volatile.Read(ref e.Fault) != 0 || Volatile.Read(ref stoppedThrough) >= e.Generation) &&
                    Snapshot.State is not (TransportState.Faulted or TransportState.Stopping)) StopCore(e.Fault != 0);
            }
            exited.TrySetResult();
        }
        catch (Exception error) { exited.TrySetException(error); }
    }
    private TransportSnapshot Start(TransportRoute route, ISimulatedAudioTransport backend, long origin, bool recover, CancellationToken token)
    {
        if (Snapshot.State == TransportState.Faulted)
        {
            if (!recover) throw new InvalidOperationException("Falha retida exige recuperação manual explícita.");
            if (active is not null) StopCore(true);
        }
        else if (Snapshot.State != TransportState.Closed || recover) throw new InvalidOperationException("Pare a sessão antes de iniciar outra.");
        if (active is not null) throw new InvalidOperationException("Callbacks/recursos antigos ainda não estão quiescentes.");
        var lease = guard.TryAcquire(AudioActivity.IsolatedTransport) ?? throw new InvalidOperationException("Diagnóstico e transporte são mutuamente exclusivos.");
        Epoch e;
        try { e = new(route, backend, lease, origin); }
        catch { lease.Dispose(); backend.Dispose(); throw; }
        Volatile.Write(ref active, e); Publish(e, TransportState.Preparing);
        e.Cancellation = token.Register(() => Fail(e, TransportFault.Canceled));
        try
        {
            CheckStart(e, token);
            backend.PrepareCapture(route, (bytes, frames, clock, at) => Capture(e, bytes, frames, clock, at));
            CheckStart(e, token);
            backend.PreparePlayback(route, (bytes, frames, clock, at) => Render(e, bytes, frames, clock, at));
            CheckStart(e, token); Publish(e, TransportState.Priming);
            e.PipeGeneration = e.Pipe.Start();
            e.Primed = e.Pipe.Produce(e.PipeGeneration, e.Output.AsSpan(0, e.Prime * e.OutputChannels));
            if (e.Primed != e.Prime) throw new InvalidOperationException("Priming incompleto.");
            backend.StartPlayback(); CheckStart(e, token);
            backend.StartCapture(); CheckStart(e, token);
            e.Ticket.Enable();
            // Stop pode ter ocorrido em paralelo com Enable. Consulte novamente seu marco monotônico.
            CheckStart(e, token);
            return Publish(e, TransportState.Running);
        }
        catch (OperationCanceledException) { Fail(e, TransportFault.Canceled); return StopCore(true); }
        catch { Fail(e, TransportFault.Backend); return StopCore(true); }
    }
    private void CheckStart(Epoch e, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref stoppedThrough) >= e.Generation || Volatile.Read(ref e.Fault) != 0) throw new OperationCanceledException();
    }
    private bool Enter(Epoch e, ref int busy, ref int roleThread)
    {
        if (!e.Ticket.IsValid || !ReferenceEquals(Volatile.Read(ref active), e)) return false;
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) { Fail(e, TransportFault.Protocol); return false; }
        if (!e.Ticket.IsValid) { Volatile.Write(ref busy, 0); return false; }
        int caller = Environment.CurrentManagedThreadId;
        int previous = Interlocked.CompareExchange(ref roleThread, caller, 0);
        if (caller == worker.ManagedThreadId || caller == Volatile.Read(ref e.ProducerThread) && caller == Volatile.Read(ref e.ConsumerThread) || previous != 0 && previous != caller)
        { Fail(e, TransportFault.Protocol); Volatile.Write(ref busy, 0); return false; }
        return true;
    }
    private static void Fail(Epoch e, TransportFault reason)
    {
        e.Ticket.Invalidate(); Interlocked.CompareExchange(ref e.Fault, (int)reason, 0);
    }
    private static bool ValidPacket(Epoch e, int frames, int bytes, int align, long arrival, long previous) =>
        frames is > 0 and <= 4096 && bytes == frames * align && arrival >= previous && arrival > Volatile.Read(ref e.Cutoff);

    private void Capture(Epoch e, ReadOnlySpan<byte> bytes, int frames, NativeClockSample clock, long at)
    {
        if (!Enter(e, ref e.ProducerBusy, ref e.ProducerThread)) return;
        try
        {
            bool silent = (clock.Flags & TransportPacketFlags.Silent) != 0;
            int byteCount = silent && bytes.IsEmpty && frames is > 0 and <= 4096 ? frames * e.InputAlign : bytes.Length;
            if (!ValidPacket(e, frames, byteCount, e.InputAlign, at, e.LastCapture)) { Fail(e, TransportFault.Protocol); return; }
            var result = e.InputClock.Convert(clock, at, out var observation);
            if (result != NativeClockResult.Valid) { Fail(e, result == NativeClockResult.Discontinuity ? TransportFault.Discontinuity : TransportFault.Clock); return; }
            e.LastCapture = at;
            var input = e.Input.AsSpan(0, frames * e.InputChannels);
            if (silent) input.Clear();
            else PcmFloatConverter.Decode(bytes, input, e.InputFormat, e.InputEncoding);
            // AdaptiveResampler é serial: somente este produtor aplica comandos e processa amostras.
            e.Adaptive.SetTargetPpm(e.Generation, Volatile.Read(ref e.Ratio).Ppm);
            var converted = e.Adaptive.Process(e.Generation, input, e.Output);
            Volatile.Write(ref e.AppliedPpm, e.Adaptive.CurrentPpm);
            if (converted.ConsumedFrames != frames) { Fail(e, TransportFault.Protocol); return; }
            // O pipeline aceita até 4096 frames por chamada. Divida explicitamente saídas maiores do aumento de taxa.
            int accepted = 0;
            for (int offset = 0; offset < converted.ProducedFrames;)
            {
                int block = Math.Min(4096, converted.ProducedFrames - offset);
                accepted += e.Pipe.Produce(e.PipeGeneration, e.Output.AsSpan(offset * e.OutputChannels, block * e.OutputChannels));
                offset += block;
            }
            e.ProducerLedger.Add(accepted, at, observation);
        }
        catch { Fail(e, TransportFault.Backend); }
        finally { Volatile.Write(ref e.ProducerBusy, 0); }
    }
    private void Render(Epoch e, Span<byte> bytes, int frames, NativeClockSample clock, long at)
    {
        bytes.Clear();
        if (!Enter(e, ref e.ConsumerBusy, ref e.ConsumerThread)) return;
        try
        {
            if (!ValidPacket(e, frames, bytes.Length, e.OutputAlign, at, e.LastRender)) { Fail(e, TransportFault.Protocol); return; }
            var result = e.OutputClock.Convert(clock, at, out var observation);
            if (result != NativeClockResult.Valid) { Fail(e, result == NativeClockResult.Discontinuity ? TransportFault.Discontinuity : TransportFault.Clock); return; }
            e.LastRender = at;
            var floats = e.RenderScratch.AsSpan(0, frames * e.OutputChannels);
            int read = e.Pipe.Render(e.PipeGeneration, floats);
            e.ConsumerLedger.Add(read, at, observation);
            var monitor = Volatile.Read(ref e.Monitor);
            if (!monitor.Muted)
            {
                for (int i = 0; i < floats.Length; i++)
                {
                    float value = floats[i] * monitor.Level;
                    if (e.OutputEncoding == PcmEncoding.Pcm16LittleEndian)
                        BinaryPrimitives.WriteInt16LittleEndian(bytes.Slice(i * 2, 2), (short)Math.Clamp((int)MathF.Round(value * 32768), -32768, 32767));
                    else BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(i * 4, 4), BitConverter.SingleToInt32Bits(value));
                }
            }
            if (e.Ticket.IsValid && ReferenceEquals(Volatile.Read(ref active), e) && e.Backend.Submit(e.Ticket, bytes))
            {
                e.Submitted += frames; if (monitor.Muted) e.Muted += frames;
            }
            else bytes.Clear();
        }
        catch { bytes.Clear(); Fail(e, TransportFault.Backend); }
        finally { Volatile.Write(ref e.ConsumerBusy, 0); }
    }
    private TransportSnapshot Tick(long now)
    {
        var e = active;
        if (e is null || !e.Ticket.IsValid) return Snapshot;
        if (now <= e.ControlTime || now > e.Backend.ClosedThrough100ns) throw new ArgumentException("Intervalo ainda aberto ou tempo inválido; não existe média exata disponível.");
        // O marco simulado fecha os dois papéis. Receber depois um pacote com marca de tempo anterior ou igual a esse instante viola o protocolo.
        Volatile.Write(ref e.Cutoff, now);
        if (!e.ProducerLedger.Read(now, out var p) || !e.ConsumerLedger.Read(now, out var c))
        { Fail(e, TransportFault.Protocol); return StopCore(true); }
        UInt128 positive = (UInt128)(ulong)e.Primed * (ulong)(now - e.Origin) + p.Area;
        if (positive < c.Area) { Fail(e, TransportFault.Protocol); return StopCore(true); }
        UInt128 integral = positive - c.Area;
        if (integral < e.PreviousArea) { Fail(e, TransportFault.Protocol); return StopCore(true); }
        double mean = (double)(integral - e.PreviousArea) / (now - e.ControlTime);
        e.PreviousArea = integral; e.ControlTime = now; e.Mean = mean;
        if (ReadBuffer(e) is not { } buffer)
        {
            e.Mean = null; Fail(e, TransportFault.SnapshotUnavailable); return StopCore(true);
        }
        if (mean < 0 || mean > e.Capacity) { Fail(e, TransportFault.Protocol); return StopCore(true); }
        if (p.Observed && c.Observed)
        {
            var observation = e.Estimator.Observe(p.Clock, c.Clock, now);
            if (observation is not (ClockObservationResult.Waiting or ClockObservationResult.Updated) || e.Estimator.RequiresReset)
            { Fail(e, TransportFault.Clock); return StopCore(true); }
        }
        int occupancy = Math.Clamp((int)Math.Round(mean), 1, e.Capacity - 1);
        if (buffer.OverrunFrames > e.PreviousDrops) occupancy = e.Capacity;
        else if (buffer.UnderrunFrames > e.PreviousUnderruns) occupancy = 0;
        e.PreviousDrops = buffer.OverrunFrames; e.PreviousUnderruns = buffer.UnderrunFrames;
        var control = e.Controller.Update(now, occupancy, e.Estimator.Latest);
        e.ControlState = control.State;
        if (control.State == DriftControlState.ReprimeRequired) { Fail(e, TransportFault.Boundary); return StopCore(true); }
        Volatile.Write(ref e.Ratio, new(control.CorrectionPpm));
        return Publish(e, TransportState.Running);
    }
    private TransportSnapshot StopCore(bool faulted)
    {
        var e = active;
        if (e is null) return Snapshot;
        e.Ticket.Invalidate(); Publish(e, TransportState.Stopping);
        try { e.Backend.RequestStop(); } catch { Fail(e, TransportFault.Backend); faulted = true; }
        // Apenas no plano de controle. O callback não realiza espera bloqueante nem destrói recursos.
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (Volatile.Read(ref e.ProducerBusy) != 0 || Volatile.Read(ref e.ConsumerBusy) != 0)
        {
            if (timeout.ElapsedMilliseconds >= 1000)
            {
                Fail(e, TransportFault.QuiescenceTimeout); e.Retained = true;
                return Publish(e, TransportState.Faulted);
            }
            Thread.Sleep(1);
        }
        e.Adaptive.Stop(e.Generation); e.Pipe.Stop();
        e.Discarded = e.Pipe.Snapshot().Buffer?.QueuedFrames ?? 0;
        try { e.Backend.Dispose(); }
        catch { Fail(e, TransportFault.Backend); e.Retained = true; return Publish(e, TransportState.Faulted); }
        e.Retained = false;
        e.Cancellation.Dispose();
        var final = Publish(e, faulted || e.Fault != 0 ? TransportState.Faulted : TransportState.Closed);
        e.Pipe.Dispose(); e.Lease.Dispose(); Volatile.Write(ref active, null);
        return final;
    }
    private TransportSnapshot Publish(Epoch e, TransportState state)
    {
        var buffer = ReadBuffer(e);
        var prior = Snapshot;
        bool same = prior.Generation == e.Generation;
        var next = new TransportSnapshot(state, e.Generation, (TransportFault)Volatile.Read(ref e.Fault), e.Primed,
            buffer is { } b ? b.WrittenFrames - e.Primed : same ? prior.ProducedFrames : 0,
            buffer?.ReadFrames ?? (same ? prior.ReadFrames : 0), buffer?.OverrunFrames ?? (same ? prior.DroppedFrames : 0),
            buffer?.UnderrunFrames ?? (same ? prior.UnderrunFrames : 0), Volatile.Read(ref e.Submitted), Volatile.Read(ref e.Muted), e.Discarded,
            buffer is null ? null : e.Mean, Volatile.Read(ref e.Ratio).Ppm, Volatile.Read(ref e.AppliedPpm), e.ControlState,
            Environment.CurrentManagedThreadId, e.Retained, buffer.HasValue);
        Volatile.Write(ref snapshot, next); return next;
    }
    private FrameBufferSnapshot? ReadBuffer(Epoch e)
    {
        try { return readBuffer(e.Pipe); }
        catch { return null; } // Falha de diagnóstico indica indisponibilidade. Não se atribui um valor fictício à fila.
    }
    public async ValueTask DisposeAsync()
    {
        bool ownsDisposal;
        lock (posting) ownsDisposal = Interlocked.CompareExchange(ref disposing, 1, 0) == 0;
        if (!ownsDisposal)
        { await exited.Task.WaitAsync(TimeSpan.FromSeconds(3)); return; }
        Freeze();
        try
        {
            var final = await Post(() => StopCore(false), duringDisposal: true).WaitAsync(TimeSpan.FromSeconds(3));
            if (final.ResourcesRetained) throw new InvalidOperationException("Callbacks pendentes: recursos preservados; aguarde e solicite descarte novamente.");
            await Post(() => { shutdown = true; return Snapshot; }, duringDisposal: true).WaitAsync(TimeSpan.FromSeconds(3));
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(3));
            commands.Dispose();
        }
        catch { if (!shutdown) Volatile.Write(ref disposing, 0); throw; }
    }
    private sealed record RatioCommand(double Ppm);
    private sealed record MonitorCommand(bool Muted, float Level);
    private sealed class Epoch
    {
        public readonly TransportRoute Route;
        public readonly ISimulatedAudioTransport Backend;
        public readonly IDisposable Lease;
        public readonly DeliveryTicket Ticket;
        public readonly long Generation, Origin;
        public readonly int Capacity, Prime, InputChannels, OutputChannels, InputAlign, OutputAlign;
        public readonly PcmEncoding InputEncoding, OutputEncoding;
        public readonly PcmFloatFormat InputFormat;
        public readonly float[] Input, Output, RenderScratch;
        public readonly AdaptiveResampler Adaptive;
        public readonly SyntheticAudioPipeline Pipe;
        public readonly WasapiClockAdapter InputClock, OutputClock;
        public readonly TransportLedger ProducerLedger, ConsumerLedger;
        public readonly ClockRatioEstimator Estimator;
        public readonly ClockRatioController Controller;
        public RatioCommand Ratio = new(0);
        public MonitorCommand Monitor = new(true, .2f);
        public CancellationTokenRegistration Cancellation;
        public int ProducerBusy, ConsumerBusy, ProducerThread, ConsumerThread, Fault;
        public long PipeGeneration, Primed, Submitted, Muted, Discarded, LastCapture, LastRender, Cutoff, ControlTime, PreviousDrops, PreviousUnderruns;
        public UInt128 PreviousArea;
        public double? Mean;
        public double AppliedPpm;
        public DriftControlState ControlState;
        public bool Retained;
        public Epoch(TransportRoute route, ISimulatedAudioTransport backend, IDisposable lease, long origin)
        {
            Route = route; Backend = backend; Lease = lease; Ticket = new(route); Generation = route.Capture.Endpoint.Generation;
            Origin = ControlTime = LastCapture = LastRender = origin; Cutoff = origin - 1;
            Capacity = route.CapacityFrames; Prime = Capacity / 2;
            var input = route.Capture.Mix!; var output = route.Playback.Mix!;
            InputChannels = input.Channels; OutputChannels = output.Channels; InputAlign = input.BlockAlign; OutputAlign = output.BlockAlign;
            InputEncoding = input.Validate(); OutputEncoding = output.Validate();
            InputFormat = input.FloatFormat;
            Input = new float[4096 * InputChannels]; Output = new float[8192 * OutputChannels]; RenderScratch = new float[4096 * OutputChannels];
            var policy = InputChannels == OutputChannels ? ChannelPolicy.Preserve : InputChannels == 1 ? ChannelPolicy.DuplicateMono : ChannelPolicy.AverageStereo;
            Adaptive = new(input.FloatFormat, output.FloatFormat, policy, generation: Generation);
            Pipe = new(output.FloatFormat, output.FloatFormat, Capacity, 4096);
            InputClock = new(route.Capture.Endpoint, input.SampleRate); OutputClock = new(route.Playback.Endpoint, output.SampleRate);
            ProducerLedger = new(origin); ConsumerLedger = new(origin);
            Estimator = new(InputClock.Clock, OutputClock.Clock);
            Controller = new(input.SampleRate, output.SampleRate, Frequency, Capacity, Prime, 500); Controller.Reset(origin);
        }
    }
}
