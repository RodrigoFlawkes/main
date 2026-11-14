using NAudio.Dsp;

namespace WaveStudio.Audio.Dsp;

// Immutable, copied at command submission. No persistence or device settings.
public sealed class GraphicEqualizerSettings
{
    public static IReadOnlyList<int> Frequencies { get; } = Array.AsReadOnly(new[] { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 });
    public static GraphicEqualizerSettings Off { get; } = new(false, new double[10]);
    public bool Enabled { get; }
    public IReadOnlyList<double> GainsDb { get; }
    public double PreampDb { get; }
    public double HeadroomDb { get; }
    public double EffectivePreampDb => Enabled ? PreampDb - HeadroomDb : 0;
    public static bool SupportsRate(int rate) => rate is 44100 or 48000;
    public GraphicEqualizerSettings(bool enabled, IEnumerable<double> gainsDb, double preampDb = 0)
    {
        ArgumentNullException.ThrowIfNull(gainsDb);
        var values = gainsDb.Take(11).ToArray();
        if (values.Length != 10 || values.Any(v => !double.IsFinite(v) || v is < -15 or > 15))
            throw new ArgumentException("Informe dez bandas entre −15 e +15 dB.", nameof(gainsDb));
        if (!double.IsFinite(preampDb) || preampDb is < -30 or > 0) throw new ArgumentOutOfRangeException(nameof(preampDb));
        Enabled = enabled; GainsDb = Array.AsReadOnly(values); PreampDb = preampDb;
        HeadroomDb = values.Sum(v => Math.Max(0, v));
    }
}

// Serial per playback. Public NAudio peaking biquads, one independent cascade per channel.
// Preamp/headroom precedes the cascade; final digital level/representation protection belongs to the provider.
internal sealed class GraphicEqualizer
{
    private readonly BiQuadFilter?[] filters;
    private readonly float preamp;
    private readonly int channels;
    private long sanitized;
    public long SanitizedSamples => Volatile.Read(ref sanitized);
    public GraphicEqualizer(int sampleRate, int channels, GraphicEqualizerSettings settings)
    {
        if (!GraphicEqualizerSettings.SupportsRate(sampleRate) || channels is not (1 or 2) ||
            GraphicEqualizerSettings.Frequencies.Any(f => f <= 0 || f >= sampleRate / 2d))
            throw new NotSupportedException("EQ disponível somente em 44,1/48 kHz, mono/estéreo.");
        this.channels = channels;
        preamp = (float)Math.Pow(10, settings.EffectivePreampDb / 20);
        filters = new BiQuadFilter?[10 * channels];
        if (!settings.Enabled) return;
        for (int channel = 0; channel < channels; channel++)
            for (int band = 0; band < 10; band++)
                if (settings.GainsDb[band] != 0)
                    filters[channel * 10 + band] = BiQuadFilter.PeakingEQ(sampleRate,
                        GraphicEqualizerSettings.Frequencies[band], (float)Math.Sqrt(2), (float)settings.GainsDb[band]);
    }
    public float Process(float sample, int channel)
    {
        if ((uint)channel >= channels) throw new ArgumentOutOfRangeException(nameof(channel));
        // Reject representation outliers as corrupt input, without hard clipping normal audio before the filters.
        if (!float.IsFinite(sample) || Math.Abs(sample) > 1e12f) { Interlocked.Increment(ref sanitized); sample = 0; }
        sample *= preamp;
        for (int band = 0; band < 10; band++)
            if (filters[channel * 10 + band] is { } filter) sample = filter.Transform(sample);
        if (!float.IsFinite(sample)) throw new InvalidOperationException("EQ indisponível: resultado não finito. Pare e tente novamente com ajustes neutros.");
        return sample;
    }
}
