using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

internal static class AdaptiveMetrics
{
    internal static void Save(string name, object value)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WaveStudio.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string directory = Path.Combine(root.FullName, "artifacts", "adaptive-resampling");
        Directory.CreateDirectory(directory);
        string config = typeof(AdaptiveMetrics).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        File.WriteAllText(Path.Combine(directory, $"{name}-{config}.json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}

[Collection("Synthetic engine measurement")]
public sealed class AdaptiveResamplingQualityTests
{
    [Theory]
    [InlineData(48000, 48000, -500)]
    [InlineData(48000, 48000, 0)]
    [InlineData(48000, 48000, 500)]
    [InlineData(44100, 48000, -500)]
    [InlineData(44100, 48000, 0)]
    [InlineData(44100, 48000, 500)]
    [InlineData(48000, 44100, -500)]
    [InlineData(48000, 44100, 0)]
    [InlineData(48000, 44100, 500)]
    public void StaticExtremesMeetPredeclaredGainRippleDcAliasAndImageGoals(int inputRate, int outputRate, int ppm)
    {
        double effectiveOutput = outputRate * (1 + ppm * 1e-6);
        double worstOutput = outputRate * .9995;
        double minGain = 0, maxGain = 0;
        float[] Convert(float[] source) => AdaptiveResamplerTests.Convert(new(new(inputRate, 1), new(outputRate, 1), ChannelPolicy.Preserve, ppm), source);
        for (int point = 1; point <= 16; point++)
        {
            double frequency = .4 * Math.Min(inputRate, worstOutput) * point / 16;
            double gainDb = 20 * Math.Log10(Fit(Convert(Tone(inputRate, frequency)), effectiveOutput, frequency).Amplitude / .25);
            minGain = Math.Min(minGain, gainDb); maxGain = Math.Max(maxGain, gainDb);
        }
        var dc = Convert(Enumerable.Repeat(.25f, inputRate / 8).ToArray());
        double dcGainError = dc[512..^512].Max(value => Math.Abs(value / .25 - 1));
        double? worstAliasDb = null, imageDb = null;
        if (effectiveOutput < inputRate)
        {
            for (int point = 0; point < 16; point++)
            {
                double frequency = effectiveOutput / 2 + (inputRate / 2d - effectiveOutput / 2) * point / 16;
                var samples = Convert(Tone(inputRate, frequency));
                double rms = Math.Sqrt(samples[512..^512].Average(value => (double)value * value));
                worstAliasDb = Math.Max(worstAliasDb ?? double.NegativeInfinity, 20 * Math.Log10(Math.Max(rms, 1e-30) / (.25 / Math.Sqrt(2))));
            }
        }
        if (inputRate == 44100)
        {
            const double tone = 16000;
            var samples = Convert(Tone(inputRate, tone));
            var fit = Fit(samples, effectiveOutput, tone);
            for (int index = 0; index < samples.Length; index++)
                samples[index] -= (float)(fit.Sin * Math.Sin(2 * Math.PI * tone * index / effectiveOutput) + fit.Cos * Math.Cos(2 * Math.PI * tone * index / effectiveOutput));
            imageDb = 20 * Math.Log10(Math.Max(Fit(samples, effectiveOutput, effectiveOutput - (inputRate - tone)).Amplitude, 1e-30) / .25);
        }
        AdaptiveMetrics.Save($"quality-{inputRate}-{outputRate}-{ppm}", new { inputRate, outputRate, ppm,
            MaxGainErrorDb = Math.Max(Math.Abs(minGain), Math.Abs(maxGain)), RippleDb = maxGain - minGain,
            DcGainError = dcGainError, WorstAliasDb = worstAliasDb, ImageDb = imageDb });
        Assert.InRange(Math.Max(Math.Abs(minGain), Math.Abs(maxGain)), 0, .1);
        Assert.InRange(maxGain - minGain, 0, .2);
        Assert.InRange(dcGainError, 0, 1e-5);
        if (worstAliasDb is double alias) Assert.True(alias <= -60, $"Alias {alias:F3} dB");
        if (imageDb is double image) Assert.True(image <= -60, $"Imagem {image:F3} dB");
    }

    [Theory]
    [InlineData(48000, 48000)]
    [InlineData(44100, 48000)]
    [InlineData(48000, 44100)]
    public void ImpulseDelayAndIndependentChannelsArePreserved(int inputRate, int outputRate)
    {
        var source = new float[2048]; source[0] = 1;
        var stage = new AdaptiveResampler(new(inputRate, 2), new(outputRate, 2), ChannelPolicy.Preserve, 500);
        var output = AdaptiveResamplerTests.Convert(stage, source, 1, true);
        int peak = 0;
        for (int index = 0; index < output.Length / 2; index++)
        {
            if (Math.Abs(output[2 * index]) > Math.Abs(output[2 * peak])) peak = index;
            Assert.Equal(0, output[2 * index + 1]);
        }
        Assert.InRange(Math.Abs(peak - 80d * outputRate * 1.0005 / inputRate), 0, 1);
        Assert.Equal(80000d / inputRate, stage.GroupDelayMilliseconds);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(8000)]
    [InlineData(16000)]
    public void SlewedTransitionsMatchVariablePhaseReferenceIncludingPhaseBoundary(int frequency)
    {
        const int rate = 48000;
        var source = Enumerable.Range(0, rate * 4).Select(index => (float)(.25 * Math.Sin(2 * Math.PI * frequency * index / rate + .37))).ToArray();
        var stage = new AdaptiveResampler(new(rate, 1), new(rate, 1), ChannelPolicy.Preserve, -10);
        stage.SetTargetPpm(1, 10);
        var destination = new float[1];
        int offset = 0, compared = 0, seamCrossings = 0;
        double errorSquared = 0, maximumError = 0, priorPpm = stage.CurrentPpm;
        for (int frame = 0; frame < rate * 3; frame++)
        {
            if (frame == rate) stage.SetTargetPpm(1, -10);
            if (frame == rate * 2) stage.SetTargetPpm(1, 10);
            var phase = stage.Cursor;
            var step = stage.Process(1, source.AsSpan(offset, Math.Min(4096, source.Length - offset)), destination);
            Assert.Equal(1, step.ProducedFrames); offset += step.ConsumedFrames;
            Assert.InRange(Math.Abs(stage.CurrentPpm - priorPpm), 0, 25d / rate + 1e-12);
            priorPpm = stage.CurrentPpm;
            int oldPhase = (int)(phase.Fraction * AdaptiveResampler.Phases >> SampleCursor.Bits);
            int newPhase = (int)(stage.Cursor.Fraction * AdaptiveResampler.Phases >> SampleCursor.Bits);
            if ((oldPhase <= 1 && newPhase >= AdaptiveResampler.Phases - 2) || (newPhase <= 1 && oldPhase >= AdaptiveResampler.Phases - 2)) seamCrossings++;
            if (frame < 512) continue;
            double time = (phase.Index - 80 + phase.Fraction / (double)SampleCursor.Scale) / rate;
            double expected = .25 * Math.Sin(2 * Math.PI * frequency * time + .37);
            double error = destination[0] - expected;
            errorSquared += error * error; maximumError = Math.Max(maximumError, Math.Abs(error)); compared++;
        }
        double rmsDb = 20 * Math.Log10(Math.Sqrt(errorSquared / compared) / (.25 / Math.Sqrt(2)));
        AdaptiveMetrics.Save($"transition-{frequency}", new { frequency, RmsErrorDb = rmsDb, PeakError = maximumError, SeamCrossings = seamCrossings, stage.CurrentPpm });
        Assert.True(seamCrossings > 0);
        Assert.True(rmsDb <= -70, $"Resíduo {rmsDb:F3} dB");
        Assert.InRange(maximumError, 0, .0002);
    }

    [Fact]
    public void PartialOutputHasLinearMappingWorkNoRecurringAllocationsAndEquivalentAudio()
    {
        Measure(512); Measure(1);
        var regular = Measure(512); var tiny = Measure(1);
        Assert.Equal(regular.Hash, tiny.Hash);
        Assert.Equal(regular.OutputFrames, tiny.OutputFrames);
        Assert.Equal(8192, regular.MappedFrames); Assert.Equal(8192, tiny.MappedFrames);
        Assert.Equal(0, regular.AllocatedBytes); Assert.Equal(0, tiny.AllocatedBytes);
        AdaptiveMetrics.Save("cost", new { Regular = regular, OneFrame = tiny });
    }
    private readonly record struct Cost(long MappedFrames, long OutputFrames, ulong Hash, double WallMilliseconds,
        long AllocatedBytes, int StorageBytes, double FramesPerSecond);
    private static Cost Measure(int capacity)
    {
        var stage = new AdaptiveResampler(new(44100, 1), new(48000, 2), ChannelPolicy.DuplicateMono);
        stage.SetTargetPpm(1, 500); // Active slew in measured path, not only a cached static step.
        var source = Enumerable.Range(0, 8192).Select(index => .25f * (float)Math.Sin(index * .1)).ToArray();
        var destination = new float[capacity * 2];
        int offset = 0; ulong hash = 14695981039346656037;
        long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        while (offset < source.Length)
        {
            var result = stage.Process(1, source.AsSpan(offset, Math.Min(4096, source.Length - offset)), destination);
            offset += result.ConsumedFrames; Hash(result.ProducedFrames);
        }
        stage.BeginFlush(1);
        while (!stage.Completed) Hash(stage.Flush(1, destination).ProducedFrames);
        double wall = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return new(stage.MappedFrames, stage.OutputFrames, hash, wall, GC.GetAllocatedBytesForCurrentThread() - bytes, stage.StorageBytes, stage.OutputFrames * 1000 / wall);
        void Hash(int frames)
        {
            for (int index = 0; index < frames * 2; index++) hash = unchecked((hash ^ (uint)BitConverter.SingleToInt32Bits(destination[index])) * 1099511628211);
        }
    }
    private static float[] Tone(int rate, double frequency) => Enumerable.Range(0, rate / 8)
        .Select(index => (float)(.25 * Math.Sin(2 * Math.PI * frequency * index / rate + .37))).ToArray();
    private static (double Sin, double Cos, double Amplitude) Fit(float[] samples, double rate, double frequency)
    {
        double ss = 0, cc = 0, sc = 0, ys = 0, yc = 0;
        for (int index = 512; index < samples.Length - 512; index++)
        {
            double sin = Math.Sin(2 * Math.PI * frequency * index / rate), cos = Math.Cos(2 * Math.PI * frequency * index / rate);
            ss += sin * sin; cc += cos * cos; sc += sin * cos; ys += samples[index] * sin; yc += samples[index] * cos;
        }
        double determinant = ss * cc - sc * sc;
        double a = (ys * cc - yc * sc) / determinant, b = (yc * ss - ys * sc) / determinant;
        return (a, b, Math.Sqrt(a * a + b * b));
    }
}
