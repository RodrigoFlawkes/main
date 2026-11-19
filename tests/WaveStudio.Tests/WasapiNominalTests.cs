using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiNominalTests
{
    // Origem comum subtraída do histórico de captura da prova 59cc56e. Os pacotes do replay são sintéticos.
    private static readonly (ulong Position, ulong Qpc, long Now)[] Replay =
    [
        (19200, 1638950, 1714691),
        (38400, 2638849, 2714806),
        (57600, 3638761, 3715731),
        (76800, 4638833, 4726789),
        (97920, 5638731, 5759366),
        (117120, 6644612, 6762586),
        (136320, 7638808, 7809486),
        (155520, 8639003, 8810408),
        (174720, 9638756, 9811197),
        (193920, 10638683, 10813028),
        (213120, 11638664, 11813241),
        (232320, 12644560, 12813934),
        (251520, 13638835, 13815002),
        (270720, 14638783, 14815702),
        (289920, 15638899, 15816628),
        (309120, 16638777, 16817538),
        (328320, 17638710, 17818458),
        (347520, 18644513, 18819656),
        (366720, 19638481, 19830286),
        (385920, 20638539, 20831175),
        (405120, 21638661, 21832079),
        (424320, 22638619, 22832981),
        (443520, 23638647, 23833945),
        (462720, 24644465, 24834841),
        (481920, 25638667, 25835777),
        (501120, 26638566, 26836636),
        (520320, 27638675, 27837524),
        (539520, 28638616, 28838425),
        (560640, 29838452, 29840875),
        (579840, 30838592, 30841756),
        (599040, 31844406, 31892701),
        (618240, 32838432, 32902168),
        (637440, 33838521, 33903292),
        (656640, 34838559, 34903992),
        (675840, 35838436, 35914902),
        (695040, 36838444, 36915772),
        (714240, 37844357, 37918141),
    ];


    [Fact]
    public void PersistentRollingDisagreementExhaustsTheClockEvidenceBudget()
    {
        var timing = new WasapiCaptureTiming(48000, 100_000, 1056, limitedNominal: true);
        var clock = new WasapiRunningClock(WasapiContractTests.Route().Capture.Endpoint, 48000, 500_000, timing);
        ulong nextPacket = 1_000_000, packetPosition = 0;
        long firstRejected = 0;
        for (int i = 0; i < 46; i++)
        {
            long qpc = 1_000_000 + i * 1_000_000L;
            while (nextPacket <= (ulong)qpc)
            {
                timing.ValidatePacket(packetPosition, 480, nextPacket);
                timing.AcceptReleasedPacket(packetPosition, 480, nextPacket);
                packetPosition += 480;
                nextPacket += 100_000;
            }

            long frames = (long)Math.Floor(i * 4800 * .9997) + (i is >= 10 and <= 16 ? 480 : 0);
            var result = clock.Observe(new(0, 192_000, 0, (ulong)(400_000 + frames * 4), (ulong)qpc), qpc);
            if (result == NativeClockResult.NominalEvidenceUnavailable && firstRejected == 0) firstRejected = qpc;
            if (result != NativeClockResult.InvalidPosition) continue;
            Assert.True(firstRejected > 0);
            Assert.Equal(5_000_000, qpc - firstRejected);
            return;
        }
        Assert.Fail("A discordância sustentada precisa encerrar mesmo com pacotes contíguos.");
    }

    [Fact]
    public void APhaseExceptionDoesNotAllowAClockRegressionOrAStalePacket()
    {
        foreach (bool regressive in new[] { true, false })
        {
            var timing = new WasapiCaptureTiming(48000, 100_000, 1056, limitedNominal: true);
            var clock = new WasapiRunningClock(WasapiContractTests.Route().Capture.Endpoint, 48000, 500_000, timing);
            ulong packetTime = 648_783, position = 650_414_880;
            foreach (var sample in Replay.Take(36))
            {
                while (packetTime + 100_000 <= (ulong)(regressive ? sample.Now : Math.Min(sample.Now, 31_000_000)))
                {
                    timing.ValidatePacket(position, 480, packetTime);
                    timing.AcceptReleasedPacket(position, 480, packetTime);
                    packetTime += 100_000;
                    position += 480;
                }
                clock.Observe(new(0, 192_000, 0, sample.Position, sample.Qpc), sample.Now);
            }
            var last = Replay[^1];
            var result = clock.Observe(new(0, 192_000, 0, regressive ? Replay[^2].Position - 4 : last.Position, last.Qpc), last.Now);
            Assert.Equal(NativeClockResult.InvalidPosition, result);
            Assert.Equal(regressive ? NativeClockReason.PositionRegressed : NativeClockReason.WindowRateOutsideTolerance,
                clock.Snapshot().Rejection!.Value.Validation.Reason);
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void Reading37RemainsRejectedUnlessNominalModeHasIndependentFreshPackets(bool nominal, bool packets)
    {
        var expected = nominal && packets ? NativeClockResult.NominalEvidenceUnavailable : NativeClockResult.InvalidPosition;
        var timing = new WasapiCaptureTiming(48000, 100_000, 1056, limitedNominal: nominal);
        var clock = new WasapiRunningClock(WasapiContractTests.Route().Capture.Endpoint, 48000, 500_000, timing);
        ulong packetTime = 648_783, packetPosition = 650_414_880;
        NativeClockResult result = default;
        foreach (var (position, qpc, now) in Replay)
        {
            while (packets && packetTime + 100_000 <= (ulong)now)
            {
                timing.ValidatePacket(packetPosition, 480, packetTime);
                timing.AcceptReleasedPacket(packetPosition, 480, packetTime);
                packetTime += 100_000;
                packetPosition += 480;
            }
            result = clock.Observe(new(0, 192000, 0, position, qpc), now);
        }

        Assert.Equal(expected, result);
        var rejected = clock.Snapshot().Rejection!.Value;
        Assert.Equal(NativeClockReason.WindowRateOutsideTolerance, rejected.Validation.Reason);
        Assert.Equal(-507.1056, rejected.Validation.ClockRolling!.Value.SignedErrorFrames, 6);
        Assert.Equal(486.935528, rejected.Validation.ClockRolling.Value.LimitFrames, 6);
        if (expected == NativeClockResult.NominalEvidenceUnavailable)
        {
            var last = Replay[^1];
            var unavailable = clock.Observe(new(0, 192000, 1, last.Position + 19200, last.Qpc + 1_000_000), last.Now + 1_000_000);
            Assert.Equal(NativeClockResult.Unavailable, unavailable);
        }
    }
}
