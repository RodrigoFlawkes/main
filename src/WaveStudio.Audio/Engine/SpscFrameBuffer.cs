namespace WaveStudio.Audio.Engine;

internal readonly record struct FrameBufferSnapshot(int QueuedFrames, long WrittenFrames, long ReadFrames,
    long OverrunFrames, long UnderrunFrames);

// Exactly one producer and one consumer. Reset requires BOTH to be quiescent.
// Producer publishes payload with release(write); consumer acquires(write) before reading.
// Consumer release(read) publishes completed copies; producer acquires(read) before reuse.
internal sealed class SpscFrameBuffer
{
    public const int MaximumCapacityFrames = 65536;
    private readonly float[] samples;
    private readonly int channels;
    private readonly uint mask;
    private uint writeSequence;
    private uint readSequence;
    private long written;
    private long read;
    private long overrun;
    private long underrun;
    public int CapacityFrames { get; }
    public int StorageBytes => samples.Length * sizeof(float);

    public SpscFrameBuffer(PcmFloatFormat format, int capacityFrames, uint initialSequence = 0)
    {
        if (capacityFrames < 1 || capacityFrames > MaximumCapacityFrames || (capacityFrames & (capacityFrames - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(capacityFrames), "A capacidade deve ser potência de dois, até 65536 frames.");
        channels = format.Channels;
        CapacityFrames = capacityFrames;
        mask = (uint)(capacityFrames - 1);
        samples = new float[capacityFrames * channels];
        writeSequence = readSequence = initialSequence;
    }

    public int Write(ReadOnlySpan<float> input)
    {
        int requested = Frames(input.Length);
        uint start = Volatile.Read(ref writeSequence);
        uint consumed = Volatile.Read(ref readSequence);
        int count = Math.Min(requested, CapacityFrames - (int)unchecked(start - consumed));
        int position = (int)(start & mask);
        int first = Math.Min(count, CapacityFrames - position) * channels;
        input[..first].CopyTo(samples.AsSpan(position * channels));
        input.Slice(first, count * channels - first).CopyTo(samples);
        Volatile.Write(ref written, written + count);
        Volatile.Write(ref overrun, overrun + requested - count);
        Volatile.Write(ref writeSequence, unchecked(start + (uint)count));
        return count;
    }

    public int Read(Span<float> output)
    {
        int requested = Frames(output.Length);
        uint start = Volatile.Read(ref readSequence);
        uint produced = Volatile.Read(ref writeSequence);
        int count = Math.Min(requested, (int)unchecked(produced - start));
        int position = (int)(start & mask);
        int first = Math.Min(count, CapacityFrames - position) * channels;
        samples.AsSpan(position * channels, first).CopyTo(output);
        samples.AsSpan(0, count * channels - first).CopyTo(output[first..]);
        output[(count * channels)..].Clear();
        Volatile.Write(ref read, read + count);
        Volatile.Write(ref underrun, underrun + requested - count);
        Volatile.Write(ref readSequence, unchecked(start + (uint)count));
        return count;
    }

    // Bounded diagnostic sampling, never spin until success. Totals are individually
    // atomic, not a transaction. Full conservation is checked once quiescent.
    public bool TryGetSnapshot(out FrameBufferSnapshot snapshot)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint before = Volatile.Read(ref readSequence);
            uint produced = Volatile.Read(ref writeSequence);
            uint after = Volatile.Read(ref readSequence);
            uint queued = unchecked(produced - before);
            if (before != after || queued > CapacityFrames) continue;
            snapshot = new((int)queued, Volatile.Read(ref written), Volatile.Read(ref read),
                Volatile.Read(ref overrun), Volatile.Read(ref underrun));
            return true;
        }
        snapshot = default;
        return false;
    }

    internal void ResetQuiescent()
    {
        Array.Clear(samples);
        Volatile.Write(ref writeSequence, 0);
        Volatile.Write(ref readSequence, 0);
        Volatile.Write(ref written, 0);
        Volatile.Write(ref read, 0);
        Volatile.Write(ref overrun, 0);
        Volatile.Write(ref underrun, 0);
    }
    private int Frames(int length) => length % channels == 0 ? length / channels
        : throw new ArgumentException("O bloco deve conter frames completos.");
}
