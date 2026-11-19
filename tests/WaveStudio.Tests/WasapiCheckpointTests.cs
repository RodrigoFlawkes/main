using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiCheckpointTests
{
    [Fact]
    public void PendingOwnerCannotProduceACoherentCut()
    {
        var fixture = new Fixture();
        fixture.Initialize();
        fixture.Ledger.Capture.Begin();
        fixture.Now += 10;

        var rejected = fixture.Ledger.Read(fixture.Generation);

        Assert.Equal(WasapiCheckpointStatus.OwnerBusy, rejected.Status);
        Assert.Null(rejected.Checkpoint);
    }

    [Fact]
    public void InterleavedEventsProduceExactOccupancyAndBoundedMean()
    {
        var fixture = new Fixture();
        fixture.Initialize();
        var baseline = fixture.Take();

        fixture.Now = 110;
        fixture.Ledger.Capture.Begin();
        fixture.Now = 112;
        fixture.Ledger.Capture.Complete(new(1, 1, 8, 8, 1, 8, 0, 0, 8, 0, 0));
        fixture.Now = 120;
        fixture.Ledger.Render.Begin();
        fixture.Now = 123;
        fixture.Ledger.Render.Complete(new(1, 1, 6, 6, 0, 0, 0, 0, 6, 0, 0));
        fixture.Now = 130;
        var cut = fixture.Take(baseline);

        Assert.Equal(2, cut.QueuedFrames);
        // 8 frames entram entre 110 e 112. Seis saem entre 120 e 123.
        Assert.Equal((Int128)84, cut.Mean!.Value.MinimumFrameTicks);
        Assert.Equal((Int128)118, cut.Mean.Value.MaximumFrameTicks);
        Assert.Equal(30, cut.Mean.Value.Duration100ns);
    }


    [Fact]
    public void ExactEventTimesMatchReferenceArea()
    {
        var fixture = new Fixture();
        fixture.Initialize();
        var baseline = fixture.Take();
        fixture.Now = 102;
        fixture.Write(4);
        fixture.Now = 105;
        fixture.Consume(2);
        fixture.Now = 111;
        fixture.Write(7);
        fixture.Now = 114;
        var cut = fixture.Take(baseline);

        Assert.Equal(9, cut.QueuedFrames);
        Assert.Equal(new WasapiOccupancyBounds(51, 51, 14), cut.Mean);
        Assert.Equal(0, baseline.QueuedFrames);
        Assert.Equal(0, baseline.Capture.Sequence);
    }

    [Fact]
    public void SimultaneousOwnersRetainConservativeBounds()
    {
        var fixture = new Fixture();
        fixture.Initialize();
        var baseline = fixture.Take();
        fixture.Now = 105;
        fixture.Ledger.Capture.Begin();
        fixture.Ledger.Render.Begin();
        fixture.Now = 115;
        fixture.Ledger.Render.Complete(new(1, 1, 3, 3, 0, 0, 0, 0, 3, 0, 0));
        Assert.Equal(WasapiCheckpointStatus.OwnerBusy, fixture.Ledger.Read(fixture.Generation, baseline).Status);
        fixture.Ledger.Capture.Complete(new(1, 1, 5, 5, 1, 5, 0, 0, 5, 0, 0));
        fixture.Now = 120;
        var cut = fixture.Take(baseline);

        Assert.Equal(2, cut.QueuedFrames);
        Assert.Equal(new WasapiOccupancyBounds(0, 60, 20), cut.Mean);
        Assert.Equal(115, cut.Capture.Completed100ns);
        Assert.Equal(115, cut.Render.Completed100ns);
    }

    [Fact]
    public void OperationStartingDuringReadInvalidatesThatCut()
    {
        var fixture = new Fixture();
        fixture.Initialize();
        fixture.OnTimeRead = () =>
        {
            fixture.OnTimeRead = null;
            fixture.Ledger.Capture.Begin();
        };
        var result = fixture.Ledger.Read(fixture.Generation);
        Assert.Equal(WasapiCheckpointStatus.OwnerBusy, result.Status);
        Assert.Null(result.Checkpoint);

        fixture.Ledger.Capture.Complete(new(1, 1, 5, 5, 1, 5, 0, 0, 5, 0, 0));
        Assert.Equal(5, fixture.Take().QueuedFrames);
    }

    [Fact]
    public void RingWrapRetainsRecentIntervalButRejectsLostHistory()
    {
        var fixture = new Fixture(history: 2);
        fixture.Initialize();
        var old = fixture.Take();
        fixture.Now = 101;
        fixture.Write(1);
        var recent = fixture.Take();
        fixture.Now = 102;
        fixture.Write(1);
        fixture.Now = 103;
        fixture.Write(1);
        fixture.Now = 104;

        var rejected = fixture.Ledger.Read(fixture.Generation, old);
        Assert.Equal(WasapiCheckpointStatus.HistoryLost, rejected.Status);
        Assert.Null(rejected.Checkpoint);
        var cut = fixture.Take(recent);
        Assert.Equal(3, cut.Capture.Sequence);
        Assert.Equal(new WasapiOccupancyBounds(6, 6, 3), cut.Mean);
        Assert.Null(fixture.Take().Mean);
    }

    [Fact]
    public void MissingAndDelayedAcknowledgmentNeverReturnsAPartialCut()
    {
        var fixture = new Fixture();
        Assert.Equal(WasapiCheckpointStatus.OwnerUnconfirmed, fixture.Ledger.Read(fixture.Generation).Status);
        fixture.Initialize();
        fixture.Ledger.Capture.Begin();
        fixture.Now += WasapiCheckpointLedger.MaxAcknowledgmentAge100ns + 1;
        fixture.Ledger.Render.Begin();
        fixture.Ledger.Render.Complete(default);

        var result = fixture.Ledger.Read(fixture.Generation);
        Assert.Equal(WasapiCheckpointStatus.OwnerStale, result.Status);
        Assert.Null(result.Checkpoint);
    }

    [Fact]
    public void FutureConfirmationAndRegressiveOwnerTimeAreRejected()
    {
        var fixture = new Fixture();
        fixture.Initialize();
        fixture.Now = 110;
        fixture.Write(1);
        fixture.Now = 109;
        Assert.Equal(WasapiCheckpointStatus.InvalidTime, fixture.Ledger.Read(fixture.Generation).Status);
        fixture.Ledger.Capture.Begin();
        Assert.Equal(WasapiCheckpointStatus.InvalidTime, fixture.Ledger.Capture.Complete(default));
        Assert.Null(fixture.Ledger.Read(fixture.Generation).Checkpoint);
    }

    [Fact]
    public void WrongGenerationForeignBaselineAndRevokedTicketCannotUseAPreviousCut()
    {
        var fixture = new Fixture();
        fixture.Initialize();
        var previous = fixture.Take();
        var other = new Fixture();
        other.Initialize();
        other.Now++;
        Assert.Equal(WasapiCheckpointStatus.InvalidBaseline, other.Ledger.Read(other.Generation, previous).Status);
        Assert.Equal(WasapiCheckpointStatus.InvalidBaseline, fixture.Ledger.Read(fixture.Generation, previous).Status);
        Assert.Equal(WasapiCheckpointStatus.WrongGeneration, fixture.Ledger.Read(fixture.Generation + 1).Status);
        fixture.OnTimeRead = fixture.Ticket.Invalidate;
        var rejected = fixture.Ledger.Read(fixture.Generation);
        Assert.Equal(WasapiCheckpointStatus.Inactive, rejected.Status);
        Assert.Null(rejected.Checkpoint);
    }

    [Fact]
    public void DuplicateBeginAndUnconfirmedReleaseCannotAuthorizeACut()
    {
        var fixture = new Fixture();
        fixture.Initialize();
        fixture.Ledger.Capture.Begin();
        fixture.Ledger.Capture.Begin();
        Assert.Equal(WasapiCheckpointStatus.InvalidSequence, fixture.Ledger.Capture.Complete(default));
        Assert.Equal(WasapiCheckpointStatus.InvalidSequence, fixture.Ledger.Read(fixture.Generation).Status);

        var other = new Fixture();
        other.Initialize();
        other.Ledger.Render.Begin();
        Assert.Equal(WasapiCheckpointStatus.OwnerFault, other.Ledger.Render.Abort());
        Assert.Null(other.Ledger.Read(other.Generation).Checkpoint);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InconsistentCountsAndImpossibleQueueAreRejected(bool mismatch)
    {
        var fixture = new Fixture(capacity: 4);
        fixture.Initialize();
        fixture.Ledger.Capture.Begin();
        var status = fixture.Ledger.Capture.Complete(new(1, mismatch ? 0 : 1, 5, 5, 1, 5, 0, 0, 5, 0, 0));
        Assert.Equal(mismatch ? WasapiCheckpointStatus.InvalidAccounting : WasapiCheckpointStatus.Available, status);
        Assert.Equal(WasapiCheckpointStatus.InvalidAccounting, fixture.Ledger.Read(fixture.Generation).Status);
    }

    [Fact]
    public void AttemptedFrameOverflowIsDetectedBeforePublication()
    {
        var fixture = new Fixture();
        fixture.Initialize();
        fixture.Ledger.Capture.Begin();
        var status = fixture.Ledger.Capture.Complete(new(1, 1, 1, 1, 1, 1, 0, 0, long.MaxValue, 1, 0));

        Assert.Equal(WasapiCheckpointStatus.Overflow, status);
        Assert.Equal(WasapiCheckpointStatus.Overflow, fixture.Ledger.Read(fixture.Generation).Status);
    }

    [Fact]
    public Task PublicationUsesBoundedPreallocatedMemoryAndNoRecurringAllocations() =>
        AllocationIsolation.Run<WasapiCheckpointTests>(MeasureAllocation, TestContext.Current.CancellationToken);

    private static void MeasureAllocation()
    {
        var warm = new Fixture(history: WasapiCheckpointLedger.MaxHistory);
        warm.Initialize();
        for (int i = 0; i < 20_000; i++) PublishPair(warm);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var fixture = new Fixture(history: WasapiCheckpointLedger.MaxHistory);
        long setup = GC.GetAllocatedBytesForCurrentThread() - before;
        fixture.Initialize();

        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++) PublishPair(fixture);
        long allocations = GC.GetAllocatedBytesForCurrentThread() - before;
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);

        Assert.InRange(setup, 16_384, 20_480);
        Assert.Equal(0, allocations);
        Assert.True(elapsed < TimeSpan.FromSeconds(5));
        var result = fixture.Take();
        Assert.Equal(100_000, result.Capture.Sequence);
        Assert.Equal(100_000, result.Render.Sequence);
        Assert.Equal(0, result.QueuedFrames);
        TestContext.Current.SendDiagnosticMessage("Diário sintético: {0} bytes na criação da fixture, {1} publicações em {2:F2} ms, {3} bytes recorrentes.",
            setup, 200_000, elapsed.TotalMilliseconds, allocations);
    }

    private static void PublishPair(Fixture fixture)
    {
        fixture.Now++;
        fixture.Write(1);
        fixture.Consume(1);
    }

    [Fact]
    public async Task FakeOwnersReportNativeFramesAndActualOutputFifoWithoutChangingSilentRender()
    {
        var route = WasapiContractTests.Route(inputRate: 44_100, outputRate: 48_000);
        long generation = route.Capture.Endpoint.Generation;
        var calls = new WasapiSilentTests.Calls(route);
        calls.Input.PacketFrames = calls.Input.Capacity;
        calls.Input.PacketLimit = 7;
        calls.Input.StartupFlags = 3;
        calls.Output.PaddingAfterStart = calls.Output.Capacity;
        int drain = 0;
        calls.Output.OnStep = step =>
        {
            if (step == "Padding" && Volatile.Read(ref drain) != 0) calls.Output.PaddingAfterStart = 0;
        };

        var guard = new TransportActivityGuard();
        await using var session = new WasapiSilentSession(route, guard, calls.Open, observeCheckpoints: true);
        var run = session.RunAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var full = await WaitForCut(session, generation, cut => cut.Capture.Counts.PacketsAccepted == 6);
        Assert.Equal(7L * calls.Input.Capacity, full.Capture.Counts.FramesReleased);
        Assert.Equal(calls.Input.Capacity, full.Capture.Counts.StartupDiscardedFrames);
        Assert.Equal(calls.Output.Capacity, full.Render.Counts.PrimedFrames);
        Assert.Equal(0, full.Render.Counts.FifoFrames);
        Assert.True(full.Capture.Counts.FifoAttemptedFrames > full.Capture.Counts.FramesAccepted);
        Assert.True(full.Capture.Counts.FifoMissingFrames > 0);
        Assert.Equal(full.Capture.Counts.FifoFrames, full.QueuedFrames);

        Volatile.Write(ref drain, 1);
        var empty = await WaitForCut(session, generation, cut => cut.Render.Counts.FifoMissingFrames > 0, full);
        Assert.Equal(0, empty.QueuedFrames);
        Assert.Equal(full.QueuedFrames, empty.Render.Counts.FifoFrames);
        Assert.Equal(empty.Render.Counts.FramesReleased,
            empty.Render.Counts.FifoAttemptedFrames + empty.Render.Counts.PrimedFrames + empty.Render.Counts.PreTicketRenderFrames);
        Assert.NotNull(empty.Mean);
        session.RequestStop();
        var stopped = await run;

        Assert.True(stopped.Completed);
        Assert.Null(stopped.Failure);
        Assert.True(stopped.AlwaysSilent);
        Assert.False(stopped.DriftControlEnabled);
        Assert.Equal(WasapiCheckpointStatus.Inactive, session.ReadCheckpoint(generation, empty).Status);
        using var lease = guard.TryAcquire(AudioActivity.NativePreparation);
        Assert.NotNull(lease);
    }

    [Fact]
    public void InstrumentationIsDisabledByDefaultAndRequiresAnInjectedBackend()
    {
        var route = WasapiContractTests.Route();
        var calls = new WasapiSilentTests.Calls(route);
        var session = new WasapiSilentSession(route, new(), calls.Open);
        Assert.Equal(WasapiCheckpointStatus.Disabled, session.ReadCheckpoint(route.Capture.Endpoint.Generation).Status);
        Assert.Throws<ArgumentException>(() => new WasapiSilentSession(route, observeCheckpoints: true));
    }


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetFailureInEitherOwnerStopsWithoutPublishingATerminalCut(bool capture)
    {
        var route = WasapiContractTests.Route();
        long generation = route.Capture.Endpoint.Generation;
        var calls = new WasapiSilentTests.Calls(route);
        var guard = new TransportActivityGuard();
        if (capture) calls.Input.FailStep = "GetCapture"; else calls.Output.FailStep = "GetRender";
        await using var session = new WasapiSilentSession(route, guard, calls.Open, observeCheckpoints: true);

        var result = await session.RunAsync(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);

        Assert.Equal(capture ? "Capture/GetCapture" : "Render/GetRender", result.Failure);
        Assert.Equal(0, (capture ? result.Capture : result.Playback).PacketsAcquired);
        Assert.True(result.Completed);
        Assert.False(result.ResourcesRetained);
        Assert.Equal(WasapiCheckpointStatus.Inactive, session.ReadCheckpoint(generation).Status);
        using var lease = guard.TryAcquire(AudioActivity.NativePreparation);
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task StaleOwnerInvalidatesTicketAndRetainsGuardUntilQuiescence()
    {
        var route = WasapiContractTests.Route();
        long generation = route.Capture.Endpoint.Generation;
        var calls = new WasapiSilentTests.Calls(route);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        calls.Input.OnStep = step =>
        {
            if (step != "ReleaseCapture") return;
            entered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(8)));
        };
        var guard = new TransportActivityGuard();
        var token = TestContext.Current.CancellationToken;
        var session = new WasapiSilentSession(route, guard, calls.Open, TimeSpan.FromSeconds(1), observeCheckpoints: true);
        try
        {
            var run = session.RunAsync(TimeSpan.FromSeconds(5), token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            WasapiCheckpointResult status;
            do
            {
                status = session.ReadCheckpoint(generation);
                if (status.Status == WasapiCheckpointStatus.OwnerBusy) await Task.Delay(1, timeout.Token);
            } while (status.Status == WasapiCheckpointStatus.OwnerBusy);

            Assert.Equal(WasapiCheckpointStatus.OwnerStale, status.Status);
            Assert.Null(status.Checkpoint);
            var partial = await run;
            Assert.Equal("Checkpoint/OwnerStale", partial.Failure);
            Assert.False(partial.Completed);
            Assert.True(partial.ResourcesRetained);
            Assert.True(partial.Capture.BufferInFlight);
            Assert.Null(guard.TryAcquire(AudioActivity.NativePreparation));
            Assert.Equal(WasapiCheckpointStatus.Inactive, session.ReadCheckpoint(generation).Status);
        }
        finally
        {
            release.Set();
            await session.DisposeAsync();
        }

        Assert.Single(calls.Input.CaptureReleases);
        using var lease = guard.TryAcquire(AudioActivity.NativePreparation);
        Assert.NotNull(lease);
    }

    private static async Task<WasapiCheckpoint> WaitForCut(WasapiSilentSession session, long generation,
        Func<WasapiCheckpoint, bool> ready, WasapiCheckpoint? baseline = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        while (true)
        {
            var attempt = session.ReadCheckpoint(generation, baseline);
            if (attempt.Checkpoint is { } cut && ready(cut)) return cut;
            Assert.Contains(attempt.Status, new[]
            {
                WasapiCheckpointStatus.Available, WasapiCheckpointStatus.Inactive,
                WasapiCheckpointStatus.OwnerUnconfirmed, WasapiCheckpointStatus.OwnerBusy
            });
            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class Fixture
    {
        public long Now = 100;
        public Action? OnTimeRead;
        public DeliveryTicket Ticket { get; }
        public long Generation { get; }
        public WasapiCheckpointLedger Ledger { get; }

        public Fixture(int history = 8, int capacity = 16)
        {
            var route = WasapiContractTests.Route();
            Generation = route.Capture.Endpoint.Generation;
            Ticket = new DeliveryTicket(route);
            Ticket.Enable();
            Ledger = new(Ticket, capacity, () => { OnTimeRead?.Invoke(); return Now; }, history);
        }

        public void Initialize()
        {
            Ledger.Capture.Begin();
            Ledger.Capture.Complete(default);
            Ledger.Render.Begin();
            Ledger.Render.Complete(default);
        }


        public void Write(long frames)
        {
            var counts = Ledger.Capture.State.Counts;
            Ledger.Capture.Begin();
            Ledger.Capture.Complete(new(counts.PacketsAcquired + 1, counts.PacketsReleased + 1,
                counts.FramesAcquired + frames, counts.FramesReleased + frames, counts.PacketsAccepted + 1,
                counts.FramesAccepted + frames, 0, 0, counts.FifoFrames + frames, 0, 0));
        }

        public void Consume(long frames)
        {
            var counts = Ledger.Render.State.Counts;
            Ledger.Render.Begin();
            Ledger.Render.Complete(new(counts.PacketsAcquired + 1, counts.PacketsReleased + 1,
                counts.FramesAcquired + frames, counts.FramesReleased + frames, 0, 0, 0, 0, counts.FifoFrames + frames, 0, 0));
        }

        public WasapiCheckpoint Take(WasapiCheckpoint? baseline = null)
        {
            var attempt = Ledger.Read(Generation, baseline);
            Assert.Equal(WasapiCheckpointStatus.Available, attempt.Status);
            return Assert.IsType<WasapiCheckpoint>(attempt.Checkpoint);
        }
    }
}
