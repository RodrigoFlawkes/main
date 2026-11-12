using System.Buffers.Binary;
using NAudio.Wave;

namespace WaveStudio.Audio;

// A single short diagnostic clip. No file, settings or network access.
internal sealed class BoundedTestAudio : IDisposable
{
    internal const int MaximumSeconds = 10;
    internal const int MaximumBytes = 8 * 1024 * 1024;
    private readonly object sync = new();
    private readonly byte[] samples;
    private int length;
    private bool sealedBuffer;
    public WaveFormat Format { get; }
    public int Length => Volatile.Read(ref length);
    public int Capacity => samples.Length;
    public bool Full => Length == Capacity;
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Length / Format.AverageBytesPerSecond);

    public BoundedTestAudio(WaveFormat format)
    {
        Format = ValidateFormat(format);
        int capacity = Math.Min(MaximumBytes, checked(Format.AverageBytesPerSecond * MaximumSeconds));
        samples = new byte[capacity - capacity % Format.BlockAlign];
    }

    internal static WaveFormat ValidateFormat(WaveFormat format)
    {
        var standard = format.AsStandardWaveFormat();
        if (standard.Channels is < 1 or > 2 || standard.SampleRate is < 8000 or > 96000 ||
            !((standard.Encoding == WaveFormatEncoding.IeeeFloat && standard.BitsPerSample == 32) ||
              (standard.Encoding == WaveFormatEncoding.Pcm && standard.BitsPerSample == 16)) ||
            standard.BlockAlign != standard.Channels * standard.BitsPerSample / 8 ||
            standard.AverageBytesPerSecond != standard.SampleRate * standard.BlockAlign)
            throw new NotSupportedException("Formato do teste indisponível: requer mono/estéreo, PCM 16 bits ou float 32 bits, de 8 a 96 kHz.");
        return standard;
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        lock (sync)
        {
            if (sealedBuffer) return;
            int count = Math.Min(data.Length, samples.Length - length);
            count -= count % Format.BlockAlign;
            data[..count].CopyTo(samples.AsSpan(length));
            Volatile.Write(ref length, length + count);
        }
    }

    public void Seal() { lock (sync) sealedBuffer = true; }

    public IWaveProvider CreatePlayback(float level, Func<bool> isCurrent)
    {
        if (!float.IsFinite(level) || level is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(level));
        Seal();
        return new ClipProvider(this, level, isCurrent);
    }

    public void Dispose()
    {
        lock (sync)
        {
            sealedBuffer = true;
            Array.Clear(samples);
            Volatile.Write(ref length, 0);
        }
    }

    private sealed class ClipProvider(BoundedTestAudio clip, float level, Func<bool> isCurrent) : IWaveProvider
    {
        private int position;
        public WaveFormat WaveFormat => clip.Format;
        public int Read(Span<byte> buffer)
        {
            if (!isCurrent()) return 0;
            int size = Math.Min(buffer.Length, Math.Max(0, clip.Length - position));
            size -= size % WaveFormat.BlockAlign;
            var destination = buffer[..size];
            clip.samples.AsSpan(position, size).CopyTo(destination);
            if (WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                for (int i = 0; i < size; i += 4)
                {
                    float sample = BinaryPrimitives.ReadSingleLittleEndian(destination[i..]);
                    sample = float.IsFinite(sample) ? Math.Clamp(sample, -1f, 1f) * level : 0;
                    BinaryPrimitives.WriteSingleLittleEndian(destination[i..], sample);
                }
            }
            else
            {
                for (int i = 0; i < size; i += 2)
                {
                    short sample = BinaryPrimitives.ReadInt16LittleEndian(destination[i..]);
                    BinaryPrimitives.WriteInt16LittleEndian(destination[i..], (short)(sample * level));
                }
            }
            position += size;
            // Invalidation during a read also suppresses the packet already copied.
            if (!isCurrent()) { destination.Clear(); return 0; }
            return size;
        }
    }
}
