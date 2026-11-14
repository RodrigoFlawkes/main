using System.Diagnostics;
using WaveStudio.Audio.Dsp;
using WaveStudio.Audio.Engine;

namespace WaveStudio.Audio;

public sealed class MicrophoneTestSession : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly Queue<(long Revision, Action<MicrophoneTestEngine> Run, TaskCompletionSource Done)> commands = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MicrophoneTestEngine engine;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly TimeSpan shutdownTimeout;
    private long revision;
    private long cancellation;
    private bool stopping;
    private MicrophoneTestSnapshot latest;
    public MicrophoneTestSnapshot Latest
    {
        get
        {
            var snapshot = Volatile.Read(ref latest);
            return Volatile.Read(ref stopping) && !completion.Task.IsCompleted
                ? snapshot with { State = MicrophoneTestState.Stopping, CanRecord = false, CanPlay = false,
                    ShutdownPending = true, ResourcesRetained = true,
                    Message = "Encerramento ainda não confirmado; recursos preservados. Aguarde e tente fechar novamente. " + snapshot.Message }
                : snapshot;
        }
    }

    public MicrophoneTestSession() : this(new CoreAudioMicrophoneTestBackend(), activityGuard: TransportActivityGuard.Shared) { }
    internal MicrophoneTestSession(IMicrophoneTestBackend backend, TimeSpan? shutdownTimeout = null, TransportActivityGuard? activityGuard = null)
    {
        this.shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(3);
        if (this.shutdownTimeout <= TimeSpan.Zero || this.shutdownTimeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
        engine = new MicrophoneTestEngine(backend, value => value == Volatile.Read(ref revision), activityGuard);
        latest = engine.Snapshot;
        var thread = new Thread(Run) { IsBackground = true, Name = "Wave Studio — teste local de microfone" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    public Task SelectAsync(string? captureId, string? playbackId)
    {
        lock (sync)
        {
            if (stopping) return Task.CompletedTask;
            long next = Interlocked.Increment(ref revision); // Immediately suppress old packets/providers.
            return Enqueue(next, target => target.Select(captureId, playbackId, next));
        }
    }
    public Task RecordAsync() => Submit((target, value, requested) => target.Record(value, clock.Elapsed, requested));
    public Task PlayAsync(float digitalLevel) => Submit((target, value, requested) => target.Play(value, digitalLevel, clock.Elapsed, requested));
    public Task PlayEqualizedAsync(float digitalLevel, GraphicEqualizerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Submit((target, value, requested) => target.Play(value, digitalLevel, clock.Elapsed, requested, settings));
    }
    public Task StopAsync() => Submit((target, _, _) => target.Stop(), cancel: true);
    public Task ClearAsync() => Submit((target, _, _) => target.Clear(), cancel: true);

    private Task Submit(Action<MicrophoneTestEngine, long, Func<bool>> action, bool cancel = false)
    {
        lock (sync)
        {
            if (stopping) return Task.CompletedTask;
            long value = revision;
            if (cancel) Interlocked.Increment(ref cancellation);
            long operation = cancellation;
            return Enqueue(value, target => action(target, value, () => operation == Volatile.Read(ref cancellation)));
        }
    }
    private Task Enqueue(long value, Action<MicrophoneTestEngine> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Enqueue((value, action, done));
        wake.Set();
        return done.Task;
    }

    private void Run()
    {
        try
        {
            bool shutdownStarted = false;
            while (true)
            {
                (long Revision, Action<MicrophoneTestEngine> Run, TaskCompletionSource Done)? command;
                bool end;
                lock (sync)
                {
                    end = stopping;
                    command = commands.Count > 0 ? commands.Dequeue() : null;
                }
                if (command is { } next)
                {
                    try
                    {
                        if (!end && next.Revision == Volatile.Read(ref revision)) next.Run(engine);
                        Publish();
                        next.Done.TrySetResult();
                    }
                    catch (Exception error)
                    {
                        if (error is not ArgumentException) { Interlocked.Increment(ref cancellation); engine.ReportControlFailure(error); }
                        Publish(); next.Done.TrySetException(error);
                    }
                }
                if (end && !shutdownStarted)
                {
                    shutdownStarted = true;
                    engine.Clear("Encerrando e descartando o teste…");
                }
                try { engine.Tick(clock.Elapsed); }
                catch (Exception error)
                {
                    Interlocked.Increment(ref cancellation);
                    engine.ReportControlFailure(error);
                }
                Publish();
                if (end && !engine.Busy)
                {
                    engine.MarkDisposed();
                    Publish();
                    break;
                }
                if (command is null) wake.WaitOne(engine.Busy ? (end ? 100 : 25) : Timeout.Infinite);
            }
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            Interlocked.Increment(ref revision);
            Volatile.Write(ref latest, latest with { State = MicrophoneTestState.Error, CanRecord = false, CanPlay = false,
                ResourcesRetained = true, ShutdownPending = true, PrimaryError = error.GetType().Name,
                Message = "O worker falhou; encerramento não confirmado. Recursos preservados. " + error.GetType().Name });
            completion.TrySetException(error);
        }
        finally
        {
            lock (sync)
            {
                stopping = true;
                while (commands.TryDequeue(out var pending)) pending.Done.TrySetResult();
                wake.Dispose();
            }
        }
    }
    private void Publish() => Volatile.Write(ref latest, engine.Snapshot);

    public async ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (!stopping)
            {
                stopping = true;
                Interlocked.Increment(ref revision);
                Interlocked.Increment(ref cancellation);
                wake.Set();
            }
            else if (!completion.Task.IsCompleted) wake.Set();
        }
        await completion.Task.WaitAsync(shutdownTimeout).ConfigureAwait(false);
    }
}
