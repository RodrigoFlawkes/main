using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class SingleCableIdentityTests
{
    private static readonly PnpAudioAdapter Cable = new("devnode-cabo", 8, 0, "VBAudioVACMME", ["VBAudioVACWDM"]);

    [Fact]
    public void IncludesBothPlaybackPinsAndTheCaptureFromTheSameAdapter()
    {
        var pins = new[]
        {
            Pin("render", EndpointDirection.Playback, Cable),
            Pin("render-extra", EndpointDirection.Playback, Cable, namedCandidate: false),
            Pin("capture", EndpointDirection.Capture, Cable with { InstanceId = "DEVNODE-CABO" }),
            Pin("headset", EndpointDirection.Playback, new("usb", 8, 0, "usbaudio", ["USB"]), namedCandidate: false)
        };

        var isolation = NativeCableIsolation.IdentifyPins(Cable.InstanceId, "render", "capture", pins);

        Assert.Equal(new[] { "render", "render-extra" }, isolation.RenderIds);
        Assert.Equal(new[] { "capture", "render", "render-extra" }, isolation.EndpointIds);
    }

    [Theory]
    [InlineData(8, 10, "VBAudioVACMME", "VBAudioVACWDM")]
    [InlineData(0, 0, "VBAudioVACMME", "VBAudioVACWDM")]
    [InlineData(8, 0, "outro", "VBAudioVACWDM")]
    [InlineData(8, 0, "VBAudioVACMME", "outro")]
    [InlineData(8, 0, "outro", "outro")]
    public void RejectsUnhealthyOrUnverifiedCandidate(uint flags, uint problem, string service, string hardware)
    {
        var adapter = Cable with { Status = flags, Problem = problem, Service = service, HardwareIds = [hardware] };

        Assert.Throws<InvalidOperationException>(() => adapter.RequireRole(expectsCable: true));
    }

    [Fact]
    public void RejectsVirtualCableInAPhysicalRole()
    {
        Assert.Throws<InvalidOperationException>(() => Cable.RequireRole(expectsCable: false));
    }

    [Theory]
    [InlineData("missing-capture")]
    [InlineData("different-instance")]
    [InlineData("mixed-physical-capture")]
    [InlineData("wrong-direction")]
    [InlineData("additional-capture")]
    public void RejectsIncompleteOrAmbiguousPins(string failure)
    {
        var render = Pin("render", EndpointDirection.Playback, Cable);
        var capture = Pin("capture", EndpointDirection.Capture, Cable);
        var pins = failure switch
        {
            "missing-capture" => new[] { render },
            "different-instance" => new[] { render, capture with { Adapter = Cable with { InstanceId = "outro-devnode" } } },
            "mixed-physical-capture" => new[] { render, Pin("capture", EndpointDirection.Capture,
                new("usb", 8, 0, "usbaudio", ["USB"]), namedCandidate: false) },
            "wrong-direction" => new[] { render, capture with { Direction = EndpointDirection.Playback } },
            _ => new[] { render, capture, capture with { Id = "captura-extra" } }
        };

        Assert.Throws<InvalidOperationException>(() =>
            NativeCableIsolation.IdentifyPins(Cable.InstanceId, "render", "capture", pins));
    }

    private static SingleCablePin Pin(string id, EndpointDirection direction, PnpAudioAdapter adapter,
        bool namedCandidate = true) => new(id, direction, namedCandidate, adapter);
}
