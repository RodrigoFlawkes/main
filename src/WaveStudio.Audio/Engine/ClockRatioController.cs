namespace WaveStudio.Audio.Engine;

internal enum DriftControlState { Waiting, Tracking, Saturated, ReprimeRequired }
internal readonly record struct DriftControl(double OutputPerInput, double CorrectionPpm, double FilteredOccupancy,
    double IntegralPpm, DriftControlState State);

// Experimento no domínio das contagens. Não varia a taxa de um resampler de amostras nem interage com endpoints.
internal sealed class ClockRatioController
{
    private readonly double nominal, limit, slew;
    private readonly int capacity, target;
    private readonly long frequency;
    private long previous;
    private double correction, integral, occupancy, occupancyUpper;
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
        previous = now; correction = integral = 0; occupancy = occupancyUpper = target; tracking = failed = false;
    }
    // bufferedFrames é a média ponderada pelo tempo no intervalo de controle. Falhas reais
    // nos limites da fila devem ser reportadas como 0/capacidade ou tratadas com reinicialização pelo proprietário.
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
        // Integração condicional: permite somente atualizações sem saturação ou que reduzam a saturação.
        if (Math.Abs(requested) <= limit || (requested > limit && error > 0) || (requested < -limit && error < 0))
            integral = candidateIntegral;
        requested = feedForward - .8 * error + integral;
        double bounded = Math.Clamp(requested, -limit, limit);
        correction += Math.Clamp(bounded - correction, -slew * dt, slew * dt);
        return Snapshot(Math.Abs(requested) > limit ? DriftControlState.Saturated : DriftControlState.Tracking);
    }
    public DriftControl UpdateBounded(long now, double minimumFrames, double maximumFrames, PpmInterval estimate)
    {
        double dt = now >= previous ? (now - previous) / (double)frequency : -1;
        if (dt <= 0 || dt > .5 || !double.IsFinite(minimumFrames) || !double.IsFinite(maximumFrames) ||
            minimumFrames <= 0 || maximumFrames >= capacity || minimumFrames > maximumFrames ||
            !double.IsFinite(estimate.Minimum) || !double.IsFinite(estimate.Maximum) || estimate.Minimum > estimate.Maximum)
            failed = true;
        if (failed) return Snapshot(DriftControlState.ReprimeRequired);
        previous = now;
        double weight = dt / (2 + dt);
        occupancy += weight * (minimumFrames - occupancy);
        occupancyUpper += weight * (maximumFrames - occupancyUpper);
        double error = new PpmInterval(occupancy - target, occupancyUpper - target).NearestZero;
        double candidate = Math.Clamp(integral - .02 * error * dt, -limit, limit);
        var requested = new PpmInterval(estimate.Minimum - .8 * (occupancyUpper - target),
            estimate.Maximum - .8 * (occupancy - target));
        double next = new PpmInterval(requested.Minimum + candidate, requested.Maximum + candidate).NearestZero;
        if (Math.Abs(next) <= limit || (next > limit && error > 0) || (next < -limit && error < 0))
            integral = candidate;
        next = new PpmInterval(requested.Minimum + integral, requested.Maximum + integral).NearestZero;
        double bounded = Math.Clamp(next, -limit, limit);
        correction += Math.Clamp(bounded - correction, -slew * dt, slew * dt);
        return Snapshot(Math.Abs(next) > limit ? DriftControlState.Saturated : DriftControlState.Tracking);
    }
    private DriftControl Snapshot(DriftControlState state) => new(nominal * (1 + correction * 1e-6), correction, occupancy, integral, state);
}
