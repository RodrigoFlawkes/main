using System.Buffers.Binary;

namespace WaveStudio.Audio.Engine;

// Base interna, intencionalmente sem conexão com a interface desktop ou com um backend WASAPI.
internal sealed record PcmFloatFormat
{
    public int SampleRate { get; }
    public int Channels { get; }
    public int BytesPerFrame => Channels * sizeof(float);
    public PcmFloatFormat(int sampleRate, int channels)
    {
        if (sampleRate is < 8000 or > 192000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(channels), "A fundação aceita mono ou estéreo frontal L/R.");
        SampleRate = sampleRate;
        Channels = channels;
    }
    public int FramesFromSamples(int samples)
    {
        if (samples < 0 || samples % Channels != 0) throw new ArgumentException("O bloco deve conter frames completos.", nameof(samples));
        return samples / Channels;
    }
    public void RequireCompatible(PcmFloatFormat other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (this != other) throw new NotSupportedException("Taxa/canais diferentes exigem adaptação e relógios validados em incremento posterior.");
    }
}

internal enum PcmEncoding { Pcm16LittleEndian, Float32LittleEndian }

internal static class PcmFloatConverter
{
    public static int Decode(ReadOnlySpan<byte> input, Span<float> output, PcmFloatFormat format, PcmEncoding encoding)
    {
        int bytesPerSample = encoding switch
        {
            PcmEncoding.Pcm16LittleEndian => 2,
            PcmEncoding.Float32LittleEndian => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };
        if (input.Length % (bytesPerSample * format.Channels) != 0 || output.Length != input.Length / bytesPerSample)
            throw new ArgumentException("Conversão exige frames completos e destino de tamanho exato.");
        for (int index = 0; index < output.Length; index++)
            output[index] = encoding == PcmEncoding.Pcm16LittleEndian
                ? BinaryPrimitives.ReadInt16LittleEndian(input[(index * 2)..]) / 32768f
                : BinaryPrimitives.ReadSingleLittleEndian(input[(index * 4)..]);
        return output.Length / format.Channels;
    }
}
