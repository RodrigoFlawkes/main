using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

[Collection("Synthetic engine measurement")]
public sealed class ResamplingQualityTests(ITestOutputHelper output)
{
    // Fixed before evaluating the filter; see docs/RESAMPLING_QUALITY.md.
    private const double MaximumPassbandErrorDb = .1;
    private const double MinimumStopbandRejectionDb = 60;
    private const double MaximumPassbandRippleDb = 2 * MaximumPassbandErrorDb;

    [Fact]
    public void MeasuredPassbandStopbandDcAndUpsamplingImagesMeetDeclaredGoals()
    {
        var measurements = new List<object>();
        foreach (var (inputRate, outputRate) in new[] { (44100, 48000), (48000, 44100), (96000, 48000) })
        {
            double maxError = 0, minimumGainDb = 0, maximumGainDb = 0;
            for (int bin = 1; bin <= 16; bin++)
            {
                double frequency = .4 * Math.Min(inputRate, outputRate) * bin / 16;
                var signal = Tone(inputRate, frequency);
                var converted = NominalResamplingTests.Convert(new(inputRate, outputRate, 1), signal, 1);
                double gain = Fit(converted, outputRate, frequency).Amplitude / .25;
                double gainDb = 20 * Math.Log10(gain);
                maxError = Math.Max(maxError, Math.Abs(gainDb));
                minimumGainDb = Math.Min(minimumGainDb, gainDb);
                maximumGainDb = Math.Max(maximumGainDb, gainDb);
            }
            var dc = NominalResamplingTests.Convert(new(inputRate, outputRate, 1), Enumerable.Repeat(.25f, inputRate / 8).ToArray(), 1);
            double dcError = dc[512..^512].Max(value => Math.Abs(value - .25));
            double? worstAliasDb = null;
            if (inputRate > outputRate)
            {
                for (int bin = 0; bin < 16; bin++)
                {
                    double frequency = outputRate / 2d + (inputRate / 2d - outputRate / 2d) * bin / 16;
                    var converted = NominalResamplingTests.Convert(new(inputRate, outputRate, 1), Tone(inputRate, frequency), 1);
                    double rms = Math.Sqrt(converted[512..^512].Average(value => (double)value * value));
                    double db = 20 * Math.Log10(Math.Max(rms, 1e-30) / (.25 / Math.Sqrt(2)));
                    worstAliasDb = Math.Max(worstAliasDb ?? double.NegativeInfinity, db);
                }
            }
            double? imageDb = null;
            if (inputRate < outputRate)
            {
                const double fundamental = 16000;
                double imageFrequency = outputRate - (inputRate - fundamental);
                var converted = NominalResamplingTests.Convert(new(inputRate, outputRate, 1), Tone(inputRate, fundamental), 1);
                var fit = Fit(converted, outputRate, fundamental);
                for (int index = 0; index < converted.Length; index++)
                    converted[index] -= (float)(fit.Sin * Math.Sin(2 * Math.PI * fundamental * index / outputRate) + fit.Cos * Math.Cos(2 * Math.PI * fundamental * index / outputRate));
                imageDb = 20 * Math.Log10(Math.Max(Fit(converted, outputRate, imageFrequency).Amplitude, 1e-30) / .25);
            }
            var record = new { InputRate = inputRate, OutputRate = outputRate, PassbandGridPoints = 16,
                MaxPassbandErrorDb = maxError, PassbandRippleDb = maximumGainDb - minimumGainDb,
                DcAbsoluteError = dcError, WorstAliasDb = worstAliasDb,
                UpsamplingImageDb = imageDb, GroupDelayMilliseconds = 80000d / inputRate };
            output.WriteLine(JsonSerializer.Serialize(record));
            measurements.Add(record);
            Assert.InRange(maxError, 0, MaximumPassbandErrorDb);
            Assert.InRange(maximumGainDb - minimumGainDb, 0, MaximumPassbandRippleDb);
            Assert.InRange(dcError, 0, 1e-5);
            if (worstAliasDb is double alias) Assert.True(alias <= -MinimumStopbandRejectionDb, $"Alias {alias:F3} dB");
            if (imageDb is double image) Assert.True(image <= -MinimumStopbandRejectionDb, $"Imagem {image:F3} dB");
        }
        SaveReport("resampling-quality", measurements);
    }

    [Fact]
    public void ConversionPlusSpscHasNoSteadyStateAllocationsAndReportsCost()
    {
        var stage = new NominalConversionStage(new(44100, 1), new(48000, 2), ChannelPolicy.DuplicateMono);
        using var pipeline = new SyntheticAudioPipeline(stage.Output, stage.Output, 1024, 512);
        long generation = pipeline.Start(TestContext.Current.CancellationToken);
        float[] source = Enumerable.Repeat(.125f, 256).ToArray();
        var converted = new float[1024];
        var rendered = new float[1024];
        void Tick()
        {
            var result = stage.Process(source, converted);
            if (result.ConsumedFrames != 256 || pipeline.Produce(generation, converted.AsSpan(0, result.ProducedFrames * 2)) != result.ProducedFrames ||
                pipeline.Render(generation, rendered.AsSpan(0, result.ProducedFrames * 2)) != result.ProducedFrames)
                throw new InvalidOperationException("Contabilidade inválida no ensaio sintético.");
        }
        for (int block = 0; block < 500; block++) Tick();
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        long inputStart = stage.InputFrames, outputStart = stage.OutputFrames;
        long allocated = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
        for (int block = 0; block < 2000; block++) Tick();
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        double cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        Assert.Equal(0, bytes);
        var metrics = new { InputRate = 44100, OutputRate = 48000, InputChannels = 1, OutputChannels = 2,
            InputFrames = stage.InputFrames - inputStart, OutputFrames = stage.OutputFrames - outputStart,
            WallMilliseconds = elapsed, ProcessCpuMilliseconds = cpuMs, AllocatedBytes = bytes,
            OutputFramesPerSecond = (stage.OutputFrames - outputStart) * 1000d / elapsed,
            StageStorageBytes = stage.StorageBytes, TotalPayloadBytes = stage.StorageBytes + pipeline.Snapshot().StorageBytes,
            stage.GroupDelayMilliseconds, Scope = "synthetic adaptation + FIR + SPSC + final protection; no physical latency" };
        output.WriteLine(JsonSerializer.Serialize(metrics));
        SaveReport("resampling-performance", metrics);
    }

    private static float[] Tone(int rate, double frequency) => Enumerable.Range(0, rate / 8)
        .Select(index => (float)(.25 * Math.Sin(2 * Math.PI * frequency * index / rate + .37))).ToArray();

    private static (double Sin, double Cos, double Amplitude) Fit(float[] samples, int rate, double frequency)
    {
        double ss = 0, cc = 0, sc = 0, ys = 0, yc = 0;
        for (int index = 512; index < samples.Length - 512; index++)
        {
            double sin = Math.Sin(2 * Math.PI * frequency * index / rate), cos = Math.Cos(2 * Math.PI * frequency * index / rate);
            ss += sin * sin; cc += cos * cos; sc += sin * cos;
            ys += samples[index] * sin; yc += samples[index] * cos;
        }
        double determinant = ss * cc - sc * sc;
        double a = (ys * cc - yc * sc) / determinant, b = (yc * ss - ys * sc) / determinant;
        return (a, b, Math.Sqrt(a * a + b * b));
    }
    private static void SaveReport(string name, object metrics)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WaveStudio.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string configuration = typeof(ResamplingQualityTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string directory = Path.Combine(root.FullName, "artifacts", "engine-foundation");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{name}-{configuration}.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
    }
}
