using System.Buffers.Binary;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class EngineFoundationTests
{
    [Theory]
    [InlineData(8000, 1)]
    [InlineData(44100, 2)]
    [InlineData(48000, 1)]
    [InlineData(192000, 2)]
    public void FormatsHaveExplicitFloatFrames(int rate, int channels)
    {
        var format = new PcmFloatFormat(rate, channels);
        Assert.Equal(channels * 4, format.BytesPerFrame);
        Assert.Equal(3, format.FramesFromSamples(3 * channels));
        format.RequireCompatible(new(rate, channels));
    }

    [Theory]
    [InlineData(7999, 1)]
    [InlineData(192001, 2)]
    [InlineData(48000, 0)]
    [InlineData(48000, 3)]
    public void UnsupportedFormatsAreRejected(int rate, int channels) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcmFloatFormat(rate, channels));

    [Theory]
    [InlineData(44100, 2)]
    [InlineData(48000, 1)]
    public void DifferentClockRateOrChannelLayoutRequiresFutureAdapter(int rate, int channels) =>
        Assert.Throws<NotSupportedException>(() => new SyntheticAudioPipeline(new(48000, 2), new(rate, channels), 8, 8));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(131072)]
    public void RingCapacityMustBeBoundedPowerOfTwo(int capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpscFrameBuffer(new(48000, 2), capacity));

    [Fact]
    public void PartialFramesAndOversizedBlocksFailBeforeMutatingState()
    {
        using var pipeline = new SyntheticAudioPipeline(new(48000, 2), new(48000, 2), 8, 2);
        long generation = pipeline.Start(TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() => pipeline.Produce(generation, new float[3]));
        Assert.Throws<ArgumentException>(() => pipeline.Produce(generation, new float[6]));
        Assert.Throws<ArgumentException>(() => pipeline.Render(generation, new float[3]));
        Assert.Throws<ArgumentException>(() => pipeline.ProducePcm(generation, new byte[3], PcmEncoding.Pcm16LittleEndian));
        Assert.Throws<ArgumentException>(() => pipeline.ProducePcm(generation, new byte[12], PcmEncoding.Pcm16LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.ProducePcm(generation, new byte[4], (PcmEncoding)99));
        Assert.Equal(0, pipeline.Snapshot().Buffer!.Value.WrittenFrames);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public void ProcessingBlockMemoryIsBounded(int block) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SyntheticAudioPipeline(new(48000, 1), new(48000, 1), 8, block));

    [Fact]
    public void Pcm16LittleEndianPreservesInterleavedChannelsAndSignedLimits()
    {
        short[] source = [short.MinValue, short.MaxValue, -16384, 16384, 0, -1];
        byte[] input = new byte[source.Length * 2];
        for (int i = 0; i < source.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(i * 2), source[i]);
        using var pipeline = new SyntheticAudioPipeline(new(48000, 2), new(48000, 2), 8, 8);
        long generation = pipeline.Start(TestContext.Current.CancellationToken);
        Assert.Equal(3, pipeline.ProducePcm(generation, input, PcmEncoding.Pcm16LittleEndian));
        var output = new float[source.Length];
        Assert.Equal(3, pipeline.Render(generation, output));
        Assert.Equal(source.Select(value => value / 32768f), output);
    }

    [Fact]
    public void FloatConversionAndNeutralProtectionAreSeparateAndKeepUnitySamples()
    {
        float[] input = [-1, -.25f, -0f, .25f, 1, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1.5f, -2];
        byte[] bytes = new byte[input.Length * 4];
        for (int i = 0; i < input.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), input[i]);
        var decoded = new float[input.Length];
        Assert.Equal(input.Length, PcmFloatConverter.Decode(bytes, decoded, new(48000, 1), PcmEncoding.Float32LittleEndian));
        Assert.True(float.IsNaN(decoded[5]));
        using var pipeline = new SyntheticAudioPipeline(new(48000, 1), new(48000, 1), 16, 16);
        long generation = pipeline.Start(TestContext.Current.CancellationToken);
        pipeline.ProducePcm(generation, bytes, PcmEncoding.Float32LittleEndian);
        var output = new float[input.Length];
        pipeline.Render(generation, output);
        Assert.Equal(new float[] { -1, -.25f, -0f, .25f, 1, 0, 0, 0, 1, -1 }, output);
        Assert.Equal(BitConverter.SingleToInt32Bits(-0f), BitConverter.SingleToInt32Bits(output[2]));
        Assert.Equal(3, pipeline.Snapshot().NonFiniteSamples);
        Assert.Equal(2, pipeline.Snapshot().ClippedSamples);
    }

    [Fact]
    public void ConverterRejectsPartialFramesAndMismatchedDestination()
    {
        Assert.Throws<ArgumentException>(() => PcmFloatConverter.Decode(new byte[2], new float[1], new(48000, 2), PcmEncoding.Pcm16LittleEndian));
        Assert.Throws<ArgumentException>(() => PcmFloatConverter.Decode(new byte[4], new float[3], new(48000, 1), PcmEncoding.Pcm16LittleEndian));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcmFloatConverter.Decode([], [], new(48000, 1), (PcmEncoding)9));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OverflowDropsOnlyNewestFramesAndUnderrunFillsSilence(int channels)
    {
        var ring = new SpscFrameBuffer(new(48000, channels), 4);
        float[] input = Enumerable.Range(1, 6 * channels).Select(value => (float)value).ToArray();
        Assert.Equal(4, ring.Write(input));
        Assert.Equal(0, ring.Write(input.AsSpan(0, channels)));
        var output = Enumerable.Repeat(99f, 6 * channels).ToArray();
        Assert.Equal(4, ring.Read(output));
        Assert.Equal(input[..(4 * channels)], output[..(4 * channels)]);
        Assert.All(output[(4 * channels)..], value => Assert.Equal(0, value));
        Assert.True(ring.TryGetSnapshot(out var snapshot));
        Assert.Equal(new(0, 4, 4, 3, 2), snapshot);
        Assert.Equal(0, ring.Read(output));
        Assert.All(output, value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue - 2)]
    public void RingWrapAndUnsignedCursorRolloverPreserveFrameOrder(uint seed)
    {
        var ring = new SpscFrameBuffer(new(48000, 2), 4, seed);
        Assert.Equal(3, ring.Write([1, -1, 2, -2, 3, -3]));
        var first = new float[4];
        Assert.Equal(2, ring.Read(first));
        Assert.Equal(new float[] { 1, -1, 2, -2 }, first);
        Assert.Equal(3, ring.Write([4, -4, 5, -5, 6, -6]));
        var second = new float[8];
        Assert.Equal(4, ring.Read(second));
        Assert.Equal(new float[] { 3, -3, 4, -4, 5, -5, 6, -6 }, second);
        ring.ResetQuiescent();
        Assert.True(ring.TryGetSnapshot(out var reset));
        Assert.Equal(default, reset);
    }

    [Fact]
    public void CreationStopResetAndRestartNeverRouteStaleGeneration()
    {
        using var pipeline = new SyntheticAudioPipeline(new(48000, 1), new(48000, 1), 8, 8);
        var output = new float[] { 9, 9 };
        Assert.Equal(SyntheticPipelineState.Stopped, pipeline.State);
        Assert.Equal(0, pipeline.Render(0, output));
        Assert.Equal(new float[] { 0, 0 }, output);
        long first = pipeline.Start(TestContext.Current.CancellationToken);
        pipeline.Produce(first, [.5f, .7f]);
        Assert.False(pipeline.TryReset());
        Assert.Throws<InvalidOperationException>(() => pipeline.Start(TestContext.Current.CancellationToken));
        pipeline.Stop();
        Assert.Equal(0, pipeline.Render(first, output));
        Assert.Equal(0, pipeline.Produce(first, [.9f]));
        Assert.True(pipeline.TryReset());
        long next = pipeline.Start(TestContext.Current.CancellationToken);
        Assert.True(next > first);
        Assert.Equal(0, pipeline.Produce(first, [1]));
        pipeline.Produce(next, [.25f]);
        Assert.Equal(0, pipeline.Render(first, output));
        Assert.Equal(1, pipeline.Render(next, output));
        Assert.Equal(new float[] { .25f, 0 }, output);
    }

    [Fact]
    public void CancellationSuppressesInputAndOutputAndDisposalIsIdempotent()
    {
        using var cancellation = new CancellationTokenSource();
        var pipeline = new SyntheticAudioPipeline(new(48000, 1), new(48000, 1), 8, 8);
        long generation = pipeline.Start(cancellation.Token);
        pipeline.Produce(generation, [.7f]);
        cancellation.Cancel();
        var output = new float[] { 9 };
        Assert.Equal(0, pipeline.Render(generation, output));
        Assert.Equal(0, output[0]);
        Assert.Equal(0, pipeline.Produce(generation, [.5f]));
        Assert.Equal(SyntheticPipelineState.Canceled, pipeline.State);
        Assert.True(pipeline.TryReset());
        Assert.Throws<OperationCanceledException>(() => pipeline.Start(cancellation.Token));
        pipeline.Dispose();
        pipeline.Dispose();
        Assert.Equal(SyntheticPipelineState.Disposed, pipeline.State);
        Assert.Throws<ObjectDisposedException>(() => pipeline.Start(TestContext.Current.CancellationToken));
        Assert.Equal(0, pipeline.Render(generation, output));
    }

    [Fact]
    public void TheoreticalBufferDelayAndMemoryBoundAreExplicit()
    {
        using var pipeline = new SyntheticAudioPipeline(new(48000, 2), new(48000, 2), 1024, 480);
        long generation = pipeline.Start(TestContext.Current.CancellationToken);
        pipeline.Produce(generation, new float[960]);
        var snapshot = pipeline.Snapshot();
        Assert.Equal(10, snapshot.BufferedMilliseconds);
        Assert.Equal(1024d / 48, snapshot.CapacityMilliseconds);
        Assert.Equal((1024 + 480) * 2 * 4, snapshot.StorageBytes);
        using var largest = new SyntheticAudioPipeline(new(192000, 2), new(192000, 2), 65536, 4096);
        Assert.Equal(557056, largest.Snapshot().StorageBytes);
    }

    [Theory]
    [InlineData(1, 0u)]
    [InlineData(2, 0u)]
    [InlineData(1, uint.MaxValue - 8)]
    [InlineData(2, uint.MaxValue - 8)]
    public async Task ConcurrentSpscStressPreservesOrderAndFramesAcrossWrap(int channels, uint seed)
    {
        const int total = 100000;
        var ring = new SpscFrameBuffer(new(48000, channels), 64, seed);
        var token = TestContext.Current.CancellationToken;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        void CheckDeadline()
        {
            token.ThrowIfCancellationRequested();
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(10)) throw new TimeoutException();
        }
        var producer = Task.Run(() =>
        {
            var block = new float[31 * channels];
            int next = 1;
            while (next <= total)
            {
                CheckDeadline();
                int count = Math.Min(1 + next % 31, total - next + 1);
                for (int frame = 0; frame < count; frame++)
                    for (int channel = 0; channel < channels; channel++) block[frame * channels + channel] = channel == 0 ? next + frame : -(next + frame);
                int accepted = ring.Write(block.AsSpan(0, count * channels));
                next += accepted;
                if (accepted == 0) Thread.Yield(); // Apenas o driver de teste. O buffer circular nunca espera nem tenta novamente.
            }
        }, token);
        var consumer = Task.Run(() =>
        {
            var block = new float[17 * channels];
            int next = 1;
            while (next <= total)
            {
                CheckDeadline();
                int count = ring.Read(block);
                for (int frame = 0; frame < count; frame++)
                    for (int channel = 0; channel < channels; channel++)
                        Assert.Equal(channel == 0 ? next + frame : -(next + frame), block[frame * channels + channel]);
                for (int sample = count * channels; sample < block.Length; sample++) Assert.Equal(0, block[sample]);
                next += count;
                if (count == 0) Thread.Yield();
            }
        }, token);
        await Task.WhenAll(producer, consumer).WaitAsync(TimeSpan.FromSeconds(12), token);
        Assert.True(ring.TryGetSnapshot(out var final));
        Assert.Equal(total, final.WrittenFrames);
        Assert.Equal(total, final.ReadFrames);
        Assert.Equal(0, final.QueuedFrames);
    }
}
