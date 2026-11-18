using System.Diagnostics;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

[Collection("Synthetic engine measurement")]
public sealed class AdaptiveIntegrationTests
{
    [Theory]
    [InlineData(48000, 48000, -300, 100)]
    [InlineData(48000, 48000, 300, 100)]
    [InlineData(44100, 48000, 100, 35)]
    [InlineData(48000, 44100, -100, 35)]
    public void ActualSamplesUseClockControlAndRealSpscWithAccountedLosses(int inputRate, int outputRate, int driftPpm, int seconds)
    {
        var enabled = Run(inputRate, outputRate, driftPpm, seconds, true);
        var disabled = Run(inputRate, outputRate, driftPpm, seconds, false);
        AdaptiveMetrics.Save($"integration-{inputRate}-{outputRate}-{driftPpm}", new { Enabled = enabled, Disabled = disabled });
        Assert.Equal(0, enabled.Dropped + enabled.Underrun);
        Assert.True(enabled.Peak > .02);
        Assert.InRange(enabled.ConvergedSeconds, 0, seconds);
        Assert.InRange(Math.Abs(enabled.FinalRatioErrorPpm), 0, 30);
        Assert.InRange(Math.Abs(enabled.LastMean - 1024), 0, 128);
        Assert.Equal(enabled.InputFrames, enabled.MappedFrames);
        if (seconds >= 100) Assert.True(disabled.Dropped + disabled.Underrun > 0);
        else Assert.True(Math.Abs(enabled.LastMean - 1024) < Math.Abs(disabled.LastMean - 1024));
    }

    [Fact]
    public void ReprimeStopsSampleDeliveryAndNewGenerationRejectsPreviousCommandsAndClocks()
    {
        var format = new PcmFloatFormat(48000, 1);
        var stage = new AdaptiveResampler(format, format, ChannelPolicy.Preserve);
        using var pipe = new SyntheticAudioPipeline(format, format, 2048, 512);
        long generation = pipe.Start(TestContext.Current.CancellationToken);
        var inputClock = new AudioClock("in", generation, 48000, "us", 1000000);
        var outputClock = new AudioClock("out", generation, 48000, "us", 1000000);
        var estimator = new ClockRatioEstimator(inputClock, outputClock);
        var controller = new ClockRatioController(48000, 48000, 1000000);
        var source = Enumerable.Repeat(.2f, 256).ToArray();
        var destination = new float[512];
        var converted = stage.Process(generation, source, destination);
        pipe.Produce(generation, destination.AsSpan(0, converted.ProducedFrames));
        long written = pipe.Snapshot().Buffer!.Value.WrittenFrames;
        controller.Update(100000, 1024, new(1, 100000));
        var failed = controller.Update(4200000, 1024, new(1, 100000));
        Assert.Equal(DriftControlState.ReprimeRequired, failed.State);
        // A proteção entre proprietários é explícita. A entrega não continua usando a última razão limitada.
        stage.Stop(generation); pipe.Stop();
        Assert.Equal(0, stage.Process(generation, source, destination).ConsumedFrames);
        Assert.Equal(0, pipe.Render(generation, destination));
        Assert.All(destination, value => Assert.Equal(0, value));
        Assert.Equal(written, pipe.Snapshot().Buffer!.Value.WrittenFrames);
        long discarded = pipe.Snapshot().Buffer!.Value.QueuedFrames;
        Assert.Equal(written, discarded);
        Assert.True(pipe.TryReset());
        long next = pipe.Start(TestContext.Current.CancellationToken);
        stage.Reset(next);
        controller.Reset(4200000);
        estimator = new(new("in", next, 48000, "us", 1000000), new("out", next, 48000, "us", 1000000));
        Assert.Equal(ClockObservationResult.InvalidIdentity, estimator.Observe(new(inputClock, 0, 4300000), new(outputClock, 0, 4300000), 4300000));
        Assert.Null(estimator.Latest);
        Assert.False(stage.SetTargetPpm(generation, 500));
        Assert.Equal(0, stage.Process(generation, source, destination).ProducedFrames);
        Assert.Equal(0, stage.InputFrames);
        converted = stage.Process(next, source, destination);
        Assert.Equal(256, converted.ConsumedFrames);
        Assert.Equal(converted.ProducedFrames, pipe.Produce(next, destination.AsSpan(0, converted.ProducedFrames)));
        var rendered = new float[converted.ProducedFrames];
        Assert.Equal(converted.ProducedFrames, pipe.Render(next, rendered));
        Assert.True(rendered.Max() > .1);
        AdaptiveMetrics.Save("recovery", new { DiscardedBeforeReset = discarded, NewGeneration = next,
            InputFramesAfterReset = stage.InputFrames, ProducedAfterReset = converted.ProducedFrames, LateCommandsAccepted = false });
    }

