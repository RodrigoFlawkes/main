using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;
using static WaveStudio.Tests.WasapiContractTests;

namespace WaveStudio.Tests;

[Collection("Synthetic engine measurement")]
public sealed class IsolatedTransportTests
{
    [Theory]
    [InlineData(false, true, 48000, 48000)] [InlineData(true, false, 48000, 48000)]
    [InlineData(false, true, 44100, 48000)] [InlineData(true, false, 48000, 44100)]
    public async Task RealSamplesTraverseAdaptiveAndFifoOnSeparateThreads(bool inputFloat, bool outputFloat, int inputRate, int outputRate)
    {
        var route = Route(inputRate: inputRate, outputRate: outputRate, inputFloat: inputFloat, outputFloat: outputFloat);
        using var fake = new FakeTransport(); await using var session = new IsolatedTransportSession(new());
        var started = await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken);
        Assert.Equal(TransportState.Running, started.State); Assert.Equal(1024, started.PrimedFrames);
        Assert.Equal(new[] { "prepare-capture", "prepare-render", "start-render", "start-capture" }, fake.Events.ToArray());
        fake.Render(route, outputRate / 100, 100000);
        Assert.True(fake.Last.AsSpan(0, fake.LastLength).IndexOfAnyExcept((byte)0) < 0);
        await session.SetMonitorAsync(1, false, .2);
        long captureTime = 0, renderTime = 100000;
        int[] blocks = [37, 443, 211, 512, 127, 480];
        for (int i = 0; i < 120; i++)
        {
            int block = blocks[i % blocks.Length];
            captureTime += block * 10000000L / inputRate;
            renderTime += block * 10000000L / inputRate;
            fake.Capture(route, block, captureTime, .5f);
            fake.Render(route, (int)Math.Round(block * (double)outputRate / inputRate), renderTime);
        }
        // Uma transição em degrau gera oscilações no FIR. Verifica o ganho estabilizado separadamente do pico transitório limitado.
        Assert.InRange(fake.LastValue, .099, .101); Assert.InRange(fake.Peak, .099, .12);
        Assert.True(fake.EqualChannels); Assert.True(fake.NonzeroSamples > 0);
        var stopped = await session.StopAsync();
        Assert.Equal(TransportState.Closed, stopped.State);
        Assert.Equal(stopped.PrimedFrames + stopped.ProducedFrames, stopped.ReadFrames + stopped.DiscardedQueuedFrames);
        Assert.Equal(0, stopped.DroppedFrames + stopped.UnderrunFrames);
        Assert.Equal(new[] { "stop", "dispose-render", "dispose-capture" }, fake.Events.TakeLast(3).ToArray());
        Assert.Single(fake.ControlThreads.Distinct()); Assert.Equal(started.OwnerThreadId, fake.ControlThreads.First());
        Assert.NotEqual(fake.Producer.ThreadId, fake.Consumer.ThreadId); Assert.NotEqual(started.OwnerThreadId, fake.Producer.ThreadId);
        Save($"samples-{inputRate}-{outputRate}-{inputFloat}", new { stopped, fake.Peak, fake.NonzeroSamples, fake.Submissions });
    }
    [Fact]
    public async Task SilentPacketIgnoresNonzeroPayloadAndDefaultIsMuted()
    {
        var route = Route(); using var fake = new FakeTransport(); await using var session = new IsolatedTransportSession(new());
        await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken);
        for (int i = 1; i <= 8; i++) { fake.Capture(route, 480, i * 100000, 1, TransportPacketFlags.Silent); fake.Render(route, 480, i * 100000); }
        var state = await session.TickAsync(fake.CloseThrough(800000));
        Assert.Equal(3840, state.MutedFrames); Assert.Equal(0, fake.NonzeroSamples);
        await session.SetMonitorAsync(1, false, .2);
        for (int i = 9; i <= 16; i++) { fake.Capture(route, 480, i * 100000, 1, TransportPacketFlags.Silent); fake.Render(route, 480, i * 100000); }
        Assert.Equal(0, fake.NonzeroSamples);
        fake.Producer.Invoke(() => fake.CaptureSink!([], 480, Clock(route.Capture.Endpoint, 1700000, 48000, TransportPacketFlags.Silent), 1700000));
        Assert.Equal(TransportState.Closed, (await session.StopAsync()).State);
    }
    [Fact]
    public async Task ClosedIntervalMeanUsesEventCountersAndClockController()
    {
        var route = Route(); using var fake = new FakeTransport(); await using var session = new IsolatedTransportSession(new());
        await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => session.TickAsync(100000));
        // Fila com 1024 até .01 s, 1504 até .02 s e então 1024: média em .03 s = 1184.
        fake.Capture(route, 480, 100000, .2f); fake.Render(route, 480, 200000);
        var first = await session.TickAsync(fake.CloseThrough(300000));
        Assert.Equal(1184, first.MeanOccupancy); Assert.Equal(DriftControlState.Waiting, first.ControlState);
        for (int i = 4; i <= 150; i++)
        {
            fake.Capture(route, 480, i * 100000, .2f); fake.Render(route, 480, i * 100000);
            if (i % 10 == 0) await session.TickAsync(fake.CloseThrough(i * 100000));
        }
        var last = session.Snapshot; Assert.Equal(DriftControlState.Tracking, last.ControlState);
        Assert.InRange(last.TargetPpm, -500, 500); Assert.InRange(last.MeanOccupancy!.Value, 1023, 1025);
        Save("occupancy", new { first, last });
    }
    [Theory]
    [InlineData("timestamp")] [InlineData("unavailable")] [InlineData("discontinuity")]
    [InlineData("disconnect")] [InlineData("suspend")] [InlineData("backend")]
    public async Task FailureInvalidatesDeliveryImmediatelyAndRecoveryIsExplicit(string mode)
    {
        var route = Route(); using var old = new FakeTransport(); await using var session = new IsolatedTransportSession(new());
        await session.StartAsync(route, old, 0, cancellation: TestContext.Current.CancellationToken);
        if (mode == "disconnect" || mode == "suspend") session.SignalInvalidation(1, TransportFault.Discontinuity);
        else if (mode == "backend") { old.ThrowSubmit = true; old.Render(route, 480, 100000); }
        else old.Capture(route, 480, 100000, .2f, mode == "timestamp" ? TransportPacketFlags.TimestampError : mode == "discontinuity" ? TransportPacketFlags.Discontinuity : 0,
            mode == "unavailable" ? NativeClockQuality.Unavailable : NativeClockQuality.Accurate);
        long submitted = old.Submissions;
        old.Render(route, 480, 200000); Assert.Equal(submitted, old.Submissions);
        var faulted = await session.StopAsync(); Assert.Equal(TransportState.Faulted, faulted.State);
        using var next = new FakeTransport();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(Route(2), next, 300000, cancellation: TestContext.Current.CancellationToken));
        var recovered = await session.StartAsync(Route(3), next, 300000, recover: true, cancellation: TestContext.Current.CancellationToken);
        Assert.Equal(3, recovered.Generation); Assert.Equal(TransportState.Running, recovered.State);
        old.Capture(route, 480, 400000, 1); old.Render(route, 480, 400000);
        Assert.Equal(submitted, old.Submissions); Assert.Equal(0, next.Submissions);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetMonitorAsync(1, false, 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.SetMonitorAsync(3, false, double.NaN));
    }
    [Fact]
    public async Task StopDuringSubmissionInvalidatesTicketBeforeWaitingForCallback()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var route = Route(); using var fake = new FakeTransport { SubmitEntered = entered, SubmitRelease = release };
        await using var session = new IsolatedTransportSession(new()); await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken);
        var callback = Task.Run(() => fake.Render(route, 480, 100000), TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        var stopped = session.StopAsync(); Assert.False(fake.PendingTicket!.IsValid); Assert.False(stopped.IsCompleted);
        release.Set(); await callback.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var result = await stopped.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(0, fake.Submissions); Assert.Equal(TransportState.Closed, result.State); Assert.False(result.ResourcesRetained);
    }
    [Fact]
    public async Task QuiescenceTimeoutRetainsResourcesUntilManualRecovery()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var route = Route(); using var fake = new FakeTransport { SubmitEntered = entered, SubmitRelease = release };
        await using var session = new IsolatedTransportSession(new()); await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken);
        var callback = Task.Run(() => fake.Render(route, 480, 100000), TestContext.Current.CancellationToken); Assert.True(entered.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        var watch = Stopwatch.StartNew(); var stopped = await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(TransportState.Faulted, stopped.State); Assert.True(stopped.ResourcesRetained); Assert.DoesNotContain("dispose-render", fake.Events);
        release.Set(); await callback.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        using var next = new FakeTransport(); var recovered = await session.StartAsync(Route(2), next, 200000, recover: true, cancellation: TestContext.Current.CancellationToken);
        Assert.Equal(TransportState.Running, recovered.State); Assert.Contains("dispose-render", fake.Events); Assert.Equal(0, fake.Submissions);
        Save("quiescence", new { stopped, ElapsedMs = watch.Elapsed.TotalMilliseconds, recovered });
    }
    [Theory]
    [InlineData("prepare-render")] [InlineData("start-render")] [InlineData("start-capture")]
    public async Task PartialStartDisposesResourcesOnOwnerThread(string failingOperation)
    {
        using var fake = new FakeTransport { FailOperation = failingOperation }; await using var session = new IsolatedTransportSession(new());
        var result = await session.StartAsync(Route(), fake, 0, cancellation: TestContext.Current.CancellationToken);
        Assert.Equal(TransportState.Faulted, result.State); Assert.Equal(TransportFault.Backend, result.Fault);
        Assert.Equal(0, fake.Submissions); Assert.Contains("dispose-capture", fake.Events); Assert.Single(fake.ControlThreads.Distinct());
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task StopOrCancellationWhilePreparingNeverEnablesDelivery(bool cancel)
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var fake = new FakeTransport { PrepareEntered = entered, PrepareRelease = release };
        await using var session = new IsolatedTransportSession(new());
        var start = session.StartAsync(Route(), fake, 0, cancellation: cancellation.Token);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Task<TransportSnapshot>? stop = null;
        if (cancel) cancellation.Cancel(); else stop = session.StopAsync();
        release.Set(); var result = await start.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken); if (stop is not null) await stop;
        Assert.Equal(TransportState.Faulted, result.State); Assert.DoesNotContain("start-capture", fake.Events);
        Assert.Equal(0, fake.Submissions);
    }
    [Fact]
    public async Task DiagnosticLeaseExcludesTransportAndDoesNotStartAutomaticallyOnRelease()
    {
        var guard = new TransportActivityGuard(); var diagnostic = guard.TryAcquire(AudioActivity.ShortDiagnostic)!;
        await using var session = new IsolatedTransportSession(guard); using var fake = new FakeTransport();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(Route(), fake, 0, cancellation: TestContext.Current.CancellationToken)); Assert.Empty(fake.Events);
        diagnostic.Dispose(); Assert.Equal(TransportState.Closed, session.Snapshot.State);
        await session.StartAsync(Route(2), fake, 0, cancellation: TestContext.Current.CancellationToken); Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));
        await session.StopAsync(); using var available = guard.TryAcquire(AudioActivity.ShortDiagnostic); Assert.NotNull(available);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task RealDropsOrUnderrunsRequireReprimingInsteadOfContinuingLastRatio(bool overrun)
    {
        var route = Route(); using var fake = new FakeTransport(); await using var session = new IsolatedTransportSession(new());
        await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken);
        if (overrun) fake.Capture(route, 4096, 100000, .1f); else fake.Render(route, 4096, 100000);
        var result = await session.TickAsync(fake.CloseThrough(100000));
        Assert.Equal(TransportState.Faulted, result.State); Assert.Equal(TransportFault.Boundary, result.Fault);
        Assert.True(overrun ? result.DroppedFrames > 0 : result.UnderrunFrames > 0);
        Save(overrun ? "overrun" : "underrun", result);
    }
    [Fact]
    public async Task BorrowedBlockBoundsAndRoleAffinityAreEnforcedWithoutFallback()
    {
        using var fake = new FakeTransport(); await using var session = new IsolatedTransportSession(new()); var route = Route();
        await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken);
        fake.Producer.Invoke(() => fake.CaptureSink!(new byte[2], 4097, Clock(route.Capture.Endpoint, 100000, 48000), 100000));
        var result = await session.StopAsync(); Assert.Equal(TransportFault.Protocol, result.Fault);
        using var next = new FakeTransport(); route = Route(2); await session.StartAsync(route, next, 0, recover: true, cancellation: TestContext.Current.CancellationToken);
        next.Capture(route, 480, 100000, .1f);
        next.Consumer.Invoke(() => next.CaptureSink!(new byte[960], 480, Clock(route.Capture.Endpoint, 200000, 48000), 200000));
        result = await session.StopAsync(); Assert.Equal(TransportFault.Protocol, result.Fault);
    }
    [Fact]
    public async Task CallbackPathHasNoRecurringAllocationsAfterWarmup()
    {
        var route = Route(); using var fake = new FakeTransport(); await using var session = new IsolatedTransportSession(new());
        await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken); await session.SetMonitorAsync(1, false, .2);
        var input = FakeTransport.Bytes(route.Capture.Mix!, 480, .25f); var output = new byte[480 * route.Playback.Mix!.BlockAlign];
        long at = 0, allocated = 0; double elapsed = 0;
        for (int i = 0; i < 50; i++)
        {
            at += 100000; long time = at;
            fake.Producer.Invoke(() => fake.CaptureSink!(input, 480, Clock(route.Capture.Endpoint, time, 48000), time));
            fake.Consumer.Invoke(() => fake.RenderSink!(output, 480, Clock(route.Playback.Endpoint, time, 48000), time));
        }
        for (int i = 0; i < 100; i++)
        {
            at += 100000; long time = at;
            fake.Producer.Invoke(() => { long b = GC.GetAllocatedBytesForCurrentThread(), t = Stopwatch.GetTimestamp(); fake.CaptureSink!(input, 480, Clock(route.Capture.Endpoint, time, 48000), time); elapsed += Stopwatch.GetElapsedTime(t).TotalMilliseconds; allocated += GC.GetAllocatedBytesForCurrentThread() - b; });
            fake.Consumer.Invoke(() => { long b = GC.GetAllocatedBytesForCurrentThread(), t = Stopwatch.GetTimestamp(); fake.RenderSink!(output, 480, Clock(route.Playback.Endpoint, time, 48000), time); elapsed += Stopwatch.GetElapsedTime(t).TotalMilliseconds; allocated += GC.GetAllocatedBytesForCurrentThread() - b; });
        }
        Assert.Equal(0, allocated); Assert.True(fake.NonzeroSamples > 0);
        Save("callbacks", new { AllocatedBytes = allocated, CallbackPairs = 100, Frames = 48000, ElapsedMs = elapsed });
    }
    [Fact]
    public async Task SubmittedBytesMatchReferenceFirAndFifoIncludingCountedPriming()
    {
        var route = Route(); using var fake = new FakeTransport(); await using var session = new IsolatedTransportSession(new());
        await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken); await session.SetMonitorAsync(1, false, .2);
        var reference = new AdaptiveResampler(new(48000, 1), new(48000, 2), ChannelPolicy.DuplicateMono);
        var samples = new float[480]; var converted = new float[960]; var sequence = new List<float>(1024 * 2 + 48000 * 2);
        sequence.AddRange(new float[2048]); ulong expected = 14695981039346656037UL; int cursor = 0;
        for (int block = 1; block <= 100; block++)
        {
            var bytes = new byte[960];
            for (int i = 0; i < 480; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2, 2), (short)(12000 * Math.Sin((block * 480 + i) * .071)));
            PcmFloatConverter.Decode(bytes, samples, new(48000, 1), PcmEncoding.Pcm16LittleEndian);
            var result = reference.Process(1, samples, converted); sequence.AddRange(converted.AsSpan(0, result.ProducedFrames * 2).ToArray());
            long at = block * 100000;
            fake.Producer.Invoke(() => fake.CaptureSink!(bytes, 480, Clock(route.Capture.Endpoint, at, 48000), at));
            fake.Render(route, 480, at);
            for (int i = 0; i < 960; i++)
            {
                int bits = BitConverter.SingleToInt32Bits(sequence[cursor++] * .2f);
                for (int b = 0; b < 4; b++) expected = unchecked((expected ^ (byte)(bits >> (b * 8))) * 1099511628211UL);
            }
        }
        Assert.Equal(expected, fake.Hash); var resultState = await session.StopAsync();
        Assert.Equal(48000, resultState.ProducedFrames); Assert.Equal(0, resultState.DroppedFrames + resultState.UnderrunFrames);
        Save("fifo-hash", new { Hash = fake.Hash.ToString("X16"), resultState });
    }
    [Fact]
    public async Task FullControlQueueRejectsStopCommandButStillInvalidatesPreparingGeneration()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var fake = new FakeTransport { PrepareEntered = entered, PrepareRelease = release };
        await using var session = new IsolatedTransportSession(new());
        var start = session.StartAsync(Route(), fake, 0, cancellation: TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        var queued = Enumerable.Range(1, 8).Select(i => session.TickAsync(i)).ToArray();
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => session.StopAsync()); }
        finally { release.Set(); }
        Assert.Equal(TransportState.Faulted, (await start).State); await Task.WhenAll(queued);
        Assert.DoesNotContain("start-render", fake.Events);
    }
    [Fact]
    public async Task DisposeIsBoundedAndCanBeRetriedAfterBorrowedCallbackReturns()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var fake = new FakeTransport { SubmitEntered = entered, SubmitRelease = release };
        var session = new IsolatedTransportSession(new()); var route = Route();
        await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken);
        var callback = Task.Run(() => fake.Render(route, 480, 100000), TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask()); Assert.True(session.Snapshot.ResourcesRetained); }
        finally { release.Set(); }
        await callback.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await session.DisposeAsync(); await session.DisposeAsync();
        Assert.False(session.Snapshot.ResourcesRetained); Assert.Equal(0, fake.Submissions);
        Assert.Throws<ObjectDisposedException>(() => { _ = session.TickAsync(200000); });
    }
    [Fact]
    public async Task ConcurrentProducerAndConsumerStopWithoutResetRacingAdaptiveProcess()
    {
        var route = Route(); using var fake = new FakeTransport(); await using var session = new IsolatedTransportSession(new());
        await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken);
        var input = FakeTransport.Bytes(route.Capture.Mix!, 480, .2f); var output = new byte[3840];
        using var progressed = new ManualResetEventSlim();
        var producer = Task.Run(() => fake.Producer.Invoke(() =>
        {
            for (int i = 1; i <= 300; i++) { fake.CaptureSink!(input, 480, Clock(route.Capture.Endpoint, i * 100000, 48000), i * 100000); if (i == 20) progressed.Set(); }
        }), TestContext.Current.CancellationToken);
        var consumer = Task.Run(() => fake.Consumer.Invoke(() =>
        {
            for (int i = 1; i <= 300; i++) fake.RenderSink!(output, 480, Clock(route.Playback.Endpoint, i * 100000, 48000), i * 100000);
        }), TestContext.Current.CancellationToken);
        Assert.True(progressed.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        var stopped = await session.StopAsync(); await Task.WhenAll(producer, consumer);
        Assert.Equal(TransportState.Closed, stopped.State); Assert.False(stopped.ResourcesRetained);
        long delivered = fake.Submissions;
        fake.Render(route, 480, 40000000); Assert.Equal(delivered, fake.Submissions);
    }
    [Fact]
    public async Task PacketFromWrongIdentityOrAlreadyClosedIntervalCannotBeDelivered()
    {
        var route = Route(); using var fake = new FakeTransport(); await using var session = new IsolatedTransportSession(new());
        await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken);
        await session.TickAsync(fake.CloseThrough(100000)); fake.Capture(route, 480, 100000, .2f);
        Assert.Equal(TransportFault.Protocol, (await session.StopAsync()).Fault);
        using var next = new FakeTransport(); route = Route(2);
        await session.StartAsync(route, next, 0, recover: true, cancellation: TestContext.Current.CancellationToken);
        next.Producer.Invoke(() => next.CaptureSink!(new byte[960], 480,
            Clock(route.Capture.Endpoint with { Id = "another-endpoint" }, 100000, 48000), 100000));
        next.Render(route, 480, 100000);
        Assert.Equal(0, next.Submissions); Assert.Equal(TransportFault.Clock, (await session.StopAsync()).Fault);
    }
    [Fact]
    public async Task CounterBeyondClosedCutoffIsUnavailableInsteadOfInventingPastMean()
    {
        var route = Route(); using var fake = new FakeTransport(); await using var session = new IsolatedTransportSession(new());
        await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken);
        fake.Capture(route, 480, 200000, .2f);
        var result = await session.TickAsync(fake.CloseThrough(100000));
        Assert.Equal(TransportState.Faulted, result.State); Assert.Equal(TransportFault.Protocol, result.Fault);
        Assert.Null(result.MeanOccupancy); Assert.Equal(0, fake.Submissions);
    }
    [Fact]
    public async Task IndependentFakeClockActuallyChangesSampleResamplerOnProducerOwner()
    {
        var route = Route(); using var fake = new FakeTransport(); await using var session = new IsolatedTransportSession(new());
        await session.StartAsync(route, fake, 0, cancellation: TestContext.Current.CancellationToken);
        long previous = 0;
        for (int i = 1; i <= 500; i++)
        {
            long time = i * 100000L;
            long position = time * 48000L * 1000300 / 10000000 / 1000000;
            int frames = (int)(position - previous); previous = position;
            var bytes = FakeTransport.Bytes(route.Capture.Mix!, frames, .2f);
            var clock = Clock(route.Capture.Endpoint, time, 48000) with { Position = (ulong)position };
            fake.Producer.Invoke(() => fake.CaptureSink!(bytes, frames, clock, time)); fake.Render(route, 480, time);
            if (i % 10 == 0) await session.TickAsync(fake.CloseThrough(time));
        }
        var result = session.Snapshot;
        Assert.Equal(TransportState.Running, result.State); Assert.Equal(DriftControlState.Tracking, result.ControlState);
        Assert.InRange(result.TargetPpm, -500, -20); Assert.InRange(result.AppliedPpm, -500, -20);
        Assert.Equal(0, result.DroppedFrames + result.UnderrunFrames);
        Save("applied-drift", new { SimulatedSeconds = 5, CapturePpm = 300, result });
    }
    [Fact]
    public async Task CommandsRacingDisposalCompleteOrRejectWithoutOrphanedTasks()
    {
        using var fake = new FakeTransport(); var session = new IsolatedTransportSession(new());
        await session.StartAsync(Route(), fake, 0, cancellation: TestContext.Current.CancellationToken);
        using var begin = new ManualResetEventSlim(); int finished = 0;
        var posters = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            begin.Wait(TestContext.Current.CancellationToken);
            for (int i = 1; i <= 64; i++)
            {
                try { await session.TickAsync(i); }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException) { }
                Interlocked.Increment(ref finished);
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        var disposal = Task.Run(async () => { begin.Wait(TestContext.Current.CancellationToken); await session.DisposeAsync(); }, TestContext.Current.CancellationToken);
        begin.Set(); await Task.WhenAll(posters.Append(disposal)).WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
        Assert.Equal(256, finished); Assert.Equal(TransportState.Closed, session.Snapshot.State); Assert.False(session.Snapshot.ResourcesRetained);
        await session.DisposeAsync();
    }
    private static void Save(string name, object data)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "wasapi-backend");
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal sealed class RoleThread : IDisposable
    {
        private readonly BlockingCollection<Action> work = new(); private readonly Thread thread;
        public int ThreadId => thread.ManagedThreadId;
        public RoleThread(string name) { thread = new Thread(() => { foreach (var action in work.GetConsumingEnumerable()) action(); }) { IsBackground = true, Name = name }; thread.SetApartmentState(ApartmentState.MTA); thread.Start(); }
        public void Invoke(Action action)
        {
            Exception? failure = null; using var done = new ManualResetEventSlim();
            work.Add(() => { try { action(); } catch (Exception error) { failure = error; } finally { done.Set(); } });
            if (!done.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Driver simulado não retornou.");
            if (failure is not null) throw failure;
        }
        public void Dispose() { work.CompleteAdding(); Assert.True(thread.Join(TimeSpan.FromSeconds(2))); work.Dispose(); }
    }
    internal sealed class FakeTransport : ISimulatedAudioTransport
    {
        public readonly RoleThread Producer = new("test transport producer"), Consumer = new("test transport consumer");
        public readonly ConcurrentQueue<string> Events = new(); public readonly ConcurrentQueue<int> ControlThreads = new();
        public CapturePacket? CaptureSink; public RenderPacket? RenderSink;
        public ManualResetEventSlim? SubmitEntered, SubmitRelease, PrepareEntered, PrepareRelease;
        public DeliveryTicket? PendingTicket; public string? FailOperation; public bool ThrowSubmit;
        public Action? BeforeStartPlayback, BeforeStartCapture;
        public readonly byte[] Last = new byte[32768]; public int LastLength; public long Submissions, NonzeroSamples; public double Peak, LastValue; public bool EqualChannels = true;
        public ulong Hash = 14695981039346656037UL;
        private bool capture, render, driversDisposed; private WasapiPcm? output;
        private long watermark;
        public long ClosedThrough100ns => Volatile.Read(ref watermark);
        public long CloseThrough(long value) { Volatile.Write(ref watermark, value); return value; }
        private void Event(string operation) { ControlThreads.Enqueue(Environment.CurrentManagedThreadId); Assert.Equal(ApartmentState.MTA, Thread.CurrentThread.GetApartmentState()); Events.Enqueue(operation); if (FailOperation == operation) throw new IOException("Falha simulada."); }
        public void PrepareCapture(TransportRoute route, CapturePacket callback) { Event("prepare-capture"); capture = true; CaptureSink = callback; PrepareEntered?.Set(); if (PrepareRelease is not null && !PrepareRelease.Wait(TimeSpan.FromSeconds(4))) throw new TimeoutException(); }
        public void PreparePlayback(TransportRoute route, RenderPacket callback) { Event("prepare-render"); render = true; RenderSink = callback; output = route.Playback.Mix; }
        public void StartPlayback() { Event("start-render"); BeforeStartPlayback?.Invoke(); }
        public void StartCapture() { Event("start-capture"); BeforeStartCapture?.Invoke(); }
        public void RequestStop() => Event("stop");
        public bool Submit(DeliveryTicket ticket, ReadOnlySpan<byte> bytes)
        {
            if (ThrowSubmit) throw new IOException("Submissão simulada falhou.");
            PendingTicket = ticket; SubmitEntered?.Set();
            // Barreira usada apenas no teste para exercitar a invalidação no último ponto de entrega, sem uso em callbacks de produção.
            if (SubmitRelease is not null && !SubmitRelease.Wait(TimeSpan.FromSeconds(4))) throw new TimeoutException();
            if (!ticket.IsValid) return false;
            bytes.CopyTo(Last); LastLength = bytes.Length; Submissions++;
            foreach (byte value in bytes) Hash = unchecked((Hash ^ value) * 1099511628211UL);
            int sampleBytes = output!.ContainerBits / 8;
            for (int i = 0; i < bytes.Length; i += sampleBytes)
            {
                float value = sampleBytes == 2 ? BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(i, 2)) / 32768f : BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(i, 4)));
                LastValue = value; Peak = Math.Max(Peak, Math.Abs(value)); if (value != 0) NonzeroSamples++;
                if (output.Channels == 2 && i % (2 * sampleBytes) == 0) EqualChannels &= bytes.Slice(i, sampleBytes).SequenceEqual(bytes.Slice(i + sampleBytes, sampleBytes));
            }
            return true;
        }
        public void Capture(TransportRoute route, int frames, long at, float level, TransportPacketFlags flags = 0, NativeClockQuality quality = NativeClockQuality.Accurate)
        {
            var bytes = Bytes(route.Capture.Mix!, frames, level);
            Producer.Invoke(() => CaptureSink!(bytes, frames, Clock(route.Capture.Endpoint, at, route.Capture.Mix!.SampleRate, flags) with { Quality = quality }, at));
        }
        public void Render(TransportRoute route, int frames, long at)
        {
            var bytes = new byte[frames * route.Playback.Mix!.BlockAlign];
            Consumer.Invoke(() => RenderSink!(bytes, frames, Clock(route.Playback.Endpoint, at, route.Playback.Mix.SampleRate), at));
        }
        public static byte[] Bytes(WasapiPcm format, int frames, float level)
        {
            var bytes = new byte[frames * format.BlockAlign]; int size = format.ContainerBits / 8;
            for (int i = 0; i < bytes.Length; i += size)
                if (size == 2) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i, 2), (short)(level * 32767));
                else BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i, 4), BitConverter.SingleToInt32Bits(level));
            return bytes;
        }
        void IDisposable.Dispose() { if (render) { Event("dispose-render"); render = false; } if (capture) { Event("dispose-capture"); capture = false; } }
        public void Dispose() { if (driversDisposed) return; driversDisposed = true; Producer.Dispose(); Consumer.Dispose(); }
    }
}
