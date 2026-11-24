namespace WaveStudio.Audio.Engine;

// O controle pode preparar uma nova topologia enquanto o callback termina. Toda geração tem
// buffers próprios. Somente o callback admitido usa o transporte e altera o estado dos buffers.
internal sealed class BusMixSession(IBusPcmTransport transport)
{
    public const int BlockMilliseconds = 10;
    public const int MaximumSources = 8;
    private readonly object control = new();
    private readonly IBusPcmTransport transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private MixGeneration? current;
    private long generation;
    private int processing;

    public void Start(long generation, PcmFloatFormat format, ReadOnlySpan<MixSource> sources,
        MixExternalDestination? external = null, CancellationToken cancellation = default)
    {
        lock (control)
        {
            if (generation <= this.generation) throw new ArgumentOutOfRangeException(nameof(generation));
            cancellation.ThrowIfCancellationRequested();
            var next = new MixGeneration(generation, format, sources, external, cancellation);
            cancellation.ThrowIfCancellationRequested();
            current?.Ticket.Invalidate();
            this.generation = generation;
            Volatile.Write(ref current, next);
        }
    }

    public void Stop(long generation)
    {
        lock (control)
        {
            if (current is not { } active || active.Ticket.Generation != generation) return;
            active.Ticket.Invalidate();
            Volatile.Write(ref current, null);
        }
    }

    public bool SetSend(long generation, string sourceId, MixBus bus, MixLevel level)
    {
        level.Validate();
        int index = BusIndex(bus);
        lock (control)
        {
            return current is { } active && active.Ticket.Generation == generation && active.Ticket.IsValid &&
                active.SetSend(sourceId, index, level.EffectiveGain);
        }
    }

    public bool SetMaster(long generation, MixBus bus, MixLevel level)
    {
        level.Validate();
        int index = BusIndex(bus);
        lock (control)
        {
            if (current is not { } active || active.Ticket.Generation != generation || !active.Ticket.IsValid) return false;
            active.SetTarget(index, level.EffectiveGain);
            return true;
        }
    }

    // O agendador pode omitir índices quando perde prazo. Índices atrasados ou repetidos nunca são
    // tocados. Uma colisão de callbacks é recusada imediatamente, sem espera pelo outro produtor.
    public bool Process(long generation, long block)
    {
        if (block < 0 || Interlocked.CompareExchange(ref processing, 1, 0) != 0) return false;
        MixGeneration? active = null;
        try
        {
            active = Volatile.Read(ref current);
            if (active is null || active.Ticket.Generation != generation || !active.Ticket.IsValid) return false;
            return active.Process(transport, block);
        }
        catch
        {
            active?.Ticket.Invalidate();
            throw;
        }
        finally
        {
            if (active is not null && !active.Ticket.IsValid) active.Clear();
            Volatile.Write(ref processing, 0);
        }
    }

