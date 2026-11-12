using WaveStudio.Audio;
using Xunit;

namespace WaveStudio.Tests;

public sealed class RevisionRegressionTests
{
    [Fact]
    public async Task ManySelectionsReleaseTheirGenerationImmediately()
    {
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(new()));
        for (int index = 0; index < 2000; index++) session.Select(index.ToString());
        Assert.Equal(1, session.RetainedGenerationCount);
        await session.DisposeAsync();
        Assert.Equal(0, session.RetainedGenerationCount);
    }

    [Fact]
    public async Task DisposeWaitsForOperationBeforeReleasingItsGeneration()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new FakeEndpointState { ReadingVolume = () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
        } };
        await using var session = new EndpointVolumeSession(_ => new FakeEndpointVolume(state));
        var reading = session.ReadAsync(session.Select("out"));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            for (int index = 0; index < 2000; index++) session.Select(index.ToString());
            Assert.Equal(2, session.RetainedGenerationCount);
            var disposal = session.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            Assert.Equal(1, session.RetainedGenerationCount);
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
            await disposal;
            await session.DisposeAsync();
            Assert.Equal(0, session.RetainedGenerationCount);
            Assert.Equal(1, state.Disposals);
        }
        finally { release.Set(); }
    }

    [Fact]
    public void DetectableExternalBalanceChangeIsNotOverwrittenByRollback()
    {
        var state = new FakeEndpointState { FailChannelWrite = 2 };
        state.ChannelWritten = () => state.Stereo = new StereoLevels(0.1, 0.2);
        using var endpoint = new FakeEndpointVolume(state);
        Assert.Throws<InvalidOperationException>(() => EndpointVolumeOperations.Apply(endpoint, new SetEndpointBalance(100), CancellationToken.None));
        Assert.Equal(new StereoLevels(0.1, 0.2), state.Stereo);
        Assert.Equal(2, state.ChannelWrites);
    }
}
