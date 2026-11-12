using WaveStudio.Audio;
using WaveStudio.Desktop.Settings;
using Xunit;

namespace WaveStudio.Tests;

public sealed class SettingsValidationTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidSavedVolumeIsRejected(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SettingsJson.Encode(new StudioSettings
        {
            Endpoints = [new("out", EndpointDirection.Playback, value)]
        }));

    [Theory]
    [InlineData(-101)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSavedBalanceIsRejected(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SettingsJson.Encode(new StudioSettings
        {
            Endpoints = [new("out", EndpointDirection.Playback, Balance: value)]
        }));

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void SavedLimitsAndUnsupportedFieldsRoundTrip(double percent)
    {
        var settings = new StudioSettings
        {
            Endpoints = [new("out", EndpointDirection.Playback, percent, null, percent == 0 ? -100 : 100)]
        };
        Assert.Equal(settings.Endpoints, SettingsJson.Decode(SettingsJson.Encode(settings)).Endpoints);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("out\nother")]
    public void InvalidIdsAreRejected(string id) => Assert.Throws<ArgumentException>(() =>
        new DeviceSelection().SetPreference(EndpointDirection.Playback, id));

    [Fact]
    public void OversizedIdsUnknownDirectionAndDuplicatesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new DeviceSelection().SetPreference(EndpointDirection.Capture, new string('x', 2049)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeviceSelection().SetPreference((EndpointDirection)88, "x"));
        Assert.Throws<InvalidDataException>(() => SettingsJson.Encode(new StudioSettings
        {
            Endpoints = [new("out", EndpointDirection.Playback), new("out", EndpointDirection.Playback)]
        }));
    }

    [Fact]
    public void WindowPlacementFitsVisibleMonitorAndSurvivesMonitorRemoval()
    {
        var areas = new WindowArea[] { new(0, 0, 1366, 728), new(-1280, 0, 1280, 1024) };
        var left = new WindowPlacement(-1200, 30, 900, 600, true).Fit(areas);
        Assert.Equal(-1200, left.X);
        Assert.True(left.Maximized);
        var removed = left.Fit([areas[0]]);
        Assert.Equal(0, removed.X);
        var tooLarge = new WindowPlacement(50000, 50000, 10000, 10000).Fit([areas[0]]);
        Assert.Equal(new WindowPlacement(0, 0, 1366, 728), tooLarge);
        var tinyArea = new WindowPlacement(0, 0, 100, 100).Fit([new(0, 0, 400, 300)]);
        Assert.Equal(400, tinyArea.Width);
        Assert.Equal(300, tinyArea.Height);
    }

    [Theory]
    [InlineData(0, 600, 0)]
    [InlineData(800, -1, 0)]
    [InlineData(20000, 600, 0)]
    [InlineData(800, 600, int.MinValue)]
    public void InvalidWindowDataIsRejected(int width, int height, int x) =>
        Assert.Throws<InvalidDataException>(() => SettingsJson.Encode(new StudioSettings { Window = new(x, 0, width, height) }));

    [Fact]
    public void InactiveDefaultFallsBackToActiveWithoutSavingItAsPreference()
    {
        var inactive = new DeviceSnapshot("a", "Padrão", EndpointDirection.Playback, EndpointState.Unplugged, true, false, null, null);
        var active = inactive with { Id = "b", State = EndpointState.Active, IsDefaultMultimedia = false };
        var selection = new DeviceSelection();
        Assert.Equal(active, selection.Resolve(new DeviceCatalog([inactive, active], []), EndpointDirection.Playback));
        Assert.Null(selection.PlaybackId);
        selection.SetPreference(EndpointDirection.Playback, inactive.Id);
        Assert.Equal(inactive, selection.Resolve(new DeviceCatalog([inactive, active], []), EndpointDirection.Playback));
    }
}
