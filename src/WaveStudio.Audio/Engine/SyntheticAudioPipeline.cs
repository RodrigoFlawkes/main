namespace WaveStudio.Audio.Engine;

internal enum SyntheticPipelineState { Stopped, Running, Canceled, Disposed }
internal readonly record struct SyntheticPipelineSnapshot(SyntheticPipelineState State, long Generation,
    FrameBufferSnapshot? Buffer, long NonFiniteSamples, long ClippedSamples, double? BufferedMilliseconds,
    double CapacityMilliseconds, int StorageBytes);

// Sem threads, dispositivos, agendamento, I/O de arquivos ou rede, ou início automático.
// Os métodos de controle usam um lock. O produtor e o consumidor não o adquirem nem esperam por ele.
internal sealed class SyntheticAudioPipeline : IDisposable
{
    public const int MaximumBlockFrames = 4096;
    private readonly object control = new();
    private readonly SpscFrameBuffer buffer;
    private readonly float[] conversion;
    private readonly int blockFrames;
    private int state;
    private long generation;
    private CancellationToken cancellation;
    private int producerBusy;
    private int consumerBusy;
    private long nonFinite;
    private long clipped;
    public PcmFloatFormat Format { get; }
    public SyntheticPipelineState State
    {
        get
        {
            var current = (SyntheticPipelineState)Volatile.Read(ref state);
            return current == SyntheticPipelineState.Running && cancellation.IsCancellationRequested
                ? SyntheticPipelineState.Canceled : current;
        }
    }

    public SyntheticAudioPipeline(PcmFloatFormat input, PcmFloatFormat output, int capacityFrames, int maximumBlockFrames)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.RequireCompatible(output);
        if (maximumBlockFrames is < 1 or > MaximumBlockFrames) throw new ArgumentOutOfRangeException(nameof(maximumBlockFrames));
        Format = input;
        buffer = new(input, capacityFrames);
        blockFrames = maximumBlockFrames;
        conversion = new float[maximumBlockFrames * input.Channels];
    }

    public long Start(CancellationToken token = default)
    {
        lock (control)
        {
            ObjectDisposedException.ThrowIf(State == SyntheticPipelineState.Disposed, this);
            token.ThrowIfCancellationRequested();
            if (State == SyntheticPipelineState.Running || !TryReset()) throw new InvalidOperationException("Pare o pipeline e aguarde os dois participantes antes de iniciar.");
            cancellation = token;
            long next = checked(generation + 1);
            Volatile.Write(ref generation, next);
            Volatile.Write(ref state, (int)SyntheticPipelineState.Running);
            return next;
        }
    }

    public void Stop()
    {
        lock (control)
            if (State != SyntheticPipelineState.Disposed) Interlocked.Exchange(ref state, (int)SyntheticPipelineState.Stopped);
    }

    public bool TryReset()
    {
        lock (control)
        {
            ObjectDisposedException.ThrowIf(State == SyntheticPipelineState.Disposed, this);
            if (State == SyntheticPipelineState.Running) return false;
            // A barreira completa de memória atua em conjunto com o CAS do participante. Um callback ao entrar deve observar
            // Stopped ou deixar seu sinal de ocupação visível antes de a memória ser limpa.
            Interlocked.Exchange(ref state, (int)SyntheticPipelineState.Stopped);
            if (Volatile.Read(ref producerBusy) != 0 || Volatile.Read(ref consumerBusy) != 0) return false;
            buffer.ResetQuiescent();
            Array.Clear(conversion);
            Volatile.Write(ref nonFinite, 0);
            Volatile.Write(ref clipped, 0);
            return true;
        }
    }

    public int Produce(long targetGeneration, ReadOnlySpan<float> input)
    {
        ValidateBlock(input.Length);
        Enter(ref producerBusy);
        try { return Current(targetGeneration) ? buffer.Write(input) : 0; }
        finally { Volatile.Write(ref producerBusy, 0); }
    }

    public int ProducePcm(long targetGeneration, ReadOnlySpan<byte> input, PcmEncoding encoding)
    {
        Enter(ref producerBusy);
        try
        {
            if (!Current(targetGeneration)) return 0;
            int sampleBytes = encoding switch { PcmEncoding.Pcm16LittleEndian => 2, PcmEncoding.Float32LittleEndian => 4,
                _ => throw new ArgumentOutOfRangeException(nameof(encoding)) };
            if (input.Length % sampleBytes != 0) throw new ArgumentException("Amostra PCM incompleta.");
            int length = input.Length / sampleBytes;
            ValidateBlock(length);
            var converted = conversion.AsSpan(0, length);
            PcmFloatConverter.Decode(input, converted, Format, encoding);
            return Current(targetGeneration) ? buffer.Write(converted) : 0;
        }
        finally { Volatile.Write(ref producerBusy, 0); }
    }

    public int Render(long targetGeneration, Span<float> output)
    {
        ValidateBlock(output.Length);
        Enter(ref consumerBusy);
        try
        {
            if (!Current(targetGeneration)) { output.Clear(); return 0; }
            int frames = buffer.Read(output);
            var protection = NeutralProcessor.Process(output);
            Volatile.Write(ref nonFinite, nonFinite + protection.NonFiniteSamples);
            Volatile.Write(ref clipped, clipped + protection.ClippedSamples);
            if (!Current(targetGeneration)) { output.Clear(); return 0; }
            return frames;
        }
        finally { Volatile.Write(ref consumerBusy, 0); }
    }

    public SyntheticPipelineSnapshot Snapshot()
    {
        lock (control)
        {
            FrameBufferSnapshot? values = buffer.TryGetSnapshot(out var current) ? current : null;
            return new(State, Volatile.Read(ref generation), values, Volatile.Read(ref nonFinite), Volatile.Read(ref clipped),
                values?.QueuedFrames * 1000d / Format.SampleRate, buffer.CapacityFrames * 1000d / Format.SampleRate,
                buffer.StorageBytes + conversion.Length * sizeof(float));
        }
    }

    public void Dispose()
    {
        lock (control)
        {
            if (State == SyntheticPipelineState.Disposed) return;
            Stop();
            if (!TryReset()) throw new InvalidOperationException("Aguarde os participantes antes de descartar o pipeline.");
            Volatile.Write(ref state, (int)SyntheticPipelineState.Disposed);
        }
    }

    private bool Current(long target) => Volatile.Read(ref state) == (int)SyntheticPipelineState.Running &&
        Volatile.Read(ref generation) == target && !cancellation.IsCancellationRequested;
    private void ValidateBlock(int samples)
    {
        if (Format.FramesFromSamples(samples) > blockFrames) throw new ArgumentException("Bloco excede o limite pré-alocado.");
    }
    private static void Enter(ref int participant)
    {
        if (Interlocked.CompareExchange(ref participant, 1, 0) != 0)
            throw new InvalidOperationException("SPSC requer exatamente um produtor e um consumidor, sem reentrada.");
    }
}
