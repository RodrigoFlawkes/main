using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class BusMixSessionTests
{
    private static readonly PcmFloatFormat Mono = new(48000, 1);
    private static readonly PcmFloatFormat Stereo = new(48000, 2);
    private static readonly MixLevel On = new(1);

    [Fact]
    public void SumsTwoSourcesWithIndependentSendsAndMeters()
    {
        var io = new Transport(("game", .25f), ("aux", .125f));
        var mix = new BusMixSession(io);
        mix.Start(1, Mono, [Source("game", stream: new(.5f)), Source("aux", kind: MixSourceKind.Auxiliary)], cancellation: TestContext.Current.CancellationToken);
        Assert.True(mix.Process(1, 0));
        Assert.All(io.Personal[..io.SampleCount], x => Assert.Equal(.375f, x));
        Assert.All(io.Stream[..io.SampleCount], x => Assert.Equal(.125f, x));
        Assert.Equal(.375f, io.Meters.Personal.Peak);
        Assert.Equal(.125, io.Meters.Stream.Rms, 7);
        Assert.Equal(0, io.ExternalCalls);
    }

    [Theory]
    [InlineData((int)MixSourceKind.Microphone, (int)MixOrigin.Discrete)]
    [InlineData((int)MixSourceKind.Communication, (int)MixOrigin.Discrete)]
    [InlineData((int)MixSourceKind.Game, (int)MixOrigin.Aggregated)]
    [InlineData((int)MixSourceKind.Auxiliary, (int)MixOrigin.Aggregated)]
    public void SensitiveOrAggregatedSourceRequiresExplicitAuthorization(int kind, int origin)
    {
        var io = new Transport(("source", .5f));
        var mix = new BusMixSession(io);
        var source = Source("source", kind: (MixSourceKind)kind, stream: On, origin: (MixOrigin)origin);
        mix.Start(1, Mono, [source], new("publisher"), cancellation: TestContext.Current.CancellationToken);
        Assert.True(mix.Process(1, 0));
        Assert.Equal(.5f, io.Personal[0]);
        Assert.Equal(0, io.Stream[0]);
        Assert.Equal(0, io.External[0]);
        Assert.False(mix.SetSend(1, "source", MixBus.Stream, On));
        mix.Start(2, Mono, [source with { Endpoint = source.Endpoint with { Generation = 2 }, AllowSensitiveStream = true }], new("publisher"), cancellation: TestContext.Current.CancellationToken);
        Assert.True(mix.Process(2, 0));
        Assert.Equal(.5f, io.Stream[0]);
        Assert.Equal(.5f, io.External[0]);
    }

    [Fact]
    public void ReturnFromSameCallAndAggregateCannotBeSentToCall()
    {
        var io = new Transport(("call-return", .5f), ("aggregate", .25f), ("mic", .125f), ("unknown-chat", .0625f));
        var mix = new BusMixSession(io);
        mix.Start(1, Mono, [
            Source("call-return", kind: MixSourceKind.Communication, stream: On, allow: true, call: "call"),
            Source("aggregate", stream: On, origin: MixOrigin.Aggregated, allow: true),
            Source("mic", kind: MixSourceKind.Microphone, stream: On, allow: true),
            Source("unknown-chat", kind: MixSourceKind.Communication, stream: On, allow: true)
        ], new("publisher", "call"), cancellation: TestContext.Current.CancellationToken);
        Assert.True(mix.Process(1, 0));
        Assert.Equal(.9375f, io.Personal[0]);
        Assert.Equal(.125f, io.Stream[0]);
        Assert.Equal(.125f, io.External[0]);
    }

    [Fact]
    public void MuteAndMasterUseFrameRampAndDoNotChangeOtherBus()
    {
        var io = new Transport(("game", .5f));
        var mix = new BusMixSession(io);
        mix.Start(1, Stereo, [Source("game", stream: On, channelPolicy: ChannelPolicy.DuplicateMono)], cancellation: TestContext.Current.CancellationToken);
        Assert.True(mix.Process(1, 0));
        Assert.True(mix.SetSend(1, "game", MixBus.Personal, new(1, true)));
        Assert.True(mix.SetMaster(1, MixBus.Stream, new(.5f)));
        Assert.True(mix.Process(1, 1));
        Assert.InRange(io.Personal[0], .49f, .5f);
        Assert.Equal(io.Personal[0], io.Personal[1]);
        Assert.Equal(0, io.Personal[478]);
        Assert.Equal(.25f, io.Stream[478]);
        Assert.Equal(.25f, io.Stream[^1]);
    }

    [Theory]
    [InlineData(44100, 48000)]
    [InlineData(48000, 44100)]
    public void NominalConversionHasStableBlockSizeAndClearsHistoryAfterGap(int inputRate, int outputRate)
    {
        var io = new Transport(("source", .25f));
        var mix = new BusMixSession(io);
        var source = Source("source", rate: inputRate);
        mix.Start(1, new(outputRate, 1), [source], cancellation: TestContext.Current.CancellationToken);
        Assert.True(mix.Process(1, 0));
        Assert.Equal(outputRate / 100, io.SampleCount);
        Assert.InRange(io.Personal[io.SampleCount - 1], .249f, .251f);
        io.Missing = true;
        Assert.True(mix.Process(1, 1));
        Assert.All(io.Personal.AsSpan(0, io.SampleCount).ToArray(), x => Assert.Equal(0, x));
        io.Missing = false;
        Assert.True(mix.Process(1, 2));
        Assert.InRange(MathF.Abs(io.Personal[0]), 0, .001f);
        Assert.InRange(io.Personal[io.SampleCount - 1], .249f, .251f);
    }

    [Fact]
    public void LateBlockCannotReappearAndMissingSourceDoesNotDelayOtherSource()
    {
        var io = new Transport(("on-time", .25f), ("late", .5f));
        var mix = new BusMixSession(io);
        mix.Start(1, Mono, [Source("on-time"), Source("late")], cancellation: TestContext.Current.CancellationToken);
        io.LateSource = "late";
        Assert.True(mix.Process(1, 0));
        Assert.Equal(.25f, io.Personal[0]);
        Assert.False(mix.Process(1, 0));
        io.LateSource = null;
        Assert.True(mix.Process(1, 2));
        Assert.Equal(.75f, io.Personal[io.SampleCount - 1]);
    }

    [Fact]
    public void RejectsInvalidTopologyAndDoesNotReplaceActiveGeneration()
    {
        var io = new Transport(("game", .125f));
        var mix = new BusMixSession(io);
        mix.Start(1, Mono, [Source("game")], cancellation: TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() => mix.Start(2, Mono, [Source("game", 2), Source("game", 2)], cancellation: TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => mix.Start(2, Mono, [Source("echo", 2, origin: MixOrigin.BusOutput)], cancellation: TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => mix.Start(2, Stereo, [Source("game", 2)], cancellation: TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcmFloatFormat(48000, 6));
        Assert.Throws<NotSupportedException>(() => mix.Start(2, Mono, [Source("source", 2, rate: 8000)], cancellation: TestContext.Current.CancellationToken));
        Assert.True(mix.Process(1, 0));
        Assert.Equal(.125f, io.Personal[0]);
    }

    [Fact]
    public void ProtectsFinalSamplesAndReportsClippingAfterDeterministicSum()
    {
        var io = new Transport(("c", -1), ("b", 1), ("a", 1), ("d", float.NaN), ("e", float.PositiveInfinity), ("f", float.Epsilon));
        var mix = new BusMixSession(io);
        MixSource[] sources = [Source("c"), Source("e"), Source("a"), Source("f"), Source("d"), Source("b")];
        mix.Start(1, Mono, sources, cancellation: TestContext.Current.CancellationToken);
        Assert.True(mix.Process(1, 0));
        Assert.Equal(1, io.Personal[0]);
        Assert.Equal(0, io.Meters.Personal.ClippedSamples);
        mix.Start(2, Mono, [Source("b", 2), Source("a", 2)], cancellation: TestContext.Current.CancellationToken);
        Assert.True(mix.Process(2, 0));
        Assert.Equal(1, io.Personal[0]);
        Assert.Equal(480, io.Meters.Personal.ClippedSamples);
    }

    [Fact]
    public void SortsByFlowIdRegardlessOfRegistrationOrder()
    {
        var io = new Transport(("a", .125f), ("b", .25f), ("c", .5f));
        var mix = new BusMixSession(io);
        io.RememberReads = true;
        mix.Start(1, Mono, [Source("c"), Source("a"), Source("b")], cancellation: TestContext.Current.CancellationToken);
        Assert.True(mix.Process(1, 0));
        Assert.Equal(new[] { "a", "b", "c" }, io.Reads);
        Assert.Equal(.875f, io.Personal[0]);
    }

    [Fact]
    public void StopDuringSubmissionSuppressesStaleBlockAndNextGenerationStartsSilent()
    {
        var io = new Transport(("game", .5f));
        var mix = new BusMixSession(io);
        mix.Start(1, Mono, [Source("game", stream: On)], new("publisher"), cancellation: TestContext.Current.CancellationToken);
        io.BeforeSubmit = () => mix.Stop(1);
        Assert.False(mix.Process(1, 0));
        Assert.Equal(0, io.InternalCalls);
        Assert.Equal(0, io.ExternalCalls);
        Assert.False(mix.Process(1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => mix.Start(1, Mono, [], cancellation: TestContext.Current.CancellationToken));
        mix.Start(2, Mono, [], cancellation: TestContext.Current.CancellationToken);
        io.BeforeSubmit = null;
        Assert.True(mix.Process(2, 0));
        Assert.All(io.Personal, x => Assert.Equal(0, x));
        Assert.All(io.Stream, x => Assert.Equal(0, x));
    }

    [Fact]
    public void CancellationBetweenInternalAndExternalDeliveryInvalidatesRetainedTicket()
    {
        using var cancel = new CancellationTokenSource();
        var io = new Transport(("game", .5f));
        var mix = new BusMixSession(io);
        mix.Start(1, Mono, [Source("game", stream: On)], new("publisher"), cancel.Token);
        io.AfterSubmit = cancel.Cancel;
        Assert.False(mix.Process(1, 0));
        Assert.Equal(1, io.InternalCalls);
        Assert.Equal(0, io.ExternalCalls);
        Assert.False(io.LastTicket!.IsValid);
        Assert.False(mix.Process(1, 1));
    }

    [Fact]
    public void ReplacedGenerationCannotPublishAndOldPacketsAreRejectedByNewGeneration()
    {
        var io = new Transport(("game", .5f));
        var mix = new BusMixSession(io);
        var cancel = TestContext.Current.CancellationToken;
        mix.Start(1, Mono, [Source("game", stream: On)], new("publisher"), cancel);
        io.BeforeSubmit = () =>
        {
            io.BeforeSubmit = null;
            mix.Start(2, Mono, [Source("game", 2, stream: On)], cancellation: cancel);
            Assert.False(mix.Process(2, 0));
        };
        Assert.False(mix.Process(1, 0));
        Assert.False(mix.SetSend(1, "game", MixBus.Personal, On));
        Assert.Equal(0, io.InternalCalls);
        Assert.Equal(0, io.ExternalCalls);

        io.ReportedGeneration = 1;
        Assert.True(mix.Process(2, 0));
        Assert.Equal(0, io.Personal[0]);
        Assert.Equal(0, io.Stream[0]);
        io.ReportedGeneration = 2;
        Assert.True(mix.Process(2, 1));
        Assert.Equal(.5f, io.Personal[io.SampleCount - 1]);
        Assert.Equal(0, io.ExternalCalls);
    }

    [Fact]
    public void PartialBlockIsSilentAndStereoDownmixUsesBothChannels()
    {
        var io = new Transport(("aux", .75f)) { ShortRead = true };
        var mix = new BusMixSession(io);
        var source = Source("aux", channelPolicy: ChannelPolicy.AverageStereo) with { Format = Stereo };
        mix.Start(1, Mono, [source], cancellation: TestContext.Current.CancellationToken);
        Assert.True(mix.Process(1, 0));
        Assert.Equal(0, io.Meters.Personal.Peak);
        io.ShortRead = false;
        io.Right = -.25f;
        Assert.True(mix.Process(1, 1));
        Assert.Equal(.25f, io.Personal[io.SampleCount - 1]);
    }

    [Fact]
    public void ProcessingHasNoManagedAllocationsAfterWarmup()
    {
        var io = new Transport(("game", .25f), ("aux", .5f));
        var mix = new BusMixSession(io);
        mix.Start(1, Mono, [Source("game", stream: On, rate: 44100), Source("aux")], new("publisher"), cancellation: TestContext.Current.CancellationToken);
        for (int i = 0; i < 64; i++) Assert.True(mix.Process(1, i));
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool success = true;
        for (int i = 64; i < 192; i++) success &= mix.Process(1, i);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(success);
        Assert.Equal(0, allocated);
    }

    private static MixSource Source(string id, long generation = 1, MixSourceKind kind = MixSourceKind.Game,
        MixLevel? stream = null, MixOrigin origin = MixOrigin.Discrete, bool allow = false, string? call = null,
        int rate = 48000, ChannelPolicy channelPolicy = ChannelPolicy.Preserve) =>
        new(new(id, EndpointDirection.Capture, generation), new(rate, 1), kind, origin, channelPolicy, On, stream, allow, call);

    private sealed class Transport(params (string Id, float Value)[] data) : IBusPcmTransport
    {
        public float[] Personal { get; } = new float[960];
        public float[] Stream { get; } = new float[960];
        public float[] External { get; } = new float[960];
        public BusMeters Meters { get; private set; }
        public MixDeliveryTicket? LastTicket { get; private set; }
        public int InternalCalls { get; private set; }
        public int ExternalCalls { get; private set; }
        public int SampleCount { get; private set; }
        public bool Missing { get; set; }
        public string? LateSource { get; set; }
        public bool RememberReads { get; set; }
        public long? ReportedGeneration { get; set; }
        public bool ShortRead { get; set; }
        public float? Right { get; set; }
        public List<string> Reads { get; } = [];
        public Action? BeforeSubmit { get; set; }
        public Action? AfterSubmit { get; set; }

        public MixReadResult Read(MixSource source, long block, Span<float> pcm)
        {
            if (RememberReads) Reads.Add(source.Endpoint.Id);
            if (Missing) return default;
            foreach (var (id, value) in data)
            {
                if (id != source.Endpoint.Id) continue;
                pcm.Fill(value);
                if (source.Format.Channels == 2 && Right is { } right)
                    for (int i = 1; i < pcm.Length; i += 2) pcm[i] = right;
                var endpoint = ReportedGeneration is { } generation
                    ? source.Endpoint with { Generation = generation } : source.Endpoint;
                int frames = source.Format.FramesFromSamples(pcm.Length) - (ShortRead ? 1 : 0);
                return new(endpoint, LateSource == id ? block - 1 : block, frames);
            }
            return default;
        }

        public bool Submit(MixDeliveryTicket ticket, long block, ReadOnlySpan<float> personal,
            ReadOnlySpan<float> stream, BusMeters meters)
        {
            BeforeSubmit?.Invoke();
            if (!ticket.IsValid) return false;
            LastTicket = ticket;
            SampleCount = personal.Length;
            personal.CopyTo(Personal);
            stream.CopyTo(Stream);
            Meters = meters;
            InternalCalls++;
            AfterSubmit?.Invoke();
            return true;
        }

        public bool SubmitExternal(MixDeliveryTicket ticket, long block, ReadOnlySpan<float> stream)
        {
            if (!ticket.IsValid || ticket.External is null) return false;
            stream.CopyTo(External);
            ExternalCalls++;
            return true;
        }
    }
}
