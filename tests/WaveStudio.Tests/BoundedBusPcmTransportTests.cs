using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class BoundedBusPcmTransportTests
{
    private static readonly PcmFloatFormat Mono = new(48000, 1);

    [Fact]
    public void CaptureAndBothBusesDeliverRealPcmWithExternalConsent()
    {
        var game = Source("game", stream: new(.5f));
        var microphone = Source("mic", kind: MixSourceKind.Microphone);
        var destination = new MixExternalDestination("obs");
        using var bridge = new BoundedBusPcmTransport(1, Mono, [game, microphone], destination);
        var session = new BusMixSession(bridge);
        session.Start(1, Mono, [game, microphone], destination, TestContext.Current.CancellationToken);

        Assert.True(bridge.TryOfferCapture(game.Endpoint, 0, Constant(.25f)));
        Assert.True(bridge.TryOfferCapture(microphone.Endpoint, 0, Constant(.125f)));
        Assert.True(session.Process(1, 0));

        float[] personal = new float[480], stream = new float[480], external = new float[480];
        Assert.True(bridge.TryReadOutput(MixBus.Personal, 0, personal, out var personalTicket));
        Assert.True(bridge.TryReadOutput(MixBus.Stream, 0, stream, out var streamTicket));
        Assert.True(bridge.TryReadExternal(destination, 0, external, out var externalTicket));
        Assert.True(personalTicket!.IsValid);
        Assert.Same(personalTicket, streamTicket);
        Assert.Same(streamTicket, externalTicket);
        Assert.All(personal, sample => Assert.Equal(.375f, sample));
        Assert.All(stream, sample => Assert.Equal(.125f, sample));
        Assert.Equal(stream, external);
    }

    [Fact]
    public void MissingCaptureProducesSilenceAndStaleBlocksNeverReappear()
    {
        var game = Source("game");
        using var bridge = new BoundedBusPcmTransport(1, Mono, [game], capacityBlocks: 2);
        var session = new BusMixSession(bridge);
        session.Start(1, Mono, [game], cancellation: TestContext.Current.CancellationToken);

        Assert.True(session.Process(1, 0));
        Assert.True(bridge.TryOfferCapture(game.Endpoint, 1, Constant(.4f)));
        Assert.True(session.Process(1, 1));

        float[] samples = new float[480];
        Assert.True(bridge.TryReadOutput(MixBus.Personal, 1, samples, out _));
        Assert.All(samples, sample => Assert.Equal(.4f, sample));
        Assert.False(bridge.TryReadOutput(MixBus.Personal, 0, samples, out _));
        Assert.All(samples, sample => Assert.Equal(0, sample));
        Assert.False(bridge.TryOfferCapture(game.Endpoint, 0, Constant(.5f)));
        Assert.False(bridge.TryOfferCapture(game.Endpoint, 1, Constant(.5f)));
    }

    [Fact]
    public void BackpressureAndIdentityValidationRejectInvalidPackets()
    {
        var game = Source("game");
        using var bridge = new BoundedBusPcmTransport(1, Mono, [game], capacityBlocks: 2);
        Assert.False(bridge.TryOfferCapture(game.Endpoint with { Generation = 2 }, 0, Constant(.5f)));
        Assert.False(bridge.TryOfferCapture(new("other", EndpointDirection.Capture, 1), 0, Constant(.5f)));
        Assert.False(bridge.TryOfferCapture(game.Endpoint, 0, new float[479]));
        Assert.True(bridge.TryOfferCapture(game.Endpoint, 0, Constant(.1f)));
        Assert.True(bridge.TryOfferCapture(game.Endpoint, 1, Constant(.2f)));
        Assert.False(bridge.TryOfferCapture(game.Endpoint, 2, Constant(.3f)));

        float[] buffer = new float[480];
        MixReadResult read = bridge.Read(game, 0, buffer);
        Assert.Equal(480, read.Frames);
        Assert.All(buffer, x => Assert.Equal(.1f, x));
        Assert.True(bridge.TryOfferCapture(game.Endpoint, 2, Constant(.3f)));
        read = bridge.Read(game, 2, buffer);
        Assert.Equal(480, read.Frames);
        Assert.All(buffer, x => Assert.Equal(.3f, x));
    }

    [Fact]
    public void OldGenerationTicketCannotReleaseQueuedPcm()
    {
        var source = Source("game");
        using var bridge = new BoundedBusPcmTransport(1, Mono, [source]);
        var session = new BusMixSession(bridge);
        session.Start(1, Mono, [source], cancellation: TestContext.Current.CancellationToken);
        Assert.True(bridge.TryOfferCapture(source.Endpoint, 0, Constant(.75f)));
        Assert.True(session.Process(1, 0));

        var next = source with { Endpoint = source.Endpoint with { Generation = 2 } };
        session.Start(2, Mono, [next], cancellation: TestContext.Current.CancellationToken);
        float[] output = Constant(1);
        Assert.False(bridge.TryReadOutput(MixBus.Personal, 0, output, out var ticket));
        Assert.Null(ticket);
        Assert.All(output, sample => Assert.Equal(0, sample));
        Assert.False(bridge.TryOfferCapture(next.Endpoint, 1, Constant(.25f)));
    }

    [Fact]
    public void SensitiveSourceCannotEnterExternalQueueWithoutAuthorization()
    {
        var microphone = Source("mic", kind: MixSourceKind.Microphone, stream: new(1));
        var dest = new MixExternalDestination("stream");
        using var bridge = new BoundedBusPcmTransport(1, Mono, [microphone], dest);
        var session = new BusMixSession(bridge);
        session.Start(1, Mono, [microphone], dest, TestContext.Current.CancellationToken);

        Assert.True(bridge.TryOfferCapture(microphone.Endpoint, 0, Constant(.6f)));
        Assert.True(session.Process(1, 0));
        float[] personal = new float[480], external = new float[480];
        Assert.True(bridge.TryReadOutput(MixBus.Personal, 0, personal, out _));
        Assert.True(bridge.TryReadExternal(dest, 0, external, out _));
        Assert.All(personal, sample => Assert.Equal(.6f, sample));
        Assert.All(external, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void ClosedBridgeRejectsFurtherInputAndOutput()
    {
        var game = Source("game");
        var bridge = new BoundedBusPcmTransport(1, Mono, [game]);
        var session = new BusMixSession(bridge);
        session.Start(1, Mono, [game], cancellation: TestContext.Current.CancellationToken);
        Assert.True(bridge.TryOfferCapture(game.Endpoint, 0, Constant(.2f)));
        Assert.True(session.Process(1, 0));
        bridge.Dispose();

        float[] output = Constant(1);
        Assert.False(bridge.TryReadOutput(MixBus.Personal, 0, output, out _));
        Assert.All(output, value => Assert.Equal(0, value));
        Assert.False(bridge.TryOfferCapture(game.Endpoint, 1, Constant(.2f)));
        Assert.False(session.Process(1, 1));
    }

    private static MixSource Source(string id, MixSourceKind kind = MixSourceKind.Game,
        MixLevel? stream = null) =>
        new(new(id, EndpointDirection.Capture, 1), Mono, kind, MixOrigin.Discrete,
            ChannelPolicy.Preserve, new(1), stream);

    private static float[] Constant(float value)
    {
        float[] data = new float[480];
        Array.Fill(data, value);
        return data;
    }
}
