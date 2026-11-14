using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class AdaptiveResamplerTests
{
    internal static float[] Convert(AdaptiveResampler resampler, float[] input, int outputCapacity = 512, bool irregular = false)
    {
        var result = new List<float>();
        var destination = new float[outputCapacity * resampler.Output.Channels];
        int offset = 0, iteration = 0;
        while (offset < input.Length / resampler.Input.Channels)
        {
            int frames = Math.Min(irregular ? 1 + iteration++ % 97 : 4096, input.Length / resampler.Input.Channels - offset);
            var step = resampler.Process(resampler.Generation, input.AsSpan(offset * resampler.Input.Channels, frames * resampler.Input.Channels), destination);
            Assert.True(step.ConsumedFrames + step.ProducedFrames > 0);
            offset += step.ConsumedFrames;
            result.AddRange(destination.AsSpan(0, step.ProducedFrames * resampler.Output.Channels).ToArray());
            Assert.All(destination[(step.ProducedFrames * resampler.Output.Channels)..], value => Assert.Equal(0, value));
        }
        resampler.BeginFlush(resampler.Generation);
        long remaining = resampler.RemainingFlushFrames, before = resampler.OutputFrames;
        while (!resampler.Completed)
        {
            var step = resampler.Flush(resampler.Generation, destination);
            Assert.True(step.ProducedFrames > 0 || step.Completed);
            result.AddRange(destination.AsSpan(0, step.ProducedFrames * resampler.Output.Channels).ToArray());
        }
        Assert.Equal(remaining, resampler.OutputFrames - before);
        return result.ToArray();
    }

    [Theory]
    [InlineData(48000, 48000)]
    [InlineData(44100, 48000)]
    [InlineData(48000, 44100)]
    public void FragmentationOneFrameAndResetPreserveIdenticalSamples(int inputRate, int outputRate)
    {
        float[] source = Enumerable.Range(0, 4001).SelectMany(index => new[] { .2f * (float)Math.Sin(index * .11), .15f * (float)Math.Cos(index * .07) }).ToArray();
        source[^1] = float.NaN; source[^2] = float.PositiveInfinity;
        var first = new AdaptiveResampler(new(inputRate, 2), new(outputRate, 2), ChannelPolicy.Preserve, 123.5);
        var expected = Convert(first, source);
        var fragmented = new AdaptiveResampler(first.Input, first.Output, ChannelPolicy.Preserve, 123.5);
        Assert.Equal(expected, Convert(fragmented, source, 1, irregular: true));
        Assert.Equal(4001, fragmented.InputFrames);
        Assert.Equal(4001, fragmented.MappedFrames);
        Assert.Equal(2, fragmented.Protection.InputNonFinite);
        fragmented.Reset(2, 123.5);
        Assert.Equal(default, fragmented.Protection);
        Assert.Equal(expected, Convert(fragmented, source, 17, irregular: true));
        Assert.Equal(0, fragmented.RemainingFlushFrames);
        Assert.Equal(0, fragmented.Flush(2, new float[10]).ProducedFrames);
    }

    [Fact]
    public void MappingAndProtectionCountOnlyAcceptedInputAndActualOutput()
    {
        var source = new float[4096]; source[^1] = float.NaN;
        var stage = new AdaptiveResampler(new(48000, 1), new(48000, 2), ChannelPolicy.DuplicateMono);
        var one = new float[2];
        Assert.Equal(1, stage.Process(1, source, one).ConsumedFrames);
        Assert.Equal(0, stage.Protection.InputNonFinite);
        int offset = 1;
        while (offset < source.Length)
            offset += stage.Process(1, source.AsSpan(offset), one).ConsumedFrames;
        Assert.Equal(4096, stage.MappedFrames);
        Assert.Equal(1, stage.Protection.InputNonFinite);
        Assert.Equal(0, stage.Protection.OutputNonFinite);
        stage.Reset(2);
        var extreme = Convert(stage, Enumerable.Repeat(float.MaxValue, 1000).ToArray());
        Assert.All(extreme, value => Assert.InRange(value, -1, 1));
        Assert.True(stage.Protection.OutputClipped > 0);
        Assert.True(stage.Protection.RepresentationSaturations > 0);
        Assert.Equal(0, stage.Protection.InputNonFinite);
    }

    [Theory]
    [InlineData(48000, 48000)]
    [InlineData(44100, 48000)]
    [InlineData(48000, 44100)]
    public void EofFreezesEffectiveRatioAndCountsRemainingPhaseInsteadOfFixedFormula(int inputRate, int outputRate)
    {
        var stage = new AdaptiveResampler(new(inputRate, 1), new(outputRate, 1), ChannelPolicy.Preserve);
        Assert.True(stage.SetTargetPpm(1, 500));
        var output = new float[2048];
        var result = stage.Process(1, new float[997], output);
        Assert.Equal(997, result.ConsumedFrames);
        Assert.InRange(stage.CurrentPpm, .001, 499);
        stage.BeginFlush(1);
        double frozen = stage.CurrentPpm;
        long remaining = stage.RemainingFlushFrames, produced = 0;
        while (!stage.Completed) produced += stage.Flush(1, output.AsSpan(0, 1)).ProducedFrames;
        Assert.Equal(remaining, produced);
        Assert.Equal(frozen, stage.CurrentPpm);
        Assert.Equal(997, stage.InputFrames);
        Assert.Throws<InvalidOperationException>(() => stage.SetTargetPpm(1, 0));
        Assert.Throws<InvalidOperationException>(() => stage.Process(1, [], output));
    }

    [Fact]
    public void InvalidBuffersRatiosGenerationsAndStoppedTrajectoryCannotChangeState()
    {
        Assert.Throws<NotSupportedException>(() => new AdaptiveResampler(new(96000, 1), new(48000, 1), ChannelPolicy.Preserve));
        Assert.Throws<ArgumentException>(() => new AdaptiveResampler(new(48000, 1), new(48000, 2), ChannelPolicy.Preserve));
        var stage = new AdaptiveResampler(new(48000, 2), new(48000, 2), ChannelPolicy.Preserve);
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, -501, 501 })
            Assert.Throws<ArgumentOutOfRangeException>(() => stage.SetTargetPpm(1, bad));
        Assert.Throws<ArgumentException>(() => stage.Process(1, new float[3], new float[2]));
        Assert.Throws<ArgumentException>(() => stage.Process(1, new float[8194], new float[2]));
        Assert.Throws<ArgumentException>(() => stage.Process(1, new float[2], new float[16386]));
        var shared = new float[4];
        Assert.Throws<ArgumentException>(() => stage.Process(1, shared, shared));
        Assert.Equal(0, stage.Process(1, new float[2], []).ConsumedFrames);
        Assert.Equal(0, stage.MappedFrames);
        Assert.Throws<InvalidOperationException>(() => stage.Flush(1, new float[2]));
        stage.Stop(1);
        Assert.False(stage.SetTargetPpm(1, 100));
        Assert.Equal(0, stage.Process(1, new float[2], shared).ProducedFrames);
        Assert.All(shared, sample => Assert.Equal(0, sample));
        stage.Reset(2);
        Assert.False(stage.SetTargetPpm(1, -100));
        Assert.Equal(0, stage.Process(1, new float[2], shared).ConsumedFrames);
        Assert.Equal(0, stage.InputFrames);
        Assert.Throws<ArgumentOutOfRangeException>(() => stage.Reset(2));
        stage.BeginFlush(2);
        Assert.True(stage.Completed);
        Assert.Equal(0, stage.Flush(2, shared).ProducedFrames);
        Assert.InRange(stage.StorageBytes, 1, 167564);
    }

    [Fact]
    public void Q48CarryPrecisionAndOverflowAreExplicit()
    {
        ulong step = (ulong)Math.Round(SampleCursor.Scale / (48000d / 44100 * 1.000123));
        var cursor = new SampleCursor(12345678901234567, SampleCursor.Mask - 7);
        long start = cursor.Index; ulong fraction = cursor.Fraction;
        for (int index = 0; index < 1000000; index++) cursor.Advance(step);
        UInt128 total = (UInt128)fraction + (UInt128)step * 1000000;
        Assert.Equal(start + (long)(total >> SampleCursor.Bits), cursor.Index);
        Assert.Equal((ulong)(total & SampleCursor.Mask), cursor.Fraction);
        // Half a Q48 unit per output step gives this worst-case quantization budget in one day.
        Assert.InRange(48000d * 86400 / (2 * SampleCursor.Scale), 0, .00001);
        var extreme = new SampleCursor(long.MaxValue, SampleCursor.Mask);
        Assert.Throws<OverflowException>(() => extreme.Advance(step));
        Assert.Equal(long.MaxValue, extreme.Index);
        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.Advance(0));
    }
}
