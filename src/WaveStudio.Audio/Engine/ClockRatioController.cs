namespace WaveStudio.Audio.Engine;

internal enum DriftControlState { Waiting, Tracking, Saturated, ReprimeRequired }
internal readonly record struct DriftControl(double OutputPerInput, double CorrectionPpm, double FilteredOccupancy,
    double IntegralPpm, DriftControlState State);

// Count-domain experiment. Does not vary a sample resampler or interact with endpoints.
internal sealed class ClockRatioController
{
    private readonly double nominal, limit, slew;
    private readonly int capacity, target;
    private readonly long frequency;
    private long previous;
    private double correction, integral, occupancy;
    private bool tracking, failed;
    public ClockRatioController(int inputRate, int outputRate, long timestampFrequency,
        int capacity = 2048, int target = 1024, double limitPpm = 500, double slewPpmPerSecond = 25)
    {
        if (inputRate is < 8000 or > 192000 || outputRate is < 8000 or > 192000 || timestampFrequency is < 1 or > 1000000000 ||
            capacity is < 2 or > 65536 || target <= 0 || target >= capacity || !double.IsFinite(limitPpm) ||
            limitPpm is < 10 or > 1000 || !double.IsFinite(slewPpmPerSecond) || slewPpmPerSecond is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limitPpm));
        nominal = (double)outputRate / inputRate; frequency = timestampFrequency;
        this.capacity = capacity; this.target = target; limit = limitPpm; slew = slewPpmPerSecond;
        Reset(0);
    }
    public void Reset(long now)
    {
        if (now < 0) throw new ArgumentOutOfRangeException(nameof(now));
        previous = now; correction = integral = 0; occupancy = target; tracking = failed = false;
    }
    // bufferedFrames is a time-weighted mean over the control interval. Actual
    // queue-boundary failures must be reported as 0/capacity, or reset by owner.
    public DriftControl Update(long now, int bufferedFrames, ClockEstimate? estimate)
    {
        if (bufferedFrames < 0 || bufferedFrames > capacity) throw new ArgumentOutOfRangeException(nameof(bufferedFrames));
        double dt = now >= previous ? (now - previous) / (double)frequency : -1;
        bool validEstimate = estimate is { } value && double.IsFinite(value.OutputPerInput) && value.OutputPerInput > 0 &&
            value.Timestamp >= 0 && value.Timestamp <= now && now - value.Timestamp <= frequency * 3;
        if (dt <= 0 || dt > .5 || (tracking && !validEstimate) || bufferedFrames is 0 || bufferedFrames == capacity)
            failed = true;
        if (failed) return Snapshot(DriftControlState.ReprimeRequired);
        previous = now;
        if (!validEstimate) return Snapshot(DriftControlState.Waiting);
        tracking = true;
        occupancy += dt / (2 + dt) * (bufferedFrames - occupancy);
        double error = occupancy - target;
        double feedForward = (estimate!.Value.OutputPerInput / nominal - 1) * 1e6;
        double candidateIntegral = Math.Clamp(integral - .02 * error * dt, -limit, limit);
        double requested = feedForward - .8 * error + candidateIntegral;
        // Conditional integration: allow only unsaturated or unwinding updates.
        if (Math.Abs(requested) <= limit || (requested > limit && error > 0) || (requested < -limit && error < 0))
            integral = candidateIntegral;
        requested = feedForward - .8 * error + integral;
        double bounded = Math.Clamp(requested, -limit, limit);
        correction += Math.Clamp(bounded - correction, -slew * dt, slew * dt);
        return Snapshot(Math.Abs(requested) > limit ? DriftControlState.Saturated : DriftControlState.Tracking);
    }
    private DriftControl Snapshot(DriftControlState state) => new(nominal * (1 + correction * 1e-6), correction, occupancy, integral, state);
}
