using NAudio.Wave;
using WaveStudio.Audio;
using Xunit;
using static WaveStudio.Tests.MicrophoneTestUiTests;

namespace WaveStudio.Tests;

public sealed class EqualizerPreviewUiTests
{
    [Fact]
    public Task NeutralOffControlsAndRealResetDoNotStartAudio() => OnSta((panel, backend) =>
    {
        Assert.False(panel.Equalizer.Activate.Checked); Assert.Equal(10, panel.Equalizer.Bands.Length);
        Assert.All(panel.Equalizer.Bands, band => Assert.Equal(0, band.Value)); Assert.Equal(0, panel.Equalizer.Preamp.Value);
        Assert.False(panel.PlayEqualizedButton.Enabled); Assert.Empty(backend.Calls);
        panel.Equalizer.Activate.Checked = true; panel.Equalizer.Bands[5].Value = 12; panel.Equalizer.Preamp.Value = -5;
        Assert.Equal(-17, panel.Equalizer.Settings.EffectivePreampDb);
        panel.Equalizer.ResetButton.PerformClick(); Assert.All(panel.Equalizer.Bands, band => Assert.Equal(0, band.Value));
        Assert.Equal(0, panel.Equalizer.Preamp.Value); Assert.Empty(backend.Calls);
    });
    [Fact]
    public Task ProcessedPlaybackFreezesParametersAndOriginalRemainsAvailable() => OnSta((panel, backend) =>
    {
        panel.RecordButton.PerformClick(); Pump(() => panel.Snapshot.State == MicrophoneTestState.Recording);
        backend.Capture.Emit(400); backend.Capture.RequestStop(); Pump(() => panel.PlayButton.Enabled);
        panel.Equalizer.Activate.Checked = true; panel.Equalizer.Bands[5].Value = -6;
        Pump(() => panel.PlayEqualizedButton.Enabled); panel.PlayEqualizedButton.PerformClick();
        Pump(() => panel.Snapshot.State == MicrophoneTestState.Playing);
        Assert.False(panel.Equalizer.Enabled); Assert.False(panel.Equalizer.Bands[5].Enabled); Assert.False(panel.TestLevel.Enabled);
        Assert.Equal("Processado com EQ", panel.Snapshot.PlaybackMode);
        backend.Playback.RequestStop(); Pump(() => panel.PlayButton.Enabled);
        Assert.True(panel.Equalizer.Enabled); Assert.Equal(-6, panel.Equalizer.Bands[5].Value);
        panel.PlayButton.PerformClick(); Pump(() => panel.Snapshot.State == MicrophoneTestState.Playing);
        Assert.Equal("Original (sem EQ)", panel.Snapshot.PlaybackMode); Assert.Equal(1, backend.Capture.Starts);
    });
    [Fact]
    public Task ActualRecordedUnsupportedRateDisablesEqButKeepsOriginal() => OnSta((panel, backend) =>
    {
        backend.Capture.Format = WaveFormat.CreateIeeeFloatWaveFormat(96000, 1);
        panel.RecordButton.PerformClick(); Pump(() => panel.Snapshot.State == MicrophoneTestState.Recording);
        backend.Capture.Emit(400); backend.Capture.RequestStop(); Pump(() => panel.PlayButton.Enabled);
        Assert.False(panel.Equalizer.Supported); Assert.False(panel.Equalizer.Activate.Enabled); Assert.False(panel.PlayEqualizedButton.Enabled);
        panel.PlayButton.PerformClick(); Pump(() => panel.Snapshot.State == MicrophoneTestState.Playing);
        Assert.Equal("Original (sem EQ)", panel.Snapshot.PlaybackMode);
    });
}
