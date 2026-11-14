namespace WaveStudio.Audio.Engine;

internal readonly record struct ResamplingResult(int ConsumedFrames, int ProducedFrames, bool Completed);

// Single-owner, fixed rational rate, causal FIR. No scheduling, drift correction or native audio.
internal sealed class NominalResampler
{
    public const int Radius = 80;
    public const int Taps = 2 * Radius + 1;
    public const int MaximumInputBlockFrames = 4096;
    public const int MaximumOutputBlockFrames = 8192;
    private const int HistoryFrames = 256;
    private readonly int channels;
    private readonly int up;
    private readonly int down;
    private readonly bool identity;
    private readonly float[] coefficients;
    private readonly float[] history;
    private long availableFrames;
    private long nextInputIndex;
    private int phase;
    private bool ending;
    private long finalOutputFrames;
    public int InputRate { get; }
    public int OutputRate { get; }
    public long InputFrames { get; private set; }
    public long OutputFrames { get; private set; }
    public long SanitizedSamples { get; private set; }
    public bool Completed => ending && OutputFrames == finalOutputFrames;
    public double GroupDelayMilliseconds => identity ? 0 : Radius * 1000d / InputRate;
    public int StorageBytes => (coefficients.Length + history.Length) * sizeof(float);

    public NominalResampler(int inputRate, int outputRate, int channels)
    {
        if (channels is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(channels));
        bool knownRates = inputRate is 44100 or 48000 or 96000;
        identity = knownRates && inputRate == outputRate;
        if (!identity && (inputRate, outputRate) is not ((44100, 48000) or (48000, 44100) or (96000, 48000)))
            throw new NotSupportedException("Conversão nominal não validada: use igualdade em 44,1/48/96 kHz, 44,1↔48 ou 96→48 kHz.");
        InputRate = inputRate;
        OutputRate = outputRate;
        this.channels = channels;
        int gcd = Gcd(inputRate, outputRate);
        up = outputRate / gcd;
        down = inputRate / gcd;
        history = new float[HistoryFrames * channels];
        coefficients = identity ? [] : DesignCoefficients();
    }

    public ResamplingResult Process(ReadOnlySpan<float> input, Span<float> output)
    {
        Validate(input, output);
        if (ending) throw new InvalidOperationException("Fim de fluxo iniciado; use Flush ou Reset.");
        output.Clear();
        int consumed = 0, produced = 0, frames = input.Length / channels, capacity = output.Length / channels;
        if (identity)
        {
            int count = Math.Min(frames, capacity);
            for (int sample = 0; sample < count * channels; sample++) output[sample] = Sanitize(input[sample]);
            InputFrames += count;
            OutputFrames += count;
            return new(count, count, false);
        }
        while (produced < capacity)
        {
            if (availableFrames <= nextInputIndex)
            {
                if (consumed == frames) break;
                Append(input.Slice(consumed * channels, channels));
                consumed++;
                InputFrames++;
                continue;
            }
            Emit(output.Slice(produced * channels, channels));
            produced++;
        }
        return new(consumed, produced, false);
    }

    // Caller declares EOF only after every real frame has been consumed.
    public void BeginFlush()
    {
        if (ending) return;
        finalOutputFrames = ExpectedOutputFrames(InputFrames);
        ending = true;
    }

    public ResamplingResult Flush(Span<float> output)
    {
        Validate([], output);
        if (!ending) throw new InvalidOperationException("Declare o fim de fluxo antes de Flush.");
        output.Clear();
        int produced = 0, capacity = output.Length / channels;
        while (!Completed && produced < capacity)
        {
            if (availableFrames <= nextInputIndex)
            {
                // Padding is not real input and is not counted as consumed frames.
                history.AsSpan((int)(availableFrames & (HistoryFrames - 1)) * channels, channels).Clear();
                availableFrames++;
                continue;
            }
            Emit(output.Slice(produced * channels, channels));
            produced++;
        }
        return new(0, produced, Completed);
    }

    public long ExpectedOutputFrames(long realInputFrames)
    {
        if (realInputFrames < 0) throw new ArgumentOutOfRangeException(nameof(realInputFrames));
        if (identity || realInputFrames == 0) return realInputFrames;
        long padded = checked(realInputFrames + 2 * Radius);
        return checked(padded / InputRate * OutputRate + ((padded % InputRate) * OutputRate + InputRate - 1) / InputRate);
    }

    public void Reset()
    {
        Array.Clear(history);
        InputFrames = OutputFrames = SanitizedSamples = availableFrames = nextInputIndex = 0;
        phase = 0;
        ending = false;
        finalOutputFrames = 0;
    }

    private void Append(ReadOnlySpan<float> frame)
    {
        int slot = (int)(availableFrames & (HistoryFrames - 1)) * channels;
        for (int channel = 0; channel < channels; channel++) history[slot + channel] = Sanitize(frame[channel]);
        availableFrames++;
    }
    private float Sanitize(float value)
    {
        if (float.IsFinite(value)) return value;
        SanitizedSamples++;
        return 0;
    }
    private void Emit(Span<float> frame)
    {
        int coefficientStart = phase * Taps;
        for (int channel = 0; channel < channels; channel++)
        {
            double sum = 0;
            for (int tap = 0; tap < Taps; tap++)
            {
                long index = nextInputIndex - tap;
                if (index >= 0) sum += (double)history[(int)(index & (HistoryFrames - 1)) * channels + channel] * coefficients[coefficientStart + tap];
            }
            frame[channel] = (float)Math.Clamp(sum, -float.MaxValue, float.MaxValue);
        }
        int advance = phase + down;
        nextInputIndex += advance / up;
        phase = advance % up;
        OutputFrames++;
    }
    private void Validate(ReadOnlySpan<float> input, Span<float> output)
    {
        if (input.Length % channels != 0 || output.Length % channels != 0 ||
            input.Length / channels > MaximumInputBlockFrames || output.Length / channels > MaximumOutputBlockFrames)
            throw new ArgumentException("Blocos exigem frames completos e limites de 4096 de entrada / 8192 de saída.");
        if (input.Overlaps(output)) throw new ArgumentException("Resampling não permite buffers sobrepostos.");
    }
    private float[] DesignCoefficients()
    {
        var result = new float[up * Taps];
        double cutoff = .45 * Math.Min(1d, (double)OutputRate / InputRate);
        for (int p = 0; p < up; p++)
        {
            double total = 0;
            for (int tap = 0; tap < Taps; tap++) total += Kernel(tap - Radius + (double)p / up, cutoff);
            for (int tap = 0; tap < Taps; tap++) result[p * Taps + tap] = (float)(Kernel(tap - Radius + (double)p / up, cutoff) / total);
        }
        return result;
    }
    private static double Kernel(double distance, double cutoff)
    {
        if (Math.Abs(distance) > Radius) return 0;
        double window = Math.Max(0, .42 + .5 * Math.Cos(Math.PI * distance / Radius) + .08 * Math.Cos(2 * Math.PI * distance / Radius));
        double x = 2 * cutoff * distance;
        return 2 * cutoff * (Math.Abs(x) < 1e-12 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x)) * window;
    }
    private static int Gcd(int a, int b) { while (b != 0) { int remainder = a % b; a = b; b = remainder; } return a; }
}
