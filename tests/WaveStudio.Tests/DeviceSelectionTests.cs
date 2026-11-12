using WaveStudio.Audio;
using Xunit;

namespace WaveStudio.Tests;

public sealed class DeviceSelectionTests
{
    private static DeviceSnapshot Device(string id, EndpointDirection direction = EndpointDirection.Playback,
        EndpointState state = EndpointState.Active, bool multimedia = false, bool communications = false) =>
        new(id, id, direction, state, multimedia, communications, null, null);

    private static DeviceCatalog Catalog(params DeviceSnapshot[] devices) => new(devices, []);

    [Fact]
    public void PlaybackAndCaptureSelectionsAreIndependent()
    {
        var selection = new DeviceSelection();
        selection.Select(Device("headset"));
        selection.Select(Device("microphone", EndpointDirection.Capture));
        selection.Select(Device("speakers"));
        Assert.Equal("speakers", selection.PlaybackId);
        Assert.Equal("microphone", selection.CaptureId);
    }

    [Fact]
    public void InitialSelectionPrefersMultimediaDefaultOverCommunications()
    {
        var chat = Device("chat", communications: true);
        var multimedia = Device("music", multimedia: true);
        Assert.Equal(multimedia, new DeviceSelection().Resolve(Catalog(chat, multimedia), EndpointDirection.Playback));
    }

    [Fact]
    public void InitialSelectionUsesActiveEndpointWhenDefaultIsAbsent()
    {
        var disabled = Device("disabled", state: EndpointState.Disabled);
        var active = Device("active");
        Assert.Equal(active, new DeviceSelection().Resolve(Catalog(disabled, active), EndpointDirection.Playback));
    }

    [Fact]
    public void ExplicitSelectionSurvivesDefaultChangeAndReordering()
    {
        var chosen = Device("chosen");
        var selection = new DeviceSelection();
        selection.Select(chosen);
        Assert.Equal(chosen, selection.Resolve(Catalog(Device("new default", multimedia: true), chosen), EndpointDirection.Playback));
    }

    [Fact]
    public void MissingSelectionDoesNotSilentlyChooseAnotherEndpoint()
    {
        var selection = new DeviceSelection();
        selection.Select(Device("absent"));
        Assert.Null(selection.Resolve(Catalog(Device("other", multimedia: true)), EndpointDirection.Playback));
        Assert.Equal("absent", selection.PlaybackId);
        var reconnected = Device("absent");
        Assert.Equal(reconnected, selection.Resolve(Catalog(reconnected), EndpointDirection.Playback));
    }

    [Fact]
    public void EmptyCatalogHasNoSelection()
    {
        Assert.Null(new DeviceSelection().Resolve(Catalog(), EndpointDirection.Playback));
    }

    [Fact]
    public void SelectionNeverCrossesEndpointDirections()
    {
        var input = Device("input", EndpointDirection.Capture, multimedia: true);
        Assert.Null(new DeviceSelection().Resolve(Catalog(input), EndpointDirection.Playback));
    }

    [Theory]
    [InlineData(EndpointState.Disabled)]
    [InlineData(EndpointState.NotPresent)]
    [InlineData(EndpointState.Unplugged)]
    public void UnavailableSelectedEndpointRetainsItsRealState(EndpointState state)
    {
        var selection = new DeviceSelection();
        selection.Select(Device("chosen"));
        var unavailable = Device("chosen", state: state);
        Assert.Equal(unavailable, selection.Resolve(Catalog(unavailable, Device("other")), EndpointDirection.Playback));
    }
}
