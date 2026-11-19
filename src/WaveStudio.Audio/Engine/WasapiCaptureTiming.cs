namespace WaveStudio.Audio.Engine;

internal readonly record struct WasapiRatePoint(ulong Qpc100ns, ulong Frames);
internal readonly record struct WasapiRateMeasurement(WasapiRatePoint Anchor, WasapiRatePoint Current,
    ulong Interval100ns, ulong AdvancedFrames, double SignedErrorFrames, double LimitFrames)
{
    public bool WithinLimit => Math.Abs(SignedErrorFrames) <= LimitFrames;
}

internal sealed record WasapiCaptureTimingSnapshot(int PhaseBudgetFrames, long ProvisionalReadings,
    long ConfirmedClockWindows, long ConfirmedPacketWindows, long ContinuousPackets, ulong? LastPacketPosition,
    ulong? ExpectedNextPacketPosition, WasapiRateMeasurement? ClockAccumulated, WasapiRateMeasurement? ClockRolling,
    WasapiRateMeasurement? PacketAccumulated, WasapiRateMeasurement? PacketRolling,
    double? CorroborationErrorFrames, double? CorroborationLimitFrames, string? Failure, bool Confirmed);

// Um único proprietário da captura. As taxas dos pacotes e do relógio são observações independentes no mesmo domínio QPC.
internal sealed class WasapiCaptureTiming
{
    private const ulong Window = 20_000_000;
    private const ulong Freshness = 5_000_000;
    private const ulong MaximumWindow = Window + Freshness;
    private readonly int rate;
    private readonly bool limitedNominal;
    private ulong? unavailableSince;
    private ulong unavailableTime;
    public long UnavailableReadings { get; private set; }
    private readonly RateWindow clock = new(), packets = new();
    private ulong nextPacketPosition, lastPacketPosition;
    private long continuousPackets, provisionalReadings;
    private string? failure;

    public WasapiCaptureTiming(int rate, long period100ns, uint capacity, bool limitedNominal = false)
    {
        if (rate is not (44100 or 48000) || period100ns is <= 0 or > 1_000_000 || capacity is 0 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(period100ns), "Formato, período ou capacidade fora do orçamento.");
        this.rate = rate;
        this.limitedNominal = limitedNominal;
        PhaseBudgetFrames = (int)Math.Min(capacity, ((ulong)period100ns * (uint)rate + 9_999_999) / 10_000_000);
    }

    public int PhaseBudgetFrames { get; }
    public bool HasRateWindows => clock.Rolling is { WithinLimit: true } && packets.Rolling is { WithinLimit: true };
    public bool HasNominalPackets(long now) => now >= 0 && packets.IsTerminallyValid((ulong)now);

    public NativeClockReason ObserveClock(ulong qpc, ulong frames, ref NativeClockValidation validation)
    {
        if (validation.AbsoluteErrorFrames is { } shortError &&
            shortError > validation.ErrorLimitFrames!.Value + PhaseBudgetFrames)
            return NativeClockReason.AdvanceOutsideTolerance;
        var reason = clock.Observe(new(qpc, frames), rate, PhaseBudgetFrames, keepRejectedWindow: limitedNominal);
        validation.ClockAccumulated = clock.Accumulated;
        validation.ClockRolling = clock.Rolling;
        if (reason == NativeClockReason.WindowRateOutsideTolerance && limitedNominal &&
            clock.ConfirmedWindows > 0 && clock.Accumulated is { WithinLimit: true } && HasNominalPackets(validation.Now100ns))
        {
            unavailableSince ??= qpc;
            if (qpc - unavailableSince.Value + unavailableTime >= Freshness) return reason;
            validation.Reason = reason;
            UnavailableReadings++;
            return NativeClockReason.None;
        }
        if (reason != NativeClockReason.None) return reason;
        if (unavailableSince is { } since)
        {
            unavailableTime += qpc - since;
            unavailableSince = null;
        }
        if (validation.AbsoluteErrorFrames > validation.ErrorLimitFrames ||
            clock.Accumulated is { WithinLimit: false })
        {
            validation.TemporalProvisional = true;
            provisionalReadings++;
        }
        return NativeClockReason.None;
    }

    // Valida antes de acessar PCM. A posição anterior só é confirmada após ReleaseBuffer integral.
    public void ValidatePacket(ulong position, uint frames, ulong qpc)
    {
        if (position > ulong.MaxValue - frames) Reject("PacketPositionOverflow");
        if (continuousPackets == 0) return;
        if (position != nextPacketPosition) Reject("PacketPositionNotContiguous");
        if (qpc <= packets.Last.Qpc100ns) Reject("PacketTimestampNotIncreasing");
        if (qpc - packets.Last.Qpc100ns > Freshness) Reject("PacketProgressExpired");
    }

    public void AcceptReleasedPacket(ulong position, uint frames, ulong qpc)
    {
        var reason = packets.Observe(new(qpc, position), rate, PhaseBudgetFrames);
        if (reason != NativeClockReason.None) Reject("Packet/" + reason);
        nextPacketPosition = position + frames;
        lastPacketPosition = position;
        continuousPackets++;
    }

