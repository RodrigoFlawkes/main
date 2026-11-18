using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiClockDiagnosticTests
{
    private const ulong InitialQpc = 10_000_000;
    private const ulong InitialPosition = 192_000;
    private const int Rate = 48_000;
    private static TransportEndpoint Endpoint => WasapiContractTests.Route().Capture.Endpoint;
    private static NativeClockSample Initial => new(Endpoint, InitialPosition, 192_000,
        InitialQpc, NativePositionUnit.DeviceUnits, NativeClockQuality.Accurate);

    [Theory]
    [InlineData("unit", "InvalidPosition", "UnitChanged")]
    [InlineData("frequency", "InvalidPosition", "FrequencyChanged")]
    [InlineData("regression", "InvalidPosition", "PositionRegressed")]
    [InlineData("interval", "InvalidPosition", "IntervalTooLong")]
    [InlineData("fast", "InvalidPosition", "AdvanceOutsideTolerance")]
    [InlineData("slow", "InvalidPosition", "AdvanceOutsideTolerance")]
    [InlineData("future", "InvalidTime", "FutureTimestamp")]
    [InlineData("stale", "InvalidTime", "StaleTimestamp")]
    [InlineData("repeated-qpc", "InvalidTime", "NonIncreasingTimestamp")]
    [InlineData("qpc-range", "InvalidTime", "QpcOutOfRange")]
    [InlineData("negative-now", "InvalidTime", "NegativeNow")]
    [InlineData("bad-flags", "Unavailable", "UnknownFlags")]
    [InlineData("timestamp-error", "TimestampError", "PacketTimestampError")]
    [InlineData("discontinuity", "Discontinuity", "PacketDiscontinuity")]
    [InlineData("unavailable", "Unavailable", "InaccurateReading")]
    [InlineData("uncertain", "Unavailable", "InaccurateReading")]
    [InlineData("frequency-zero", "Unavailable", "ZeroFrequency")]
    [InlineData("unsupported-unit", "Unavailable", "UnsupportedUnit")]
    [InlineData("frame-frequency", "Unavailable", "FrameFrequencyMismatch")]
    [InlineData("overflow", "Overflow", "AdvanceOverflow")]
    public void RejectionRetainsItsExactReasonAndPreviousAnchor(string fault, string resultName, string reason)
    {
        var first = fault == "overflow" ? Initial with { Frequency = 1 } : Initial;
        var adapter = new WasapiClockAdapter(first.Endpoint, Rate);
        Assert.Equal(NativeClockResult.Valid, adapter.Convert(first, (long)first.Qpc100ns, out _, out var initial));
        Assert.Null(initial.Anchor);
        var next = first with { Position = first.Position + 1920, Qpc100ns = first.Qpc100ns + 100_000 };
        next = fault switch
        {
            "unit" => next with { Unit = NativePositionUnit.Frames, Frequency = Rate },
            "frequency" => next with { Frequency = 192001 },
            "regression" => next with { Position = first.Position - 1 },
            "interval" => next with { Qpc100ns = first.Qpc100ns + 20_000_001, Position = first.Position + 384_000 },
            "fast" => next with { Position = first.Position + 1940 },
            "slow" => next with { Position = first.Position + 1900 },
            "repeated-qpc" => next with { Qpc100ns = first.Qpc100ns },
            "qpc-range" => next with { Qpc100ns = ulong.MaxValue },
            "bad-flags" => next with { Flags = (TransportPacketFlags)8 },
            "timestamp-error" => next with { Flags = TransportPacketFlags.TimestampError },
            "discontinuity" => next with { Flags = TransportPacketFlags.Discontinuity },
            "unavailable" => next with { Quality = NativeClockQuality.Unavailable },
            "uncertain" => next with { Quality = NativeClockQuality.Uncertain },
            "frequency-zero" => next with { Frequency = 0 },
            "unsupported-unit" => next with { Unit = (NativePositionUnit)99 },
            "frame-frequency" => next with { Unit = NativePositionUnit.Frames },
            "overflow" => next with { Position = ulong.MaxValue },
            _ => next
        };
        long now = fault switch
        {
            "future" => (long)next.Qpc100ns - 1,
            "stale" => (long)next.Qpc100ns + 5_000_001,
            "negative-now" => -1,
            "qpc-range" => long.MaxValue,
            _ => (long)next.Qpc100ns
        };

        var result = adapter.Convert(next, now, out _, out var diagnostic);

        Assert.Equal(resultName, result.ToString());
        Assert.Equal(reason, diagnostic.Reason.ToString());
        Assert.True(adapter.RequiresReset);
        Assert.Equal(first.Position, diagnostic.Anchor!.Value.Position);
        Assert.Equal(first.Qpc100ns, diagnostic.Anchor.Value.Qpc100ns);
        Assert.Equal(first.Frequency, diagnostic.Anchor.Value.Frequency);
        Assert.Equal(0UL, diagnostic.Anchor.Value.Remainder);
        Assert.Equal(next.Flags, diagnostic.Flags);
        Assert.Equal(next.Quality, diagnostic.Quality);
        Assert.Equal(next.Unit, diagnostic.Unit);
        Assert.Equal(now, diagnostic.Now100ns);
        if (fault == "regression") Assert.Equal((Int128)(-1), diagnostic.DeltaPosition);
        if (fault == "future") Assert.Equal(-1L, diagnostic.Age100ns);
        if (fault == "qpc-range") Assert.Null(diagnostic.Age100ns);
        if (fault == "interval") Assert.True(diagnostic.IntervalSeconds > 2);
        if (fault is "fast" or "slow")
        {
            Assert.Equal(fault == "fast" ? (UInt128)485 : 475, diagnostic.AdvancedFrames);
            Assert.Equal(480d, diagnostic.ExpectedFrames);
            Assert.Equal(5d, diagnostic.AbsoluteErrorFrames);
            Assert.Equal(4.4d, diagnostic.ErrorLimitFrames);
        }
        Assert.Equal(NativeClockResult.RequiresReset, adapter.Convert(first, 10_000_000, out _, out var reset));
        Assert.Equal(NativeClockReason.ResetRequired, reset.Reason);
        Assert.Equal(diagnostic.Anchor, reset.Anchor);
    }

    [Theory]
    [InlineData(1191, false)]
    [InlineData(1192, true)]
    [InlineData(1208, true)]
    [InlineData(1209, false)]
    public void AdvanceToleranceBoundaryRemainsInclusive(int frames, bool valid)
    {
        var first = Initial;
        var adapter = new WasapiClockAdapter(first.Endpoint, Rate);
        adapter.Convert(first, (long)first.Qpc100ns, out _);
        var next = first with { Position = first.Position + (ulong)frames * 4, Qpc100ns = first.Qpc100ns + 250_000 };

        var result = adapter.Convert(next, (long)next.Qpc100ns, out _, out var diagnostic);

        Assert.Equal(valid ? NativeClockResult.Valid : NativeClockResult.InvalidPosition, result);
        Assert.Equal((UInt128)frames, diagnostic.AdvancedFrames);
        Assert.Equal(1200d, diagnostic.ExpectedFrames);
        Assert.Equal(8d, diagnostic.ErrorLimitFrames);
    }

    [Fact]
    public void ExactTwoSecondIntervalAndFiveHundredMillisecondAgeAreValid()
    {
        var first = Initial;
        var adapter = new WasapiClockAdapter(first.Endpoint, Rate);
        adapter.Convert(first, (long)first.Qpc100ns, out _);
        var next = first with { Position = first.Position + 384_000, Qpc100ns = first.Qpc100ns + 20_000_000 };

        Assert.Equal(NativeClockResult.Valid, adapter.Convert(next, (long)next.Qpc100ns + 5_000_000, out var observation, out var trace));
        Assert.Equal(96_000L, observation.FramePosition);
        Assert.Equal(5_000_000L, trace.Age100ns);
        Assert.Equal(2d, trace.IntervalSeconds);
    }

    [Fact]
    public void IntegerRemainderIsCapturedBeforeAndAfterConversion()
    {
        var first = Initial with { Frequency = 100_000 };
        var adapter = new WasapiClockAdapter(first.Endpoint, Rate);
        adapter.Convert(first, (long)first.Qpc100ns, out _);
        for (uint step = 1; step <= 3; step++)
        {
            var next = first with { Position = first.Position + step, Qpc100ns = first.Qpc100ns + step * 100 };
            Assert.Equal(NativeClockResult.Valid, adapter.Convert(next, (long)next.Qpc100ns, out var observation, out var trace));
            Assert.Equal(((step - 1) * 48_000UL) % 100_000, trace.Anchor!.Value.Remainder);
            Assert.Equal((step * 48_000UL) % 100_000, trace.RemainderAfter);
            Assert.Equal(step == 3 ? 1 : 0, observation.FramePosition);
        }
    }

    [Fact]
    public void RepeatedPositionRemainsValidUntilProgressDeadline()
    {
        var first = Initial with { Position = 0 };
        var adapter = new WasapiClockAdapter(first.Endpoint, Rate);
        adapter.Convert(first, (long)first.Qpc100ns, out _);
        for (uint elapsed = 400; elapsed <= 5_000_000; elapsed += 400)
        {
            var next = first with { Qpc100ns = first.Qpc100ns + elapsed };
            Assert.Equal(NativeClockResult.Valid, adapter.Convert(next, (long)next.Qpc100ns, out _));
        }
        var expired = first with { Qpc100ns = first.Qpc100ns + 5_000_400 };
        Assert.Equal(NativeClockResult.Stalled, adapter.Convert(expired, (long)expired.Qpc100ns, out _, out var trace));
        Assert.Equal(NativeClockReason.ProgressExpired, trace.Reason);
        Assert.Equal(first.Qpc100ns, trace.Anchor!.Value.LastProgressQpc100ns);
    }

    [Theory]
    [InlineData(0u, 5_000_000, "StartupWaiting", false)]
    [InlineData(0u, 5_000_001, "StartupExpired", true)]
    [InlineData(1u, 5_000_000, "StartupMissingTimestamp", true)]
    public void UnanchoredStartupKeepsExistingWaitingAndExpiryRules(uint position, long elapsed, string reason, bool rejected)
    {
        var clock = new WasapiRunningClock(Endpoint, Rate, (long)InitialQpc);
        var result = clock.Observe(new(0, 192_000, 0, position, 0), (long)InitialQpc + elapsed);

        Assert.Equal(elapsed > 5_000_000 ? NativeClockResult.InvalidPosition : NativeClockResult.Stalled, result);
        var report = clock.Snapshot();
        var entry = Assert.Single(report.Readings);
        Assert.Equal((Int128)elapsed, entry.SinceStart100ns);
        Assert.Equal(reason, entry.Validation.Reason.ToString());
        Assert.Null(entry.Validation.Anchor);
        Assert.Null(report.LastAccepted);
        Assert.Equal(rejected, report.Rejection.HasValue);
    }

    [Fact]
    public void RingKeepsChronologyAndFaultEvidenceAfterWrapWithDetachedSnapshots()
    {
        var clock = new WasapiRunningClock(Endpoint, Rate, (long)InitialQpc);
        for (uint i = 0; i < 70; i++)
        {
            ulong qpc = InitialQpc + i * 100_000;
            Assert.Equal(NativeClockResult.Valid, clock.Observe(new(0, 192_000, 0, InitialPosition + i * 1920, qpc), (long)qpc));
        }
        var before = clock.Snapshot();
        Assert.Equal(7, before.Readings[0].Index);
        Assert.Equal(70, before.Readings[^1].Index);

        ulong failedQpc = InitialQpc + 7_000_000;
        Assert.Equal(NativeClockResult.InvalidPosition, clock.Observe(new(0, 192_000, 0, 1, failedQpc), (long)failedQpc));
        for (int i = 0; i < 70; i++)
            Assert.Equal(NativeClockResult.RequiresReset, clock.Observe(new(0, 192_000, 0, 1, failedQpc), (long)failedQpc));

        var after = clock.Snapshot();
        Assert.Equal(141, after.TotalReadings);
        Assert.Equal(77, after.OverwrittenReadings);
        Assert.Equal(64, after.Readings.Count);
        Assert.Equal(Enumerable.Range(78, 64).Select(value => (long)value), after.Readings.Select(reading => reading.Index));
        Assert.Equal(71, after.Rejection!.Value.Index);
        Assert.Equal(NativeClockReason.PositionRegressed, after.Rejection.Value.Validation.Reason);
        Assert.Equal(70, after.LastAccepted!.Value.Index);
        Assert.Equal(after.LastAccepted.Value.Reading.Position, after.Rejection.Value.Validation.Anchor!.Value.Position);
        Assert.Equal(7, before.Readings[0].Index);
        Assert.Null(before.Rejection);
    }
}
