using NAudio.Wave;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class WasapiContractTests
{
    internal static WasapiPcm Pcm(int channels = 1, int rate = 48000, bool floating = false) => WasapiPcm.From(floating
        ? WaveFormat.CreateIeeeFloatWaveFormat(rate, channels) : new WaveFormat(rate, 16, channels));
    internal static TransportRoute Route(long generation = 1, int inputRate = 48000, int outputRate = 48000,
        int inputChannels = 1, int outputChannels = 2, bool inputFloat = false, bool outputFloat = true) => new(
        new(new("capture", EndpointDirection.Capture, generation), EndpointState.Active, Pcm(inputChannels, inputRate, inputFloat), 100000, 30000, null, null, FormatEvidence.SimulatedExact, false, null),
        new(new("render", EndpointDirection.Playback, generation), EndpointState.Active, Pcm(outputChannels, outputRate, outputFloat), 100000, 30000, null, null, FormatEvidence.SimulatedExact, false, null));
    internal static NativeClockSample Clock(TransportEndpoint endpoint, long time, int rate, TransportPacketFlags flags = 0) =>
        new(endpoint, (ulong)((UInt128)(ulong)time * (uint)rate / 10000000), (ulong)rate, (ulong)time, NativePositionUnit.Frames, NativeClockQuality.Accurate, flags);

    [Theory]
    [InlineData(1, false)] [InlineData(2, false)] [InlineData(1, true)] [InlineData(2, true)]
    public void PublicNativeFormatDescriptorsPreservePcmAndFloat(int channels, bool floating)
    {
        var pcm = Pcm(channels, 48000, floating);
        Assert.Equal(floating ? PcmEncoding.Float32LittleEndian : PcmEncoding.Pcm16LittleEndian, pcm.Validate());
        var extended = new WaveFormatExtensible(48000, floating ? 32 : 16, channels,
            floating ? WasapiPcm.FloatSubFormat : WasapiPcm.PcmSubFormat, floating ? 32 : 16,
            channels == 2 ? 3 : 4);
        Assert.Equal(pcm.Validate(), WasapiPcm.From(extended).Validate());
    }
    [Theory]
    [InlineData("pcm32")] [InlineData("packed24")] [InlineData("valid24")] [InlineData("ambiguousStereo")]
    [InlineData("maskCount")] [InlineData("align")] [InlineData("bytes")] [InlineData("tag")]
    [InlineData("guid")] [InlineData("rate")] [InlineData("multichannel")] [InlineData("extra")]
    public void UnsupportedOrAmbiguousFormatsAreRejected(string mutation)
    {
        var format = Pcm(2) with { Tag = 65534, ExtraBytes = 22, ChannelMask = 3 };
        format = mutation switch
        {
            "pcm32" => format with { ContainerBits = 32, ValidBits = 32, BlockAlign = 8, AverageBytesPerSecond = 384000 },
            "packed24" => format with { ContainerBits = 24, ValidBits = 24 },
            "valid24" => format with { SubFormat = WasapiPcm.FloatSubFormat, ContainerBits = 32, ValidBits = 24 },
            "ambiguousStereo" => format with { ChannelMask = 0 }, "maskCount" => format with { ChannelMask = 5 },
            "align" => format with { BlockAlign = 2 }, "bytes" => format with { AverageBytesPerSecond = 1 },
            "tag" => format with { Tag = 2 }, "guid" => format with { SubFormat = Guid.Empty },
            "rate" => format with { SampleRate = 96000 }, "multichannel" => format with { Channels = 8 },
            _ => format with { ExtraBytes = 0 }
        };
        Assert.Throws<NotSupportedException>(() => format.Validate());
    }
    [Fact]
    public void MonoPositionIsNotInventedAndUnsupportedRouteDoesNotFallback()
    {
        var mono = Pcm() with { Tag = 65534, ExtraBytes = 22, ChannelMask = 0 };
        Assert.Equal(PcmEncoding.Pcm16LittleEndian, mono.Validate());
        Assert.Null(Pcm().ChannelMask);
        var route = Route(); route.Validate(); Assert.Equal(2048, route.CapacityFrames);
        Assert.Throws<NotSupportedException>(() => Route(inputRate: 44100, outputRate: 44100).Validate());
        Assert.Throws<NotSupportedException>(() => (route with { Playback = route.Playback with { Evidence = FormatEvidence.MixOnly } }).Validate());
        Assert.Throws<NotSupportedException>(() => (route with { Playback = route.Playback with { Endpoint = route.Playback.Endpoint with { Generation = 2 } } }).Validate());
        Assert.Throws<NotSupportedException>(() => (route with { Playback = route.Playback with { State = EndpointState.Unplugged } }).Validate());
        Assert.Throws<NotSupportedException>(() => _ = (route with { Playback = route.Playback with { DefaultPeriod100ns = 1000000 } }).CapacityFrames);
    }
    [Fact]
    public void ClockAboveDoublePrecisionUsesIntegerDeltaAndRemainders()
    {
        var endpoint = Route().Capture.Endpoint;
        var adapter = new WasapiClockAdapter(endpoint, 48000);
        ulong position = (1UL << 53) + 17, qpc = (1UL << 53) + 99;
        var first = new NativeClockSample(endpoint, position, 10000000, qpc, NativePositionUnit.DeviceUnits, NativeClockQuality.Accurate);
        Assert.Equal(NativeClockResult.Valid, adapter.Convert(first, (long)qpc, out var initial)); Assert.Equal(0, initial.FramePosition);
        for (ulong i = 1; i <= 1000; i++)
        {
            var next = first with { Position = position + i * 3333, Qpc100ns = qpc + i * 3333 };
            Assert.Equal(NativeClockResult.Valid, adapter.Convert(next, (long)next.Qpc100ns, out var observation));
            Assert.Equal((long)(i * 3333 * 48000 / 10000000), observation.FramePosition);
            Assert.Equal((long)next.Qpc100ns, observation.Timestamp);
        }
    }
    [Theory]
    [InlineData("timestamp", NativeClockResult.TimestampError)] [InlineData("discontinuity", NativeClockResult.Discontinuity)]
    [InlineData("unavailable", NativeClockResult.Unavailable)] [InlineData("uncertain", NativeClockResult.Unavailable)]
    [InlineData("stale", NativeClockResult.InvalidTime)] [InlineData("future", NativeClockResult.InvalidTime)]
    [InlineData("regression", NativeClockResult.InvalidPosition)] [InlineData("jump", NativeClockResult.InvalidPosition)]
    [InlineData("repeatTime", NativeClockResult.InvalidTime)] [InlineData("stalled", NativeClockResult.Stalled)]
    [InlineData("overflow", NativeClockResult.Overflow)] [InlineData("flags", NativeClockResult.Unavailable)]
    public void BadNativeEvidenceImmediatelyLatchesReset(string kind, object expected)
    {
        var endpoint = Route().Capture.Endpoint; var adapter = new WasapiClockAdapter(endpoint, 48000);
        var first = Clock(endpoint, 10000000, 48000);
        if (kind == "overflow") first = first with { Position = 0, Frequency = 1, Unit = NativePositionUnit.DeviceUnits };
        Assert.Equal(NativeClockResult.Valid, adapter.Convert(first, 10000000, out _));
        var next = Clock(endpoint, 10100000, 48000);
        long now = 10100000;
        next = kind switch
        {
            "timestamp" => next with { Flags = TransportPacketFlags.TimestampError },
            "discontinuity" => next with { Flags = TransportPacketFlags.Discontinuity },
            "unavailable" => next with { Quality = NativeClockQuality.Unavailable },
            "uncertain" => next with { Quality = NativeClockQuality.Uncertain },
            "stale" => next, "future" => next with { Qpc100ns = 10200000 },
            "regression" => next with { Position = first.Position - 1 },
            "jump" => next with { Position = first.Position + 10000 },
            "repeatTime" => next with { Qpc100ns = first.Qpc100ns },
            "stalled" => next with { Position = first.Position, Qpc100ns = 16000000 },
            "overflow" => next with { Position = ulong.MaxValue, Frequency = 1, Unit = NativePositionUnit.DeviceUnits },
            _ => next with { Flags = (TransportPacketFlags)8 }
        };
        if (kind == "stale") now = 16000001;
        if (kind == "stalled") now = 16000000;
        Assert.Equal((NativeClockResult)expected, adapter.Convert(next, now, out _)); Assert.True(adapter.RequiresReset);
        Assert.Equal(NativeClockResult.RequiresReset, adapter.Convert(next, now, out _));
    }
    [Fact]
    public void OtherEndpointOrGenerationCannotPoisonClockAndSilenceIsValidEvidence()
    {
        var endpoint = Route().Capture.Endpoint; var adapter = new WasapiClockAdapter(endpoint, 48000);
        var sample = Clock(endpoint, 100000, 48000, TransportPacketFlags.Silent);
        Assert.Equal(NativeClockResult.InvalidIdentity, adapter.Convert(sample with { Endpoint = endpoint with { Generation = 2 } }, 100000, out _));
        Assert.False(adapter.RequiresReset);
        Assert.Equal(NativeClockResult.Valid, adapter.Convert(sample, 100000, out var observation));
        Assert.Equal(ClockQuality.Monotonic, observation.Quality);
        Assert.Throws<ArgumentException>(() => new TransportEndpoint("", EndpointDirection.Capture, 1).Validate());
    }
    [Fact]
    public void TimeWeightedLedgerDistinguishesSameFinalCountWithDifferentEventTimes()
    {
        var early = new TransportLedger(0); var late = new TransportLedger(0);
        early.Add(100, 10, default); late.Add(100, 90, default);
        Assert.True(early.Read(100, out var a)); Assert.True(late.Read(100, out var b));
        Assert.Equal(a.Frames, b.Frames); Assert.Equal((UInt128)9000, a.Area); Assert.Equal((UInt128)1000, b.Area);
    }
}
