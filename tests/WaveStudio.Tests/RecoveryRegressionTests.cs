using System.Diagnostics;
using WaveStudio.Audio;
using Xunit;

namespace WaveStudio.Tests;

public sealed class RecoveryRegressionTests
{
    private sealed class Backend(Action signal) : IAudioWatchBackend
    {
        public bool FailStatus;
        public string? NotificationError => FailStatus ? throw new InvalidOperationException("Falha terminal simulada.") : null;
        public long RecoveryRevision { get; private set; }
        public Action? OnRead;
        public bool Disposed;
        public void Notify(bool recovery)
        {
            if (recovery) RecoveryRevision++;
            signal();
        }
        public IAudioPeakSource OpenPeak(string endpointId) => new Source(this);
        private sealed class Source(Backend owner) : IAudioPeakSource
        {
            public double ReadPeak() { owner.OnRead?.Invoke(); return 0.5; }
            public void Dispose() { }
        }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void MetadataPreservesControlsButLifecycleAndResumeInvalidateSameId()
    {
        Backend? backend = null;
        using var engine = new AudioWatchEngine(signal => backend = new Backend(signal));
        var selection = new AudioWatchSelection("same", "mic", 1);
        engine.Tick(0, selection);
        backend!.Notify(recovery: false);
        engine.Tick(100, selection);
        var metadata = engine.Tick(300, selection);
        Assert.True(metadata.RefreshRequested);
        Assert.False(metadata.ResetControls);
        backend.Notify(recovery: true);
        backend.Notify(recovery: true);
        engine.Tick(400, selection);
        var returned = engine.Tick(600, selection);
        Assert.True(returned.ResetControls);
        Assert.Equal(selection, returned.Selection);
        Assert.False(engine.Tick(700, selection).ResetControls);
        Assert.True(engine.Tick(800, selection, restart: true).ResetControls);
    }

    [Fact]
    public async Task TerminalFailurePublishesUnavailableAndClosesWithoutBusyRestart()
    {
        Backend? backend = null;
        await using var worker = new AudioWatchWorker(signal => backend = new Backend(signal)
        {
            OnRead = () => backend!.FailStatus = true
        });
        worker.Select("out", null);
        await Until(() => worker.Latest?.WorkerFailed == true);
        var failed = worker.Latest!;
        Assert.Null(failed.Playback.Scalar);
        Assert.Null(failed.Capture.Scalar);
        Assert.Contains("Reinicie", failed.NotificationStatus);
        worker.Recover();
        Assert.True(worker.Latest!.WorkerFailed);
        await worker.DisposeAsync();
        Assert.True(backend!.Disposed);
        Assert.NotNull(worker.ShutdownError);
    }

    [Fact]
    public void StaleSnapshotCannotDisplayPreviousPeakAsCurrent()
    {
        long timestamp = Stopwatch.GetTimestamp();
        var snapshot = new AudioWatchSnapshot(new("out", "mic", 1), PeakReading.FromScalar(0.9), PeakReading.FromScalar(0.2),
            "ativa", false, 1, RecordedAt: timestamp);
        Assert.Equal(0.9, snapshot.ForDisplay(timestamp).Playback.Scalar);
        var stale = snapshot.ForDisplay(timestamp + Stopwatch.Frequency * 3);
        Assert.Null(stale.Playback.Scalar);
        Assert.Null(stale.Capture.Scalar);
        Assert.Contains("atrasada", stale.NotificationStatus);
    }

    [Fact]
    public async Task ResumeInvalidatesPendingReadEvenWhenEndpointIdDoesNotChange()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int factories = 0;
        await using var worker = new AudioWatchWorker(signal =>
        {
            int instance = Interlocked.Increment(ref factories);
            return new Backend(signal)
            {
                OnRead = () =>
                {
                    if (instance != 1) return;
                    entered.TrySetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                }
            };
        });
        worker.Select("same", null);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            worker.Recover();
            Assert.Null(worker.Latest!.Playback.Scalar);
            release.Set();
            await Until(() => Volatile.Read(ref factories) >= 2 && worker.Latest?.Playback.Scalar == 0.5);
            Assert.Equal("same", worker.Latest!.Selection.PlaybackId);
        }
        finally { release.Set(); }
    }

    private static async Task Until(Func<bool> predicate)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
