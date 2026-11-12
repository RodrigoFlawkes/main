using WaveStudio.Audio;
using Xunit;

namespace WaveStudio.Tests;

public sealed class EndpointVolumeSessionTests
{
    [Fact]
    public async Task InitializationAndExternalRefreshAreReadOnly()
    {
        var state = new FakeEndpointState();
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var target = session.Select("headset");
        Assert.Equal(40, (await session.ReadAsync(target)).VolumePercent);
        state.Volume = 0.75;
        state.Muted = true;
        state.Stereo = new StereoLevels(0.3, 0.6);
        var external = await session.ReadAsync(target);
        Assert.Equal(75, external.VolumePercent);
        Assert.True(external.Muted);
        Assert.Equal(50, external.Stereo!.Balance);
        Assert.Equal(0, state.Writes);
        Assert.Equal(2, state.Disposals);
    }

    [Fact]
    public async Task DeviceSwitchCancelsQueuedCommandsAndStaleRead()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldState = new FakeEndpointState();
        oldState.ReadingVolume = () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("O teste não liberou a consulta bloqueada.");
        };
        var newState = new FakeEndpointState { Volume = 0.7 };
        await using var session = new EndpointVolumeSession(id => new FakeEndpointVolume(id == "old" ? oldState : newState));
        var oldTarget = session.Select("old");
        var pendingRead = session.ReadAsync(oldTarget);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var delayedWrite = session.ApplyAsync(oldTarget, new SetEndpointVolume(100));
            var newTarget = session.Select("new");
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingRead);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delayedWrite);
            Assert.Equal(70, (await session.ReadAsync(newTarget)).VolumePercent);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ApplyAsync(oldTarget, new SetEndpointMute(true)));
            Assert.Equal(0, oldState.Writes);
            Assert.Equal(0, newState.Writes);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task SameEndpointReselectionStillInvalidatesOldGeneration()
    {
        var state = new FakeEndpointState();
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var stale = session.Select("same");
        session.Select("same");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ApplyAsync(stale, new SetEndpointVolume(100)));
        Assert.Equal(0, state.Writes);
    }

    [Fact]
    public async Task CommandsArePinnedToTheirEndpointAndDisposedAfterUse()
    {
        var playback = new FakeEndpointState();
        var capture = new FakeEndpointState();
        await using var playbackSession = new EndpointVolumeSession(_ => new FakeEndpointVolume(playback));
        await using var captureSession = new EndpointVolumeSession(_ => new FakeEndpointVolume(capture));
        await playbackSession.ApplyAsync(playbackSession.Select("playback"), new SetEndpointVolume(25));
        await captureSession.ApplyAsync(captureSession.Select("capture"), new SetEndpointMute(true));
        Assert.Equal(0.25, playback.Volume);
        Assert.False(playback.Muted);
        Assert.Equal(0.4, capture.Volume);
        Assert.True(capture.Muted);
        Assert.Equal(1, playback.Disposals);
        Assert.Equal(1, capture.Disposals);
    }

    [Fact]
    public async Task DisconnectDoesNotPoisonFutureReadsAndStillDisposesAccess()
    {
        var state = new FakeEndpointState { Disconnected = true };
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var target = session.Select("headset");
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApplyAsync(target, new SetEndpointMute(true)));
        state.Disconnected = false;
        Assert.Equal(40, (await session.ReadAsync(target)).VolumePercent);
        Assert.Equal(0, state.Writes);
        Assert.Equal(2, state.Disposals);
    }

    [Fact]
    public async Task DisposalIsIdempotentAndRejectsNewWork()
    {
        var session = new EndpointVolumeSession(_ => throw new InvalidOperationException());
        var target = session.Select("headset");
        await session.DisposeAsync();
        await session.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ReadAsync(target));
        Assert.Throws<ObjectDisposedException>(() => session.Select("other"));
    }
}
