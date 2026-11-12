using WaveStudio.Audio;
using Xunit;

namespace WaveStudio.Tests;

public sealed class HotplugSelectionTests
{
    private static DeviceSnapshot Device(string id, EndpointDirection direction, bool standard = false, EndpointState state = EndpointState.Active) =>
        new(id, id, direction, state, standard, standard, null, null);
    private static DeviceCatalog Catalog(params DeviceSnapshot[] devices) => new(devices, []);

    [Fact]
    public void InitialAutomaticSelectionFollowsDefaultSeparatelyWithoutSavingPreference()
    {
        var selection = new DeviceSelection();
        var a = Device("a", EndpointDirection.Playback, true);
        var input = Device("in", EndpointDirection.Capture, true);
        Assert.Equal(a, selection.Resolve(Catalog(a, input), EndpointDirection.Playback));
        Assert.Null(selection.PlaybackId);
        var b = Device("b", EndpointDirection.Playback, true);
        Assert.Equal(b, selection.Resolve(Catalog(b, input), EndpointDirection.Playback));
        Assert.Equal(input, selection.Resolve(Catalog(b, input), EndpointDirection.Capture));
        Assert.Null(selection.CaptureId);
    }

    [Fact]
    public void ExplicitMissingEndpointReturnsWithoutSwitchingToAnotherAndCanFollowAgain()
    {
        var selection = new DeviceSelection();
        var a = Device("a", EndpointDirection.Playback);
        var b = Device("b", EndpointDirection.Playback, true);
        selection.Select(a);
        Assert.Null(selection.Resolve(Catalog(b), EndpointDirection.Playback));
        var absent = a with { State = EndpointState.Unplugged };
        Assert.Equal(absent, selection.Resolve(Catalog(absent, b), EndpointDirection.Playback));
        Assert.Equal(a, selection.Resolve(Catalog(a, b), EndpointDirection.Playback));
        selection.FollowDefault(EndpointDirection.Playback);
        Assert.Equal(b, selection.Resolve(Catalog(a, b), EndpointDirection.Playback));
    }

    [Fact]
    public void MissingDefaultsAndCapturePreferenceAreIndependent()
    {
        var selection = new DeviceSelection();
        var capture = Device("mic", EndpointDirection.Capture);
        selection.Select(capture);
        Assert.Null(selection.Resolve(Catalog(), EndpointDirection.Playback));
        selection.FollowDefault(EndpointDirection.Playback);
        Assert.Equal("mic", selection.CaptureId);
    }

    [Fact]
    public void SessionBindingRejectsReselectionOfSameIdAndOtherEndpoint()
    {
        var first = new EndpointSelection("a", 1);
        var snapshot = new ApplicationSessionSnapshot(new("a", "instance"), "App", 7, false, SessionActivity.Active, 50, false, null);
        var bound = new ApplicationSessionBinding(first, snapshot);
        Assert.True(bound.IsCurrent(first));
        Assert.False(bound.IsCurrent(new("a", 2)));
        Assert.False(bound.IsCurrent(new("b", 1)));
    }
}
