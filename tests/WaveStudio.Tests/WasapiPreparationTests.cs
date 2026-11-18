using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiPreparationTests
{
    private static readonly TransportEndpoint Endpoint = new("explicit-input", EndpointDirection.Capture, 19);
    private static WasapiPcm Format => WasapiContractTests.Pcm(floating: true);
    private const int DeviceInvalidated = unchecked((int)0x88890004);

    [Theory]
    [InlineData(0, 0, 48000, (int)NativeClockQuality.Accurate)]
    [InlineData(0, 1, 48000, (int)NativeClockQuality.Uncertain)]
    [InlineData(0, -1, 48000, (int)NativeClockQuality.Unavailable)]
    [InlineData(0, DeviceInvalidated, 48000, (int)NativeClockQuality.Unavailable)]
    [InlineData(0, 2, 48000, (int)NativeClockQuality.Unavailable)]
    [InlineData(1, 0, 48000, (int)NativeClockQuality.Unavailable)]
    [InlineData(DeviceInvalidated, 0, 48000, (int)NativeClockQuality.Unavailable)]
    [InlineData(0, 0, 0, (int)NativeClockQuality.Unavailable)]
    public void HResultAccuracyAndUnsignedOutputsRemainSeparate(int fhr, int phr, int frequency, int qualityCode)
    {
        var reading = new WasapiClockReading(fhr, (ulong)frequency, phr, ulong.MaxValue - 3, (1UL << 53) + 17);
        var sample = reading.ToSample(Endpoint);
        var quality = (NativeClockQuality)qualityCode; Assert.Equal(quality, sample.Quality); Assert.Equal(NativePositionUnit.DeviceUnits, sample.Unit);
        Assert.Equal(reading.Position, sample.Position); Assert.Equal(reading.Qpc100ns, sample.Qpc100ns);
        Assert.Equal(fhr, reading.FrequencyHResult); Assert.Equal(phr, reading.PositionHResult);
        var result = new WasapiClockAdapter(Endpoint, 48000).Convert(Endpoint, reading, (long)reading.Qpc100ns, out _);
        Assert.Equal(quality == NativeClockQuality.Accurate ? NativeClockResult.Valid : NativeClockResult.Unavailable, result);
    }
    [Fact]
    public void ClockBridgeKeepsIntegerDeltasIdentityAndRequiresResetOnUncertainReading()
    {
        ulong position = (1UL << 53) + 19, qpc = (1UL << 53) + 99;
        var adapter = new WasapiClockAdapter(Endpoint, 48000);
        var reading = new WasapiClockReading(0, 10000000, 0, position, qpc);
        Assert.Equal(NativeClockResult.InvalidIdentity, adapter.Convert(Endpoint with { Generation = 20 }, reading, (long)qpc, out _));
        Assert.False(adapter.RequiresReset);
        Assert.Equal(NativeClockResult.Valid, adapter.Convert(Endpoint, reading, (long)qpc, out var first));
        Assert.Equal(0, first.FramePosition);
        var next = reading with { Position = position + 100000, Qpc100ns = qpc + 100000 };
        Assert.Equal(NativeClockResult.Valid, adapter.Convert(Endpoint, next, (long)next.Qpc100ns, out var second));
        Assert.Equal(480, second.FramePosition);
        next = next with { PositionHResult = 1, Qpc100ns = qpc + 200000 };
        Assert.Equal(NativeClockResult.Unavailable, adapter.Convert(Endpoint, next, (long)next.Qpc100ns, out _));
        Assert.True(adapter.RequiresReset);
        Assert.Equal(NativeClockResult.RequiresReset, adapter.Convert(Endpoint, next with { PositionHResult = 0 }, (long)next.Qpc100ns, out _));
    }
    [Theory]
    [InlineData((int)TransportPacketFlags.Discontinuity, (int)NativeClockResult.Discontinuity)]
    [InlineData((int)TransportPacketFlags.TimestampError, (int)NativeClockResult.TimestampError)]
    [InlineData(8, (int)NativeClockResult.Unavailable)]
    [InlineData((int)TransportPacketFlags.Silent, (int)NativeClockResult.Valid)]
    public void BridgePreservesPacketFlagsWithoutInventingClockFlags(int flagsCode, int expectedCode)
    {
        var flags = (TransportPacketFlags)flagsCode; var expected = (NativeClockResult)expectedCode;
        var reading = new WasapiClockReading(0, 48000, 0, 0, 10);
        Assert.Equal(flags, reading.ToSample(Endpoint, flags).Flags);
        Assert.Equal(expected, new WasapiClockAdapter(Endpoint, 48000).Convert(Endpoint, reading, 10, out _, flags));
    }
    [Fact]
    public void BridgeRejectsOverflowAndStoppedZeroDoesNotProveProgress()
    {
        var reading = new WasapiClockReading(0, 1, 0, 0, 0);
        var adapter = new WasapiClockAdapter(Endpoint, 48000);
        Assert.Equal(NativeClockResult.Valid, adapter.Convert(Endpoint, reading, 0, out _));
        Assert.Equal(NativeClockResult.Overflow, adapter.Convert(Endpoint, reading with { Position = ulong.MaxValue, Qpc100ns = 1 }, 1, out _));
        adapter = new(Endpoint, 48000);
        Assert.Equal(NativeClockResult.Valid, adapter.Convert(Endpoint, reading, 0, out _));
        Assert.Equal(NativeClockResult.Stalled, adapter.Convert(Endpoint, reading with { Qpc100ns = 5000001 }, 5000001, out _));
        Assert.Equal(NativeClockResult.InvalidTime, new WasapiClockAdapter(Endpoint, 48000).Convert(Endpoint,
            reading with { Qpc100ns = ulong.MaxValue }, long.MaxValue, out _));
    }

    private sealed class Fake
    {
        public List<string> Calls { get; } = [];
        public string? Fail, Cancel;
        public CancellationTokenSource Cancellation { get; } = new();
        public WasapiPcm Mix = Format;
        public TransportEndpoint Identity = Endpoint;
        public WasapiClockReading Reading = new(0, 192000, 0, 0, 12345);
        public int Thread;
        private int validations;
        private void Step(string name)
        {
            if (Thread == 0) Thread = Environment.CurrentManagedThreadId;
            Assert.Equal(Thread, Environment.CurrentManagedThreadId); Assert.Equal(ApartmentState.MTA, System.Threading.Thread.CurrentThread.GetApartmentState());
            Calls.Add(name);
            if (Cancel == name) Cancellation.Cancel();
            if (Fail == name) throw new WasapiCallException(name, DeviceInvalidated);
        }
        public IWasapiPreparationDevice Open(TransportEndpoint endpoint) { Step("Open"); return new Device(this); }
        private sealed class Device(Fake f) : IWasapiPreparationDevice
        {
            public void ValidateIdentity(TransportEndpoint endpoint)
            { f.Step("Validate" + ++f.validations); if (endpoint != f.Identity) throw new InvalidOperationException("Identidade divergente."); }
            public IWasapiPreparationClient Activate() { f.Step("Activate"); return new Client(f); }
            public void Dispose() => f.Step("DisposeDevice");
        }
        private sealed class Client(Fake f) : IWasapiPreparationClient
        {
            public WasapiPcm ReadMixFormat() { f.Step("Mix"); return f.Mix; }
            public void RequireExactSupport() => f.Step("Support");
            public (long, long) ReadPeriods() { f.Step("Periods"); return (100000, 30000); }
            public void Initialize() => f.Step("Initialize");
            public (uint, long) ReadBufferInfo() { f.Step("BufferInfo"); return (480, 100000); }
            public IWasapiPreparationClock OpenClock() { f.Step("Clock"); return new Clock(f); }
            public void Dispose() => f.Step("DisposeClient");
        }
        private sealed class Clock(Fake f) : IWasapiPreparationClock
        {
            public WasapiClockReading Read() { f.Step("Read"); return f.Reading; }
            public void Dispose() => f.Step("DisposeClock");
        }
    }
    [Fact]
    public async Task PreparationUsesSameClientRevalidatesAndReleasesInReverseOrderWithoutAudio()
    {
        var fake = new Fake(); var guard = new TransportActivityGuard();
        var result = await WasapiPreparation.PrepareAsync(Endpoint, Format, guard, TestContext.Current.CancellationToken, fake.Open);
        Assert.True(result.Succeeded); Assert.True(result.Initialized); Assert.False(result.AudioStarted); Assert.True(result.ResourcesReleased);
        Assert.Equal(fake.Reading, result.Clock); Assert.Equal(480u, result.BufferFrames); Assert.Equal(100000, result.StreamLatency100ns);
        Assert.Equal(fake.Thread, result.OwnerThreadId);
        Assert.Equal(new[] { "Open", "Validate1", "Activate", "Mix", "Support", "Periods", "Validate2", "Initialize", "BufferInfo", "Clock", "Read", "Validate3", "DisposeClock", "DisposeClient", "DisposeDevice" }, fake.Calls);
        using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic); Assert.NotNull(lease);
    }
    [Theory]
    [InlineData("Open")] [InlineData("Validate1")] [InlineData("Activate")] [InlineData("Mix")]
    [InlineData("Support")] [InlineData("Periods")] [InlineData("Validate2")] [InlineData("Initialize")]
    [InlineData("BufferInfo")] [InlineData("Clock")] [InlineData("Read")] [InlineData("Validate3")]
    public async Task PartialFailureRetainsStageAndHResultAndReleasesOnlyAcquiredResources(string stage)
    {
        var fake = new Fake { Fail = stage }; var guard = new TransportActivityGuard();
        var result = await WasapiPreparation.PrepareAsync(Endpoint, Format, guard, TestContext.Current.CancellationToken, fake.Open);
        Assert.False(result.Succeeded); Assert.Equal(stage, result.FailedStage); Assert.Equal(DeviceInvalidated, result.HResult);
        Assert.True(result.ResourcesReleased); Assert.False(result.AudioStarted); Assert.Null(result.CleanupError);
        if (stage != "Open") Assert.Equal("DisposeDevice", fake.Calls[^1]);
        Assert.Equal(fake.Calls.Contains("Clock") && stage != "Clock", fake.Calls.Contains("DisposeClock"));
        Assert.Equal(fake.Calls.Contains("Activate") && stage != "Activate", fake.Calls.Contains("DisposeClient"));
        Assert.Equal(fake.Calls.Distinct().Count(), fake.Calls.Count);
        using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic); Assert.NotNull(lease);
    }
    [Theory]
    [InlineData("Open")] [InlineData("Activate")] [InlineData("Initialize")] [InlineData("Clock")] [InlineData("Read")]
    public async Task CancellationAfterAcquisitionCleansUpBeforeUnlocking(string step)
    {
        var fake = new Fake { Cancel = step }; var guard = new TransportActivityGuard();
        var result = await WasapiPreparation.PrepareAsync(Endpoint, Format, guard, fake.Cancellation.Token, fake.Open);
        Assert.Equal("Canceled", result.FailedStage); Assert.True(result.ResourcesReleased); Assert.False(result.AudioStarted);
        Assert.Equal("DisposeDevice", fake.Calls[^1]);
        using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic); Assert.NotNull(lease);
    }
    [Theory]
    [InlineData("DisposeClock")] [InlineData("DisposeClient")] [InlineData("DisposeDevice")]
    public async Task FailedReleaseRetainsParentsAndLeaseWithoutBlindRetry(string step)
    {
        var fake = new Fake { Fail = step }; var guard = new TransportActivityGuard();
        var result = await WasapiPreparation.PrepareAsync(Endpoint, Format, guard, TestContext.Current.CancellationToken, fake.Open);
        Assert.False(result.ResourcesReleased); Assert.False(result.Succeeded); Assert.NotNull(result.CleanupError);
        Assert.Equal(step, fake.Calls[^1]); Assert.Single(fake.Calls, s => s == step);
        Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));
    }
    [Theory]
    [InlineData("identity")] [InlineData("format")] [InlineData("rate")]
    public async Task PreparationRefusesChangedIdentityAndUnsupportedFormatBeforeInitialize(string mismatch)
    {
        var fake = new Fake();
        if (mismatch == "identity") fake.Identity = Endpoint with { Direction = EndpointDirection.Playback };
        else fake.Mix = Format with { SampleRate = mismatch == "rate" ? 96000 : 44100, AverageBytesPerSecond = mismatch == "rate" ? 384000 : 176400 };
        var result = await WasapiPreparation.PrepareAsync(Endpoint, Format, new(), TestContext.Current.CancellationToken, fake.Open);
        Assert.False(result.Succeeded); Assert.False(result.Initialized); Assert.True(result.ResourcesReleased); Assert.DoesNotContain("Initialize", fake.Calls);
    }
    [Fact]
    public async Task PreparationAndDiagnosticActuallyShareLeaseUntilCompletion()
    {
        var guard = new TransportActivityGuard(); var backend = new MicrophoneTestTests.FakeBackend();
        backend.Capture.FinishOnStop = false;
        var engine = new MicrophoneTestEngine(backend, _ => true, guard);
        engine.Select("capture", "render", 1); engine.Record(1, TimeSpan.Zero); engine.Stop();
        var fake = new Fake();
        await Assert.ThrowsAsync<InvalidOperationException>(() => WasapiPreparation.PrepareAsync(Endpoint, Format, guard, TestContext.Current.CancellationToken, fake.Open));
        Assert.Empty(fake.Calls); Assert.True(engine.Busy); Assert.Equal(0, backend.Capture.Disposals);
        backend.Capture.Completed = true; engine.Tick(TimeSpan.Zero);
        Assert.False(engine.Busy); Assert.True((await WasapiPreparation.PrepareAsync(Endpoint, Format, guard, TestContext.Current.CancellationToken, fake.Open)).Succeeded);
        using (var lease = guard.TryAcquire(AudioActivity.NativePreparation))
        {
            Assert.NotNull(lease); engine.Record(1, TimeSpan.Zero); Assert.False(engine.Busy);
            Assert.Contains("Outra operação", engine.Snapshot.Message);
        }
        engine.MarkDisposed();
    }
    [Fact]
    public void DiagnosticCompletionWithFailedDisposeRetainsGuardAfterClose()
    {
        var guard = new TransportActivityGuard();
        var backend = new MicrophoneTestTests.FakeBackend();
        backend.Capture.DisposeError = new InvalidOperationException("release failed");
        var engine = new MicrophoneTestEngine(backend, _ => true, guard);
        engine.Select("capture", "render", 1);
        engine.Record(1, TimeSpan.Zero);
        engine.Stop();
        engine.Tick(TimeSpan.Zero);

        Assert.False(engine.Busy);
        Assert.False(engine.Snapshot.CanRecord);
        Assert.NotNull(engine.Snapshot.CleanupError);
        Assert.Null(guard.TryAcquire(AudioActivity.IsolatedTransport));
        Assert.Null(guard.TryAcquire(AudioActivity.NativePreparation));
        engine.MarkDisposed();
        Assert.Equal(1, backend.Capture.Disposals);
        Assert.Null(guard.TryAcquire(AudioActivity.IsolatedTransport));
    }

    [Fact]
    public async Task LeaseRemainsHeldDuringBlockedPrepareAndCancellationWaitsForOwnerReturn()
    {
        var fake = new Fake(); var guard = new TransportActivityGuard();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var task = WasapiPreparation.PrepareAsync(Endpoint, Format, guard, cancellation.Token, e =>
        {
            entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)); return fake.Open(e);
        });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)); cancellation.Cancel();
            Assert.False(task.IsCompleted); Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));
        }
        finally { release.Set(); }
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("Canceled", result.FailedStage); Assert.True(result.ResourcesReleased);
    }
    [Theory]
    [InlineData(0, 1, 192000, true)]
    [InlineData(0, DeviceInvalidated, 192000, false)]
    [InlineData(DeviceInvalidated, 0, 192000, false)]
    [InlineData(0, 0, 0, false)]
    public async Task PreparationReportsStoppedRawClockEvenWhenUncertainOrFailed(int fhr, int phr, int frequency, bool success)
    {
        var fake = new Fake { Reading = new(fhr, (ulong)frequency, phr, 0, 0) };
        var result = await WasapiPreparation.PrepareAsync(Endpoint, Format, new(), TestContext.Current.CancellationToken, fake.Open);
        Assert.Equal(success, result.Succeeded); Assert.True(result.Initialized); Assert.True(result.ResourcesReleased);
        Assert.Equal(fake.Reading, result.Clock); Assert.False(result.AudioStarted);
        if (phr == 1) Assert.Equal(NativeClockQuality.Uncertain, result.Clock!.Value.Quality);
    }
    [Fact]
    public async Task PreCanceledRequestDoesNotAcquireOrOpenAnything()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); cancellation.Cancel();
        var fake = new Fake(); var guard = new TransportActivityGuard();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WasapiPreparation.PrepareAsync(Endpoint, Format, guard, cancellation.Token, fake.Open));
        Assert.Empty(fake.Calls); using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic); Assert.NotNull(lease);
    }
    [Fact]
    public async Task ProductionDiagnosticUsesProcessSharedGuardWithoutOpeningMissingDevice()
    {
        using var lease = TransportActivityGuard.Shared.TryAcquire(AudioActivity.NativePreparation); Assert.NotNull(lease);
        await using var session = new MicrophoneTestSession();
        await session.SelectAsync("missing-explicit-input", "missing-explicit-output");
        await session.RecordAsync();
        Assert.Equal(MicrophoneTestState.Idle, session.Latest.State);
        Assert.Contains("Outra operação", session.Latest.Message);
    }
}
