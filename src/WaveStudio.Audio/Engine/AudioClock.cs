namespace WaveStudio.Audio.Engine;

internal enum ClockQuality { Unavailable, Estimated, Monotonic }
internal sealed record AudioClock
{
    public string Id { get; }
    public long Generation { get; }
    public int NominalRate { get; }
    public string TimestampDomain { get; }
    public long TimestampFrequency { get; }
    public AudioClock(string id, long generation, int nominalRate, string timestampDomain, long timestampFrequency)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || string.IsNullOrWhiteSpace(timestampDomain))
            throw new ArgumentException("Identidade e domínio de relógio explícitos são obrigatórios.");
        if (generation <= 0 || nominalRate is < 8000 or > 192000 || timestampFrequency is < 1 or > 1000000000)
            throw new ArgumentOutOfRangeException(nameof(generation));
        Id = id; Generation = generation; NominalRate = nominalRate;
        TimestampDomain = timestampDomain; TimestampFrequency = timestampFrequency;
    }
}

// Position is already converted to frames, timestamp to the declared common domain.
internal readonly record struct ClockObservation(AudioClock Clock, long FramePosition, long Timestamp,
    ClockQuality Quality = ClockQuality.Monotonic, bool Discontinuity = false);
internal readonly record struct ClockEstimate(double OutputPerInput, long Timestamp);
internal enum ClockObservationResult { Waiting, Updated, InvalidIdentity, InvalidTime, InvalidPosition, Stale, Discontinuity, Implausible }

// Single owner; no backend/API claim. Invalid observations do not advance the baseline.
internal sealed class ClockRatioEstimator
{
    private readonly AudioClock input, output;
    private ClockObservation firstInput, firstOutput, lastInput, lastOutput;
    private bool started, estimated;
    private long lastReference;
    public ClockEstimate? Latest { get; private set; }
    public ClockRatioEstimator(AudioClock input, AudioClock output)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(output);
        if (input.TimestampDomain != output.TimestampDomain || input.TimestampFrequency != output.TimestampFrequency)
            throw new ArgumentException("Os timestamps exigem conversão para referência comum antes da estimativa.");
        this.input = input; this.output = output;
    }
    public void Reset() { started = estimated = false; lastReference = 0; Latest = null; firstInput = firstOutput = lastInput = lastOutput = default; }
    public ClockObservationResult Observe(ClockObservation capture, ClockObservation render, long now)
    {
        if (capture.Clock != input || render.Clock != output) return ClockObservationResult.InvalidIdentity;
        if (capture.Discontinuity || render.Discontinuity) { Reset(); return ClockObservationResult.Discontinuity; }
        if (now < 0 || (started && now < lastReference) || capture.Timestamp < 0 || render.Timestamp < 0 || capture.Timestamp > now || render.Timestamp > now ||
            capture.Quality != ClockQuality.Monotonic || render.Quality != ClockQuality.Monotonic)
            return ClockObservationResult.InvalidTime;
        if (now - capture.Timestamp > input.TimestampFrequency / 2 || now - render.Timestamp > output.TimestampFrequency / 2)
            return ClockObservationResult.Stale;
        if (capture.FramePosition < 0 || render.FramePosition < 0) return ClockObservationResult.InvalidPosition;
        if (!started)
        {
            firstInput = lastInput = capture; firstOutput = lastOutput = render; started = true; lastReference = now;
            return ClockObservationResult.Waiting;
        }
        if (capture.Timestamp <= lastInput.Timestamp || render.Timestamp <= lastOutput.Timestamp) return ClockObservationResult.InvalidTime;
        if (capture.FramePosition < lastInput.FramePosition || render.FramePosition < lastOutput.FramePosition) return ClockObservationResult.InvalidPosition;
        double inputDt = (capture.Timestamp - lastInput.Timestamp) / (double)input.TimestampFrequency;
        double outputDt = (render.Timestamp - lastOutput.Timestamp) / (double)output.TimestampFrequency;
        if (inputDt > 2 || outputDt > 2) { Reset(); return ClockObservationResult.Discontinuity; }
        // Allow two frames of quantization per short observation; also validate the long window below.
        if (!Plausible(capture.FramePosition - lastInput.FramePosition, inputDt, input.NominalRate, 2) ||
            !Plausible(render.FramePosition - lastOutput.FramePosition, outputDt, output.NominalRate, 2))
            return ClockObservationResult.Implausible;
        lastInput = capture; lastOutput = render; lastReference = now;
        inputDt = (capture.Timestamp - firstInput.Timestamp) / (double)input.TimestampFrequency;
        outputDt = (render.Timestamp - firstOutput.Timestamp) / (double)output.TimestampFrequency;
        if (inputDt < 1 || outputDt < 1) return ClockObservationResult.Waiting;
        long inputFrames = capture.FramePosition - firstInput.FramePosition, outputFrames = render.FramePosition - firstOutput.FramePosition;
        if (inputFrames <= 0 || outputFrames <= 0 || !Plausible(inputFrames, inputDt, input.NominalRate, 2) ||
            !Plausible(outputFrames, outputDt, output.NominalRate, 2)) return ClockObservationResult.Implausible;
        double ratio = (outputFrames / outputDt) / (inputFrames / inputDt);
        double filtered = estimated ? Latest!.Value.OutputPerInput + .2 * (ratio - Latest.Value.OutputPerInput) : ratio;
        Latest = new(filtered, now); estimated = true;
        firstInput = capture; firstOutput = render;
        return ClockObservationResult.Updated;
    }
    private static bool Plausible(long frames, double seconds, int rate, int quantization) =>
        Math.Abs(frames - seconds * rate) <= seconds * rate * .005 + quantization;
}
