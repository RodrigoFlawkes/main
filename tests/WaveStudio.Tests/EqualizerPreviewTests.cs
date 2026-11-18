using System.Buffers.Binary;
using NAudio.Wave;
using WaveStudio.Audio;
using WaveStudio.Audio.Dsp;
using Xunit;

namespace WaveStudio.Tests;

public sealed class EqualizerPreviewTests
{
    public static IEnumerable<object[]> Centers =>
        from rate in new[] { 44100, 48000 } from band in Enumerable.Range(0, 10) from gain in new[] { -15d, 15d }
        select new object[] { rate, band, gain };
    [Theory]
    [MemberData(nameof(Centers))]
    public void EachBandHasSpecifiedCenterGainWithExplicitHeadroom(int rate, int band, double gain)
    {
        var gains = new double[10]; gains[band] = gain;
        var settings = new GraphicEqualizerSettings(true, gains);
        var eq = new GraphicEqualizer(rate, 1, settings);
        int frequency = GraphicEqualizerSettings.Frequencies[band];
        double inputEnergy = 0, outputEnergy = 0;
        for (int i = 0; i < rate * 2; i++)
        {
            float input = (float)(.1 * Math.Sin(2 * Math.PI * frequency * i / rate));
            float output = eq.Process(input, 0);
            if (i < rate) continue;
            inputEnergy += input * (double)input; outputEnergy += output * (double)output;
        }
        double actual = 10 * Math.Log10(outputEnergy / inputEnergy);
        Assert.InRange(actual, gain + settings.EffectivePreampDb - .12, gain + settings.EffectivePreampDb + .12);
    }
    [Fact]
    public void SettingsAreImmutableValidatedAndHeadroomNeverAddsBoost()
    {
        var gains = new[] { 15d, -15, 6, 0, 0, 0, 0, 0, 0, 0 };
        var settings = new GraphicEqualizerSettings(true, gains, -3); gains[0] = -15;
        Assert.Equal(15, settings.GainsDb[0]); Assert.Equal(21, settings.HeadroomDb); Assert.Equal(-24, settings.EffectivePreampDb);
        Assert.Throws<NotSupportedException>(() => ((IList<double>)settings.GainsDb)[0] = 0);
        Assert.Throws<ArgumentException>(() => new GraphicEqualizerSettings(true, new double[9]));
        Assert.Throws<ArgumentException>(() => new GraphicEqualizerSettings(true, new double[11]));
        Assert.Throws<ArgumentException>(() => new GraphicEqualizerSettings(true, Enumerable.Repeat(double.NaN, 10)));
        Assert.Throws<ArgumentException>(() => new GraphicEqualizerSettings(true, Enumerable.Repeat(15.01, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicEqualizerSettings(true, new double[10], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicEqualizerSettings(true, new double[10], double.NaN));
        Assert.Throws<NotSupportedException>(() => new GraphicEqualizer(32000, 1, settings));
        Assert.Throws<NotSupportedException>(() => new GraphicEqualizer(48000, 6, settings));
    }
    [Theory]
    [InlineData(44100)] [InlineData(48000)]
    public void FullCascadeStaysFiniteAndStereoStatesAreIndependent(int rate)
    {
        foreach (double gain in new[] { -15d, 15d })
        {
            var settings = new GraphicEqualizerSettings(true, Enumerable.Repeat(gain, 10));
            var stereo = new GraphicEqualizer(rate, 2, settings);
            var mono = new GraphicEqualizer(rate, 1, settings);
            for (int i = 0; i < rate; i++)
            {
                float sample = i == 0 ? .9f : (float)(.25 * Math.Sin(i * .1));
                float left = stereo.Process(sample, 0);
                Assert.True(float.IsFinite(left)); Assert.Equal(mono.Process(sample, 0), left);
                Assert.Equal(0, stereo.Process(0, 1));
            }
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FlatAndBypassPreserveOriginalAndEachPlayStartsWithFreshState(bool pcm16)
    {
        var format = pcm16 ? new WaveFormat(48000, 16, 2) : WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        using var clip = new BoundedTestAudio(format);
        byte[] original = Signal(format, 4096); clip.Append(original);
        byte[] Read(GraphicEqualizerSettings? settings)
        {
            var provider = clip.CreatePlayback(.2f, () => true, settings);
            var data = new byte[original.Length]; int at = 0;
            // Blocos irregulares alinhados a frames. EOF e espaço insuficiente no destino não devem avançar o estado.
            Assert.Equal(0, provider.Read(data.AsSpan(0, format.BlockAlign - 1)));
            while (at < data.Length) { int count = Math.Min(data.Length - at, 37 * format.BlockAlign); at += provider.Read(data.AsSpan(at, count)); }
            Assert.Equal(0, provider.Read(data)); return data;
        }
        var reference = Read(null);
        Assert.Equal(reference, Read(new(false, Enumerable.Repeat(15d, 10), -30)));
        var flat = Read(new(true, new double[10]));
        if (!pcm16) Assert.Equal(reference, flat);
        else for (int i = 0; i < flat.Length; i += 2) Assert.InRange(Math.Abs(BinaryPrimitives.ReadInt16LittleEndian(flat.AsSpan(i)) - BinaryPrimitives.ReadInt16LittleEndian(reference.AsSpan(i))), 0, 1);
        var enabled = new GraphicEqualizerSettings(true, new double[] { 0, 0, 0, 0, 0, -15, 0, 0, 0, 0 });
        var first = Read(enabled); Assert.False(reference.SequenceEqual(first)); Assert.Equal(first, Read(enabled));
        Assert.Equal(reference, Read(null)); Assert.Equal(original.Length, clip.Length);
    }
    [Fact]
    public void FinalProtectionFollowsDigitalLevelAndInvalidInputDoesNotPoisonFilters()
    {
        using var clip = new BoundedTestAudio(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1));
        var values = new[] { 2f, -2, float.NaN, float.PositiveInfinity, float.MaxValue, .5f };
        var data = new byte[values.Length * 4]; for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * 4), values[i]);
        clip.Append(data); var flat = new GraphicEqualizerSettings(true, new double[10]);
        var full = clip.CreatePlayback(1, () => true, flat); full.Read(data);
        Assert.Equal(2, full.LimitedSamples); Assert.Equal(3, full.SanitizedSamples);
        Assert.Equal(.5f, BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(20)));
        var low = clip.CreatePlayback(.2f, () => true, flat); low.Read(data);
        Assert.Equal(0, low.LimitedSamples); Assert.Equal(.4f, BinaryPrimitives.ReadSingleLittleEndian(data));
        var dsp = new GraphicEqualizer(48000, 1, new(true, Enumerable.Repeat(15d, 10)));
        for (int i = 0; i < 10000; i++) Assert.True(float.IsFinite(dsp.Process(i < 10 ? float.NaN : .2f, 0)));
    }
    [Theory]
    [InlineData(8000)] [InlineData(96000)]
    public void UnsupportedEqRateKeepsOriginalPlayable(int rate)
    {
        using var clip = new BoundedTestAudio(new WaveFormat(rate, 16, 1)); clip.Append([1, 1, 2, 2]);
        Assert.Throws<NotSupportedException>(() => clip.CreatePlayback(.2f, () => true, new(true, new double[10])));
        Assert.Equal(4, clip.CreatePlayback(.2f, () => true).Read(new byte[4]));
    }
    [Fact]
    public void ProcessedReadAllocatesNothingAndInvalidationSuppressesCopiedPacket()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2); using var clip = new BoundedTestAudio(format); clip.Append(Signal(format, 16000));
        int gates = 0; var invalid = clip.CreatePlayback(.2f, () => ++gates == 1, new(true, Enumerable.Repeat(6d, 10)));
        var bytes = new byte[128]; Assert.Equal(0, invalid.Read(bytes)); Assert.All(bytes, value => Assert.Equal(0, value));
        var source = clip.CreatePlayback(.2f, () => true, new(true, Enumerable.Repeat(6d, 10)));
        for (int i = 0; i < 100; i++) source.Read(bytes);
        long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 100; i++) source.Read(bytes);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.Equal(0, allocated);
        clip.Dispose(); Assert.Equal(0, source.Read(bytes));
    }
    [Fact]
    public void EngineUsesSameClipForAbAndKeepsSelectionAndStopGuards()
    {
        var backend = new MicrophoneTestTests.FakeBackend(); long generation = 1;
        var engine = new MicrophoneTestEngine(backend, g => g == generation);
        engine.Select("input", "output", 1); engine.Record(1, TimeSpan.Zero);
        var format = backend.Capture.Format; var original = Signal(format, 4096); backend.Capture.Receive!(original);
        engine.Stop(); engine.Tick(TimeSpan.Zero);
        var settings = new GraphicEqualizerSettings(true, new double[] { 0, 0, 0, 0, 0, -15, 0, 0, 0, 0 });
        engine.Play(1, .2f, TimeSpan.Zero, equalizer: settings);
        Assert.Equal("Processado com EQ", engine.Snapshot.PlaybackMode);
        var processed = new byte[original.Length]; Assert.Equal(processed.Length, backend.Source!.Read(processed));
        engine.Stop(); engine.Tick(TimeSpan.Zero);
        engine.Play(1, .2f, TimeSpan.Zero); var raw = new byte[original.Length]; backend.Source!.Read(raw);
        Assert.False(raw.SequenceEqual(processed)); Assert.Equal(1, backend.Capture.Starts); Assert.Equal(2, backend.Playback.Starts);
        Assert.Equal(TimeSpan.FromSeconds(4096d / 48000), engine.Snapshot.RecordedDuration);
        generation++; Assert.Equal(0, backend.Source.Read(raw)); engine.Select("other", "output", generation); engine.Tick(TimeSpan.Zero);
        Assert.False(engine.Snapshot.CanPlay); engine.MarkDisposed();
    }
    [Fact]
    public async Task StopWhileProcessedPlaybackOpensPreventsStartAndPreservesClip()
    {
        var backend = new MicrophoneTestTests.FakeBackend(); await using var session = new MicrophoneTestSession(backend);
        await session.SelectAsync("input", "output"); await session.RecordAsync(); backend.Capture.Receive!(Signal(backend.Capture.Format, 256)); await session.StopAsync();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        backend.BeforePlaybackOpen = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken)) throw new TimeoutException(); };
        var play = session.PlayEqualizedAsync(.2f, new(true, new double[10]));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken)); var stop = session.StopAsync(); release.Set();
        await play; await stop; Assert.Equal(0, backend.Playback.Starts);
        Assert.True(session.Latest.CanPlay); Assert.Equal(0, backend.Source!.Read(new byte[128]));
    }
    private static byte[] Signal(WaveFormat format, int frames)
    {
        int bytes = format.BitsPerSample / 8; var data = new byte[frames * format.BlockAlign];
        for (int i = 0; i < frames * format.Channels; i++)
        {
            float value = (float)(.4 * Math.Sin(2 * Math.PI * 1000 * (i / format.Channels) / format.SampleRate));
            if (bytes == 4) BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * bytes), value);
            else BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * bytes), (short)(value * 32767));
        }
        return data;
    }
}
