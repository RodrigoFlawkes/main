namespace WaveStudio.Audio.Engine;

internal readonly record struct ProtectionCounts(long NonFiniteSamples, long ClippedSamples);

// No effect/gain applied. Safety is NOT an effect bypass and is always active.
// Hard bounds protect the output, but are not an artistic or look-ahead limiter.
internal static class NeutralProcessor
{
    public static ProtectionCounts Process(Span<float> samples)
    {
        long nonFinite = 0, clipped = 0;
        for (int index = 0; index < samples.Length; index++)
        {
            float sample = samples[index];
            if (!float.IsFinite(sample)) { samples[index] = 0; nonFinite++; }
            else if (sample > 1) { samples[index] = 1; clipped++; }
            else if (sample < -1) { samples[index] = -1; clipped++; }
        }
        return new(nonFinite, clipped);
    }
}
