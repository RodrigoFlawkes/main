using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiAudibleProofTests
{
    [Fact]
    public void ConsentRequiresHeadsetLowLevelAndCancellableStop()
    {
        using var rig = new WasapiDriftTests.VirtualRig(48000, 48000, 0, 0);
        using var stop = new CancellationTokenSource();
        Assert.Throws<InvalidOperationException>(() => WasapiAudibleConsent.Confirm(rig.Route, false, true, stop.Token));
        Assert.Throws<InvalidOperationException>(() => WasapiAudibleConsent.Confirm(rig.Route, true, false, stop.Token));
#pragma warning disable xUnit1051 // O teste exercita exatamente a ausência de um token cancelável.
        Assert.Throws<InvalidOperationException>(() => WasapiAudibleConsent.Confirm(rig.Route, true, true, CancellationToken.None));
#pragma warning restore xUnit1051
        var consent = WasapiAudibleConsent.Confirm(rig.Route, true, true, stop.Token);
        using var other = new WasapiDriftTests.VirtualRig(44100, 48000, 0, 0);
        Assert.Throws<InvalidOperationException>(() => consent.Consume(other.Route));
        consent.Consume(rig.Route);
        Assert.Throws<InvalidOperationException>(() => consent.Consume(rig.Route));
        stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => WasapiAudibleConsent.Confirm(rig.Route, true, true, stop.Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AudibleProofUsesCoherentWarmupAndOnlyCountsConfirmedPcm(bool floating)
    {
        using var rig = new WasapiDriftTests.VirtualRig(44100, 48000, 0, 0, outputFloat: floating);
        using var stop = new CancellationTokenSource();
        var consent = WasapiAudibleConsent.Confirm(rig.Route, true, true, stop.Token);
        var guard = new TransportActivityGuard();
        await using var session = rig.CreateAudible(guard, consent);
        int block = 0, firstPcm = 0;
        long pcm = 0, silent = 0;
        rig.Calls.Output.OnRenderSubmitted = (frames, flags) =>
        {
            if (flags == 2) { silent += frames; return; }
            Assert.Equal(0u, flags);
            if (firstPcm == 0) firstPcm = block;
            pcm += frames;
        };
        var run = session.RunAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
        await WaitForRunning(session, run);
        for (block = 1; block <= 400 && !run.IsCompleted; block++)
        {
            rig.Advance(block, run);
            // Adia a primeira confirmação mesmo depois de preencher a FIFO.
            if (block >= 10) session.Step();
        }
        var report = await run;
        Assert.True(report.Transport.NominalObserved);
        Assert.False(report.Transport.RenderSilent);
        Assert.Equal(11, firstPcm);
        var output = Assert.IsType<WasapiAudibleOutput>(report.Output);
        Assert.Equal(pcm, output.PcmFrames);
        Assert.Equal(silent, output.SilentFrames);
        Assert.Equal(report.Transport.Cleanup.Playback.FramesReleased, pcm + silent);
        Assert.InRange(output.NonZeroFrames, 1, pcm);
        Assert.InRange(output.PeakAmplitude, .001f, .05002f);
        Assert.Equal(.05f, output.PeakGain);
        Assert.Equal(0, output.ClippedSamples);
    }

    [Fact]
    public async Task OperatorCancellationAtAcquisitionKeepsTheUnsubmittedBufferSilentAndReleasesGuard()
    {
        using var rig = new WasapiDriftTests.VirtualRig(48000, 48000, 0, 0);
        using var stop = new CancellationTokenSource();
        var guard = new TransportActivityGuard();
        var consent = WasapiAudibleConsent.Confirm(rig.Route, true, true, stop.Token);
        await using var session = rig.CreateAudible(guard, consent);
        int pcm = 0;
        bool finalBufferSilent = false;
        rig.Calls.Output.OnRenderSubmitted = (frames, flags) =>
        {
            if (flags == 0) pcm++;
            if (!stop.IsCancellationRequested) return;
            Assert.Equal(2u, flags);
            Assert.Equal(-1, rig.Calls.Output.Data.AsSpan(0, (int)frames * rig.Route.Playback.Mix!.BlockAlign).IndexOfAnyExcept((byte)0));
            finalBufferSilent = true;
        };
        var run = session.RunAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
        await WaitForRunning(session, run);
        for (int i = 1; i <= 12; i++) { rig.Advance(i, run); session.Step(); }
        Assert.True(pcm > 0);
        rig.Calls.Output.OnStep = step => { if (step == "GetRender") stop.Cancel(); };
        rig.Advance(13, run);
        var result = await run;
        Assert.True(result.Transport.Cleanup.Completed);
        Assert.False(result.Transport.Cleanup.DurationReached);
        Assert.True(finalBufferSilent);
        using var lease = guard.TryAcquire(AudioActivity.NativePreparation);
        Assert.NotNull(lease);
    }

    private static async Task WaitForRunning(WasapiAudibleProofSession session, Task<WasapiAudibleReport> run)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        while (!session.Running)
        {
            if (run.IsCompleted) Assert.Fail("A sessão encerrou durante a preparação: " + (await run).Transport.Cleanup.Failure);
            await Task.Delay(1, timeout.Token);
        }
    }
}
