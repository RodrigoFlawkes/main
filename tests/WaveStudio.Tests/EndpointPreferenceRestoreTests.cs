using WaveStudio.Audio;
using WaveStudio.Desktop.Settings;
using Xunit;

namespace WaveStudio.Tests;

public sealed class EndpointPreferenceRestoreTests
{
    [Fact]
    public async Task LoadingSelectionRefreshAndPreviewOnlyReadUntilExplicitRestore()
    {
        using var directory = new TemporarySettingsDirectory();
        var store = new SettingsStore(directory.FilePath);
        await store.LoadAsync(TestContext.Current.CancellationToken);
        await store.SaveAsync(new StudioSettings
        {
            Playback = new(false, "out"), Endpoints = [new("out", EndpointDirection.Playback, 25, true, 50)]
        }, TestContext.Current.CancellationToken);
        var saved = (await new SettingsStore(directory.FilePath).LoadAsync(TestContext.Current.CancellationToken)).Settings;
        var selection = new DeviceSelection();
        selection.SetPreference(EndpointDirection.Playback, saved.Playback.EndpointId);
        var state = new FakeEndpointState();
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var target = session.Select(selection.PlaybackId);
        await session.ReadAsync(target);
        var plan = await session.PreviewRestoreAsync(target, saved.Endpoints[0].Values);
        Assert.Equal(0, state.Writes);
        Assert.Equal(40, plan.Original.VolumePercent);
        var restored = await session.RestoreAsync(plan);
        Assert.True(restored.Complete);
        Assert.Equal(25, restored.Actual.VolumePercent);
        Assert.True(restored.Actual.Muted);
        Assert.Equal(50, restored.Actual.Stereo!.Balance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SwitchAndSameIdReselectionInvalidateConsent(bool sameId)
    {
        var state = new FakeEndpointState();
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var plan = await session.PreviewRestoreAsync(session.Select("a"), new(20, true, null));
        session.Select(sameId ? "a" : "b");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RestoreAsync(plan));
        Assert.Equal(0, state.Writes);
    }

    [Fact]
    public async Task ExternalChangeAfterPreviewRequiresNewConsent()
    {
        var state = new FakeEndpointState();
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var plan = await session.PreviewRestoreAsync(session.Select("out"), new(90, false, null));
        state.Volume = 0.2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RestoreAsync(plan));
        Assert.Equal(0, state.Writes);
        Assert.Equal(0.2, state.Volume);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MonoOrUnsupportedControlRejectsWholePreferenceBeforeWriting(bool mono)
    {
        var state = new FakeEndpointState { Channels = mono ? 1 : 2, UnsupportedVolume = !mono };
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            session.PreviewRestoreAsync(session.Select("mic"), new(20, true, mono ? 30 : null)));
        Assert.Equal(0, state.Writes);
    }

    [Fact]
    public async Task MissingOptionalControlsAreSkippedAndUnmuteIsLast()
    {
        var state = new FakeEndpointState { UnsupportedVolume = true, Channels = 1, Muted = true };
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var plan = await session.PreviewRestoreAsync(session.Select("mic"), new(null, false, null));
        var result = await session.RestoreAsync(plan);
        Assert.True(result.Complete);
        Assert.False(result.Actual.Muted);
        Assert.Null(result.Actual.VolumePercent);
        Assert.Null(result.Actual.Stereo);
        Assert.Equal(1, state.Writes);
    }

    [Fact]
    public async Task PartialFailureReportsActualStateAndKeepsUnmutePending()
    {
        var state = new FakeEndpointState { Muted = true, FailChannelWrite = 2 };
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var plan = await session.PreviewRestoreAsync(session.Select("out"), new(20, false, -100));
        var result = await session.RestoreAsync(plan);
        Assert.False(result.Complete);
        Assert.Contains("parcial", result.Error);
        Assert.Equal(20, result.Actual.VolumePercent);
        Assert.True(result.Actual.Muted);
        Assert.Equal(new StereoLevels(0.4, 0.4), result.Actual.Stereo);
    }

    [Fact]
    public async Task DisconnectDuringRestoreReportsUnavailableAndStopsFollowingCommands()
    {
        var state = new FakeEndpointState { Muted = true };
        state.ChannelWritten = () => state.Disconnected = true;
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var plan = await session.PreviewRestoreAsync(session.Select("out"), new(20, false, 100));
        var result = await session.RestoreAsync(plan);
        Assert.False(result.Complete);
        Assert.Null(result.Actual.VolumePercent);
        Assert.Null(result.Actual.Muted);
        Assert.True(state.Muted);
    }

    [Fact]
    public async Task GenerationChangeCancelsRestoreQueuedBehindRead()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new FakeEndpointState();
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var target = session.Select("a");
        var plan = await session.PreviewRestoreAsync(target, new(80, true, null));
        state.ReadingVolume = () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
        };
        var reading = session.ReadAsync(target);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var restoring = session.RestoreAsync(plan);
            session.Select("b");
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restoring);
            Assert.Equal(0, state.Writes);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task ReadbackBecomingUnavailableProducesIncompleteResult()
    {
        var state = new FakeEndpointState();
        state.ChannelWritten = () => state.UnsupportedVolume = true;
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var plan = await session.PreviewRestoreAsync(session.Select("out"), new(20, true, 50));
        var result = await session.RestoreAsync(plan);
        Assert.False(result.Complete);
        Assert.Null(result.Actual.VolumePercent);
        Assert.NotNull(result.Actual.VolumeError);
    }

    [Theory]
    [InlineData(0.4)]
    [InlineData(0.001)]
    public async Task ChannelChangeOnFinalUnmuteInvalidatesBalance(double level)
    {
        var state = new FakeEndpointState { Muted = true, Stereo = new(level, level) };
        state.MuteWritten = _ => state.Stereo = new(level, 0);
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var plan = await session.PreviewRestoreAsync(session.Select("out"), new(null, false, 100));
        var result = await session.RestoreAsync(plan);
        Assert.False(result.Complete);
        Assert.Contains("balanço final", result.Error);
        Assert.Equal(new StereoLevels(level, 0), result.Actual.Stereo);
    }

    [Fact]
    public async Task FinalChannelQuantizationWithinBaseRelativeToleranceIsAccepted()
    {
        var state = new FakeEndpointState { Muted = true };
        state.MuteWritten = _ => state.Stereo = new(0.202, 0.398);
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var plan = await session.PreviewRestoreAsync(session.Select("out"), new(null, false, 50));
        Assert.True((await session.RestoreAsync(plan)).Complete);
    }

    [Fact]
    public async Task BalanceAtZeroBaseIsReportedAsUnobservable()
    {
        var state = new FakeEndpointState { Volume = 0, Stereo = new(0, 0) };
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var plan = await session.PreviewRestoreAsync(session.Select("out"), new(0, false, 100));
        var result = await session.RestoreAsync(plan);
        Assert.False(result.Complete);
        Assert.Contains("não é observável", result.Error);
        Assert.Equal(new StereoLevels(0, 0), result.Actual.Stereo);
        Assert.Equal(0, result.Actual.VolumePercent);
    }
}
