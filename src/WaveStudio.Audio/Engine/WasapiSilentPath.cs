namespace WaveStudio.Audio.Engine;

// Reutiliza o FIR no domínio das amostras e a SPSC existente. Intencionalmente não há codificador para a saída nativa.
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
    private long fifoPrimingFrames;
    public long FifoPrimingFrames => Volatile.Read(ref fifoPrimingFrames);
    public double AppliedPpm => resampler.CurrentPpm;
    public bool SetTargetPpm(long generation, double ppm) => resampler.SetTargetPpm(generation, ppm);

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
    // Somente o proprietário da captura acessa os bytes emprestados, que não são retidos nem transferidos ao consumidor.
    public void CopyCapture(ReadOnlySpan<byte> bytes, int frames, bool silent)
    {
        var floats = input.AsSpan(0, checked(frames * capture.Channels));
        if (silent) floats.Clear(); else PcmFloatConverter.Decode(bytes, floats, captureFormat, captureEncoding);
    }

    public void DiscardCapture(ReadOnlySpan<byte> bytes, int frames)
    {
        var floats = input.AsSpan(0, checked(frames * capture.Channels));
        try { PcmFloatConverter.Decode(bytes, floats, captureFormat, captureEncoding); }
        finally { floats.Clear(); }
    }

    public void Produce(int frames, DeliveryTicket ticket, bool prefill = false)
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
        // Antes da primeira leitura, os frames escritos são exatamente o preenchimento da FIFO.
        if (prefill && fifoPrimingFrames == 0 && Produced >= CapacityFrames / 2)
            Volatile.Write(ref fifoPrimingFrames, Produced);
    }
    // Somente o proprietário do render. Os dados são consumidos e protegidos em memória de trabalho privada, sem cópia para o buffer WASAPI.
    public void Consume(int frames, DeliveryTicket ticket)
    {
        if (!ticket.IsValid) return;
        var floats = render.AsSpan(0, checked(frames * playback.Channels));
        int read = pipe.Render(pipeGeneration, floats); Read += read; Underrun += frames - read;
        floats.Clear();
    }
    public void Dispose()
    {
        // Chamado somente após a saída dos dois proprietários, quando nenhum participante pode acessar os arrays.
        pipe.Stop(); pipe.Dispose(); resampler.Reset(generation + 1); resampler.Stop(generation + 1);
        Array.Clear(input); Array.Clear(converted); Array.Clear(render);
    }
}
