using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace WaveStudio.Audio.Engine;

[JsonConverter(typeof(JsonStringEnumConverter<NativeClockReason>))]
internal enum NativeClockReason
{
    None, IdentityMismatch, ResetRequired, PacketDiscontinuity, PacketTimestampError, UnknownFlags,
    InaccurateReading, ZeroFrequency, UnsupportedUnit, QpcOutOfRange, NegativeNow, FutureTimestamp,
    StaleTimestamp, NonIncreasingTimestamp, FrameFrequencyMismatch, UnitChanged, FrequencyChanged,
    PositionRegressed, ProgressExpired, AdvanceOverflow, TotalFramesOverflow, IntervalTooLong,
    AdvanceOutsideTolerance, StartupWaiting, StartupMissingTimestamp, StartupExpired,
    AccumulatedPhaseOutsideTolerance, WindowRateOutsideTolerance, WindowExpired
}

internal readonly record struct NativeClockAnchor(NativePositionUnit Unit, ulong Frequency, ulong Position,
    ulong Qpc100ns, ulong LastProgressQpc100ns, long FramePosition, ulong Remainder);

// Values are populated at the point where validation uses them. Null marks a step the reading never reached.
internal record struct NativeClockValidation(NativePositionUnit Unit, NativeClockQuality Quality,
    TransportPacketFlags Flags, long Now100ns, long? Age100ns, NativeClockAnchor? Anchor)
{
    public NativeClockReason Reason { get; set; }
    public Int128? DeltaPosition { get; set; }
    public long? DeltaQpc100ns { get; set; }
    public UInt128? AdvancedFrames { get; set; }
    public double? IntervalSeconds { get; set; }
    public double? ExpectedFrames { get; set; }
    public double? AbsoluteErrorFrames { get; set; }
    public double? ErrorLimitFrames { get; set; }
    public ulong? RemainderAfter { get; set; }
    public bool TemporalProvisional { get; set; }
    public WasapiRateMeasurement? ClockAccumulated { get; set; }
    public WasapiRateMeasurement? ClockRolling { get; set; }
}

internal readonly record struct NativeClockTrace(long Index, Int128 SinceStart100ns, WasapiClockReading Reading,
    NativeClockResult Result, NativeClockValidation Validation);

internal sealed record NativeClockHistory(long TotalReadings, long OverwrittenReadings,
    ReadOnlyCollection<NativeClockTrace> Readings, NativeClockTrace? LastAccepted, NativeClockTrace? Rejection);
