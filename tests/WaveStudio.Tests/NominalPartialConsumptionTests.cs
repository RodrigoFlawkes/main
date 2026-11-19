using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

[Collection("Synthetic engine measurement")]
public sealed class NominalPartialConsumptionTests(ITestOutputHelper output)
{
    private readonly record struct Cost(long OfferedFrames, long ConsumedFrames, long ProducedFrames,
        ulong Hash, double WallMilliseconds, long AllocatedBytes);

    [Fact]
    public Task SmallOutputPreservesSamplesButRepeatsChannelMappingWork() =>
        AllocationIsolation.Run<NominalPartialConsumptionTests>(MeasureAllocation, TestContext.Current.CancellationToken);

    private void MeasureAllocation()
    {
        Measure(512); Measure(1); // aquecimento fora dos intervalos relatados
        var regular = Measure(512);
        var tiny = Measure(1);
        Assert.Equal(regular.Hash, tiny.Hash);
        Assert.Equal(8192, tiny.ConsumedFrames);
        Assert.Equal(regular.ProducedFrames, tiny.ProducedFrames);
        Assert.Equal(0, regular.AllocatedBytes);
        Assert.Equal(0, tiny.AllocatedBytes);
        Assert.True(tiny.OfferedFrames > regular.OfferedFrames * 100, "Mensurar trabalho redundante sem prometer custo constante.");
        string json = JsonSerializer.Serialize(new { Regular = regular, OneOutputFrame = tiny }, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(json);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WaveStudio.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string directory = Path.Combine(root.FullName, "artifacts", "clock-drift");
        Directory.CreateDirectory(directory);
        string config = typeof(NominalPartialConsumptionTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        File.WriteAllText(Path.Combine(directory, $"partial-consumption-{config}.json"), json);
    }

    private static Cost Measure(int capacity)
    {
        var stage = new NominalConversionStage(new(44100, 1), new(48000, 2), ChannelPolicy.DuplicateMono);
        float[] source = Enumerable.Range(0, 8192).Select(index => .25f * (float)Math.Sin(index * .1)).ToArray();
        var destination = new float[capacity * 2];
        int offset = 0;
        long offered = 0;
        ulong hash = 14695981039346656037;
        long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        while (offset < source.Length)
        {
            int frames = Math.Min(4096, source.Length - offset);
            offered += frames;
            var step = stage.Process(source.AsSpan(offset, frames), destination);
            offset += step.ConsumedFrames;
            Hash(step.ProducedFrames);
        }
        stage.BeginFlush();
        while (!stage.Completed) Hash(stage.Flush(destination).ProducedFrames);
        double wall = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return new(offered, stage.InputFrames, stage.OutputFrames, hash, wall, GC.GetAllocatedBytesForCurrentThread() - bytes);
        void Hash(int frames)
        {
            for (int index = 0; index < frames * 2; index++)
                hash = unchecked((hash ^ (uint)BitConverter.SingleToInt32Bits(destination[index])) * 1099511628211);
        }
    }
}
