using NAudio.Wave;

namespace WaveStudio.Audio.Engine;

internal readonly record struct TransportEndpoint(string Id, EndpointDirection Direction, long Generation)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 2048 || Generation <= 0 ||
            Direction is not (EndpointDirection.Capture or EndpointDirection.Playback))
            throw new ArgumentException("Endpoint/direção/geração explícitos são obrigatórios.");
    }
}

internal sealed record WasapiPcm(int Tag, int SampleRate, int Channels, int ContainerBits, int ValidBits,
    int BlockAlign, int AverageBytesPerSecond, int ExtraBytes, Guid SubFormat, uint? ChannelMask)
{
    public static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");
    public static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    public static WasapiPcm From(WaveFormat format) => format is WaveFormatExtensible extended
        ? new((int)format.Encoding, format.SampleRate, format.Channels, format.BitsPerSample, extended.ValidBitsPerSample,
            format.BlockAlign, format.AverageBytesPerSecond, format.ExtraSize, extended.SubFormat, (uint)extended.ChannelMask)
        : new((int)format.Encoding, format.SampleRate, format.Channels, format.BitsPerSample, format.BitsPerSample,
            format.BlockAlign, format.AverageBytesPerSecond, format.ExtraSize,
            format.Encoding == WaveFormatEncoding.Pcm ? PcmSubFormat : format.Encoding == WaveFormatEncoding.IeeeFloat ? FloatSubFormat : Guid.Empty, null);

    public PcmEncoding Validate()
    {
        if (SampleRate is not (44100 or 48000) || Channels is not (1 or 2))
            throw new NotSupportedException("Backend isolado aceita 44,1/48 kHz, mono/LR.");
        if (Tag == 65534)
        {
            if (ExtraBytes < 22 || ChannelMask is null || (ChannelMask.Value & ~0x3ffffu) != 0 || (Channels == 2 && ChannelMask != 3) ||
                (Channels == 1 && ChannelMask != 0 && System.Numerics.BitOperations.PopCount(ChannelMask.Value) != 1))
                throw new NotSupportedException("Layout extensível ambíguo: estéreo requer FL/FR; mono mantém posição declarada/indefinida.");
        }
        else if (Tag is not (1 or 3) || ChannelMask is not null || ExtraBytes is not (0 or 2))
            throw new NotSupportedException("WAVEFORMATEX não suportado.");
        bool pcm = SubFormat == PcmSubFormat && ContainerBits == 16 && ValidBits == 16 && Tag is 1 or 65534;
        bool floating = SubFormat == FloatSubFormat && ContainerBits == 32 && ValidBits == 32 && Tag is 3 or 65534;
        if (!pcm && !floating) throw new NotSupportedException("Somente PCM16 LE ou float32 LE com subformato confirmado; contêiner 32 não implica float.");
        if (BlockAlign != Channels * (ContainerBits / 8) || AverageBytesPerSecond != SampleRate * BlockAlign)
            throw new NotSupportedException("Block align/taxa de bytes inconsistentes.");
        return pcm ? PcmEncoding.Pcm16LittleEndian : PcmEncoding.Float32LittleEndian;
    }
    public PcmFloatFormat FloatFormat => new(SampleRate, Channels);
}

internal enum FormatEvidence { MixOnly, ExactCapability, SimulatedExact }
internal sealed record WasapiEndpointInfo(TransportEndpoint Endpoint, EndpointState State, WasapiPcm? Mix,
    long? DefaultPeriod100ns, long? MinimumExclusivePeriod100ns, int? BufferCapacityFrames,
    long? StreamLatency100ns, FormatEvidence Evidence, bool StreamInitialized, string? Error);

internal sealed record TransportRoute(WasapiEndpointInfo Capture, WasapiEndpointInfo Playback)
{
    public void Validate()
    {
        Capture.Endpoint.Validate(); Playback.Endpoint.Validate();
        if (Capture.Endpoint.Direction != EndpointDirection.Capture || Playback.Endpoint.Direction != EndpointDirection.Playback ||
            Capture.Endpoint.Generation != Playback.Endpoint.Generation || Capture.State != EndpointState.Active || Playback.State != EndpointState.Active ||
            Capture.Mix is null || Playback.Mix is null || Capture.Evidence == FormatEvidence.MixOnly || Playback.Evidence == FormatEvidence.MixOnly)
            throw new NotSupportedException("Par explícito ativo e capacidade exata são obrigatórios; sem fallback.");
        Capture.Mix.Validate(); Playback.Mix.Validate();
        if ((Capture.Mix.SampleRate, Playback.Mix.SampleRate) is not ((48000, 48000) or (44100, 48000) or (48000, 44100)))
            throw new NotSupportedException("Par não validado pelo adaptativo.");
        if (Capture.DefaultPeriod100ns is null or <= 0 || Playback.DefaultPeriod100ns is null or <= 0)
            throw new NotSupportedException("Períodos consultados/simulados explícitos são obrigatórios.");
    }
    // FIFO units are always output frames, including the capture period after nominal conversion.
    public int PeriodFrames => Math.Max(ToFrames(Capture, Playback.Mix!.SampleRate), ToFrames(Playback, Playback.Mix.SampleRate));
    public int CapacityFrames
    {
        get
        {
            int period = PeriodFrames;
            if (period is <= 0 or > 512) throw new NotSupportedException("Período fora do orçamento isolado (até 512 frames).");
            int target = 1;
            while (target < Math.Max(512, period * 2)) target <<= 1;
            return target * 2;
        }
    }
    private static int ToFrames(WasapiEndpointInfo info, int outputRate) => checked((int)(((UInt128)(ulong)info.DefaultPeriod100ns!.Value * (uint)outputRate + 9999999) / 10000000));
}
