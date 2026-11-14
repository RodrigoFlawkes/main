using System.Diagnostics;
using WaveStudio.Audio;
using WaveStudio.Desktop;
using Xunit;

namespace WaveStudio.Tests;

public sealed class MicrophoneMeterTests
{
    private static readonly AudioWatchSelection Target = new("output", "input", 1);
    private static AudioWatchSnapshot Snapshot(PeakReading peak, AudioWatchSelection? target = null, long? at = null) =>
        new(target ?? Target, PeakReading.FromScalar(.9), peak, "ativa", false, 0, RecordedAt: at ?? Stopwatch.GetTimestamp());

    private static Task OnSta(Action<MicrophoneInputMeter> test)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var meter = new MicrophoneInputMeter();
                meter.BindSelection(Target, "input");
                meter.SetMeteringEnabled(true);
                test(meter);
                done.TrySetResult();
            }
            catch (Exception error) { done.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(.375, 38)]
    [InlineData(1, 100)]
    public Task RealCapturePeakUsesZeroToHundredRangeAndNumericText(double scalar, int expected) => OnSta(meter =>
    {
        meter.ShowSnapshot(Snapshot(PeakReading.FromScalar(scalar)), Stopwatch.GetTimestamp());
        Assert.True(meter.PeakBar.Enabled);
        Assert.Equal(100, meter.PeakBar.Maximum);
        Assert.Equal(expected, meter.PeakBar.Value);
        Assert.Contains("%", meter.ReadingText);
        if (scalar == 0) Assert.Contains("nenhum pico", meter.ReadingText);
        else Assert.Contains((scalar * 100).ToString("F1"), meter.ReadingText);
        Assert.Equal(meter.ReadingText, meter.PeakBar.AccessibleDescription);
    });

    [Fact]
    public Task UnavailableIsDistinctFromAnActualZeroReading() => OnSta(meter =>
    {
        meter.ShowSnapshot(Snapshot(PeakReading.Unavailable), Stopwatch.GetTimestamp());
        Assert.False(meter.PeakBar.Enabled);
        Assert.Equal(0, meter.PeakBar.Value);
        Assert.Contains("indisponível", meter.ReadingText);
        meter.ShowSnapshot(Snapshot(PeakReading.FromScalar(0)), Stopwatch.GetTimestamp());
        Assert.True(meter.PeakBar.Enabled);
        Assert.Contains("0%", meter.ReadingText);
    });

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-.1)]
    [InlineData(1.1)]
    public Task InvalidValuesNeverReachTheProgressBar(double scalar) => OnSta(meter =>
    {
        meter.ShowSnapshot(Snapshot(new(scalar, null)), Stopwatch.GetTimestamp());
        Assert.False(meter.PeakBar.Enabled);
        Assert.Equal(0, meter.PeakBar.Value);
        Assert.Contains("inválido", meter.ReadingText);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ExpiredOrUntimestampedReadingsAreDelayedNotSilence(bool untimestamped) => OnSta(meter =>
    {
        long now = Stopwatch.GetTimestamp();
        var snapshot = Snapshot(PeakReading.FromScalar(.8), at: untimestamped ? 0 : now);
        meter.ShowSnapshot(snapshot, now + Stopwatch.Frequency * 3);
        Assert.False(meter.PeakBar.Enabled);
        Assert.Equal(0, meter.PeakBar.Value);
        Assert.Contains("atrasada", meter.ReadingText);
        meter.ShowSnapshot(Snapshot(PeakReading.FromScalar(0)), Stopwatch.GetTimestamp());
        Assert.True(meter.PeakBar.Enabled);
    });

    [Theory]
    [InlineData("input", 0)]
    [InlineData("other-input", 1)]
    public Task PreviousGenerationOrOtherEndpointCannotSupplyTheBar(string input, long revision) => OnSta(meter =>
    {
        meter.ShowSnapshot(Snapshot(PeakReading.FromScalar(.8), new("output", input, revision)), Stopwatch.GetTimestamp());
        Assert.False(meter.PeakBar.Enabled);
        Assert.Contains("seleção atual", meter.ReadingText);
        meter.ShowSnapshot(Snapshot(PeakReading.FromScalar(.2)), Stopwatch.GetTimestamp());
        Assert.Equal(20, meter.PeakBar.Value);
    });

    [Fact]
    public Task DeviceChangeClearsImmediatelyBeforeAnyFreshSnapshot() => OnSta(meter =>
    {
        var previous = Snapshot(PeakReading.FromScalar(.8));
        meter.ShowSnapshot(previous, Stopwatch.GetTimestamp());
        meter.BindSelection(new("output", "new-input", 2), "new-input");
        Assert.False(meter.PeakBar.Enabled);
        Assert.Equal(0, meter.PeakBar.Value);
        meter.ShowSnapshot(previous, Stopwatch.GetTimestamp());
        Assert.Equal(0, meter.PeakBar.Value);
        meter.ShowSnapshot(Snapshot(PeakReading.FromScalar(.4), new("output", "new-input", 2)), Stopwatch.GetTimestamp());
        Assert.Equal(40, meter.PeakBar.Value);
    });

    [Fact]
    public Task LifecycleInvalidationBlocksSameIdUntilNewBindingAndHandlesDisconnect() => OnSta(meter =>
    {
        var previous = Snapshot(PeakReading.FromScalar(.8));
        meter.ShowSnapshot(previous, Stopwatch.GetTimestamp());
        var notification = new Thread(meter.InvalidateSelection);
        notification.Start();
        notification.Join();
        meter.RefreshInvalidation();
        Assert.Equal(0, meter.PeakBar.Value);
        meter.ShowSnapshot(previous, Stopwatch.GetTimestamp());
        Assert.False(meter.PeakBar.Enabled);
        meter.BindSelection(Target with { Revision = 2 }, "input");
        meter.ShowSnapshot(previous, Stopwatch.GetTimestamp());
        Assert.Equal(0, meter.PeakBar.Value);
        meter.ShowSnapshot(Snapshot(PeakReading.FromScalar(.3), Target with { Revision = 2 }), Stopwatch.GetTimestamp());
        Assert.Equal(30, meter.PeakBar.Value);
        meter.BindSelection(new("output", null, 3), null);
        Assert.False(meter.PeakBar.Enabled);
        Assert.Contains("Microfone selecionado indisponível", meter.ReadingText);
    });

    [Fact]
    public Task PausingAndStoppingClearTheValueAndRejectUpdates() => OnSta(meter =>
    {
        var snapshot = Snapshot(PeakReading.FromScalar(.8));
        meter.ShowSnapshot(snapshot, Stopwatch.GetTimestamp());
        meter.SetMeteringEnabled(false);
        Assert.Equal(0, meter.PeakBar.Value);
        meter.ShowSnapshot(snapshot, Stopwatch.GetTimestamp());
        Assert.False(meter.PeakBar.Enabled);
        Assert.Contains("pausada", meter.ReadingText);
        meter.SetMeteringEnabled(true);
        meter.ShowSnapshot(snapshot, Stopwatch.GetTimestamp());
        Assert.Equal(80, meter.PeakBar.Value);
        meter.Stop();
        meter.ShowSnapshot(snapshot, Stopwatch.GetTimestamp());
        Assert.Equal(0, meter.PeakBar.Value);
        Assert.False(meter.MeteringEnabled);
        Assert.Contains("encerrada", meter.ReadingText);
    });

    [Fact]
    public Task WorkerFailureAndDriverErrorsRemainReadableWithoutFalseSilence() => OnSta(meter =>
    {
        meter.ShowSnapshot(Snapshot(new(null, "Leitura não exposta pelo dispositivo.")), Stopwatch.GetTimestamp());
        Assert.False(meter.PeakBar.Enabled);
        Assert.Contains("não exposta", meter.ReadingText);
        meter.ShowSnapshot(Snapshot(PeakReading.FromScalar(.8)) with { WorkerFailed = true, NotificationStatus = "Serviço parado. Reinicie." }, Stopwatch.GetTimestamp());
        Assert.False(meter.PeakBar.Enabled);
        Assert.Contains("Reinicie", meter.ReadingText);
    });
}
