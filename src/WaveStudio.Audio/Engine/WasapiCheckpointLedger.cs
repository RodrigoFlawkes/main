namespace WaveStudio.Audio.Engine;

internal enum WasapiCheckpointStatus
{
    Disabled, Inactive, WrongGeneration, OwnerBusy, OwnerUnconfirmed, OwnerStale,
    InvalidTime, InvalidAccounting, InvalidSequence, Overflow, OwnerFault, InvalidBaseline, HistoryLost, Available
}

// As contagens nativas usam os frames do endpoint correspondente. As da FIFO usam frames de saída.
internal readonly record struct WasapiCheckpointCounts(
    long PacketsAcquired, long PacketsReleased, long FramesAcquired, long FramesReleased,
    long PacketsAccepted, long FramesAccepted, long StartupDiscardedPackets, long StartupDiscardedFrames,
    long FifoFrames, long FifoMissingFrames, long PrimedFrames, long PreTicketRenderFrames = 0, long WarmupRenderFrames = 0)
{
    public long FifoAttemptedFrames => checked(FifoFrames + FifoMissingFrames);
}

internal readonly record struct WasapiCheckpointOwner(long Sequence, long Completed100ns, WasapiCheckpointCounts Counts, WasapiDriftOwner Evidence = default);
internal readonly record struct WasapiOccupancyBounds(Int128 MinimumFrameTicks, Int128 MaximumFrameTicks, long Duration100ns);
internal sealed record WasapiCheckpoint(object LedgerIdentity, long Cut100ns,
    WasapiCheckpointOwner Capture, WasapiCheckpointOwner Render, long QueuedFrames, WasapiOccupancyBounds? Mean);
internal readonly record struct WasapiCheckpointResult(WasapiCheckpointStatus Status, WasapiCheckpoint? Checkpoint = null);

// Cada MTA escreve seu próprio diário. O coordenador lê somente e faz tentativas limitadas.
internal sealed class WasapiCheckpointLedger
{
    internal const int MaxHistory = 256;
    internal const long MaxAcknowledgmentAge100ns = 5_000_000;
    private const int ReadAttempts = 3;
    private readonly DeliveryTicket ticket;
    private readonly object identity = new();
    private readonly int capacity;
    private readonly Func<long> now;
    private readonly long created;

    public Journal Capture { get; }
    public Journal Render { get; }

