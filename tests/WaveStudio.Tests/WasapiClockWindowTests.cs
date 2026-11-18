using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiClockWindowTests
{
    // PROOF-3: all 11 reads per role. QPC and local time share a subtracted origin.
    // Frequencies, raw positions and HRESULTs are unchanged. No packet sequence was recorded.
    private static readonly (ulong Position, ulong Qpc, long Now)[] Capture =
    [
        (21120, 1118258, 1263674),
        (40320, 2118237, 2271281),
        (59520, 3118212, 3273991),
        (78720, 4118204, 4302675),
        (97920, 5118197, 5303462),
        (117120, 6118083, 6304368),
        (136320, 7118115, 7305366),
        (155520, 8118128, 8306204),
        (174720, 9118128, 9317169),
        (193920, 10118057, 10318061),
        (215040, 11318187, 11321824),
    ];
    private static readonly (ulong Position, ulong Qpc, long Now)[] Render =
    [
        (32704, 1208763, 1209681),
        (71944, 2230699, 2230703),
        (110760, 3241604, 3241607),
        (149200, 4242506, 4242511),
        (187632, 5243406, 5243412),
        (226064, 6244307, 6244312),
        (264504, 7245217, 7245222),
        (302936, 8246148, 8246153),
        (341376, 9247054, 9247058),
        (379816, 10247977, 10247982),
        (418240, 11248887, 11248892),
    ];

    [Fact]
    public void Proof3UsesOnePeriodOfPhaseBudgetWithoutConfirmingTransport()
    {
        var endpoint = WasapiContractTests.Route().Capture.Endpoint;
        var timing = new WasapiCaptureTiming(48000, 100000, 1056);
        var clock = new WasapiRunningClock(endpoint, 48000, 227978, timing);
        var strict = new WasapiRunningClock(endpoint, 48000, 227978);
        foreach (var (position, qpc, now) in Capture)
        {
            var reading = new WasapiClockReading(0, 192000, 0, position, qpc);
            strict.Observe(reading, now);
            clock.Observe(reading, now);
        }
        var rejected = strict.Snapshot().Rejection!.Value;
        Assert.Equal(NativeClockReason.AdvanceOutsideTolerance, rejected.Validation.Reason);
        Assert.Equal(480.624, rejected.Validation.AbsoluteErrorFrames!.Value, 6);
        Assert.Equal(30.80312, rejected.Validation.ErrorLimitFrames!.Value, 6);
        var provisional = clock.Snapshot().Readings[^1];
        Assert.Equal(NativeClockResult.Provisional, provisional.Result);
        Assert.Equal(-479.6592, provisional.Validation.ClockAccumulated!.Value.SignedErrorFrames, 6);
        var evidence = timing.Snapshot(Capture[^1].Now);
        Assert.Equal(1, evidence.ProvisionalReadings);
        Assert.Equal(0, evidence.ConfirmedClockWindows);
        Assert.Equal(0, evidence.ContinuousPackets);
        Assert.False(evidence.Confirmed);

        var render = new WasapiRunningClock(WasapiContractTests.Route().Playback.Endpoint, 48000, 173761);
        foreach (var (position, qpc, now) in Render)
            Assert.Equal(NativeClockResult.Valid, render.Observe(new(0, 384000, 0, position, qpc), now));
    }

    [Theory]
    [InlineData(48000, 100000, 1056, 480)]
    [InlineData(44100, 100001, 1056, 442)]
    [InlineData(48000, 50000, 1056, 240)]
    [InlineData(48000, 1000000, 1056, 1056)]
    public void BudgetIsRoundedUpFromTheEndpointPeriodAndLimitedByCapacity(int rate, long period, uint capacity, int budget)
    {
        Assert.Equal(budget, new WasapiCaptureTiming(rate, period, capacity).PhaseBudgetFrames);
    }

    [Theory]
    [InlineData(-506, true)]
    [InlineData(506, true)]
    [InlineData(-507, false)]
    [InlineData(507, false)]
    public void ShortPhaseEnvelopeIsInclusiveInBothDirections(int error, bool allowed)
    {
        var timing = new WasapiCaptureTiming(48000, 100000, 1056);
        var adapter = new WasapiClockAdapter(WasapiContractTests.Route().Capture.Endpoint, 48000, timing);
        var first = InitialClock(48000);
        Assert.Equal(NativeClockResult.Valid, adapter.Convert(first, (long)first.Qpc100ns, out _));
        var next = first with { Position = first.Position + (ulong)(4800 + error), Qpc100ns = first.Qpc100ns + 1000000 };

        var result = adapter.Convert(next, (long)next.Qpc100ns, out var observation, out var evidence);

        Assert.Equal(allowed ? NativeClockResult.Provisional : NativeClockResult.InvalidPosition, result);
        Assert.Equal(allowed ? NativeClockReason.None : NativeClockReason.AdvanceOutsideTolerance, evidence.Reason);
        Assert.Equal(default, observation);
        Assert.Equal(!allowed, adapter.RequiresReset);
        Assert.False(timing.Snapshot((long)next.Qpc100ns).Confirmed);
    }

    [Theory]
    [InlineData(48000, 100000, -480)]
    [InlineData(48000, 100000, 480)]
    [InlineData(44100, 50000, -220)]
    [InlineData(44100, 50000, 220)]
    public void SyntheticRecoveryCompletesFiveSecondsWithIndependentPacketWindows(int rate, long period, int offset)
    {
        var timing = new WasapiCaptureTiming(rate, period, 1056);
        var adapter = new WasapiClockAdapter(WasapiContractTests.Route().Capture.Endpoint, rate, timing);
        var first = InitialClock(rate);
        ulong packetOrigin = 50_000_000; // Deliberately unrelated to the clock's absolute position.
        uint packetFrames = (uint)rate / 100;
        for (int tick = 0; tick <= 500; tick++)
        {
            ulong qpc = first.Qpc100ns + (ulong)tick * 100000;
            ulong packetPosition = packetOrigin + (ulong)tick * packetFrames;
            timing.ValidatePacket(packetPosition, packetFrames, qpc);
            timing.AcceptReleasedPacket(packetPosition, packetFrames, qpc);
            if (tick % 10 == 0)
            {
                ulong position = first.Position + (ulong)(tick * (int)packetFrames + (tick == 50 ? offset : 0));
                var result = adapter.Convert(first with { Position = position, Qpc100ns = qpc }, (long)qpc, out var observation);
                Assert.Equal(tick is 50 or 60 ? NativeClockResult.Provisional : NativeClockResult.Valid, result);
                Assert.Equal(default, observation);
            }
            timing.CheckProgress((long)qpc, (long)first.Qpc100ns);
        }

        var evidence = timing.Snapshot((long)first.Qpc100ns + 50_000_000);
        Assert.True(evidence.Confirmed);
        Assert.Equal(2, evidence.ProvisionalReadings);
        Assert.True(evidence.ConfirmedClockWindows > 1 && evidence.ConfirmedPacketWindows > 1);
        Assert.Equal(501, evidence.ContinuousPackets);
        Assert.Equal(0d, evidence.CorroborationErrorFrames);
        Assert.False(timing.Snapshot((long)first.Qpc100ns + 55_000_001).Confirmed);
    }

    [Theory]
    [InlineData(25, "WindowRateOutsideTolerance")]
    [InlineData(-25, "WindowRateOutsideTolerance")]
    [InlineData(200, "AccumulatedPhaseOutsideTolerance")]
    [InlineData(-200, "AccumulatedPhaseOutsideTolerance")]
    public void SustainedClockBiasFailsWithoutResettingTheOrigin(int bias, string reason)
    {
        var timing = new WasapiCaptureTiming(48000, 100000, 1056);
        var adapter = new WasapiClockAdapter(WasapiContractTests.Route().Capture.Endpoint, 48000, timing);
        var first = InitialClock(48000);
        NativeClockValidation evidence = default;
        NativeClockResult status = default;
        for (int tick = 0; tick <= 25 && !adapter.RequiresReset; tick++)
        {
            ulong qpc = first.Qpc100ns + (ulong)tick * 1000000;
            status = adapter.Convert(first with { Position = first.Position + (ulong)(tick * (4800 + bias)), Qpc100ns = qpc },
                (long)qpc, out _, out evidence);
        }
        Assert.Equal(NativeClockResult.InvalidPosition, status);
        Assert.Equal(reason, evidence.Reason.ToString());
        Assert.Equal(first.Qpc100ns, evidence.ClockAccumulated!.Value.Anchor.Qpc100ns);
        Assert.Equal(NativeClockResult.RequiresReset, adapter.Convert(first, (long)first.Qpc100ns, out _));
    }

    [Theory]
    [InlineData("gap", 481, 100100000, "PacketPositionNotContiguous")]
    [InlineData("duplicate", 0, 100100000, "PacketPositionNotContiguous")]
    [InlineData("overlap", 479, 100100000, "PacketPositionNotContiguous")]
    [InlineData("repeat-time", 480, 100000000, "PacketTimestampNotIncreasing")]
    [InlineData("regress-time", 480, 99999999, "PacketTimestampNotIncreasing")]
    [InlineData("expire", 480, 105000001, "PacketProgressExpired")]
    [InlineData("overflow", -1, 100100000, "PacketPositionOverflow")]
    public void PacketContinuityFaultIsRejectedBeforeAcceptance(string fault, int position, long qpc, string reason)
    {
        var timing = new WasapiCaptureTiming(48000, 100000, 1056);
        timing.ValidatePacket(0, 480, 100000000);
        timing.AcceptReleasedPacket(0, 480, 100000000);

        ulong next = fault == "overflow" ? ulong.MaxValue : (ulong)position;
        var error = Assert.Throws<InvalidOperationException>(() => timing.ValidatePacket(next, 480, (ulong)qpc));

        Assert.Equal(reason, error.Message);
        Assert.Equal(1, timing.Snapshot(qpc).ContinuousPackets);
        Assert.False(timing.Snapshot(qpc).Confirmed);
    }

    [Theory]
    [InlineData(99400)]
    [InlineData(100600)]
    public void PacketRateMustPassItsOwnRollingWindow(int spacing100ns)
    {
        var timing = new WasapiCaptureTiming(48000, 100000, 1056);
        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            for (uint index = 0; index <= 250; index++)
            {
                ulong qpc = 100000000 + index * (ulong)spacing100ns;
                var position = index * 480UL;
                timing.ValidatePacket(position, 480, qpc);
                timing.AcceptReleasedPacket(position, 480, qpc);
            }
        });
        Assert.Equal("Packet/WindowRateOutsideTolerance", error.Message);
    }

    [Theory]
    [InlineData("regression", "PositionRegressed")]
    [InlineData("repeat-qpc", "NonIncreasingTimestamp")]
    [InlineData("frequency", "FrequencyChanged")]
    [InlineData("unit", "UnitChanged")]
    [InlineData("uncertain", "InaccurateReading")]
    [InlineData("flags", "PacketDiscontinuity")]
    [InlineData("timestamp", "PacketTimestampError")]
    [InlineData("overflow", "AdvanceOverflow")]
    [InlineData("repeat-position", "ProgressExpired")]
    public void TemporalEnvelopePreservesImmediateStructuralRejections(string fault, string expected)
    {
        var timing = new WasapiCaptureTiming(48000, 100000, 1056);
        var adapter = new WasapiClockAdapter(WasapiContractTests.Route().Capture.Endpoint, 48000, timing);
        var first = InitialClock(48000);
        adapter.Convert(first, (long)first.Qpc100ns, out _);
        var next = first with { Qpc100ns = first.Qpc100ns + 1000000, Position = first.Position + 4800 };
        next = fault switch
        {
            "regression" => next with { Position = first.Position - 1 },
            "repeat-qpc" => next with { Qpc100ns = first.Qpc100ns },
            "frequency" => next with { Frequency = 192000 },
            "unit" => next with { Unit = NativePositionUnit.Frames },
            "uncertain" => next with { Quality = NativeClockQuality.Uncertain },
            "flags" => next with { Flags = TransportPacketFlags.Discontinuity },
            "timestamp" => next with { Flags = TransportPacketFlags.TimestampError },
            "overflow" => next with { Position = ulong.MaxValue },
            "repeat-position" => next with { Position = first.Position, Qpc100ns = first.Qpc100ns + 5_000_001 },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };

        adapter.Convert(next, (long)next.Qpc100ns, out _, out var evidence);

        Assert.Equal(expected, evidence.Reason.ToString());
        Assert.False(evidence.TemporalProvisional);
        Assert.True(adapter.RequiresReset);
    }

    [Fact]
    public void IndependentlyPlausibleRatesMustCorroborateEachOtherAtStop()
    {
        var timing = new WasapiCaptureTiming(48000, 100000, 1056);
        var adapter = new WasapiClockAdapter(WasapiContractTests.Route().Capture.Endpoint, 48000, timing);
        var first = InitialClock(48000);
        for (uint tick = 0; tick <= 250; tick++)
        {
            ulong packetQpc = first.Qpc100ns + tick * 100400UL; // Approximately -3984 ppm.
            timing.ValidatePacket(tick * 480UL, 480, packetQpc);
            timing.AcceptReleasedPacket(tick * 480UL, 480, packetQpc);
            if (tick % 10 != 0) continue;
            ulong clockQpc = first.Qpc100ns + tick * 100000UL;
            Assert.Equal(NativeClockResult.Valid, adapter.Convert(first with
            {
                Position = first.Position + tick * 482UL, // Approximately +4167 ppm.
                Qpc100ns = clockQpc
            }, (long)clockQpc, out _));
        }
        var snapshot = timing.Snapshot((long)first.Qpc100ns + 25_100_000);
        Assert.True(snapshot.ClockRolling!.Value.WithinLimit);
        Assert.True(snapshot.PacketRolling!.Value.WithinLimit);
        Assert.True(snapshot.CorroborationErrorFrames > snapshot.CorroborationLimitFrames);
        Assert.False(snapshot.Confirmed);
    }

    private static NativeClockSample InitialClock(int rate) =>
        new(WasapiContractTests.Route().Capture.Endpoint, 19200, (ulong)rate, 100000000,
            NativePositionUnit.DeviceUnits, NativeClockQuality.Accurate);
}
