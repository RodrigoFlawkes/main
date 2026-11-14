using System.Diagnostics;
using WaveStudio.Audio.Dsp;

namespace WaveStudio.Audio;

public sealed class MicrophoneTestSession : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly Queue<(long Revision, Action<MicrophoneTestEngine> Run, TaskCompletionSource Done)> commands = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MicrophoneTestEngine engine;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private long revision;
    private long cancellation;
    private bool stopping;
    private MicrophoneTestSnapshot latest;
    public MicrophoneTestSnapshot Latest => Volatile.Read(ref latest);

    public MicrophoneTestSession() : this(new CoreAudioMicrophoneTestBackend()) { }
    internal MicrophoneTestSession(IMicrophoneTestBackend backend)
    {
        engine = new MicrophoneTestEngine(backend, value => value == Volatile.Read(ref revision));
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
                    catch (Exception error) { next.Done.TrySetException(error); }
                }
                if (end && !shutdownStarted)
                {
                    shutdownStarted = true;
                    engine.Clear("Encerrando e descartando o teste…");
                }
                engine.Tick(clock.Elapsed);
                Publish();
                if (end && !engine.Busy)
                {
                    engine.MarkDisposed();
                    Publish();
                    break;
                }
                if (command is null) wake.WaitOne(engine.Busy ? 25 : Timeout.Infinite);
            }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
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

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (!stopping)
            {
                stopping = true;
                Interlocked.Increment(ref revision);
                wake.Set();
            }
        }
        return new ValueTask(completion.Task);
    }
}
