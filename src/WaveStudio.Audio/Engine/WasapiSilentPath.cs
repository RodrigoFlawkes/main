namespace WaveStudio.Audio.Engine;

// Reuses the sample-domain FIR and existing SPSC. There is deliberately no native output encoder.
internal sealed class WasapiSilentPath : IDisposable
{
    private readonly AdaptiveResampler resampler;
    private readonly SyntheticAudioPipeline pipe;
    private readonly float[] input, converted, render;
    private readonly long generation, pipeGeneration;
    private readonly WasapiPcm capture, playback;
    private readonly PcmFloatFormat captureFormat;
    private readonly PcmEncoding captureEncoding;
    public int CapacityFrames { get; }
    public long Produced, Read, Dropped, Underrun;

    public static int Budget(WasapiPcm input, WasapiPcm output, uint captureCapacity, uint renderCapacity, long capturePeriod, long renderPeriod)
    {
        if (captureCapacity is 0 or > 4096 || renderCapacity is 0 or > 4096 ||
            capturePeriod is <= 0 or > 1000000 || renderPeriod is <= 0 or > 1000000)
            throw new NotSupportedException("Capacidade/período fora do orçamento da prova silenciosa.");
        int converted = checked((int)(((ulong)captureCapacity * (uint)output.SampleRate + (uint)input.SampleRate - 1) / (uint)input.SampleRate));
        int period = checked((int)(((ulong)Math.Max(capturePeriod, renderPeriod) * (uint)output.SampleRate + 9999999) / 10000000));
        int target = 512;
        while (target < Math.Max(Math.Max(converted, (int)renderCapacity), 2 * period)) target <<= 1;
        if (target > 8192) throw new NotSupportedException("Fila necessária excede o orçamento da prova.");
        return target * 2;
    }
    public WasapiSilentPath(TransportRoute route, uint captureCapacity, uint renderCapacity, long capturePeriod, long renderPeriod)
    {
        capture = route.Capture.Mix!; playback = route.Playback.Mix!; generation = route.Capture.Endpoint.Generation;
        captureFormat = capture.FloatFormat; captureEncoding = capture.Validate();
        if (generation == long.MaxValue) throw new ArgumentException("Geração deve reservar espaço para limpeza.");
        CapacityFrames = Budget(capture, playback, captureCapacity, renderCapacity, capturePeriod, renderPeriod);
        var policy = capture.Channels == playback.Channels ? ChannelPolicy.Preserve
            : capture.Channels == 1 ? ChannelPolicy.DuplicateMono : ChannelPolicy.AverageStereo;
        resampler = new(capture.FloatFormat, playback.FloatFormat, policy, generation: generation);
        pipe = new(playback.FloatFormat, playback.FloatFormat, CapacityFrames, 4096);
        input = new float[4096 * capture.Channels]; converted = new float[8192 * playback.Channels]; render = new float[4096 * playback.Channels];
        pipeGeneration = pipe.Start();
    }
    // Capture owner only. Borrowed bytes are never stored or transferred to the consumer.
    public void CopyCapture(ReadOnlySpan<byte> bytes, int frames, bool silent)
    {
        var floats = input.AsSpan(0, checked(frames * capture.Channels));
        if (silent) floats.Clear(); else PcmFloatConverter.Decode(bytes, floats, captureFormat, captureEncoding);
    }

    public void Produce(int frames, DeliveryTicket ticket)
    {
        if (!ticket.IsValid) return;
        var floats = input.AsSpan(0, checked(frames * capture.Channels));
        var result = resampler.Process(generation, floats, converted);
        floats.Clear();
        if (result.ConsumedFrames != frames) throw new InvalidOperationException("Conversão parcial fora do orçamento.");
        for (int offset = 0; offset < result.ProducedFrames && ticket.IsValid;)
        {
            int count = Math.Min(4096, result.ProducedFrames - offset);
            int accepted = pipe.Produce(pipeGeneration, converted.AsSpan(offset * playback.Channels, count * playback.Channels));
            Produced += accepted; Dropped += count - accepted; offset += count;
        }
    }
    // Render owner only. Data is consumed/protected in private scratch, never copied into a WASAPI buffer.
    public void Consume(int frames, DeliveryTicket ticket)
    {
        if (!ticket.IsValid) return;
        var floats = render.AsSpan(0, checked(frames * playback.Channels));
        int read = pipe.Render(pipeGeneration, floats); Read += read; Underrun += frames - read;
        floats.Clear();
    }
    public void Dispose()
    {
        // Called only after both owners have exited and no participant can touch the arrays.
        pipe.Stop(); pipe.Dispose(); resampler.Reset(generation + 1); resampler.Stop(generation + 1);
        Array.Clear(input); Array.Clear(converted); Array.Clear(render);
    }
}
