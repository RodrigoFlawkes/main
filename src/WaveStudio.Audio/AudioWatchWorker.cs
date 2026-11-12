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
    private long epoch;
    private string? terminalError;
    private bool pendingRefresh;
    private bool pendingReset;
    public event Action<bool>? CatalogChanged;
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
            epoch++;
            PublishUnavailable(terminalError ?? "Consultando medidores do dispositivo selecionado…", terminalError is not null);
            wake.Set();
            return selection;
        }
    }

    public void Recover()
    {
        lock (sync)
        {
            if (stopping) return;
            epoch++;
            PublishUnavailable(terminalError ?? "Recuperando medidores e dispositivos…", terminalError is not null);
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
                long requestedEpoch;
                lock (sync)
                {
                    if (stopping) break;
                    target = selection;
                    recover = restart;
                    restart = false;
                    measure = metering;
                    requestedEpoch = epoch;
                }
                var snapshot = engine.Tick(clock.ElapsedMilliseconds, target, recover, measure);
                lock (sync)
                {
                    if (stopping) break;
                    if (epoch != requestedEpoch || target != selection)
                    {
                        pendingRefresh |= snapshot.RefreshRequested;
                        pendingReset |= snapshot.ResetControls;
                        continue;
                    }
                    snapshot = snapshot with { RefreshRequested = snapshot.RefreshRequested || pendingRefresh,
                        ResetControls = snapshot.ResetControls || pendingReset };
                    pendingRefresh = pendingReset = false;
                    Volatile.Write(ref latest, snapshot);
                }
                if (snapshot.RefreshRequested)
                {
                    try { CatalogChanged?.Invoke(snapshot.ResetControls); }
                    catch (Exception error) { ShutdownError = EndpointVolumeOperations.Describe(error); }
                }
                wake.WaitOne(100);
            }
        }
        catch (Exception error)
        {
            ShutdownError = EndpointVolumeOperations.Describe(error);
            lock (sync)
            {
                terminalError = "O serviço de medição parou. Reinicie o Wave Studio. " + ShutdownError;
                PublishUnavailable(terminalError, failed: true);
            }
        }
        finally
        {
            engine.Dispose();
            ShutdownError ??= engine.ShutdownError;
            completion.TrySetResult();
        }
    }

    private void PublishUnavailable(string message, bool failed) => Volatile.Write(ref latest,
        new AudioWatchSnapshot(selection, PeakReading.Unavailable, PeakReading.Unavailable, message, false, 0,
            RecordedAt: Stopwatch.GetTimestamp(), WorkerFailed: failed));

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