    private static int BusIndex(MixBus bus) => bus switch
    {
        MixBus.Personal => 0,
        MixBus.Stream => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(bus))
    };

    private sealed class MixGeneration
    {
        private readonly SourceState[] sources;
        private readonly double[] personalSum;
        private readonly double[] streamSum;
        private readonly float[] personal;
        private readonly float[] stream;
        private readonly int channels;
        private readonly int rampFrames;
        private float[] targets;
        private float personalMaster = 1;
        private float streamMaster = 1;
        private long previousBlock = -1;

        public MixDeliveryTicket Ticket { get; }

        public MixGeneration(long generation, PcmFloatFormat format, ReadOnlySpan<MixSource> sources,
            MixExternalDestination? external, CancellationToken cancellation)
        {
            ArgumentNullException.ThrowIfNull(format);
            ValidateRate(format);
            if (sources.Length > MaximumSources) throw new ArgumentOutOfRangeException(nameof(sources));
            if (external is not null)
            {
                ValidateId(external.Id);
                if (external.CallId is not null) ValidateId(external.CallId);
            }

            Ticket = new(generation, format, external, cancellation);
            channels = format.Channels;
            int samples = SamplesPerBlock(format);
            rampFrames = format.SampleRate / 200;
            personalSum = new double[samples];
            streamSum = new double[samples];
            personal = new float[samples];
            stream = new float[samples];
            this.sources = new SourceState[sources.Length];
            targets = new float[2 + 2 * sources.Length];
            targets[0] = targets[1] = 1;

            var sorted = sources.ToArray();
            foreach (var source in sorted) ValidateSource(source, generation, external);
            Array.Sort(sorted, static (a, b) => StringComparer.Ordinal.Compare(a.Endpoint.Id, b.Endpoint.Id));
            for (int i = 0; i < sorted.Length; i++)
            {
                var source = sorted[i];
                if (i > 0 && source.Endpoint.Id == sorted[i - 1].Endpoint.Id)
                    throw new ArgumentException("Identidade de fluxo repetida.", nameof(sources));
                bool personalAllowed = source.Personal.HasValue;
                bool streamAllowed = AllowsStream(source, external);
                targets[SendIndex(i, 0)] = personalAllowed ? source.Personal!.Value.EffectiveGain : 0;
                targets[SendIndex(i, 1)] = streamAllowed ? source.Stream!.Value.EffectiveGain : 0;
                this.sources[i] = new(source, format, personalAllowed, streamAllowed,
                    targets[SendIndex(i, 0)], targets[SendIndex(i, 1)]);
            }
        }

        public bool SetSend(string sourceId, int bus, float gain)
        {
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                if (source.Definition.Endpoint.Id != sourceId) continue;
                if (!(bus == 0 ? source.PersonalAllowed : source.StreamAllowed)) return false;
                SetTarget(SendIndex(i, bus), gain);
                return true;
            }
            return false;
        }

        public void SetTarget(int index, float gain)
        {
            if (targets[index] == gain) return;
            var next = (float[])targets.Clone();
            next[index] = gain;
            Volatile.Write(ref targets, next);
        }

        public bool Process(IBusPcmTransport transport, long block)
        {
            if (block <= previousBlock) return false;
            bool gap = block - 1 != previousBlock;
            previousBlock = block;
            var levels = Volatile.Read(ref targets);
            Array.Clear(personalSum);
            Array.Clear(streamSum);

            for (int i = 0; i < sources.Length; i++)
            {
                if (!Ticket.IsValid) return false;
                sources[i].Accumulate(transport, block, gap, channels, rampFrames,
                    levels[SendIndex(i, 0)], levels[SendIndex(i, 1)], personalSum, streamSum);
            }

            var personalMeter = Finish(personalSum, personal, personalMaster, levels[0]);
            var streamMeter = Finish(streamSum, stream, streamMaster, levels[1]);
            personalMaster = levels[0];
            streamMaster = levels[1];
            if (!Ticket.IsValid || !transport.Submit(Ticket, block, personal, stream, new(personalMeter, streamMeter)))
                return false;
            if (Ticket.External is not null && (!Ticket.IsValid || !transport.SubmitExternal(Ticket, block, stream)))
                return false;
            return Ticket.IsValid;
        }

        private MixMeter Finish(double[] sum, float[] output, float from, float to)
        {
            float peak = 0;
            double squares = 0;
            int clipped = 0;
            for (int frame = 0, sample = 0; sample < output.Length; frame++)
            {
                float gain = Ramp(from, to, frame, rampFrames);
                for (int channel = 0; channel < channels; channel++, sample++)
                {
                    double raw = sum[sample] * gain;
                    if (Math.Abs(raw) > 1) clipped++;
                    float value = Normalized((float)Math.Clamp(raw, -1d, 1d));
                    output[sample] = value;
                    peak = Math.Max(peak, Math.Abs(value));
                    squares += (double)value * value;
                }
            }
            return new(peak, Math.Sqrt(squares / output.Length), clipped);
        }

        public void Clear()
        {
            Array.Clear(personal);
            Array.Clear(stream);
            Array.Clear(personalSum);
            Array.Clear(streamSum);
            foreach (var source in sources) source.Clear();
        }

        private static int SendIndex(int source, int bus) => 2 + 2 * source + bus;
    }

    private sealed class SourceState
    {
        private readonly NominalConversionStage? conversion;
        private readonly ChannelAdapter? adapter;
        private readonly float[] input;
        private readonly float[] output;
        private readonly int inputFrames;
        private bool interrupted;
        private float personal;
        private float stream;

        public MixSource Definition { get; }
        public bool PersonalAllowed { get; }
        public bool StreamAllowed { get; }

        public SourceState(MixSource source, PcmFloatFormat format, bool personalAllowed, bool streamAllowed,
            float personal, float stream)
        {
            Definition = source;
            PersonalAllowed = personalAllowed;
            StreamAllowed = streamAllowed;
            this.personal = personal;
            this.stream = stream;
            inputFrames = source.Format.SampleRate / 100;
            input = new float[SamplesPerBlock(source.Format)];
            if (source.Format.SampleRate != format.SampleRate)
                conversion = new(source.Format, format, source.Channels);
            else
                adapter = new((ChannelLayout)source.Format.Channels, (ChannelLayout)format.Channels, source.Channels);
            // Uma vaga extra permite ao FIR consumir os últimos frames de entrada mesmo no downsampling.
            output = new float[SamplesPerBlock(format) + (conversion is null ? 0 : format.Channels)];
        }

        public void Accumulate(IBusPcmTransport transport, long block, bool gap, int channels, int rampFrames,
            float personalTarget, float streamTarget, double[] personalSum, double[] streamSum)
        {
            if (!PersonalAllowed && !StreamAllowed) return;
            if (gap) Interrupt();

            Array.Clear(input);
            var received = transport.Read(Definition, block, input);
            if (received.Endpoint != Definition.Endpoint || received.Block != block || received.Frames != inputFrames)
            {
                Interrupt();
                personal = personalTarget;
                stream = streamTarget;
                return;
            }

            for (int i = 0; i < input.Length; i++) input[i] = Normalized(input[i]);
            if (conversion is not null)
            {
                var result = conversion.Process(input, output);
                if (result.ConsumedFrames != inputFrames || result.ProducedFrames != personalSum.Length / channels)
                {
                    Interrupt();
                    personal = personalTarget;
                    stream = streamTarget;
                    return;
                }
            }
            else
            {
                adapter!.Process(input, output);
            }

            for (int frame = 0, sample = 0; sample < personalSum.Length; frame++)
            {
                float recovery = interrupted ? Ramp(0, 1, frame, rampFrames) : 1;
                double p = Ramp(personal, personalTarget, frame, rampFrames) * recovery;
                double s = Ramp(stream, streamTarget, frame, rampFrames) * recovery;
                for (int channel = 0; channel < channels; channel++, sample++)
                {
                    personalSum[sample] += output[sample] * p;
                    streamSum[sample] += output[sample] * s;
                }
            }
            personal = personalTarget;
            stream = streamTarget;
            interrupted = false;
        }

        private void Interrupt()
        {
            if (!interrupted) conversion?.Reset();
            interrupted = true;
        }

        public void Clear()
        {
            Array.Clear(input);
            Array.Clear(output);
            Interrupt();
        }
    }

    private static bool AllowsStream(MixSource source, MixExternalDestination? external)
    {
        if (source.Stream is null) return false;
        bool aggregate = source.Origin == MixOrigin.Aggregated;
        if (external?.CallId is { } call &&
            (aggregate || source.ReturnCallId == call ||
             (source.Kind == MixSourceKind.Communication && source.ReturnCallId is null))) return false;
        bool sensitive = aggregate || source.ReturnCallId is not null ||
            source.Kind is MixSourceKind.Microphone or MixSourceKind.Communication;
        return !sensitive || source.AllowSensitiveStream;
    }

    private static void ValidateSource(MixSource source, long generation, MixExternalDestination? external)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Format);
        source.Endpoint.Validate();
        if (source.Endpoint.Direction != EndpointDirection.Capture || source.Endpoint.Generation != generation ||
            source.Origin is not (MixOrigin.Discrete or MixOrigin.Aggregated) ||
            !Enum.IsDefined(source.Kind) || source.Endpoint.Id == external?.Id)
            throw new ArgumentException("A fonte deve ser uma captura válida e independente das saídas, na geração ativa.");
        if (source.ReturnCallId is not null) ValidateId(source.ReturnCallId);
        ValidateRate(source.Format);
        source.Personal?.Validate();
        source.Stream?.Validate();
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 2048)
            throw new ArgumentException("Identidade explícita obrigatória.");
    }

    private static void ValidateRate(PcmFloatFormat format)
    {
        if (format.SampleRate is not (44100 or 48000))
            throw new NotSupportedException("O barramento aceita PCM de 44,1 e 48 kHz.");
    }

    private static int SamplesPerBlock(PcmFloatFormat format) => format.SampleRate / 100 * format.Channels;
    private static float Normalized(float value) =>
        float.IsNormal(value) ? Math.Clamp(value, -1f, 1f) : 0;
    private static float Ramp(float from, float to, int frame, int rampFrames) =>
        frame >= rampFrames - 1 ? to : from + (to - from) * ((frame + 1f) / rampFrames);
}
