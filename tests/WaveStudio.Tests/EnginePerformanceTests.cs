using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

[CollectionDefinition("Synthetic engine measurement", DisableParallelization = true)]
public sealed class EngineMeasurementCollection;

[Collection("Synthetic engine measurement")]
public sealed class EnginePerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void WarmPipelineHasNoRecurringAllocationAndReportsSyntheticThroughput()
    {
        const int framesPerBlock = 256, blocks = 200000, rate = 48000;
        using var pipeline = new SyntheticAudioPipeline(new(rate, 2), new(rate, 2), 1024, framesPerBlock);
        long generation = pipeline.Start(TestContext.Current.CancellationToken);
        var input = new byte[framesPerBlock * 2 * 2];
        var target = new float[framesPerBlock * 2];
        for (int i = 0; i < input.Length; i += 2) { input[i] = 0; input[i + 1] = 32; } // +0.25 PCM16
        for (int i = 0; i < 10000; i++)
        {
            pipeline.ProducePcm(generation, input, PcmEncoding.Pcm16LittleEndian);
            pipeline.Render(generation, target);
        }
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < blocks; i++)
        {
            if (pipeline.ProducePcm(generation, input, PcmEncoding.Pcm16LittleEndian) != framesPerBlock ||
                pipeline.Render(generation, target) != framesPerBlock) throw new InvalidOperationException("Perda inesperada no ensaio alternado.");
        }
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        double cpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        Assert.Equal(0, allocatedBytes);
        Assert.All(target, value => Assert.Equal(.25f, value));
        var snapshot = pipeline.Snapshot();
        Assert.Equal(0, snapshot.Buffer!.Value.OverrunFrames);
        Assert.Equal(0, snapshot.Buffer.Value.UnderrunFrames);
        var metrics = new
        {
            Configuration = typeof(EnginePerformanceTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration,
            Frames = (long)blocks * framesPerBlock, Channels = 2, SampleRate = rate, Blocks = blocks,
            WallMilliseconds = milliseconds, ProcessCpuMilliseconds = cpuMilliseconds, AllocatedBytes = allocatedBytes,
            FramesPerSecond = blocks * (double)framesPerBlock * 1000 / milliseconds,
            MeanBlockMilliseconds = milliseconds / blocks, BufferCapacityMilliseconds = snapshot.CapacityMilliseconds,
            StorageBytes = snapshot.StorageBytes, Scope = "PCM16 -> float32 -> ring -> neutral protection; synthetic, no physical audio"
        };
        string json = JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(json);
        // Gera o relatório somente depois do trecho medido. Nenhum sinal ou áudio é gravado.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WaveStudio.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string directory = Path.Combine(root.FullName, "artifacts", "engine-foundation");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"metrics-{metrics.Configuration}.json"), json);
    }
}
