using System.Buffers.Binary;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class ExternalRouteTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task DeliversOnlyToSelectedDestinationAndAppliesKnownTrim(bool browser, bool floating)
    {
        var flow = browser ? ExternalFlow.Browser : ExternalFlow.Microphone;
        using var rig = new WasapiDriftTests.VirtualRig(44100, 48000, 0, 0, outputFloat: floating);
        var topology = Topology(rig.Route, flow);
        var settings = Configuration(topology);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var guard = new TransportActivityGuard();
        await using var session = rig.CreateExternal(topology, flow, settings.Confirm(settings.Applied, true, true, stop.Token), guard);
        var opened = rig.Calls;
        double bypass = 0, effect = 0;
        int block = 0;
        long pcm = 0, silent = 0;
        opened.Output.OnRenderSubmitted = (frames, flags) =>
        {
            if (flags == 2) { silent += frames; return; }
            Assert.Equal(0u, flags);
            Assert.True(block > 5);
            pcm += frames;
            var data = opened.Output.Data.AsSpan(0, checked((int)frames * rig.Route.Playback.Mix!.BlockAlign));
            for (int i = 0; i < data.Length; i += floating ? 4 : 2)
            {
                double value = floating ? BinaryPrimitives.ReadSingleLittleEndian(data[i..])
                    : BinaryPrimitives.ReadInt16LittleEndian(data[i..]) / 32768d;
                if (block is >= 25 and <= 130) bypass = Math.Max(bypass, Math.Abs(value));
                if (block is >= 180 and <= 380) effect = Math.Max(effect, Math.Abs(value));
            }
        };
        var run = session.RunAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
        await WaitForRunning(session, run);
        for (block = 1; block <= 400 && !run.IsCompleted; block++)
        {
            if (block == 150) session.SetBypass(false);
            rig.Advance(block, run);
            if (block >= 5) session.Step();
        }
        var result = await run;
        Assert.True(result.Passed, result.Transport.Cleanup.Failure);
        Assert.False(result.Transport.Cleanup.DriftControlEnabled);
        Assert.InRange(bypass, .001, .05002);
        Assert.InRange(effect / bypass, .24, .26);
        Assert.Equal(pcm, result.Output!.Value.PcmFrames);
        Assert.Equal(silent, result.Output.Value.SilentFrames);
        Assert.Equal(result.Transport.Cleanup.Playback.FramesReleased, pcm + silent);
        Assert.Equal(0, result.Transport.Cleanup.DroppedFrames);
        Assert.Equal(0, result.Transport.Cleanup.UnderrunFrames);
        Assert.True(result.Levels is { BypassNonZeroFrames: > 0, EffectNonZeroFrames: > 0 });
    }

    [Fact]
    public void RejectsMergedCableAndMixedPhysicalDestination()
    {
        var graph = Topology(WasapiContractTests.Route(), ExternalFlow.Browser);
        Assert.Throws<ArgumentException>(() => (graph with { MicCable = graph.BrowserCable }).Validate());
        Assert.Throws<ArgumentException>(() => (graph with { Headset = graph.MicCable.Render }).Validate());
        Assert.Throws<ArgumentException>(() => (graph with
        {
            MicCable = graph.MicCable with { Capture = graph.MicCable.Capture with { AdapterId = graph.BrowserCable.Render.AdapterId } }
        }).Validate());
    }

    [Fact]
    public void RestorationIsIdempotentAndReportsPartialRollbackAndExternalConflict()
    {
        var graph = Topology(WasapiContractTests.Route(), ExternalFlow.Browser);
        var tx = Configuration(graph);
        Assert.Equal(ExternalRestoreState.Restore, tx.PlanRestore(tx.Applied).Browser);
        var partial = tx.Applied with { Browser = tx.Before.Browser, Consumer = tx.Applied.Consumer with { EndpointId = "external" } };
        Assert.Equal(new ExternalRestoreReport(ExternalRestoreState.AlreadyRestored, ExternalRestoreState.Conflict), tx.PlanRestore(partial));
        Assert.Equal(ExternalRestoreState.Unavailable, tx.PlanRestore(tx.Applied with
        {
            Consumer = tx.Applied.Consumer with { Accessible = false }
        }).Consumer);
        Assert.Equal(ExternalRestoreState.Unavailable, tx.PlanRestore(tx.Applied with
        {
            Consumer = tx.Applied.Consumer with { EndpointPresent = false }
        }).Consumer);
        Assert.Equal(ExternalRestoreState.Conflict, tx.PlanRestore(tx.Applied with
        {
            Consumer = tx.Applied.Consumer with { ClientId = "outro perfil" }
        }).Consumer);
        Assert.True(tx.PlanRestore(tx.Before).Complete);
        Assert.Equal(ExternalRestoreState.Unavailable, tx.PlanRestore(tx.Applied, originalConsumerPresent: false).Consumer);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => tx.Confirm(partial, true, true, stop.Token));
        Assert.Throws<InvalidOperationException>(() => tx.Confirm(tx.Applied, false, true, stop.Token));
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("selection")]
    [InlineData("get")]
    [InlineData("disconnect")]
    [InlineData("stopfailure")]
    [InlineData("release")]
    [InlineData("silent")]
    public async Task FailsClosedOnLossOfRouteOrSignal(string failure)
    {
        using var rig = new WasapiDriftTests.VirtualRig(48000, 48000, 0, 0);
        var graph = Topology(rig.Route, ExternalFlow.Browser);
        var tx = Configuration(graph);
        var guard = new TransportActivityGuard();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var consent = tx.Confirm(tx.Applied, true, true, stop.Token);
        var session = rig.CreateExternal(graph, ExternalFlow.Browser, consent, guard);
        rig.Calls.Output.OnRenderSubmitted = (_, flags) => Assert.True(flags is 0 or 2);
        if (failure == "silent") rig.Calls.Input.Data.AsSpan().Clear();
        var run = session.RunAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
        await WaitForRunning(session, run);
        for (int block = 1; block <= 400 && !run.IsCompleted; block++)
        {
            if (block == 15)
            {
                switch (failure)
                {
                    case "stop": stop.Cancel(); break;
                    case "selection": session.Invalidate(rig.Generation); break;
                    case "get": rig.Calls.Output.FailStep = "GetRender"; break;
                    case "disconnect": rig.Calls.Input.FailStep = "Next"; break;
                    case "stopfailure": rig.Calls.Output.StopResult = WasapiSilentTests.Invalidated; stop.Cancel(); break;
                    case "release": rig.Calls.Output.ReleaseResult = WasapiSilentTests.Invalidated; break;
                }
            }
            rig.Advance(block, run);
            session.Step();
        }
        var report = await run;
        Assert.False(report.Passed);
        if (failure == "release")
        {
            Assert.True(report.Transport.Cleanup.ResourcesRetained);
            Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());
        }
        else if (failure == "stopfailure")
        {
            Assert.True(report.Transport.Cleanup.ResourcesRetained);
            Assert.Null(guard.TryAcquire(AudioActivity.ShortDiagnostic));
            rig.Calls.Output.StopResult = 0;
            await session.DisposeAsync();
            using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic);
            Assert.NotNull(lease);
        }
        else
        {
            if (failure == "silent")
            {
                Assert.True(report.Transport.NominalObserved);
                Assert.Equal(0, report.Output!.Value.NonZeroFrames);
            }
            await session.DisposeAsync();
            Assert.True(report.Transport.Cleanup.Completed);
            using var lease = guard.TryAcquire(AudioActivity.ShortDiagnostic);
            Assert.NotNull(lease);
        }
    }

    [Fact]
    public async Task ConflictsAndRepeatAttemptsNeedFreshConsentAndQuiescence()
    {
        using var rig = new WasapiDriftTests.VirtualRig(48000, 48000, 0, 0);
        var graph = Topology(rig.Route, ExternalFlow.Browser);
        var config = Configuration(graph);
        var guard = new TransportActivityGuard();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var consent = config.Confirm(config.Applied, true, true, stop.Token);
        await using var browser = rig.CreateExternal(graph, ExternalFlow.Browser, consent, guard);
        Assert.Throws<InvalidOperationException>(() => rig.CreateExternal(graph, ExternalFlow.Browser, consent, guard));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => browser.RunAsync(TimeSpan.FromSeconds(5.1), stop.Token));
        using (guard.TryAcquire(AudioActivity.ShortDiagnostic))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => browser.RunAsync(TimeSpan.FromSeconds(5), stop.Token));
            Assert.Empty(rig.Calls.Log);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => browser.RunAsync(TimeSpan.FromSeconds(5), stop.Token));
        await using var recovered = rig.CreateExternal(graph, ExternalFlow.Browser,
            config.Confirm(config.Applied, true, true, stop.Token), guard);
        rig.Calls.Output.OnRenderSubmitted = (_, flags) => Assert.True(flags is 0 or 2);
        var run = recovered.RunAsync(TimeSpan.FromSeconds(5), stop.Token);
        await WaitForRunning(recovered, run);
        stop.Cancel();
        Assert.True((await run).Transport.Cleanup.Completed);
    }

    [Fact]
    public Task ExternalEncoderKeepsItsMeasuredWindowsAllocationFree() =>
        AllocationIsolation.Run<ExternalRouteTests>(MeasureEncoder, TestContext.Current.CancellationToken);

    private static void MeasureEncoder()
    {
        var format = WasapiContractTests.Route().Playback.Mix!;
        var encoder = new WasapiMonitorEncoder(format, compareGains: true);
        float[] samples = new float[format.Channels * 128];
        Array.Fill(samples, .5f);
        samples[0] = float.NaN;
        var output = new byte[128 * format.BlockAlign];
        for (int i = 0; i < 500; i++) encoder.Encode(samples, output, out _);
        encoder.SetGain(WasapiExternalRouteSession.EffectGain);
        for (int i = 0; i < 100; i++) encoder.Encode(samples, output, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        WasapiEncodedBlock last = default;
        for (int i = 0; i < 1000; i++) encoder.Encode(samples, output, out last);
        long measured = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, measured);
        Assert.InRange(last.PeakAmplitude, .0062f, .0063f);
        Assert.Equal(0, BinaryPrimitives.ReadSingleLittleEndian(output));
    }

    internal static ExternalRouteTopology Topology(TransportRoute route, ExternalFlow flow)
    {
        long generation = route.Capture.Endpoint.Generation;
        ExternalEndpoint Port(string id, string adapter, EndpointDirection direction, WasapiEndpointInfo? selected = null) =>
            new(selected ?? (direction == EndpointDirection.Capture ? route.Capture : route.Playback) with
            { Endpoint = new TransportEndpoint(id, direction, generation) }, adapter);
        var a = new ExternalCable(Port("a-render", "A", EndpointDirection.Playback),
            Port("a-capture", "A", EndpointDirection.Capture, flow == ExternalFlow.Browser ? route.Capture : null));
        var b = new ExternalCable(Port("b-render", "B", EndpointDirection.Playback, flow == ExternalFlow.Microphone ? route.Playback : null),
            Port("b-capture", "B", EndpointDirection.Capture));
        return new(a, b, Port("headset", "physical-render", EndpointDirection.Playback, flow == ExternalFlow.Browser ? route.Playback : null),
            Port("microphone", "physical-capture", EndpointDirection.Capture, flow == ExternalFlow.Microphone ? route.Capture : null));
    }

    internal static ExternalRouteConfiguration Configuration(ExternalRouteTopology graph)
    {
        var a = new ExternalSelection("browser/profile", null, true, true);
        var b = new ExternalSelection("obs/scene/source", "original", true, true);
        return new(graph, new(a, b));
    }

    private static async Task WaitForRunning(WasapiExternalRouteSession session, Task<ExternalRouteReport> run)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        while (!session.Running)
        {
            if (run.IsCompleted) Assert.Fail("A sessão virtual encerrou na preparação: " + (await run).Transport.Cleanup.Failure);
            await Task.Delay(1, timeout.Token);
        }
    }
}
