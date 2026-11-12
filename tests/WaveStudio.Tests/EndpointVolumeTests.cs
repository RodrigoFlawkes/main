using WaveStudio.Audio;
using Xunit;

namespace WaveStudio.Tests;

public sealed class EndpointVolumeTests
{
    [Fact]
    public void CancellationBeforeFirstChannelWriteDoesNotAttemptRestoration()
    {
        using var cancellation = new CancellationTokenSource();
        var state = new FakeEndpointState { ReadingStereo = cancellation.Cancel };
        using var endpoint = new FakeEndpointVolume(state);
        Assert.ThrowsAny<OperationCanceledException>(() => EndpointVolumeOperations.Apply(endpoint,
            new SetEndpointBalance(100), cancellation.Token));
        Assert.Equal(0, state.Writes);
    }

    [Fact]
    public void DriverIgnoringWritesIsReportedAsUnsupported()
    {
        var state = new FakeEndpointState { IgnoreWrites = true };
        using var endpoint = new FakeEndpointVolume(state);
        Assert.Throws<NotSupportedException>(() => EndpointVolumeOperations.Apply(endpoint,
            new SetEndpointVolume(80), CancellationToken.None));
        Assert.Throws<NotSupportedException>(() => EndpointVolumeOperations.Apply(endpoint,
            new SetEndpointMute(true), CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(37.5)]
    public void VolumeUsesTheRequestedScalar(double percent)
    {
        var state = new FakeEndpointState();
        using var endpoint = new FakeEndpointVolume(state);
        EndpointVolumeOperations.Apply(endpoint, new SetEndpointVolume(percent), CancellationToken.None);
        Assert.Equal(percent, EndpointVolumeOperations.Read(endpoint).VolumePercent);
        Assert.Equal(1, state.Writes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidVolumeNeverWrites(double percent)
    {
        var state = new FakeEndpointState();
        using var endpoint = new FakeEndpointVolume(state);
        Assert.Throws<ArgumentOutOfRangeException>(() => EndpointVolumeOperations.Apply(endpoint,
            new SetEndpointVolume(percent), CancellationToken.None));
        Assert.Equal(0, state.Writes);
    }

    [Fact]
    public void MuteAndUnmutePreserveVolumeAndBalance()
    {
        var state = new FakeEndpointState { Stereo = new(0.2, 0.4) };
        using var endpoint = new FakeEndpointVolume(state);
        EndpointVolumeOperations.Apply(endpoint, new SetEndpointMute(true), CancellationToken.None);
        Assert.True(EndpointVolumeOperations.Read(endpoint).Muted);
        EndpointVolumeOperations.Apply(endpoint, new SetEndpointMute(false), CancellationToken.None);
        Assert.False(EndpointVolumeOperations.Read(endpoint).Muted);
        Assert.Equal(0.4, state.Volume);
        Assert.Equal(new StereoLevels(0.2, 0.4), state.Stereo);
    }

    [Theory]
    [InlineData(-100, 0.6, 0)]
    [InlineData(0, 0.6, 0.6)]
    [InlineData(100, 0, 0.6)]
    [InlineData(-50, 0.6, 0.3)]
    [InlineData(50, 0.3, 0.6)]
    public void BalancePreservesBaseWithoutAmplification(double balance, double left, double right)
    {
        var state = new FakeEndpointState { Stereo = new(0.3, 0.6) };
        using var endpoint = new FakeEndpointVolume(state);
        EndpointVolumeOperations.Apply(endpoint, new SetEndpointBalance(balance), CancellationToken.None);
        Assert.Equal(left, state.Stereo.Left, 6);
        Assert.Equal(right, state.Stereo.Right, 6);
        Assert.Equal(0.6, state.Stereo.BaseLevel);
        Assert.Equal(balance, state.Stereo.Balance, 6);
        Assert.Equal(0.4, state.Volume);
    }

    [Fact]
    public void RepeatedBalanceAndCenterDoNotAccumulateAttenuation()
    {
        var state = new FakeEndpointState();
        using var endpoint = new FakeEndpointVolume(state);
        foreach (double balance in new[] { -100d, 100, -50, 0, 100, 0 })
            EndpointVolumeOperations.Apply(endpoint, new SetEndpointBalance(balance), CancellationToken.None);
        Assert.Equal(new StereoLevels(0.4, 0.4), state.Stereo);
    }

    [Fact]
    public void SilentChannelsStaySilentWhenCentered()
    {
        Assert.Equal(new StereoLevels(0, 0), new StereoLevels(0, 0).WithBalance(0));
    }

    [Theory]
    [InlineData(-101)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    public void InvalidBalanceNeverWrites(double balance)
    {
        var state = new FakeEndpointState();
        using var endpoint = new FakeEndpointVolume(state);
        Assert.Throws<ArgumentOutOfRangeException>(() => EndpointVolumeOperations.Apply(endpoint,
            new SetEndpointBalance(balance), CancellationToken.None));
        Assert.Equal(0, state.Writes);
    }

    [Theory]
    [InlineData(1, 1, 4, false)]
    [InlineData(2, 2, 0, false)]
    [InlineData(2, 2, null, false)]
    [InlineData(6, 6, 63, false)]
    [InlineData(2, 6, 63, false)]
    [InlineData(2, 2, 3, true)]
    public void BalanceRequiresConfirmedStereoLayout(int endpointChannels, int mixChannels, int? mask, bool expected)
    {
        Assert.Equal(expected, StereoLevels.IsConfirmedStereo(endpointChannels, mixChannels, mask));
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 0)]
    public void UnsupportedBalanceLeavesVolumeAndMuteAvailable(int channels, int mask)
    {
        var state = new FakeEndpointState { Channels = channels, ChannelMask = mask };
        using var endpoint = new FakeEndpointVolume(state);
        var snapshot = EndpointVolumeOperations.Read(endpoint);
        Assert.Null(snapshot.Stereo);
        Assert.NotNull(snapshot.BalanceError);
        Assert.NotNull(snapshot.VolumePercent);
        Assert.NotNull(snapshot.Muted);
        Assert.Throws<NotSupportedException>(() => EndpointVolumeOperations.Apply(endpoint,
            new SetEndpointBalance(0), CancellationToken.None));
        Assert.Equal(0, state.Writes);
    }

    [Fact]
    public void UnsupportedMasterControlsReportTheirOwnCapabilities()
    {
        var state = new FakeEndpointState { UnsupportedVolume = true, UnsupportedMute = true };
        using var endpoint = new FakeEndpointVolume(state);
        var snapshot = EndpointVolumeOperations.Read(endpoint);
        Assert.Null(snapshot.VolumePercent);
        Assert.Null(snapshot.Muted);
        Assert.NotNull(snapshot.VolumeError);
        Assert.NotNull(snapshot.MuteError);
        Assert.NotNull(snapshot.Stereo);
        Assert.Equal(0, state.Writes);
    }

    [Fact]
    public void PartialBalanceFailureRestoresOriginalLevels()
    {
        var state = new FakeEndpointState { FailChannelWrite = 2 };
        using var endpoint = new FakeEndpointVolume(state);
        Assert.Throws<InvalidOperationException>(() => EndpointVolumeOperations.Apply(endpoint,
            new SetEndpointBalance(100), CancellationToken.None));
        Assert.Equal(new StereoLevels(0.4, 0.4), state.Stereo);
    }

    [Fact]
    public void CoupledChannelsRejectBalanceAndRestoreOriginalLevels()
    {
        var state = new FakeEndpointState { LinkedChannels = true };
        using var endpoint = new FakeEndpointVolume(state);
        Assert.Throws<NotSupportedException>(() => EndpointVolumeOperations.Apply(endpoint,
            new SetEndpointBalance(100), CancellationToken.None));
        Assert.Equal(new StereoLevels(0.4, 0.4), state.Stereo);
    }

    [Fact]
    public void CancellationBetweenChannelWritesRestoresOriginalLevels()
    {
        using var cancellation = new CancellationTokenSource();
        var state = new FakeEndpointState { ChannelWritten = cancellation.Cancel };
        using var endpoint = new FakeEndpointVolume(state);
        Assert.ThrowsAny<OperationCanceledException>(() => EndpointVolumeOperations.Apply(endpoint,
            new SetEndpointBalance(100), cancellation.Token));
        Assert.Equal(new StereoLevels(0.4, 0.4), state.Stereo);
    }

    [Fact]
    public void DisconnectDuringBalanceReportsFailedRestoration()
    {
        var state = new FakeEndpointState();
        state.ChannelWritten = () => state.Disconnected = true;
        using var endpoint = new FakeEndpointVolume(state);
        var error = Assert.Throws<InvalidOperationException>(() => EndpointVolumeOperations.Apply(endpoint,
            new SetEndpointBalance(100), CancellationToken.None));
        Assert.Contains("restauração", error.Message);
    }
}
