using System.Diagnostics;

namespace WaveStudio.Audio;

public sealed class AudioWatchWorker : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<Action, IAudioWatchBackend> factory;
    private AudioWatchSelection selection = new(null, null, 0);
    private AudioWatchSnapshot? latest;
    private bool stopping;
    private bool restart;
    private bool metering = true;
    public event Action? CatalogChanged;
    public AudioWatchSnapshot? Latest => Volatile.Read(ref latest);
    public string? ShutdownError { get; private set; }

    public AudioWatchWorker() : this(signal => new CoreAudioWatchBackend(signal)) { }
    public AudioWatchWorker(Func<Action, IAudioWatchBackend> factory)
    {
        this.factory = factory;
        var thread = new Thread(Run) { IsBackground = true, Name = "Wave Studio — medição e dispositivos" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    public AudioWatchSelection Select(string? playbackId, string? captureId)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            selection = new AudioWatchSelection(playbackId, captureId, selection.Revision + 1);
            wake.Set();
            return selection;
        }
    }

    public void Recover()
    {
        lock (sync)
        {
            if (stopping) return;
            restart = true;
            wake.Set();
        }
    }

    public void EnableMetering(bool enabled)
    {
        lock (sync)
        {
            if (stopping) return;
            metering = enabled;
            wake.Set();
        }
    }

    private void Run()
    {
        var engine = new AudioWatchEngine(factory);
        var clock = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                AudioWatchSelection target;
                bool recover;
                bool measure;
                lock (sync)
                {
                    if (stopping) break;
                    target = selection;
                    recover = restart;
                    restart = false;
                    measure = metering;
                }
                var snapshot = engine.Tick(clock.ElapsedMilliseconds, target, recover, measure);
                Volatile.Write(ref latest, snapshot);
                if (snapshot.RefreshRequested)
                {
                    try { CatalogChanged?.Invoke(); }
                    catch (Exception error) { ShutdownError = EndpointVolumeOperations.Describe(error); }
                }
                wake.WaitOne(100);
            }
        }
        catch (Exception error) { ShutdownError = EndpointVolumeOperations.Describe(error); }
        finally
        {
            engine.Dispose();
            ShutdownError ??= engine.ShutdownError;
            completion.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (!stopping) { stopping = true; wake.Set(); }
        }
        await completion.Task.ConfigureAwait(false);
        // WaitHandle is kept until all concurrent disposers have stopped signaling it.
        lock (sync) wake.Dispose();
    }
}
