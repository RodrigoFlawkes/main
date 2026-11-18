namespace WaveStudio.Audio.Engine;

internal struct SampleCursor
{
    public const int Bits = 48;
    public const ulong Scale = 1UL << Bits;
    public const ulong Mask = Scale - 1;
    public long Index { get; private set; }
    public ulong Fraction { get; private set; }
    public SampleCursor(long index, ulong fraction)
    {
        if (index < 0 || fraction >= Scale) throw new ArgumentOutOfRangeException(nameof(index));
        Index = index; Fraction = fraction;
    }
    public void Advance(ulong step)
    {
        if (step is 0 or > 2 * Scale) throw new ArgumentOutOfRangeException(nameof(step));
        ulong total = Fraction + step;
        long next = checked(Index + (long)(total >> Bits));
        Fraction = total & Mask; Index = next;
    }
}

internal readonly record struct AdaptiveProtection(long InputNonFinite, long OutputNonFinite,
    long RepresentationSaturations, long OutputClipped);

// FIR serial no domínio das amostras. Sem backend, retenção de spans emprestados ou alocação no processamento válido.
internal sealed class AdaptiveResampler
{
    public const int Radius = 80, Taps = 161, Phases = 256;
    public const int MaximumInputFrames = 4096, MaximumOutputFrames = 8192;
    public const double MaximumPpm = 500, SlewPpmPerSecond = 25;
    private const int HistoryFrames = 256;
    private readonly ChannelAdapter adapter;
    private readonly float[] history, mapped, coefficients;
    private readonly double nominal;
    private readonly int inputChannels, outputChannels, outputRate;
    private long inputNonFinite, outputNonFinite, representationSaturations, outputClipped;
    private SampleCursor cursor;
    private long available, tailEnd;
    private ulong step;
    private bool ending;
    public PcmFloatFormat Input { get; }
    public PcmFloatFormat Output { get; }
    public long Generation { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long InputFrames { get; private set; }
    public long OutputFrames { get; private set; }
    public long MappedFrames { get; private set; }
    public double CurrentPpm { get; private set; }
    public double TargetPpm { get; private set; }
    public SampleCursor Cursor => cursor;
    public AdaptiveProtection Protection => new(inputNonFinite, outputNonFinite, representationSaturations, outputClipped);
    public double GroupDelayMilliseconds => Radius * 1000d / Input.SampleRate;
    public int StorageBytes => (history.Length + mapped.Length + coefficients.Length) * sizeof(float);
    public bool Completed => ending && cursor.Index >= tailEnd;

    public AdaptiveResampler(PcmFloatFormat input, PcmFloatFormat output, ChannelPolicy policy, double initialPpm = 0, long generation = 1)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(output);
        if ((input.SampleRate, output.SampleRate) is not ((48000, 48000) or (44100, 48000) or (48000, 44100)))
            throw new NotSupportedException("Adaptativo validado apenas em 48→48 e 44,1↔48 kHz.");
        ValidatePpm(initialPpm);
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        Input = input; Output = output; nominal = (double)output.SampleRate / input.SampleRate;
        inputChannels = input.Channels; outputChannels = output.Channels; outputRate = output.SampleRate;
        adapter = new((ChannelLayout)input.Channels, (ChannelLayout)output.Channels, policy);
        history = new float[HistoryFrames * output.Channels];
        mapped = new float[output.Channels];
        coefficients = Design();
        Generation = generation; CurrentPpm = TargetPpm = initialPpm; UpdateStep();
    }
    public bool SetTargetPpm(long generation, double ppm)
    {
        ValidatePpm(ppm);
        if (generation != Generation || !IsActive) return false;
        if (ending) throw new InvalidOperationException("EOF congela a razão; use reset explícito.");
        TargetPpm = ppm;
        return true;
    }
    public ResamplingResult Process(long generation, ReadOnlySpan<float> input, Span<float> output)
    {
        Validate(input, output); output.Clear();
        if (generation != Generation || !IsActive) return new(0, 0, false);
        if (ending) throw new InvalidOperationException("Fim de fluxo iniciado; use Flush ou Reset.");
        int consumed = 0, produced = 0;
        while (produced < output.Length / outputChannels)
        {
            if (available <= cursor.Index)
            {
                if (consumed == input.Length / inputChannels) break;
                var mappedResult = adapter.Process(input.Slice(consumed * inputChannels, inputChannels), mapped);
                inputNonFinite += mappedResult.SanitizedSamples;
                mapped.CopyTo(history.AsSpan((int)(available & (HistoryFrames - 1)) * outputChannels, outputChannels));
                available++; consumed++; InputFrames++; MappedFrames++;
                continue;
            }
            Emit(output.Slice(produced * outputChannels, outputChannels)); produced++;
        }
        return new(consumed, produced, false);
    }
    public bool BeginFlush(long generation)
    {
        if (generation != Generation || !IsActive) return false;
        if (ending) return true;
        tailEnd = InputFrames == 0 ? 0 : checked(InputFrames + 2 * Radius);
        TargetPpm = CurrentPpm; ending = true;
        return true;
    }
    public long RemainingFlushFrames
    {
        get
        {
            if (!ending) throw new InvalidOperationException("Declare EOF primeiro.");
            if (Completed) return 0;
            UInt128 distance = ((UInt128)(ulong)(tailEnd - cursor.Index) << SampleCursor.Bits) - cursor.Fraction;
            return checked((long)((distance + step - 1) / step));
        }
    }
    public ResamplingResult Flush(long generation, Span<float> output)
    {
        Validate([], output); output.Clear();
        if (generation != Generation || !IsActive) return new(0, 0, false);
        if (!ending) throw new InvalidOperationException("Declare EOF primeiro.");
        int produced = 0;
        while (!Completed && produced < output.Length / outputChannels)
        {
            if (available <= cursor.Index)
            {
                history.AsSpan((int)(available & (HistoryFrames - 1)) * outputChannels, outputChannels).Clear();
                available++; continue;
            }
            Emit(output.Slice(produced * outputChannels, outputChannels)); produced++;
        }
        return new(0, produced, Completed);
    }
    public void Reset(long generation, double initialPpm = 0)
    {
        ValidatePpm(initialPpm);
        if (generation <= Generation) throw new ArgumentOutOfRangeException(nameof(generation), "Exigida geração nova.");
        Array.Clear(history); Array.Clear(mapped); cursor = default;
        available = tailEnd = InputFrames = OutputFrames = MappedFrames = 0;
        ending = false; IsActive = true; inputNonFinite = outputNonFinite = representationSaturations = outputClipped = 0; Generation = generation;
        CurrentPpm = TargetPpm = initialPpm; UpdateStep();
    }
    public void Stop(long generation) { if (generation == Generation) IsActive = false; }
    private void Emit(Span<float> frame)
    {
        ulong scaled = cursor.Fraction * Phases;
        int first = (int)(scaled >> SampleCursor.Bits) * Taps, second = first + Taps;
        double blend = (scaled & SampleCursor.Mask) / (double)SampleCursor.Scale;
        double left = 0, right = 0;
        int validTaps = cursor.Index >= Taps - 1 ? Taps : (int)cursor.Index + 1;
        // Os canais compartilham coeficientes e índices circulares, com históricos independentes.
        for (int tap = 0; tap < validTaps; tap++)
        {
            int slot = (int)((cursor.Index - tap) & (HistoryFrames - 1)) * outputChannels;
            double coefficient = coefficients[first + tap] + blend * ((double)coefficients[second + tap] - coefficients[first + tap]);
            left += history[slot] * coefficient;
            if (outputChannels == 2) right += history[slot + 1] * coefficient;
        }
        for (int channel = 0; channel < outputChannels; channel++)
        {
            double sum = channel == 0 ? left : right;
            if (Math.Abs(sum) > float.MaxValue) representationSaturations++;
            frame[channel] = (float)Math.Clamp(sum, -float.MaxValue, float.MaxValue);
        }
        var final = NeutralProcessor.Process(frame);
        outputNonFinite += final.NonFiniteSamples; outputClipped += final.ClippedSamples;
        if (!ending && CurrentPpm != TargetPpm)
        {
            CurrentPpm += Math.Clamp(TargetPpm - CurrentPpm, -SlewPpmPerSecond / outputRate, SlewPpmPerSecond / outputRate);
            UpdateStep();
        }
        cursor.Advance(step); OutputFrames++;
    }
    private void UpdateStep() => step = (ulong)Math.Round(SampleCursor.Scale / (nominal * (1 + CurrentPpm * 1e-6)));
    private static void ValidatePpm(double ppm)
    {
        if (!double.IsFinite(ppm) || Math.Abs(ppm) > MaximumPpm) throw new ArgumentOutOfRangeException(nameof(ppm));
    }
    private void Validate(ReadOnlySpan<float> input, Span<float> output)
    {
        if (input.Length % Input.Channels != 0 || output.Length % Output.Channels != 0 ||
            input.Length / Input.Channels > MaximumInputFrames || output.Length / Output.Channels > MaximumOutputFrames)
            throw new ArgumentException("Frames completos; limites 4096 de entrada / 8192 de saída.");
        if (input.Overlaps(output)) throw new ArgumentException("Buffers sobrepostos não suportados.");
    }
    private float[] Design()
    {
        var bank = new float[(Phases + 1) * Taps];
        double cutoff = .45 * Math.Min(1, nominal * (1 - MaximumPpm * 1e-6));
        for (int phase = 0; phase <= Phases; phase++)
        {
            double total = 0;
            for (int tap = 0; tap < Taps; tap++) total += Kernel(tap - Radius + (double)phase / Phases, cutoff);
            for (int tap = 0; tap < Taps; tap++) bank[phase * Taps + tap] = (float)(Kernel(tap - Radius + (double)phase / Phases, cutoff) / total);
        }
        return bank;
    }
    private static double Kernel(double distance, double cutoff)
    {
        if (Math.Abs(distance) > Radius) return 0;
        double window = Math.Max(0, .42 + .5 * Math.Cos(Math.PI * distance / Radius) + .08 * Math.Cos(2 * Math.PI * distance / Radius));
        double x = 2 * cutoff * distance;
        return 2 * cutoff * (Math.Abs(x) < 1e-12 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x)) * window;
    }
}
