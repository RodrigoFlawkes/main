using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed partial class WasapiDriftTests
{
    [Theory]
    [InlineData(44100, 48000, 300)]
    [InlineData(48000, 44100, -300)]
    [InlineData(48000, 48000, 0)]
    public void BoundsContainTrueDriftWithIndependentClockOriginsAndJitter(int input, int output, int drift)
    {
        var first = Pair(0, input, output, drift);
        var last = Pair(30_000_000, input, output, drift);
        var range = WasapiDriftMath.Estimate(first, last, input, output, new(2, 2, 2));

        Assert.NotNull(range);
        double idealPpm = (1 / (1 + drift * 1e-6) - 1) * 1e6;
        Assert.InRange(idealPpm, range.Value.Minimum, range.Value.Maximum);
        Assert.InRange(range.Value.Width, 0, 160);
        if (drift == 0) Assert.Equal(0, range.Value.NearestZero);
        else Assert.Equal(-Math.Sign(drift), Math.Sign(range.Value.NearestZero));
    }

    [Fact]
    public void APeriodOfPhaseCanExplainTheEntireApparentCorrection()
    {
        var first = Pair(0, 48000, 48000, 300);
        var last = Pair(30_000_000, 48000, 48000, 300);
        var range = WasapiDriftMath.Estimate(first, last, 48000, 48000, new(480, 480, 480));

        Assert.NotNull(range);
        Assert.True(range.Value.Width > 2 * AdaptiveResampler.MaximumPpm);
        Assert.Equal(0, range.Value.NearestZero);
        Assert.Null(WasapiDriftMath.Estimate(first, last with { CaptureEpoch = first.CaptureEpoch + 1 }, 48000, 48000, new(2, 2, 2)));
    }

    [Fact]
    public void DisagreeingCaptureSeriesAndIncompatibleClockUnitsWithholdAnEstimate()
    {
        var first = Pair(0, 48000, 48000, 300);
        var last = Pair(30_000_000, 48000, 48000, 300);
        Assert.Null(WasapiDriftMath.Estimate(first, last with { Packet = last.Packet with { Position = last.Packet.Position - 40 } },
            48000, 48000, new(2, 2, 2)));
        Assert.Null(WasapiDriftMath.Estimate(first, last with { Render = last.Render with { Frequency = 1 } },
            48000, 48000, new(2, 2, 2)));
    }

    [Fact]
    public void ControlUsesTheWholeIntervalAndReprimesAfterAnExpiredUpdate()
    {
        var controller = new ClockRatioController(48000, 48000, 10_000_000);
        var neutral = controller.UpdateBounded(1_000_000, 980, 1150, new(-20, 20));
        Assert.Equal(DriftControlState.Tracking, neutral.State);
        Assert.Equal(0, neutral.CorrectionPpm);
        var signed = controller.UpdateBounded(2_000_000, 980, 1150, new(-350, -250));
        Assert.Equal(-2.5, signed.CorrectionPpm);
        Assert.Equal(DriftControlState.ReprimeRequired, controller.UpdateBounded(7_000_001, 980, 1150, new(-350, -250)).State);
    }

    private static WasapiDriftPair Pair(long offset, int input, int output, int drift)
    {
        const ulong origin = 1_000_000_000;
        ulong t = origin + (ulong)offset;
        return new(
            new((ulong)(40_000_000 + Math.Floor(offset * (input * 4 * (1 + drift * 1e-6) / 10_000_000))), (ulong)input * 4, t),
            new((ulong)(90_000_000 + Math.Floor((offset + 12) * (output * 8d / 10_000_000))), (ulong)output * 8, t + 12),
            new((ulong)(500_000_000 + Math.Floor((offset + 23) * (input * (1 + drift * 1e-6) / 10_000_000))), (ulong)input, t + 23),
            0, 0);
    }
}
