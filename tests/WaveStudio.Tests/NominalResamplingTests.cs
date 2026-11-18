using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class NominalResamplingTests
{
    internal static float[] Convert(NominalResampler resampler, float[] input, int channels,
        int inputBlock = 4096, int outputBlock = 8192, bool irregular = false)
    {
        var result = new List<float>();
        var output = new float[outputBlock * channels];
        int offset = 0, iterations = 0;
        while (offset < input.Length / channels)
        {
            if (++iterations > input.Length * 4 + 1000) throw new InvalidOperationException("Fluxo não avançou.");
            int frames = Math.Min(irregular ? 1 + iterations * 17 % inputBlock : inputBlock, input.Length / channels - offset);
            int capacity = irregular ? 1 + iterations * 11 % outputBlock : outputBlock;
            var step = resampler.Process(input.AsSpan(offset * channels, frames * channels), output.AsSpan(0, capacity * channels));
            if (step.ConsumedFrames == 0 && step.ProducedFrames == 0) throw new InvalidOperationException("Sem progresso.");
            result.AddRange(output.AsSpan(0, step.ProducedFrames * channels).ToArray());
            offset += step.ConsumedFrames;
        }
        resampler.BeginFlush();
        while (!resampler.Completed)
        {
            var step = resampler.Flush(output);
            Assert.True(step.ProducedFrames > 0 || step.Completed);
            result.AddRange(output.AsSpan(0, step.ProducedFrames * channels).ToArray());
        }
        Assert.Equal(resampler.ExpectedOutputFrames(input.Length / channels), result.Count / channels);
        return result.ToArray();
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 2, 0)]
    [InlineData(1, 2, 1)]
    [InlineData(2, 1, 2)]
    public void ChannelPoliciesAreExplicitFiniteAndDoNotBoost(int fromValue, int toValue, int policyValue)
    {
        var from = (ChannelLayout)fromValue; var to = (ChannelLayout)toValue; var policy = (ChannelPolicy)policyValue;
        var adapter = new ChannelAdapter(from, to, policy);
        float[] input = from == ChannelLayout.Mono ? [.25f, float.NaN, 1] : [.5f, -.5f, float.NaN, .5f, 1, 1];
        float[] expected = policy switch
        {
            ChannelPolicy.DuplicateMono => [.25f, .25f, 0, 0, 1, 1],
            ChannelPolicy.AverageStereo => [0, .25f, 1],
            _ => from == ChannelLayout.Mono ? [.25f, 0, 1] : [.5f, -.5f, 0, .5f, 1, 1]
        };
        var output = new float[expected.Length];
        var result = adapter.Process(input, output);
        Assert.Equal(3, result.Frames);
        Assert.Equal(1, result.SanitizedSamples);
        Assert.Equal(expected, output);
    }

    [Fact]
    public void InvalidPoliciesLayoutsBuffersAndOverlapAreRejected()
    {
        Assert.Throws<NotSupportedException>(() => new ChannelAdapter((ChannelLayout)6, ChannelLayout.Mono, ChannelPolicy.Preserve));
        Assert.Throws<ArgumentException>(() => new ChannelAdapter(ChannelLayout.Mono, ChannelLayout.StereoLeftRight, ChannelPolicy.Preserve));
        var adapter = new ChannelAdapter(ChannelLayout.StereoLeftRight, ChannelLayout.Mono, ChannelPolicy.AverageStereo);
        Assert.Throws<ArgumentException>(() => adapter.Process(new float[3], new float[1]));
        Assert.Throws<ArgumentException>(() => adapter.Process(new float[4], new float[3]));
        Assert.Throws<ArgumentException>(() => adapter.Process(new float[8194], new float[4097]));
        var shared = new float[4];
        Assert.Throws<ArgumentException>(() => adapter.Process(shared, shared.AsSpan(0, 2)));
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(96000)]
    public void EqualRatesAreExactAndHaveNoFilterDelay(int rate)
    {
        var resampler = new NominalResampler(rate, rate, 2);
        float[] input = [-0f, .25f, -.75f, .5f, float.PositiveInfinity, float.NaN];
        var output = Convert(resampler, input, 2, 1, 1);
        Assert.Equal(new float[] { -0f, .25f, -.75f, .5f, 0, 0 }, output);
        Assert.Equal(BitConverter.SingleToInt32Bits(input[0]), BitConverter.SingleToInt32Bits(output[0]));
        Assert.Equal(0, resampler.GroupDelayMilliseconds);
        Assert.Equal(2, resampler.SanitizedSamples);
    }

    [Theory]
    [InlineData(44100, 48000)]
    [InlineData(48000, 44100)]
    [InlineData(96000, 48000)]
    public void FragmentationSmallOutputAndResetPreserveIdenticalSequence(int inputRate, int outputRate)
    {
        float[] input = Enumerable.Range(0, 3007).SelectMany(i => new[] { (float)Math.Sin(i * .13) * .25f, (float)Math.Cos(i * .07) * .2f }).ToArray();
        var reference = Convert(new(inputRate, outputRate, 2), input, 2);
        var fragmented = new NominalResampler(inputRate, outputRate, 2);
        Assert.Equal(reference, Convert(fragmented, input, 2, 7, 1));
        fragmented.Reset();
        Assert.False(fragmented.Completed);
        Assert.Equal(0, fragmented.InputFrames);
        Assert.Equal(reference, Convert(fragmented, input, 2, 113, 17, irregular: true));
        Assert.Equal(0, fragmented.Flush(new float[8]).ProducedFrames);
        Assert.Throws<InvalidOperationException>(() => fragmented.Process(new float[2], new float[2]));
    }

    [Theory]
    [InlineData(44100, 48000)]
    [InlineData(48000, 44100)]
    [InlineData(96000, 48000)]
    public void ImpulseDelayAndChannelIsolationAreExplicit(int inputRate, int outputRate)
    {
        var input = new float[2048];
        input[0] = 1;
        var resampler = new NominalResampler(inputRate, outputRate, 2);
        var output = Convert(resampler, input, 2, 13, 11);
        int peak = 0;
        for (int i = 0; i < output.Length / 2; i++)
        {
            if (Math.Abs(output[i * 2]) > Math.Abs(output[peak * 2])) peak = i;
            Assert.Equal(0, output[i * 2 + 1]);
        }
        Assert.InRange(Math.Abs(peak - NominalResampler.Radius * (double)outputRate / inputRate), 0, 1);
        Assert.Equal(NominalResampler.Radius * 1000d / inputRate, resampler.GroupDelayMilliseconds);
    }

    [Theory]
    [InlineData(44100, 48000)]
    [InlineData(48000, 44100)]
    [InlineData(96000, 48000)]
    public void LongCountIsExactWithoutFloatingPhaseDrift(int inputRate, int outputRate)
    {
        int frames = inputRate * 2 + 17;
        var resampler = new NominalResampler(inputRate, outputRate, 1);
        var result = Convert(resampler, new float[frames], 1, 129, 71);
        Assert.Equal(((long)frames + 160) * outputRate / inputRate + ((((long)frames + 160) * outputRate) % inputRate == 0 ? 0 : 1), result.LongLength);
        Assert.Equal(frames, resampler.InputFrames);
        Assert.Equal(result.LongLength, resampler.OutputFrames);
        Assert.All(result, value => Assert.Equal(0, value));
        // Verifica a contagem de longa duração sem alocar ou filtrar um dia inteiro de áudio.
        long dayFrames = (long)inputRate * 86400 + 17;
        Assert.Equal(((dayFrames + 160) * outputRate + inputRate - 1) / inputRate,
            resampler.ExpectedOutputFrames(dayFrames));
        Assert.Throws<ArgumentOutOfRangeException>(() => resampler.ExpectedOutputFrames(-1));
        Assert.Throws<OverflowException>(() => resampler.ExpectedOutputFrames(long.MaxValue));
    }

    [Fact]
    public void NonFiniteSamplesAreRemovedBeforeHistoryAndFinalProtectionIsSeparate()
    {
        var input = new float[2000];
        input[200] = float.NaN;
        input[201] = float.PositiveInfinity;
        var resampler = new NominalResampler(44100, 48000, 1);
        var output = Convert(resampler, input, 1);
        Assert.Equal(2, resampler.SanitizedSamples);
        Assert.All(output, value => Assert.Equal(0, value));
        var stage = new NominalConversionStage(new(48000, 2), new(48000, 1), ChannelPolicy.AverageStereo);
        float[] destination = new float[2];
        stage.Process([float.MaxValue, float.MaxValue, -float.MaxValue, -float.MaxValue], destination);
        Assert.Equal(new float[] { 1, -1 }, destination);
        var extreme = Convert(new(44100, 48000, 1), Enumerable.Repeat(float.MaxValue, 2000).ToArray(), 1);
        Assert.All(extreme, value => Assert.True(float.IsFinite(value)));
        // A saída bruta do FIR intencionalmente não recebe normalização ou clipping na faixa final de reprodução.
        Assert.Contains(extreme, value => value > 1);
        NeutralProcessor.Process(extreme);
        Assert.All(extreme, value => Assert.InRange(value, -1, 1));
    }

    [Fact]
    public void ResamplerValidatesBeforeChangingStateAndEmptyOutputDoesNotConsume()
    {
        Assert.Throws<NotSupportedException>(() => new NominalResampler(48000, 96000, 1));
        Assert.Throws<NotSupportedException>(() => new NominalResampler(8000, 8000, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NominalResampler(48000, 48000, 6));
        var resampler = new NominalResampler(44100, 48000, 2);
        Assert.Throws<ArgumentException>(() => resampler.Process(new float[3], new float[2]));
        Assert.Throws<ArgumentException>(() => resampler.Process(new float[8194], new float[2]));
        Assert.Throws<ArgumentException>(() => resampler.Process(new float[2], new float[16386]));
        var shared = new float[4];
        Assert.Throws<ArgumentException>(() => resampler.Process(shared, shared));
        Assert.Throws<InvalidOperationException>(() => resampler.Flush(new float[2]));
        Assert.Equal(0, resampler.Process(new float[2], []).ConsumedFrames);
        Assert.Equal(0, resampler.InputFrames);
        resampler.BeginFlush();
        Assert.True(resampler.Completed);
        Assert.Equal(0, resampler.Flush(new float[2]).ProducedFrames);
        Assert.InRange(new NominalConversionStage(new(44100, 1), new(48000, 2), ChannelPolicy.DuplicateMono).StorageBytes, 1, 137856);
    }

    [Theory]
    [InlineData(48000, 48000, 1, 2, 1)]
    [InlineData(44100, 48000, 2, 2, 0)]
    [InlineData(96000, 48000, 2, 1, 2)]
    public void ConvertedOutputFeedsExistingFifoInOutputFrameUnits(int inputRate, int outputRate, int inputChannels, int outputChannels, int policy)
    {
        var stage = new NominalConversionStage(new(inputRate, inputChannels), new(outputRate, outputChannels), (ChannelPolicy)policy);
        using var pipeline = new SyntheticAudioPipeline(stage.Output, stage.Output, 32, 32);
        long generation = pipeline.Start(TestContext.Current.CancellationToken);
        float[] source = Enumerable.Range(0, 301 * inputChannels).Select(i => .1f + i * .0001f).ToArray();
        var converted = new float[17 * outputChannels];
        var rendered = new float[17 * outputChannels];
        long produced = 0;
        int consumed = 0;
        while (consumed < 301)
        {
            int block = Math.Min(11, 301 - consumed);
            var step = stage.Process(source.AsSpan(consumed * inputChannels, block * inputChannels), converted);
            consumed += step.ConsumedFrames;
            Forward(step.ProducedFrames);
        }
        stage.BeginFlush();
        while (!stage.Completed) Forward(stage.Flush(converted).ProducedFrames);
        Assert.Equal(301, stage.InputFrames);
        Assert.Equal(stage.OutputFrames, produced);
        var snapshot = pipeline.Snapshot().Buffer!.Value;
        Assert.Equal(produced, snapshot.WrittenFrames);
        Assert.Equal(produced, snapshot.ReadFrames);
        Assert.Equal(0, snapshot.OverrunFrames);
        Assert.Equal(0, snapshot.UnderrunFrames);
        void Forward(int frames)
        {
            Assert.Equal(frames, pipeline.Produce(generation, converted.AsSpan(0, frames * outputChannels)));
            Assert.Equal(frames, pipeline.Render(generation, rendered.AsSpan(0, frames * outputChannels)));
            Assert.Equal(converted[..(frames * outputChannels)], rendered[..(frames * outputChannels)]);
            produced += frames;
        }
    }

    [Fact]
    public void OutputRateOverflowAndUnderrunDoNotPretendToBeInputFrames()
    {
        var stage = new NominalConversionStage(new(44100, 1), new(48000, 2), ChannelPolicy.DuplicateMono);
        var converted = new float[64];
        var step = stage.Process(new float[20], converted);
        using var pipeline = new SyntheticAudioPipeline(stage.Output, stage.Output, 8, 32);
        long generation = pipeline.Start(TestContext.Current.CancellationToken);
        int accepted = pipeline.Produce(generation, converted.AsSpan(0, step.ProducedFrames * 2));
        Assert.Equal(8, accepted);
        var rendered = new float[24];
        Assert.Equal(8, pipeline.Render(generation, rendered));
        var counters = pipeline.Snapshot().Buffer!.Value;
        Assert.Equal(step.ProducedFrames - 8, counters.OverrunFrames);
        Assert.Equal(4, counters.UnderrunFrames);
        Assert.Equal(step.ConsumedFrames, stage.InputFrames);
    }
}