    private readonly record struct Result(int InputRate, int OutputRate, int DriftPpm, int SimulatedSeconds, bool Controlled,
        long InputFrames, long MappedFrames, long GeneratedFrames, long PrimedFrames, long ReadBeforeDrain, long DrainFrames,
        long Dropped, long Underrun, int FinalQueued, int Minimum, int Maximum, double LastMean, double ConvergedSeconds,
        double FinalRatioErrorPpm, float Peak, double WallMilliseconds, int PayloadBytes);

    private static Result Run(int inputRate, int outputRate, int driftPpm, int seconds, bool controlled)
    {
        const long timestampFrequency = 1000000;
        int inputChannels = inputRate == 44100 ? 2 : 1, outputChannels = inputRate == 44100 ? 1 : 2;
        var inputFormat = new PcmFloatFormat(inputRate, inputChannels); var outputFormat = new PcmFloatFormat(outputRate, outputChannels);
        var stage = new AdaptiveResampler(inputFormat, outputFormat, inputChannels == 1 ? ChannelPolicy.DuplicateMono : ChannelPolicy.AverageStereo);
        using var pipe = new SyntheticAudioPipeline(outputFormat, outputFormat, 2048, 512);
        long generation = pipe.Start(TestContext.Current.CancellationToken);
        var inputClock = new AudioClock("input", generation, inputRate, "simulation-us", timestampFrequency);
        var outputClock = new AudioClock("output", generation, outputRate, "simulation-us", timestampFrequency);
        var estimator = new ClockRatioEstimator(inputClock, outputClock);
        var controller = new ClockRatioController(inputRate, outputRate, timestampFrequency);
        int inputBlock = inputRate / 200, outputBlock = outputRate / 200;
        var source = new float[inputBlock * inputChannels];
        var converted = new float[512 * outputChannels]; var rendered = new float[512 * outputChannels];
        ulong writtenHash = 14695981039346656037, readHash = writtenHash;
        int queued = 0, minimum = 1024, maximum = 1024;
        float peak = 0;
        for (int chunk = 0; chunk < 2; chunk++) { pipe.Produce(generation, converted); Hash(ref writtenHash, converted); queued += 512; }
        double inputActualRate = inputRate * (1 + (125 + driftPpm) * 1e-6), outputActualRate = outputRate * 1.000125;
        long inputOrdinal = 1, outputOrdinal = 1, inputFramePosition = 0;
        double nextInput = inputBlock / inputActualRate, nextOutput = outputBlock / outputActualRate;
        double sumLast = 0, countLast = 0, stableFor = 0, converged = -1;
        uint random = 20261114;
        var started = Stopwatch.GetTimestamp();
        for (int tick = 1; tick <= seconds * 10; tick++)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            double nowSeconds = tick / 10d, lastEvent = (tick - 1) / 10d, area = 0;
            bool overflow = false, underrun = false;
            while (Math.Min(nextInput, nextOutput) <= nowSeconds)
            {
                double eventTime = Math.Min(nextInput, nextOutput);
                area += (eventTime - lastEvent) * queued; lastEvent = eventTime;
                if (nextInput <= nextOutput)
                {
                    for (int frame = 0; frame < inputBlock; frame++)
                    {
                        source[frame * inputChannels] = (float)(.1 * Math.Sin(2 * Math.PI * 997 * inputFramePosition / inputRate));
                        if (inputChannels == 2) source[frame * 2 + 1] = (float)(.075 * Math.Sin(2 * Math.PI * 443 * inputFramePosition / inputRate));
                        inputFramePosition++;
                    }
                    var produced = stage.Process(generation, source, converted);
                    Assert.Equal(inputBlock, produced.ConsumedFrames);
                    int accepted = pipe.Produce(generation, converted.AsSpan(0, produced.ProducedFrames * outputChannels));
                    Hash(ref writtenHash, converted.AsSpan(0, accepted * outputChannels));
                    queued += accepted; overflow |= accepted < produced.ProducedFrames;
                    nextInput = ++inputOrdinal * inputBlock / inputActualRate + Jitter();
                }
                else
                {
                    int read = pipe.Render(generation, rendered.AsSpan(0, outputBlock * outputChannels));
                    Hash(ref readHash, rendered.AsSpan(0, read * outputChannels));
                    queued -= read; underrun |= read < outputBlock;
                    nextOutput = ++outputOrdinal * outputBlock / outputActualRate + Jitter();
                }
                minimum = Math.Min(minimum, queued); maximum = Math.Max(maximum, queued);
            }
            area += (nowSeconds - lastEvent) * queued;
            double mean = area / .1;
            long now = tick * 100000L, inputTime = now - (long)(Jitter() * timestampFrequency), outputTime = now - (long)(Jitter() * timestampFrequency);
            estimator.Observe(new(inputClock, (long)Math.Floor(inputTime * inputActualRate / timestampFrequency), inputTime),
                new(outputClock, (long)Math.Floor(outputTime * outputActualRate / timestampFrequency), outputTime), now);
            if (controlled)
            {
                var control = controller.Update(now, overflow ? 2048 : underrun ? 0 : (int)Math.Round(mean), estimator.Latest);
                if (control.State == DriftControlState.ReprimeRequired || estimator.RequiresReset)
                {
                    stage.Stop(generation); pipe.Stop();
                    Assert.Fail("Trajetória regular exigiu repriming; interrompida em vez de entregar a última razão.");
                }
                Assert.True(stage.SetTargetPpm(generation, control.CorrectionPpm));
                double error = ((double)outputRate / inputRate * (1 + stage.CurrentPpm * 1e-6) / (outputActualRate / inputActualRate) - 1) * 1e6;
                if (Math.Abs(control.FilteredOccupancy - 1024) <= 64 && Math.Abs(error) <= 30 && control.State == DriftControlState.Tracking) stableFor += .1;
                else stableFor = 0;
                if (stableFor >= 20 && converged < 0) converged = nowSeconds;
            }
            if (nowSeconds > seconds - 5) { sumLast += mean; countLast++; }
        }
        var snapshot = pipe.Snapshot(); var buffer = snapshot.Buffer!.Value;
        Assert.Equal(queued, buffer.QueuedFrames);
        Assert.Equal(stage.OutputFrames + 1024, buffer.ReadFrames + buffer.OverrunFrames + buffer.QueuedFrames);
        long drained = 0;
        while (queued > 0)
        {
            int frames = Math.Min(512, queued);
            int read = pipe.Render(generation, rendered.AsSpan(0, frames * outputChannels));
            Hash(ref readHash, rendered.AsSpan(0, read * outputChannels)); queued -= read; drained += read;
        }
        Assert.Equal(writtenHash, readHash); // Amostras finitas reais e uma FIFO sujeita a retorno circular, descarte e underrun.
        Assert.Equal(0, stage.Protection.InputNonFinite + stage.Protection.OutputNonFinite + stage.Protection.OutputClipped);
        return new(inputRate, outputRate, driftPpm, seconds, controlled, stage.InputFrames, stage.MappedFrames, stage.OutputFrames,
            1024, buffer.ReadFrames, drained, buffer.OverrunFrames, buffer.UnderrunFrames, buffer.QueuedFrames, minimum, maximum,
            sumLast / countLast, converged, ((double)outputRate / inputRate * (1 + stage.CurrentPpm * 1e-6) / (outputActualRate / inputActualRate) - 1) * 1e6,
            peak, Stopwatch.GetElapsedTime(started).TotalMilliseconds, stage.StorageBytes + snapshot.StorageBytes);

        double Jitter() { random = unchecked(random * 1664525 + 1013904223); return (random >> 8) / 16777216d * .0001; }
        void Hash(ref ulong hash, ReadOnlySpan<float> samples)
        {
            foreach (float sample in samples)
            {
                if (!float.IsFinite(sample) || Math.Abs(sample) > 1) throw new InvalidOperationException("Amostra inválida na integração sintética.");
                peak = Math.Max(peak, Math.Abs(sample));
                hash = unchecked((hash ^ (uint)BitConverter.SingleToInt32Bits(sample)) * 1099511628211);
            }
        }
    }
}
