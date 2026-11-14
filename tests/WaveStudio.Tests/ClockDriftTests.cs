using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class ClockDriftTests(ITestOutputHelper output)
{
    private const long Frequency = 1000000;
    private static AudioClock Clock(string id, int rate = 48000, long generation = 1) => new(id, generation, rate, "simulation-us", Frequency);

    [Theory]
    [InlineData(0, 2)] // identity
    [InlineData(1, 2)] // generation
    [InlineData(2, 3)] // repeated timestamp
    [InlineData(3, 3)] // reversed timestamp
    [InlineData(4, 4)] // reversed position
    [InlineData(5, 5)] // stale
    [InlineData(6, 7)] // position jump, including extreme long
    [InlineData(7, 4)] // negative position
    [InlineData(8, 3)] // clock quality
    [InlineData(9, 3)] // future timestamp
    [InlineData(10, 6)] // discontinuity
    public void InvalidObservationsCannotCreateAnEstimate(int scenario, int expected)
    {
        var input = Clock("input"); var render = Clock("output");
        var estimator = new ClockRatioEstimator(input, render);
        Assert.Equal(ClockObservationResult.Waiting, estimator.Observe(new(input, 48000, Frequency), new(render, 48000, Frequency), Frequency));
        var capture = new ClockObservation(input, 52800, 1100000);
        long now = 1100000;
        capture = scenario switch
        {
            0 => capture with { Clock = Clock("other") },
            1 => capture with { Clock = Clock("input", generation: 2) },
            2 => capture with { Timestamp = Frequency },
            3 => capture with { Timestamp = Frequency - 1 },
            4 => capture with { FramePosition = 47999 },
            6 => capture with { FramePosition = long.MaxValue },
            7 => capture with { FramePosition = -1 },
            8 => capture with { Quality = ClockQuality.Estimated },
            9 => capture with { Timestamp = now + 1 },
            10 => capture with { Discontinuity = true },
            _ => capture
        };
        if (scenario == 5) now = 2100000;
        Assert.Equal((ClockObservationResult)expected, estimator.Observe(capture, new(render, 52800, 1100000), now));
        Assert.Null(estimator.Latest);
    }

    [Fact]
    public void RateEstimateUsesDeltasAndCommonReferenceAndResetIsExplicit()
    {
        var input = Clock("input", 44100); var render = Clock("output");
        var estimator = new ClockRatioEstimator(input, render);
        long offset = long.MaxValue - 10000000;
        estimator.Observe(new(input, offset, offset), new(render, offset - 100000, offset), offset);
        for (int index = 1; index <= 10; index++)
            estimator.Observe(new(input, offset + 4410L * index, offset + 100000L * index),
                new(render, offset - 100000 + 4800L * index, offset + 100000L * index), offset + 100000L * index);
        Assert.Equal(48000d / 44100, estimator.Latest!.Value.OutputPerInput, 12);
        Assert.Equal(ClockObservationResult.Discontinuity, estimator.Observe(new(input, 0, offset + 4000000),
            new(render, 0, offset + 4000000, Discontinuity: true), offset + 4000000));
        Assert.Null(estimator.Latest);
        Assert.Throws<ArgumentException>(() => new ClockRatioEstimator(input, new("different", 1, 48000, "other-domain", Frequency)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioClock("x", 0, 48000, "qpc", Frequency));
        Assert.Throws<ArgumentException>(() => new AudioClock("", 1, 48000, "qpc", Frequency));
    }

    [Fact]
    public void CommonTimeRegressionGapAndQueueBoundariesRequireRecovery()
    {
        var input = Clock("input"); var render = Clock("render");
        var estimator = new ClockRatioEstimator(input, render);
        estimator.Observe(new(input, 0, 1000000), new(render, 0, 1000000), 1400000);
        Assert.Equal(ClockObservationResult.InvalidTime,
            estimator.Observe(new(input, 4800, 1100000), new(render, 4800, 1100000), 1300000));
        Assert.Equal(ClockObservationResult.Discontinuity,
            estimator.Observe(new(input, 144000, 4000000), new(render, 144000, 4000000), 4000000));
        Assert.Null(estimator.Latest);
        var controller = new ClockRatioController(48000, 48000, Frequency);
        Assert.Equal(DriftControlState.ReprimeRequired, controller.Update(100000, 0, new(1, 100000)).State);
        controller.Reset(100000);
        Assert.Equal(DriftControlState.ReprimeRequired, controller.Update(200000, 2048, new(1, 200000)).State);
        controller.Reset(200000);
        Assert.Equal(DriftControlState.ReprimeRequired, controller.Update(200000, 1024, new(1, 200000)).State);
    }

    [Theory]
    [InlineData(-300)]
    [InlineData(300)]
    public void ControllerSignSlewAndWaitingAreExplicit(int producerPpm)
    {
        var controller = new ClockRatioController(48000, 48000, Frequency);
        Assert.Equal(DriftControlState.Waiting, controller.Update(100000, 1024, null).State);
        double prior = 0;
        DriftControl result = default;
        for (int tick = 2; tick <= 300; tick++)
        {
            long now = tick * 100000L;
            result = controller.Update(now, 1024, new(1 / (1 + producerPpm * 1e-6), now));
            Assert.InRange(Math.Abs(result.CorrectionPpm - prior), 0, 2.5000001);
            Assert.InRange(result.CorrectionPpm, -500, 500);
            prior = result.CorrectionPpm;
        }
        Assert.Equal(-Math.Sign(producerPpm), Math.Sign(result.CorrectionPpm));
        Assert.InRange(Math.Abs(result.OutputPerInput - 1 / (1 + producerPpm * 1e-6)), 0, 1e-10);
    }

    [Fact]
    public void SaturationUnwindsAndFailureRequiresExplicitReset()
    {
        var controller = new ClockRatioController(48000, 48000, Frequency);
        DriftControl state = default;
        for (int tick = 1; tick <= 1000; tick++)
        {
            long now = tick * 100000L;
            state = controller.Update(now, 1800, new(1.002, now));
        }
        Assert.Equal(DriftControlState.Saturated, state.State);
        Assert.InRange(state.IntegralPpm, -500, 500);
        double initialIntegral = state.IntegralPpm;
        for (int tick = 1001; tick <= 3000; tick++)
        {
            long now = tick * 100000L;
            state = controller.Update(now, 248, new(1, now));
            Assert.InRange(state.CorrectionPpm, -500, 500);
        }
        Assert.True(state.IntegralPpm > initialIntegral, "Erro oposto deve desfazer windup.");
        Assert.Equal(DriftControlState.ReprimeRequired, controller.Update(300100000, 1024, new(1, 296100000)).State);
        Assert.Equal(DriftControlState.ReprimeRequired, controller.Update(300200000, 1024, new(1, 300200000)).State);
        controller.Reset(300200000);
        Assert.Equal(DriftControlState.Tracking, controller.Update(300300000, 1024, new(1, 300300000)).State);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Update(304300000, 2049, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClockRatioController(48000, 48000, Frequency, limitPpm: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClockRatioController(48000, 48000, Frequency, limitPpm: 1001));
    }

    [Fact]
    public void DeterministicIndependentClocksCompareControlAndNoControl()
    {
        var records = new List<object>();
        var results = new List<(DriftSimulation.Result Enabled, DriftSimulation.Result Disabled)>();
        foreach (var rates in new[] { (48000, 48000), (44100, 48000), (48000, 44100) })
        foreach (int drift in new[] { -300, -100, -50, 50, 100, 300 })
        {
            var enabled = DriftSimulation.Run(rates.Item1, rates.Item2, drift, true);
            var disabled = DriftSimulation.Run(rates.Item1, rates.Item2, drift, false);
            records.Add(new { rates.Item1, rates.Item2, DriftPpm = drift, Enabled = enabled, Disabled = disabled });
            results.Add((enabled, disabled));
        }
        SaveReport("simulation", records);
        foreach (var (enabled, disabled) in results)
        {
            Assert.Equal(0, enabled.Dropped + enabled.Underrun);
            Assert.True(disabled.Dropped + disabled.Underrun > 0, "A duração deve atravessar o orçamento sem controle.");
            Assert.InRange(enabled.Minimum, 1, 2047);
            Assert.InRange(enabled.Maximum, 1, 2047);
            Assert.InRange(Math.Abs(enabled.LastMinuteMean - 1024), 0, 128);
            Assert.InRange(Math.Abs(enabled.FinalRatioErrorPpm), 0, 30);
            Assert.InRange(enabled.ConvergedSeconds, 0, 300);
        }
        Assert.Equal(DriftSimulation.Run(44100, 48000, 100, true), DriftSimulation.Run(44100, 48000, 100, true));
    }

    [Fact]
    public void OutOfBudgetAndPauseAreAccountedAndRecoverWithNewGeneration()
    {
        var result = DriftSimulation.Run(48000, 48000, 1500, true, restartAt: 300);
        Assert.True(result.SaturatedUpdates > 0);
        Assert.True(result.Dropped + result.Underrun > 0);
        Assert.Equal(1, result.Resets);
        Assert.True(result.DiscardedForReset >= 0);
        Assert.Equal(2048, result.Primed);
        Assert.InRange(Math.Abs(result.FinalRatioErrorPpm), 0, 30);
        Assert.InRange(Math.Abs(result.LastMinuteMean - 1024), 0, 128);
        Assert.Equal(0, result.RecoveryDropped + result.RecoveryUnderrun);
        Assert.InRange(result.ConvergedSeconds, 0, 300);
        SaveReport("recovery", result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-250)]
    public void CommonRateOffsetsDoNotBecomeRelativeDrift(int commonPpm)
    {
        var result = DriftSimulation.Run(44100, 48000, -100, true, commonPpm: commonPpm);
        Assert.Equal(0, result.Dropped + result.Underrun);
        Assert.InRange(Math.Abs(result.FinalRatioErrorPpm), 0, 30);
        Assert.Equal(0, result.RecoveryDropped + result.RecoveryUnderrun);
        Assert.InRange(Math.Abs(result.LastMinuteMean - 1024), 0, 128);
    }

    [Fact]
    public void InvalidClockBurstIsRejectedAndRequiresExplicitRepriming()
    {
        var result = DriftSimulation.Run(48000, 48000, 100, true, restartAt: 154, invalidClockBurst: true);
        Assert.True(result.RejectedObservations >= 40);
        Assert.True(result.ReprimeUpdates > 0);
        Assert.Equal(1, result.Resets);
        Assert.Equal(2048, result.Primed);
        Assert.InRange(Math.Abs(result.FinalRatioErrorPpm), 0, 30);
        SaveReport("invalid-clock-recovery", result);
    }

    [Fact]
    public void ClockControlHasNoSteadyStateAllocationAndMeasuresCost()
    {
        var input = Clock("in"); var render = Clock("out");
        var estimator = new ClockRatioEstimator(input, render);
        var controller = new ClockRatioController(48000, 48000, Frequency);
        void Tick(int tick)
        {
            long now = tick * 100000L;
            estimator.Observe(new(input, tick * 4800L, now), new(render, tick * 4800L, now), now);
            controller.Update(now, 1024, estimator.Latest);
        }
        for (int tick = 1; tick <= 1000; tick++) Tick(tick);
        long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        for (int tick = 1001; tick <= 101000; tick++) Tick(tick);
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        SaveReport("cost", new { Updates = 100000, WallMilliseconds = milliseconds, AllocatedBytes = allocated });
    }

    private void SaveReport(string name, object value)
    {
        string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(json);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WaveStudio.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string directory = Path.Combine(root.FullName, "artifacts", "clock-drift");
        Directory.CreateDirectory(directory);
        string config = typeof(ClockDriftTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        File.WriteAllText(Path.Combine(directory, $"{name}-{config}.json"), json);
    }
}

// Event-driven integer frame accounting, deliberately no PCM buffers/resampling.
internal static class DriftSimulation
{
    internal readonly record struct Result(long Produced, long Consumed, long Dropped, long Underrun, int FinalOccupancy,
        int Minimum, int Maximum, double LastMinuteMean, double FinalRatioErrorPpm, double ConvergedSeconds,
        long SaturatedUpdates, int Resets, long DiscardedForReset, int Primed, long RejectedObservations, long ReprimeUpdates,
        long RecoveryDropped, long RecoveryUnderrun);

    public static Result Run(int inputRate, int outputRate, double driftPpm, bool controlled, double restartAt = -1,
        double commonPpm = 125, bool invalidClockBurst = false)
    {
        const int capacity = 2048, target = 1024;
        const double duration = 900;
        const long scale = 1L << 32, frequency = 1000000;
        var input = new AudioClock("input", 1, inputRate, "simulation-us", frequency);
        var render = new AudioClock("output", 1, outputRate, "simulation-us", frequency);
        var estimator = new ClockRatioEstimator(input, render);
        var controller = new ClockRatioController(inputRate, outputRate, frequency);
        int inputBlock = inputRate / 200, outputBlock = outputRate / 200;
        double actualInput = inputRate * (1 + (commonPpm + driftPpm) * 1e-6), actualOutput = outputRate * (1 + commonPpm * 1e-6);
        double ratio = (double)outputRate / inputRate, timeOrigin = 0;
        double nextInput = inputBlock / actualInput, nextOutput = outputBlock / actualOutput;
        long inputOrdinal = 1, outputOrdinal = 1, fractional = 0, produced = 0, consumed = 0, dropped = 0, underrun = 0, saturated = 0;
        int occupancy = target, minimum = target, maximum = target, primed = target, resets = 0;
        long discarded = 0;
        long rejected = 0, reprime = 0;
        long droppedAtReset = 0, underrunAtReset = 0;
        double lastSum = 0, lastCount = 0, converged = -1, stableFor = 0;
        uint random = 20261114;
        DriftControl state = default;
        for (int tick = 1; tick <= duration * 10; tick++)
        {
            double nowSeconds = tick / 10d;
            if (restartAt > 0 && resets == 0 && nowSeconds > restartAt)
            {
                // Explicit pause: stop both callbacks, invalidate old generation, discard queue,
                // pause 300 ms, then re-prime. Paused frames are not fabricated as captured audio.
                discarded += occupancy; occupancy = target; primed += target; resets++;
                droppedAtReset = dropped; underrunAtReset = underrun; converged = -1; stableFor = 0;
                fractional = 0; timeOrigin = restartAt + .3;
                input = new("input", 2, inputRate, "simulation-us", frequency);
                render = new("output", 2, outputRate, "simulation-us", frequency);
                estimator = new(input, render);
                controller.Reset((long)(timeOrigin * frequency));
                driftPpm = 100; actualInput = inputRate * (1 + (commonPpm + driftPpm) * 1e-6);
                ratio = (double)outputRate / inputRate;
                inputOrdinal = outputOrdinal = 1;
                nextInput = timeOrigin + inputBlock / actualInput;
                nextOutput = timeOrigin + outputBlock / actualOutput;
            }
            if (nowSeconds <= timeOrigin) continue;
            double intervalStart = Math.Max(timeOrigin, (tick - 1) / 10d), lastEvent = intervalStart, occupancyArea = 0;
            bool hitOverflow = false, hitUnderrun = false;
            while (Math.Min(nextInput, nextOutput) <= nowSeconds)
            {
                double eventTime = Math.Min(nextInput, nextOutput);
                occupancyArea += (eventTime - lastEvent) * occupancy;
                lastEvent = eventTime;
                if (nextInput <= nextOutput)
                {
                    long accumulator = fractional + inputBlock * (long)Math.Round(ratio * scale);
                    int generated = (int)(accumulator / scale); fractional = accumulator % scale;
                    int accepted = Math.Min(generated, capacity - occupancy);
                    hitOverflow |= accepted != generated;
                    produced += generated; dropped += generated - accepted; occupancy += accepted;
                    inputOrdinal++;
                    nextInput = timeOrigin + inputOrdinal * inputBlock / actualInput + Jitter();
                }
                else
                {
                    int read = Math.Min(outputBlock, occupancy);
                    hitUnderrun |= read != outputBlock;
                    consumed += read; underrun += outputBlock - read; occupancy -= read;
                    outputOrdinal++;
                    nextOutput = timeOrigin + outputOrdinal * outputBlock / actualOutput + Jitter();
                }
                minimum = Math.Min(minimum, occupancy); maximum = Math.Max(maximum, occupancy);
            }
            occupancyArea += (nowSeconds - lastEvent) * occupancy;
            double meanOccupancy = occupancyArea / (nowSeconds - intervalStart);
            long now = (long)Math.Round(nowSeconds * frequency);
            // Independently delayed observations of hardware position in the same monotonic domain.
            long inputTime = now - (long)(Jitter() * frequency), outputTime = now - (long)(Jitter() * frequency);
            long inputPosition = 123456789 + (long)Math.Floor((inputTime / (double)frequency - timeOrigin) * actualInput);
            long outputPosition = 987654321 + (long)Math.Floor((outputTime / (double)frequency - timeOrigin) * actualOutput);
            if (invalidClockBurst && nowSeconds is >= 150 and <= 154) inputTime = now + 1;
            var observed = estimator.Observe(new(input, inputPosition, inputTime), new(render, outputPosition, outputTime), now);
            if (observed is not (ClockObservationResult.Waiting or ClockObservationResult.Updated)) rejected++;
            if (controlled)
            {
                // Time-weighted mean avoids aliasing a 5 ms callback sawtooth at 100 ms.
                // Boundary failures still report the actual boundary, never an averaged success.
                int controlOccupancy = hitOverflow ? capacity : hitUnderrun ? 0 : (int)Math.Round(meanOccupancy);
                state = controller.Update(now, controlOccupancy, estimator.Latest);
                ratio = state.OutputPerInput;
                if (state.State is DriftControlState.Saturated or DriftControlState.ReprimeRequired) saturated++;
                if (state.State == DriftControlState.ReprimeRequired) reprime++;
                double ppmError = (ratio / (actualOutput / actualInput) - 1) * 1e6;
                if (Math.Abs(state.FilteredOccupancy - target) <= 64 && Math.Abs(ppmError) <= 30 && state.State == DriftControlState.Tracking)
                    stableFor += .1;
                else stableFor = 0;
                if (stableFor >= 20 && converged < 0) converged = nowSeconds - timeOrigin;
            }
            if (nowSeconds > duration - 60) { lastSum += meanOccupancy; lastCount++; }
        }
        // Every produced/primed frame has exactly one destination, including reset discards.
        Assert.Equal(produced + primed, consumed + dropped + discarded + occupancy);
        return new(produced, consumed, dropped, underrun, occupancy, minimum, maximum, lastSum / lastCount,
            (ratio / (actualOutput / actualInput) - 1) * 1e6, converged, saturated, resets, discarded, primed, rejected, reprime,
            resets > 0 ? dropped - droppedAtReset : 0, resets > 0 ? underrun - underrunAtReset : 0);

        double Jitter()
        {
            random = unchecked(random * 1664525 + 1013904223);
            return (random >> 8) / 16777216d * .0001;
        }
    }
}
