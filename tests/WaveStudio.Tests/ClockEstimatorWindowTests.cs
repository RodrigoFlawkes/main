using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class ClockEstimatorWindowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlausibleShortDeltasCannotCommitAnImplausibleLongWindow(bool priorEstimate)
    {
        var input = new AudioClock("input", 1, 8000, "us", 1000000);
        var output = new AudioClock("output", 1, 8000, "us", 1000000);
        var estimator = new ClockRatioEstimator(input, output);
        estimator.Observe(new(input, 0, 0), new(output, 0, 0), 0);
        long origin = 0, position = 0;
        if (priorEstimate)
        {
            for (int tick = 1; tick <= 10; tick++)
                estimator.Observe(new(input, tick * 800, tick * 100000), new(output, tick * 800, tick * 100000), tick * 100000);
            Assert.NotNull(estimator.Latest);
            origin = 1000000; position = 8000;
        }
        for (int tick = 1; tick <= 9; tick++)
            Assert.Equal(ClockObservationResult.Waiting, estimator.Observe(new(input, position + tick * 805, origin + tick * 100000),
                new(output, position + tick * 800, origin + tick * 100000), origin + tick * 100000));
        Assert.Equal(ClockObservationResult.Implausible, estimator.Observe(new(input, position + 8050, origin + 1000000),
            new(output, position + 8000, origin + 1000000), origin + 1000000));
        Assert.True(estimator.RequiresReset);
        Assert.Null(estimator.Latest);
        Assert.Equal(ClockObservationResult.Discontinuity, estimator.Observe(new(input, position + 8800, origin + 1100000),
            new(output, position + 8800, origin + 1100000), origin + 1100000));
        estimator.Reset();
        Assert.False(estimator.RequiresReset);
        estimator.Observe(new(input, 0, origin + 2000000), new(output, 0, origin + 2000000), origin + 2000000);
        Assert.Equal(ClockObservationResult.Updated, estimator.Observe(new(input, 8000, origin + 3000000),
            new(output, 8000, origin + 3000000), origin + 3000000));
        Assert.Equal(1, estimator.Latest!.Value.OutputPerInput);
    }
}
