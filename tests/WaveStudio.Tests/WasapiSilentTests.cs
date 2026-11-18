using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiSilentTests
{
    internal const int Invalidated = unchecked((int)0x88890004);
    internal sealed class Calls
    {
        public readonly Role Input, Output;
        public readonly ConcurrentQueue<(bool Input, string Step, int Thread)> Log = new();
        public Calls(TransportRoute route) { Input = new(this, route.Capture, true); Output = new(this, route.Playback, false); }
        public IWasapiPreparationDevice Open(TransportEndpoint endpoint) => (endpoint.Direction == EndpointDirection.Capture ? Input : Output).Open(endpoint);
    }
    internal sealed class Role(Calls calls, WasapiEndpointInfo info, bool input)
    {
        public string? FailStep;
        public Action<string>? OnStep;
        public uint Capacity = 1056, CaptureFlags = 2;
        public uint? PacketFrames, Padding, PaddingAfterStart;
        public ulong? PacketQpc;
        public readonly ConcurrentQueue<uint> RenderRequests = new();
        public int ClockResult, ReleaseResult, StopResult, StartResult;
        public bool NullData, EmptyPacket, Stagnant, RegressClock, ChangedFormat;
        public int Owner, Stops, DisposeCalls;
        public bool BufferHeld;
        public long StartTime, AcquiredFrames, ReleasedFrames;
        public bool Started;
        private ulong packetPosition;
        private int clockSamples;
        private bool packetDue = true;
        private GCHandle memory;
        public readonly byte[] Data = Enumerable.Repeat((byte)0x5a, 4096 * 8).ToArray();
        private void Step(string step)
        {
            if (Owner == 0) Owner = Environment.CurrentManagedThreadId;
            Assert.Equal(Owner, Environment.CurrentManagedThreadId); Assert.Equal(ApartmentState.MTA, Thread.CurrentThread.GetApartmentState());
            calls.Log.Enqueue((input, step, Owner)); OnStep?.Invoke(step);
            if (step == FailStep) throw new WasapiCallException(step, Invalidated);
        }
        public IWasapiPreparationDevice Open(TransportEndpoint endpoint)
        {
            Step("Open"); Assert.Equal(info.Endpoint, endpoint); memory = GCHandle.Alloc(Data, GCHandleType.Pinned); return new Device(this, info);
        }
        private sealed class Device(Role role, WasapiEndpointInfo info) : IWasapiPreparationDevice
        {
            public void ValidateIdentity(TransportEndpoint endpoint) { role.Step("Identity"); Assert.Equal(info.Endpoint, endpoint); }
            public IWasapiPreparationClient Activate() { role.Step("Activate"); return new Client(role, info); }
            public void Dispose() { role.Step("DisposeDevice"); role.memory.Free(); }
        }
        private sealed class Client(Role role, WasapiEndpointInfo info) : IWasapiStreamClient
        {
            public WasapiPcm ReadMixFormat() { role.Step("Mix"); return info.Mix!; }
            public void RequireExactSupport() => role.Step("Support");
            public (long, long) ReadPeriods() { role.Step("Periods"); return (100000, 30000); }
            public void Initialize() => role.Step("Initialize");
            public (uint, long) ReadBufferInfo() { role.Step("Capacity"); return (role.Capacity, 100000); }
            public uint ReadPadding() { role.Step("Padding"); return (role.Started ? role.PaddingAfterStart : null) ?? role.Padding ?? 0; }
            public IWasapiPreparationClock OpenClock() { role.Step("Clock"); return new Clock(role, info); }
            public IWasapiCapturePackets OpenCapture() { role.Step("CaptureService"); return new Capture(role); }
            public IWasapiRenderPackets OpenRender() { role.Step("RenderService"); return new Render(role); }
            public void RevalidateFormat(WasapiPcm expected)
            { role.Step("RevalidateFormat"); Assert.Equal(info.Mix, expected); if (role.ChangedFormat) throw new InvalidOperationException("Formato alterado"); }
            public int Start() { role.Step("Start"); role.Started = true; role.StartTime = WasapiTime.Now100ns(); return role.StartResult; }
            public int Stop() { role.Stops++; role.Step("Stop"); Assert.False(role.BufferHeld && role.ReleaseResult == 0); return role.StopResult; }
            public void Dispose() { role.DisposeCalls++; role.Step("DisposeClient"); Assert.False(role.BufferHeld); }
        }
        private sealed class Clock(Role role, WasapiEndpointInfo info) : IWasapiPreparationClock
        {
            public WasapiClockReading Read()
            {
                role.Step("ReadClock"); Assert.True(role.Started);
                ulong qpc = (ulong)WasapiTime.Now100ns();
                ulong frequency = (ulong)info.Mix!.AverageBytesPerSecond;
                ulong position = (ulong)((UInt128)(qpc - (ulong)role.StartTime) * frequency / 10000000);
                if (role.Stagnant) position = 0;
                if (role.RegressClock && ++role.clockSamples > 1) position = 1;
                return new(0, frequency, role.ClockResult, position, qpc);
            }
            public void Dispose() => role.Step("DisposeClock");
        }
        private sealed class Capture(Role role) : IWasapiCapturePackets
        {
            public uint NextPacketSize()
            {
                role.Step("Next"); if (!role.packetDue) { role.packetDue = true; return 0; }
                role.packetDue = false; return role.PacketFrames ?? 48;
            }
            public WasapiCaptureBuffer GetBuffer()
            {
                role.Step("GetCapture"); Assert.False(role.BufferHeld);
                if (role.EmptyPacket) return new(0x08890001, 0, 0, 0, 0, 0);
                role.BufferHeld = true; uint frames = role.PacketFrames ?? 48; role.AcquiredFrames += frames;
                var packet = new WasapiCaptureBuffer(0, role.NullData ? 0 : role.memory.AddrOfPinnedObject(), frames,
                    role.CaptureFlags, role.packetPosition, role.PacketQpc ?? (ulong)WasapiTime.Now100ns()); role.packetPosition += frames; return packet;
            }
            public int ReleaseBuffer(uint framesRead)
            {
                role.Step("ReleaseCapture"); Assert.True(role.BufferHeld); Assert.True(framesRead == 0 || framesRead == (role.PacketFrames ?? 48));
                if (role.ReleaseResult == 0) role.BufferHeld = false; role.ReleasedFrames += framesRead; return role.ReleaseResult;
            }
            public void Dispose() { role.Step("DisposeCapture"); Assert.False(role.BufferHeld); }
        }
        private sealed class Render(Role role) : IWasapiRenderPackets
        {
            public nint GetBuffer(uint frames)
            {
                role.Step("GetRender"); Assert.False(role.BufferHeld); Assert.InRange(frames, 1u, role.Capacity);
                role.RenderRequests.Enqueue(frames);
                role.BufferHeld = true; role.AcquiredFrames += frames; return role.NullData ? 0 : role.memory.AddrOfPinnedObject();
            }
            public int ReleaseBuffer(uint framesWritten, uint flags)
            {
                role.Step("ReleaseRender"); Assert.True(role.BufferHeld); Assert.Equal(2u, flags);
                Assert.InRange(framesWritten, 1u, role.Capacity);
                Assert.All(role.Data, value => Assert.Equal(0x5a, value)); // Transport never writes samples to WASAPI output.
                if (role.ReleaseResult == 0) role.BufferHeld = false; role.ReleasedFrames += framesWritten; return role.ReleaseResult;
            }
            public void Dispose() { role.Step("DisposeRender"); Assert.False(role.BufferHeld); }
        }
    }
    private static TimeSpan Duration => TimeSpan.FromMilliseconds(350);
    [Fact]
    public async Task SilentTransportPrimesBeforeRenderThenStartsCaptureOnStableOwnersAndReleasesInOrder()
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route); var guard = new TransportActivityGuard();
        await using var session = new WasapiSilentSession(route, guard, calls.Open);
        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);
        Assert.True(result.Completed); Assert.False(result.ResourcesRetained); Assert.Null(result.Failure); Assert.True(result.MovementProven); Assert.True(result.TransportObserved);
        Assert.True(result.AlwaysSilent); Assert.False(result.DriftControlEnabled); Assert.Equal(4096, result.FifoCapacityFrames);
        Assert.Equal(1056, result.Playback.PrimedFrames); Assert.True(result.ProducedFrames > 0); Assert.True(result.Playback.FramesReleased > 0);
        Assert.Equal(result.Capture.PacketsAcquired, result.Capture.PacketsReleased);
        Assert.Equal(result.Playback.PacketsAcquired, result.Playback.PacketsReleased);
        Assert.NotEqual(calls.Input.Owner, calls.Output.Owner);
        var log = calls.Log.ToArray(); int prime = Array.FindIndex(log, c => !c.Input && c.Step == "ReleaseRender");
        int startRender = Array.FindIndex(log, c => !c.Input && c.Step == "Start"); int startCapture = Array.FindIndex(log, c => c.Input && c.Step == "Start");
        Assert.True(prime < startRender && startRender < startCapture);
        foreach (bool input in new[] { false, true })
        {
            var steps = log.Where(c => c.Input == input).Select(c => c.Step).ToArray();
            Assert.True(Array.IndexOf(steps, "ReadClock") > Array.IndexOf(steps, "Start"));
            Assert.Equal(new[] { input ? "DisposeCapture" : "DisposeRender", "DisposeClock", "DisposeClient", "DisposeDevice" }, steps.TakeLast(4));
        }
        using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic); Assert.NotNull(lease);
    }
    [Theory]
    [InlineData("Open")] [InlineData("Identity")] [InlineData("Activate")] [InlineData("Mix")] [InlineData("Support")]
    [InlineData("Periods")] [InlineData("Initialize")] [InlineData("Capacity")] [InlineData("Clock")]
    [InlineData("CaptureService")] [InlineData("RevalidateFormat")] [InlineData("Start")] [InlineData("Next")] [InlineData("GetCapture")]
    public async Task PartialCaptureFailureStopsBothAndReleasesOnTheirOwners(string step)
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route); calls.Input.FailStep = step;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);
        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);
        Assert.True(result.Completed); Assert.False(result.ResourcesRetained); Assert.NotNull(result.Failure); Assert.Equal(Invalidated, result.HResult);
        Assert.False(result.MovementProven); if (result.Playback.StartAttempted) Assert.True(result.Playback.StopConfirmed);
        Assert.False(calls.Input.BufferHeld); Assert.False(calls.Output.BufferHeld);
    }
    [Theory]
    [InlineData(1u)] [InlineData(4u)] [InlineData(8u)] [InlineData(3u)]
    public async Task BadCaptureFlagsArePreservedAndPacketIsReleasedWithoutPayload(uint flags)
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route); calls.Input.CaptureFlags = flags; calls.Input.NullData = true;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);
        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);
        Assert.NotNull(result.Failure); Assert.Equal(flags, result.Capture.PacketFlags); Assert.True(result.Completed);
        Assert.Equal(1, result.Capture.PacketsAcquired); Assert.Equal(1, result.Capture.PacketsReleased); Assert.Equal(0, result.Capture.FramesReleased);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task SilentNullCaptureAndEmptyPacketRespectDifferentReleaseContracts(bool empty)
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route); calls.Input.NullData = true; calls.Input.EmptyPacket = empty;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);
        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);
        Assert.Null(result.Failure); Assert.True(result.Completed);
        Assert.Equal(empty ? 0 : result.Capture.PacketsAcquired, result.Capture.PacketsReleased);
        Assert.Equal(empty ? 0 : result.Capture.FramesAcquired, result.Capture.FramesReleased);
        Assert.Equal(empty, result.Capture.PacketsAcquired == 0);
        if (empty) Assert.False(result.TransportObserved);
    }
    [Fact]
    public async Task CapturePayloadStaysInMemoryAndRenderUsesOnlyAvailableSpace()
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        calls.Input.CaptureFlags = 0;
        calls.Output.PaddingAfterStart = 480;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);

        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        Assert.True(result.Completed);
        Assert.True(result.ProducedFrames > 0);
        var requests = calls.Output.RenderRequests.ToArray();
        Assert.Equal(1056u, requests[0]);
        Assert.NotEmpty(requests.Skip(1));
        Assert.All(requests.Skip(1), frames => Assert.Equal(576u, frames));
    }

    [Theory]
    [InlineData("packet")] [InlineData("nullCapture")] [InlineData("nullRender")] [InlineData("padding")] [InlineData("capacity")] [InlineData("format")] [InlineData("timestamp")]
    public async Task InvalidBuffersFormatAndPaddingFailSafely(string fault)
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route);
        if (fault == "packet") calls.Input.PacketFrames = 2000;
        if (fault == "nullCapture") { calls.Input.CaptureFlags = 0; calls.Input.NullData = true; }
        if (fault == "nullRender") calls.Output.NullData = true;
        if (fault == "padding") calls.Output.Padding = 2000;
        if (fault == "capacity") calls.Output.Capacity = 5000;
        if (fault == "format") calls.Input.ChangedFormat = true;
        if (fault == "timestamp") calls.Input.PacketQpc = 1;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);
        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);
        Assert.True(result.Completed); Assert.NotNull(result.Failure); Assert.False(calls.Input.BufferHeld); Assert.False(calls.Output.BufferHeld);
    }
    [Theory]
    [InlineData("uncertain")] [InlineData("failed")] [InlineData("stagnant")] [InlineData("regress")]
    public async Task InvalidMovingClockDoesNotBecomeAccurateTransport(string fault)
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route);
        calls.Input.ClockResult = fault == "uncertain" ? 1 : fault == "failed" ? Invalidated : 0;
        calls.Input.Stagnant = fault == "stagnant"; calls.Input.RegressClock = fault == "regress";
        await using var session = new WasapiSilentSession(route, new(), calls.Open);
        var result = await session.RunAsync(TimeSpan.FromMilliseconds(700), TestContext.Current.CancellationToken);
        Assert.NotNull(result.Failure); Assert.False(result.MovementProven); Assert.True(result.Completed);
    }
    [Fact]
    public async Task CancellationAfterRenderAcquireStillReleasesSilentAndStopsBeforeCaptureStart()
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        calls.Output.OnStep = step => { if (step == "GetRender") cancellation.Cancel(); };
        await using var session = new WasapiSilentSession(route, new(), calls.Open);
        var result = await session.RunAsync(Duration, cancellation.Token);
        Assert.True(result.Completed); Assert.Equal(1, result.Playback.PacketsReleased); Assert.False(result.Capture.StartAttempted);
    }
    [Fact]
    public async Task CaptureStartFailureStopsBothRolesAndAcceptsAlreadyStopped()
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        calls.Input.StartResult = Invalidated;
        calls.Input.StopResult = 1;
        await using var session = new WasapiSilentSession(route, new(), calls.Open);

        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);

        Assert.True(result.Playback.Started);
        Assert.True(result.Capture.StartAttempted);
        Assert.False(result.Capture.Started);
        Assert.Equal(Invalidated, result.Capture.StartHResult);
        Assert.Equal(1, result.Capture.StopHResult);
        Assert.True(result.Capture.StopConfirmed);
        Assert.True(result.Playback.StopConfirmed);
        Assert.True(result.Completed);
        Assert.False(result.TransportObserved);
    }

    [Fact]
    public async Task FailedStopRetainsLeaseUntilExplicitRetryOnSameOwner()
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        var guard = new TransportActivityGuard();
        calls.Output.StopResult = Invalidated;
        var session = new WasapiSilentSession(route, guard, calls.Open, TimeSpan.FromSeconds(1));
        try
        {
            var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);

            Assert.False(result.Completed);
            Assert.True(result.ResourcesRetained);
            Assert.True(result.Capture.Released);
            Assert.False(result.Playback.StopConfirmed);
            Assert.Equal(Invalidated, result.Playback.StopHResult);
            Assert.Equal(0, calls.Output.DisposeCalls);
            Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));
        }
        finally
        {
            calls.Output.StopResult = 0;
            await session.DisposeAsync();
        }
        Assert.Equal(2, calls.Output.Stops);
        Assert.Equal(1, calls.Output.DisposeCalls);
        using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic);
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task FailedReleaseRetainsLeaseAndNeverRetriesPacketOrDisposesItsParents()
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        var guard = new TransportActivityGuard();
        calls.Input.ReleaseResult = Invalidated;
        var session = new WasapiSilentSession(route, guard, calls.Open, TimeSpan.FromSeconds(1));

        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);

        Assert.True(result.ResourcesRetained);
        Assert.False(result.Completed);
        Assert.True(result.Capture.BufferInFlight);
        Assert.True(result.Capture.StopConfirmed);
        Assert.Equal(Invalidated, result.Capture.ReleaseHResult);
        Assert.NotNull(result.Capture.CleanupError);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());
        Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));
        Assert.Equal(0, calls.Input.DisposeCalls);
        Assert.Single(calls.Log, step => step.Input && step.Step == "ReleaseCapture");
    }

    [Fact]
    public async Task CancelWhileNativeCaptureIsInFlightRetainsLeaseUntilOwnerReturns()
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        var guard = new TransportActivityGuard();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        calls.Input.OnStep = step =>
        {
            if (step != "GetCapture") return;
            entered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(8)));
        };
        var session = new WasapiSilentSession(route, guard, calls.Open, TimeSpan.FromSeconds(1));
        try
        {
            var running = session.RunAsync(TimeSpan.FromSeconds(3), cancel.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            cancel.Cancel();
            var result = await running;

            Assert.True(result.ResourcesRetained);
            Assert.False(result.DurationReached);
            Assert.Null(guard.TryAcquire(AudioActivity.NativePreparation));
        }
        finally
        {
            release.Set();
            await session.DisposeAsync();
        }
        Assert.Equal(1, calls.Input.DisposeCalls);
        Assert.Single(calls.Log, step => step.Input && step.Step == "ReleaseCapture");
        Assert.Equal(0, calls.Input.ReleasedFrames);
        using var lease = guard.TryAcquire(AudioActivity.NativePreparation);
        Assert.NotNull(lease);
    }

    [Fact]
    public async Task CurrentGenerationInvalidatesBeforeCaptureStart()
    {
        var route = WasapiContractTests.Route();
        var calls = new Calls(route);
        await using var session = new WasapiSilentSession(route, new(), calls.Open);
        calls.Output.OnStep = step =>
        {
            if (step == "Start") session.Invalidate(route.Capture.Endpoint.Generation);
        };

        var result = await session.RunAsync(Duration, TestContext.Current.CancellationToken);

        Assert.Equal("SelectionInvalidated", result.Failure);
        Assert.True(result.Completed);
        Assert.False(result.Capture.StartAttempted);
        Assert.True(result.Playback.StopConfirmed);
        Assert.False(result.TransportObserved);
    }

    [Fact]
    public async Task OldGenerationCannotInvalidateAndConcurrentDisposeIsSafe()
    {
        var route = WasapiContractTests.Route(); var calls = new Calls(route);
        var session = new WasapiSilentSession(route, new(), calls.Open);
        var running = session.RunAsync(Duration, TestContext.Current.CancellationToken);
        session.Invalidate(route.Capture.Endpoint.Generation - 1);
        var result = await running; Assert.Null(result.Failure);
        await Task.WhenAll(session.DisposeAsync().AsTask(), session.DisposeAsync().AsTask());
        Assert.Equal(1, calls.Input.DisposeCalls); Assert.Equal(1, calls.Output.DisposeCalls);
    }
}
