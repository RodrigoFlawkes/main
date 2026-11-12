using System.Collections.Concurrent;
using WaveStudio.Audio;
using Xunit;

namespace WaveStudio.Tests;

public sealed class AudioWatchTests
{
    private sealed class Backend(Action signal) : IAudioWatchBackend
    {
        public string? NotificationError { get; init; }
        public int Opens;
        public int Closed;
        public int Reads;
        public int Disposals;
        public bool Fail;
        public double Value = 0.25;
        public readonly ConcurrentBag<int> Threads = [];
        public Action? AfterRead;
        public void Notify() => signal();
        public IAudioPeakSource OpenPeak(string id)
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            Opens++;
            return new Source(this);
        }
        private sealed class Source(Backend owner) : IAudioPeakSource
        {
            public double ReadPeak()
            {
                owner.Threads.Add(Environment.CurrentManagedThreadId);
                owner.Reads++;
                owner.AfterRead?.Invoke();
                if (owner.Fail) throw new InvalidOperationException("Falha transitória simulada.");
                return owner.Value;
            }
            public void Dispose() { owner.Threads.Add(Environment.CurrentManagedThreadId); owner.Closed++; }
        }
        public void Dispose() { Threads.Add(Environment.CurrentManagedThreadId); Disposals++; Notify(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void ValidPeakAndSilenceRemainDistinctFromUnavailable(double value)
    {
        var reading = PeakReading.FromScalar(value);
        Assert.Equal(value, reading.Scalar);
        Assert.Null(reading.Error);
        Assert.Null(PeakReading.Unavailable.Scalar);
        if (value == 0) Assert.Null(reading.Decibels);
        else Assert.True(double.IsFinite(reading.Decibels!.Value));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void InvalidPeakIsUnavailable(double value)
    {
        var reading = PeakReading.FromScalar(value);
        Assert.Null(reading.Scalar);
        Assert.Null(reading.Decibels);
        Assert.NotNull(reading.Error);
    }

    [Fact]
    public void DuplicateAndBurstNotificationsAreCoalescedWithMaximumDelay()
    {
        var changes = new DeviceChangeCoalescer();
        changes.Signal(0);
        changes.Signal(0);
        Assert.False(changes.TakeReady(100));
        changes.Signal(100);
        Assert.False(changes.TakeReady(299));
        Assert.True(changes.TakeReady(300));
        Assert.False(changes.TakeReady(1000));
        for (int now = 2000; now < 3000; now += 100) changes.Signal(now);
        Assert.True(changes.TakeReady(3000));
    }

    [Fact]
    public void PersistentMetersAreReusedAndSelectionInvalidatesOldSnapshot()
    {
        Backend? backend = null;
        using var engine = new AudioWatchEngine(signal => backend = new Backend(signal));
        var old = new AudioWatchSelection("a", "b", 1);
        var first = engine.Tick(0, old);
        for (int now = 100; now <= 1000; now += 100) engine.Tick(now, old);
        Assert.Equal(2, backend!.Opens);
        Assert.Equal(22, backend.Reads);
        var next = new AudioWatchSelection("c", null, 2);
        var current = engine.Tick(1100, next);
        Assert.NotEqual(first.Selection, current.Selection);
        Assert.Equal(next, current.Selection);
        Assert.Null(current.Capture.Scalar);
        Assert.Equal(3, backend.Opens);
        Assert.Equal(2, backend.Closed);
    }

    [Fact]
    public void RemovalReturnAndSubscriptionErrorHaveBoundedRecovery()
    {
        Backend? backend = null;
        int subscriptions = 0;
        using var engine = new AudioWatchEngine(signal =>
        {
            subscriptions++;
            return backend = new Backend(signal) { NotificationError = "Inscrição indisponível" };
        });
        var target = new AudioWatchSelection("a", null, 1);
        var initial = engine.Tick(0, target);
        Assert.Contains("5 s", initial.NotificationStatus);
        backend!.Fail = true;
        Assert.Null(engine.Tick(100, target).Playback.Scalar);
        backend.Fail = false;
        Assert.Null(engine.Tick(200, target).Playback.Scalar);
        backend.Notify();
        Assert.False(engine.Tick(300, target).RefreshRequested);
        Assert.True(engine.Tick(500, target).RefreshRequested);
        Assert.Equal(0.25, engine.Tick(600, target).Playback.Scalar);
        engine.Tick(4999, target);
        Assert.Equal(1, subscriptions);
        Assert.True(engine.Tick(5000, target).RefreshRequested);
        Assert.Equal(2, subscriptions);
    }

    [Fact]
    public void FactoryFailureResumeAndHiddenMetersAreRecoverable()
    {
        bool fail = true;
        Backend? backend = null;
        using var engine = new AudioWatchEngine(signal => fail ? throw new InvalidOperationException("Serviço parado") : backend = new Backend(signal));
        var target = new AudioWatchSelection("a", null, 1);
        Assert.Null(engine.Tick(0, target).Playback.Scalar);
        fail = false;
        Assert.Equal(0.25, engine.Tick(100, target, restart: true).Playback.Scalar);
        int reads = backend!.Reads;
        Assert.Null(engine.Tick(200, target, metering: false).Playback.Scalar);
        Assert.Equal(reads, backend.Reads);
        Assert.Equal(0.25, engine.Tick(300, target).Playback.Scalar);
    }

    [Fact]
    public void ShutdownIsIdempotentAndCallbackAfterShutdownDoesNotAccessCom()
    {
        Backend? backend = null;
        var engine = new AudioWatchEngine(signal => backend = new Backend(signal));
        var target = new AudioWatchSelection("a", "b", 1);
        engine.Tick(0, target);
        engine.Dispose();
        backend!.Notify();
        engine.Dispose();
        Assert.Equal(2, backend.Closed);
        Assert.Equal(1, backend.Disposals);
        Assert.Throws<ObjectDisposedException>(() => engine.Tick(100, target));
    }

    [Fact]
    public async Task WorkerOwnsNativeOperationsAndWaitsForPendingReadAtShutdown()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Backend? backend = null;
        await using var worker = new AudioWatchWorker(signal => backend = new Backend(signal)
        {
            AfterRead = () =>
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
        });
        worker.Select("a", null);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var disposal = worker.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            release.Set();
            await disposal;
            await worker.DisposeAsync();
            Assert.Single(backend!.Threads.Distinct());
            Assert.Equal(backend.Opens, backend.Closed);
            Assert.Equal(1, backend.Disposals);
            Assert.Null(worker.ShutdownError);
        }
        finally { release.Set(); }
    }
}