    public void CheckProgress(long now, long start)
    {
        if (!packets.IsRecent((ulong)now, (ulong)start)) Reject("PacketProgressExpired");
        if (unavailableSince is { } since && (!HasNominalPackets(now) || (ulong)now - since + unavailableTime >= Freshness))
            Reject("NominalClockEvidenceExpired");
        if (!clock.IsRecent((ulong)now, (ulong)start)) Reject("ClockProgressExpired");
        if (packets.ConfirmationExpired((ulong)now)) Reject("PacketConfirmationExpired");
        if (clock.ConfirmationExpired((ulong)now)) Reject("ClockConfirmationExpired");
    }

    public WasapiCaptureTimingSnapshot Snapshot(long observationEnd)
    {
        double? error = null, limit = null;
        bool confirmed = false;
        if (failure is null && observationEnd > 0 && clock.IsTerminallyValid((ulong)observationEnd) &&
            packets.IsTerminallyValid((ulong)observationEnd) &&
            clock.Accumulated is { } native && packets.Accumulated is { } packet)
        {
            ulong start = Math.Max(native.Anchor.Qpc100ns, packet.Anchor.Qpc100ns);
            ulong end = Math.Min(native.Current.Qpc100ns, packet.Current.Qpc100ns);
            if (end >= start && end - start >= Window)
            {
                ulong overlap = end - start;
                error = Math.Abs(native.AdvancedFrames * ((double)overlap / native.Interval100ns) -
                    packet.AdvancedFrames * ((double)overlap / packet.Interval100ns));
                limit = NominalLimit(rate * (overlap / 10000000d));
                confirmed = error <= limit;
            }
        }
        return new(PhaseBudgetFrames, provisionalReadings, clock.ConfirmedWindows, packets.ConfirmedWindows,
            continuousPackets, continuousPackets == 0 ? null : lastPacketPosition, continuousPackets == 0 ? null : nextPacketPosition,
            clock.Accumulated, clock.Rolling, packets.Accumulated, packets.Rolling, error, limit, failure, confirmed);
    }

    private void Reject(string reason)
    {
        failure = reason;
        throw new InvalidOperationException(reason);
    }

    private static double NominalLimit(double expected) => expected * .005 + 2;

    private sealed class RateWindow
    {
        // Armazena no máximo uma âncora a cada 100 ms. Cada observação é validada em relação à âncora armazenada
        // mais recente com pelo menos 2 s de idade e à origem fixa, inclusive na última amostra.
        private const ulong AnchorSpacing = 1_000_000;
        private readonly WasapiRatePoint[] anchors = new WasapiRatePoint[32];
        private int head, count;
        private WasapiRatePoint origin, lastStored;
        public WasapiRatePoint Last { get; private set; }
        public WasapiRateMeasurement? Accumulated { get; private set; }
        public WasapiRateMeasurement? Rolling { get; private set; }
        public long ConfirmedWindows { get; private set; }

        public NativeClockReason Observe(WasapiRatePoint point, int rate, int phaseBudget, bool keepRejectedWindow = false)
        {
            if (count == 0)
            {
                origin = lastStored = Last = anchors[0] = point;
                count = 1;
                return NativeClockReason.None;
            }
            var accumulated = Measure(origin, point, rate);
            Accumulated = accumulated;
            if (Math.Abs(accumulated.SignedErrorFrames) > accumulated.LimitFrames + phaseBudget)
                return NativeClockReason.AccumulatedPhaseOutsideTolerance;

            while (count > 1 && point.Qpc100ns - anchors[(head + 1) % anchors.Length].Qpc100ns >= Window)
            {
                head = (head + 1) % anchors.Length;
                count--;
            }
            var result = NativeClockReason.None;
            ulong age = point.Qpc100ns - anchors[head].Qpc100ns;
            if (age >= Window)
            {
                var rolling = Measure(anchors[head], point, rate);
                Rolling = rolling;
                if (age > MaximumWindow) return NativeClockReason.WindowExpired;
                if (!rolling.WithinLimit)
                {
                    result = NativeClockReason.WindowRateOutsideTolerance;
                    if (!keepRejectedWindow) return result;
                }
                else ConfirmedWindows++;
            }
            if (point.Qpc100ns - lastStored.Qpc100ns >= AnchorSpacing)
            {
                if (count == anchors.Length) return NativeClockReason.WindowExpired;
                anchors[(head + count) % anchors.Length] = lastStored = point;
                count++;
            }
            Last = point;
            return result;
        }

        public bool IsRecent(ulong now, ulong start)
        {
            ulong reference = count == 0 ? start : Last.Qpc100ns;
            return now >= reference && now - reference <= Freshness;
        }

        public bool ConfirmationExpired(ulong now) =>
            count != 0 && ConfirmedWindows == 0 && now >= origin.Qpc100ns && now - origin.Qpc100ns > MaximumWindow;

        public bool IsTerminallyValid(ulong end) => IsRecent(end, 0) &&
            Rolling is { WithinLimit: true } rolling && rolling.Current == Last &&
            Accumulated is { WithinLimit: true } accumulated && accumulated.Current == Last;

        private static WasapiRateMeasurement Measure(WasapiRatePoint anchor, WasapiRatePoint point, int rate)
        {
            ulong ticks = point.Qpc100ns - anchor.Qpc100ns;
            ulong frames = point.Frames - anchor.Frames;
            double expected = rate * (ticks / 10000000d);
            return new(anchor, point, ticks, frames, frames - expected, NominalLimit(expected));
        }
    }
}