    public WasapiCheckpointLedger(DeliveryTicket ticket, int capacity, Func<long>? now = null, int history = MaxHistory)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(history);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(history, MaxHistory);
        this.ticket = ticket;
        this.capacity = capacity;
        this.now = now ?? WasapiTime.Now100ns;
        created = this.now();
        Capture = new(this.now, history, true);
        Render = new(this.now, history, false);
    }

    public WasapiCheckpointResult Read(long generation, WasapiCheckpoint? baseline = null)
    {
        if (generation != ticket.Capture.Generation) return new(WasapiCheckpointStatus.WrongGeneration);
        if (baseline is not null && !ReferenceEquals(baseline.LedgerIdentity, identity)) return new(WasapiCheckpointStatus.InvalidBaseline);
        for (int retry = 0; retry < ReadAttempts; retry++)
        {
            if (!ticket.IsValid) return new(WasapiCheckpointStatus.Inactive);
            long captureVersion = Capture.Version, renderVersion = Render.Version;
            long cut = now();
            if (((captureVersion | renderVersion) & 1) != 0)
            {
                bool stale = Capture.StatusAt(cut, created) == WasapiCheckpointStatus.OwnerStale ||
                    Render.StatusAt(cut, created) == WasapiCheckpointStatus.OwnerStale;
                Thread.MemoryBarrier();
                if (stale && captureVersion == Capture.Version && renderVersion == Render.Version && ticket.IsValid)
                    return new(WasapiCheckpointStatus.OwnerStale);
                continue;
            }

            var capture = Capture.State;
            var render = Render.State;
            long queued = 0;
            WasapiOccupancyBounds? bounds = null;
            var status = CheckOwner(Capture, cut);
            if (status == WasapiCheckpointStatus.Available) status = CheckOwner(Render, cut);
            if (status == WasapiCheckpointStatus.Available)
            {
                queued = capture.Counts.FifoFrames - render.Counts.FifoFrames;
                if (queued < 0 || queued > capacity) status = WasapiCheckpointStatus.InvalidAccounting;
                else if (baseline is not null) status = Integrate(baseline, capture, render, cut, out bounds);
            }

            // O corte fica entre as duas leituras de cada versão. Leituras especulativas são descartadas.
            Thread.MemoryBarrier();
            if (captureVersion != Capture.Version || renderVersion != Render.Version) continue;
            if (!ticket.IsValid) return new(WasapiCheckpointStatus.Inactive);
            return status == WasapiCheckpointStatus.Available
                ? new(status, new(identity, cut, capture, render, queued, bounds))
                : new(status);
        }
        return new(ticket.IsValid ? WasapiCheckpointStatus.OwnerBusy : WasapiCheckpointStatus.Inactive);
    }

    private WasapiCheckpointStatus CheckOwner(Journal journal, long cut)
    {
        if (journal.Fault != WasapiCheckpointStatus.Available) return journal.Fault;
        return journal.StatusAt(cut, created);
    }

    private WasapiCheckpointStatus Integrate(WasapiCheckpoint baseline, WasapiCheckpointOwner capture, WasapiCheckpointOwner render,
        long cut, out WasapiOccupancyBounds? bounds)
    {
        bounds = null;
        if (baseline.Cut100ns < 0 || baseline.Cut100ns >= cut ||
            baseline.QueuedFrames < 0 || baseline.QueuedFrames > capacity)
            return WasapiCheckpointStatus.InvalidBaseline;

        long duration = cut - baseline.Cut100ns;
        Int128 minimum = (Int128)baseline.QueuedFrames * duration, maximum = minimum;
        var status = Capture.Accumulate(baseline.Capture, capture, baseline.Cut100ns, cut, ref minimum, ref maximum);
        if (status != WasapiCheckpointStatus.Available) return status;
        status = Render.Accumulate(baseline.Render, render, baseline.Cut100ns, cut, ref minimum, ref maximum);
        if (status != WasapiCheckpointStatus.Available) return status;

        minimum = Int128.Max(0, minimum);
        maximum = Int128.Min((Int128)capacity * duration, maximum);
        if (minimum > maximum) return WasapiCheckpointStatus.InvalidAccounting;
        bounds = new(minimum, maximum, duration);
        return WasapiCheckpointStatus.Available;
    }

    internal sealed class Journal(Func<long> now, int history, bool input)
    {
        private readonly record struct Event(long Sequence, long Begin100ns, long End100ns, long FifoFrames);
        private readonly Event[] events = new Event[history];
        private long version, begin, completed = -1;
        public long Version => Volatile.Read(ref version);
        public WasapiCheckpointOwner State { get; private set; }
        public WasapiCheckpointStatus Fault { get; private set; } = WasapiCheckpointStatus.Available;

        // A versão ímpar cobre toda a operação, inclusive GetBuffer, ReleaseBuffer e as mutações da FIFO.
        public void Begin()
        {
            if (Fault != WasapiCheckpointStatus.Available) return;
            long current = Version;
            if ((current & 1) != 0) { Abort(WasapiCheckpointStatus.InvalidSequence); return; }
            Interlocked.Increment(ref version);
            if (current >= long.MaxValue - 3) { Abort(WasapiCheckpointStatus.Overflow); return; }
            begin = now();
        }

        public WasapiCheckpointStatus Complete(WasapiCheckpointCounts counts, WasapiDriftOwner evidence = default)
        {
            if (Fault != WasapiCheckpointStatus.Available) return Fault;
            if ((Version & 1) == 0) return Abort(WasapiCheckpointStatus.InvalidSequence);
            long end = now();
            var previous = State.Counts;
            if (begin < 0 || begin < completed || end < begin) return Abort(WasapiCheckpointStatus.InvalidTime);
            if (!Consistent(previous, counts)) return Abort(WasapiCheckpointStatus.InvalidAccounting);
            if (counts.FifoFrames > long.MaxValue - counts.FifoMissingFrames)
                return Abort(WasapiCheckpointStatus.Overflow);
            if (!input && counts.FifoAttemptedFrames != counts.FramesReleased - counts.PrimedFrames - counts.PreTicketRenderFrames - counts.WarmupRenderFrames)
                return Abort(WasapiCheckpointStatus.InvalidAccounting);

            long sequence = State.Sequence;
            if (counts != previous)
            {
                // A série registra alterações de contagem. Uma confirmação sem alteração só atualiza a fronteira segura.
                if (sequence == long.MaxValue) return Abort(WasapiCheckpointStatus.Overflow);
                sequence++;
                events[(int)((sequence - 1) % events.Length)] = new(sequence, begin, end, counts.FifoFrames - previous.FifoFrames);
            }
            State = new(sequence, end, counts, evidence);
            Volatile.Write(ref completed, end);
            Interlocked.Increment(ref version);
            return WasapiCheckpointStatus.Available;
        }

        public WasapiCheckpointStatus Abort(WasapiCheckpointStatus reason = WasapiCheckpointStatus.OwnerFault)
        {
            if (Fault != WasapiCheckpointStatus.Available) return Fault;
            if ((Version & 1) == 0) Interlocked.Increment(ref version);
            Fault = reason;
            Interlocked.Increment(ref version);
            return reason;
        }

        public WasapiCheckpointStatus StatusAt(long cut, long created)
        {
            long last = Volatile.Read(ref completed);
            if (created < 0 || cut < created || last > cut) return WasapiCheckpointStatus.InvalidTime;
            if (cut - Math.Max(created, last) > MaxAcknowledgmentAge100ns) return WasapiCheckpointStatus.OwnerStale;
            return last < 0 ? WasapiCheckpointStatus.OwnerUnconfirmed : WasapiCheckpointStatus.Available;
        }

        private bool Consistent(WasapiCheckpointCounts old, WasapiCheckpointCounts current)
        {
            if (current.PacketsAcquired != current.PacketsReleased || current.FramesAcquired != current.FramesReleased ||
                current.PacketsReleased < old.PacketsReleased || current.FramesReleased < old.FramesReleased ||
                current.FifoFrames < old.FifoFrames || current.FifoMissingFrames < old.FifoMissingFrames ||
                current.PacketsAccepted < old.PacketsAccepted || current.FramesAccepted < old.FramesAccepted ||
                current.StartupDiscardedPackets < old.StartupDiscardedPackets || current.StartupDiscardedFrames < old.StartupDiscardedFrames ||
                current.PrimedFrames < old.PrimedFrames || current.PreTicketRenderFrames < old.PreTicketRenderFrames || current.WarmupRenderFrames < old.WarmupRenderFrames)
                return false;

            if (input)
                return current.PrimedFrames == 0 && current.PreTicketRenderFrames == 0 && current.WarmupRenderFrames == 0 &&
                    current.StartupDiscardedPackets <= current.PacketsReleased && current.StartupDiscardedFrames <= current.FramesReleased &&
                    current.PacketsAccepted == current.PacketsReleased - current.StartupDiscardedPackets &&
                    current.FramesAccepted == current.FramesReleased - current.StartupDiscardedFrames &&
                    (current.PacketsAccepted != old.PacketsAccepted ||
                        (current.FramesAccepted == old.FramesAccepted && current.FifoFrames == old.FifoFrames &&
                         current.FifoMissingFrames == old.FifoMissingFrames));

            return current.PacketsAccepted == 0 && current.FramesAccepted == 0 &&
                current.StartupDiscardedPackets == 0 && current.StartupDiscardedFrames == 0 &&
                current.PrimedFrames <= current.FramesReleased &&
                current.PreTicketRenderFrames <= current.FramesReleased - current.PrimedFrames &&
                current.WarmupRenderFrames <= current.FramesReleased - current.PrimedFrames - current.PreTicketRenderFrames;
        }

        public WasapiCheckpointStatus Accumulate(WasapiCheckpointOwner baseline, WasapiCheckpointOwner final,
            long start, long cut, ref Int128 minimum, ref Int128 maximum)
        {
            if (baseline.Sequence < 0 || final.Sequence < baseline.Sequence) return WasapiCheckpointStatus.InvalidBaseline;
            long count = final.Sequence - baseline.Sequence;
            if (count > events.Length) return WasapiCheckpointStatus.HistoryLost;
            Int128 frames = 0;
            long last = start;
            for (long index = 0; index < count; index++)
            {
                long slot = baseline.Sequence + index;
                var record = events[(int)(slot % events.Length)];
                if (record.Sequence != slot + 1) return WasapiCheckpointStatus.HistoryLost;
                if (record.Begin100ns < last || record.End100ns < record.Begin100ns || record.End100ns > cut)
                    return WasapiCheckpointStatus.InvalidTime;
                if (record.FifoFrames < 0) return WasapiCheckpointStatus.InvalidAccounting;
                last = record.End100ns;
                frames += record.FifoFrames;

                // Cada mutação real ocorreu entre o início e o fim do lote. Isso também vale com lotes simultâneos.
                Int128 earliest = (Int128)record.FifoFrames * (cut - record.Begin100ns);
                Int128 latest = (Int128)record.FifoFrames * (cut - record.End100ns);
                try
                {
                    minimum = checked(input ? minimum + latest : minimum - earliest);
                    maximum = checked(input ? maximum + earliest : maximum - latest);
                }
                catch (OverflowException) { return WasapiCheckpointStatus.Overflow; }
            }
            return frames == (Int128)final.Counts.FifoFrames - baseline.Counts.FifoFrames
                ? WasapiCheckpointStatus.Available : WasapiCheckpointStatus.InvalidAccounting;
        }
    }
}
