using System.Diagnostics;
using WaveStudio.Audio;
using WaveStudio.Desktop;
using WaveStudio.Desktop.Settings;
using Xunit;

namespace WaveStudio.Tests;

public sealed class MeterVisibilityTests
{
    private sealed class Backend : IAudioWatchBackend
    {
        public string? NotificationError => null;
        public Func<double> Read = () => .5;
        public int Opens;
        public int Closed;
        public int Disposals;
        public IAudioPeakSource OpenPeak(string endpointId)
        {
            Interlocked.Increment(ref Opens);
            return new Source(this);
        }
        private sealed class Source(Backend owner) : IAudioPeakSource
        {
            public double ReadPeak() => owner.Read();
            public void Dispose() => Interlocked.Increment(ref owner.Closed);
        }
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }

    [Fact]
    public async Task PauseAndResumeDiscardInFlightReadingAndReuseExistingPeakSource()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend();
        int reads = 0;
        var token = TestContext.Current.CancellationToken;
        backend.Read = () =>
        {
            if (Interlocked.Increment(ref reads) != 1) return .2;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5), token)) throw new TimeoutException();
            return .9;
        };
        await using var worker = new AudioWatchWorker(_ => backend);
        worker.Select(null, "input");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            worker.EnableMetering(false);
            Assert.Null(worker.Latest!.Capture.Scalar);
            Assert.Contains("pausada", worker.Latest.NotificationStatus);
            worker.EnableMetering(true);
            Assert.Null(worker.Latest!.Capture.Scalar);
            release.Set();
            var clock = Stopwatch.StartNew();
            while (worker.Latest?.Capture.Scalar is null)
            {
                if (clock.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException();
                await Task.Delay(5, token);
            }
            Assert.Equal(.2, worker.Latest.Capture.Scalar);
            Assert.Equal(1, backend.Opens);
            await worker.DisposeAsync();
            Assert.Equal(1, backend.Closed);
            Assert.Equal(1, backend.Disposals);
        }
        finally { release.Set(); }
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public Task MainWindowMeasuresBothAudioTabsAndPausesOtherTabsAndMinimizedWindow()
    {
        return PreferencesUiTests.OnSta(async () =>
        {
            using var directory = new TemporarySettingsDirectory();
            var backend = new Backend();
            var microphoneBackend = new MicrophoneTestTests.FakeBackend();
            int workers = 0;
            var worker = new AudioWatchWorker(_ => { Interlocked.Increment(ref workers); return backend; });
            using var form = new MainForm(new SettingsStore(directory.FilePath), audioWatch: worker,
                microphoneSession: new MicrophoneTestSession(microphoneBackend)) { ShowInTaskbar = false, Opacity = 0 };
            form.Show();
            try
            {
                await PreferencesUiTests.PumpUntilAsync(() => form.StartupTask.IsCompleted);
                var controls = Descendants(form).ToArray();
                var tabs = form.Controls[0].Controls.OfType<TabControl>().Single();
                var microphone = controls.OfType<MicrophoneTestPanel>().Single();
                foreach (var (page, minimized, enabled) in new[]
                {
                    ("Dispositivos", false, true), ("Testar microfone", false, true),
                    ("Mesa de som", false, true), ("Configurações", false, false),
                    ("Testar microfone", true, false), ("Testar microfone", false, true)
                })
                {
                    tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(tab => tab.Text == page);
                    form.WindowState = minimized ? FormWindowState.Minimized : FormWindowState.Normal;
                    try { await PreferencesUiTests.PumpUntilAsync(() => worker.MeteringEnabled == enabled); }
                    catch (TimeoutException error)
                    {
                        throw new TimeoutException($"Aba={page}; minimizar={minimized}; estado={form.WindowState}; selecionada={tabs.SelectedTab?.Text}; medindo={worker.MeteringEnabled}; esperado={enabled}.", error);
                    }
                    Assert.Equal(page == "Testar microfone" && !minimized, microphone.InputMeter.MeteringEnabled);
                    Assert.Equal(enabled && page != "Mesa de som", worker.CaptureMeteringEnabled);
                    if (!enabled)
                    {
                        Assert.Equal(0, microphone.InputMeter.PeakBar.Value);
                        Assert.Contains("pausada", microphone.InputMeter.ReadingText);
                    }
                    Assert.Empty(microphoneBackend.Calls);
                    Assert.Equal(20, microphone.TestLevel.Value);
                }
            }
            finally { form.Close(); await PreferencesUiTests.PumpUntilAsync(() => form.IsDisposed); }
            Assert.False(worker.MeteringEnabled);
            Assert.Equal(1, workers);
            Assert.Equal(backend.Opens, backend.Closed);
            Assert.Equal(1, backend.Disposals);
            Assert.Empty(microphoneBackend.Calls);
        });
    }

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

}
