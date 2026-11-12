using System.Diagnostics;
using WaveStudio.Audio;
using WaveStudio.Desktop;
using Xunit;

namespace WaveStudio.Tests;

public sealed class MicrophoneTestUiTests
{
    private sealed class TestForm : Form { protected override bool ShowWithoutActivation => true; }
    private static DeviceSnapshot Device(string id, EndpointDirection direction) =>
        new(id, id, direction, EndpointState.Active, false, false, new(48000, 1, 32, "IeeeFloat"), null);

    private static Task OnSta(Action<MicrophoneTestPanel, MicrophoneTestTests.FakeBackend> test)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var backend = new MicrophoneTestTests.FakeBackend();
                using var form = new TestForm { ClientSize = new Size(850, 700), ShowInTaskbar = false, Opacity = 0 };
                using var panel = new MicrophoneTestPanel(new MicrophoneTestSession(backend));
                form.Controls.Add(panel);
                form.Show();
                try
                {
                    panel.SelectDevices(Device("input", EndpointDirection.Capture), Device("output", EndpointDirection.Playback));
                    Pump(() => panel.RecordButton.Enabled);
                    test(panel, backend);
                }
                finally
                {
                    var close = panel.StopAsync().AsTask();
                    Pump(() => close.IsCompleted);
                    close.GetAwaiter().GetResult();
                }
                done.TrySetResult();
            }
            catch (Exception error) { done.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static void Pump(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("O painel não terminou a operação simulada.");
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Application.DoEvents();
    }
    private static Button Button(Control root, string text) => root.Controls.OfType<Button>().FirstOrDefault(x => x.Text == text)
        ?? root.Controls.Cast<Control>().Select(child => Find(child, text)).First(x => x is not null)!;
    private static Button? Find(Control root, string text) => root is Button button && button.Text == text ? button
        : root.Controls.Cast<Control>().Select(child => Find(child, text)).FirstOrDefault(x => x is not null);

    [Fact]
    public Task OpeningAndSelectingKeepCaptureOffAndPlaybackAtTwentyPercent() => OnSta((panel, backend) =>
    {
        Assert.Empty(backend.Calls);
        Assert.Equal(20, panel.TestLevel.Value);
        Assert.False(panel.PlayButton.Enabled);
        Assert.Equal(MicrophoneTestState.Idle, panel.Snapshot.State);
    });

    [Fact]
    public Task ButtonsRecordStopPlayAndClearOnlyOnExplicitClicks() => OnSta((panel, backend) =>
    {
        panel.RecordButton.PerformClick();
        Pump(() => panel.Snapshot.State == MicrophoneTestState.Recording && Button(panel, "&Parar").Enabled);
        Assert.False(panel.PlayButton.Enabled);
        Assert.False(panel.TestLevel.Enabled);
        backend.Capture.Emit(400);
        Button(panel, "&Parar").PerformClick();
        Pump(() => panel.PlayButton.Enabled);
        Assert.Equal(0, backend.Playback.Starts);
        panel.PlayButton.PerformClick();
        Pump(() => panel.Snapshot.State == MicrophoneTestState.Playing && Button(panel, "Parar &reprodução").Enabled);
        Assert.False(panel.RecordButton.Enabled);
        Button(panel, "Parar &reprodução").PerformClick();
        Pump(() => panel.PlayButton.Enabled);
        Button(panel, "&Limpar teste").PerformClick();
        Pump(() => panel.Snapshot.State == MicrophoneTestState.Idle && panel.RecordButton.Enabled);
        Assert.False(panel.PlayButton.Enabled);
        Assert.Equal(TimeSpan.Zero, panel.Snapshot.RecordedDuration);
    });

    [Fact]
    public Task DeviceChangeDiscardsCaptureWithoutStartingAnotherStream() => OnSta((panel, backend) =>
    {
        panel.RecordButton.PerformClick();
        Pump(() => panel.Snapshot.State == MicrophoneTestState.Recording);
        backend.Capture.Emit(400);
        panel.SelectDevices(Device("other-input", EndpointDirection.Capture), Device("output", EndpointDirection.Playback));
        Pump(() => panel.Snapshot.State == MicrophoneTestState.Idle && panel.RecordButton.Enabled);
        Assert.Single(backend.Calls);
        Assert.Equal(1, backend.Capture.Disposals);
        Assert.False(panel.PlayButton.Enabled);
    });
}
