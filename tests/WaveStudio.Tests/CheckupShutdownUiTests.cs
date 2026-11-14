using WaveStudio.Audio;
using WaveStudio.Desktop;
using WaveStudio.Desktop.Settings;
using Xunit;
using static WaveStudio.Tests.PreferencesUiTests;

namespace WaveStudio.Tests;

public sealed class CheckupShutdownUiTests
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public Task CloseWithStopFailureOrTimeoutStaysOpenAndCanRetryAfterCompletion() => OnSta(async () =>
    {
        using var directory = new TemporarySettingsDirectory(); var backend = new MicrophoneTestTests.FakeBackend();
        var session = new MicrophoneTestSession(backend, TimeSpan.FromMilliseconds(80));
        using var form = new MainForm(new SettingsStore(directory.FilePath), _ => { }, microphoneSession: session,
            workAreas: () => [new WindowArea(0, 0, 1920, 1080)],
            shutdownTimeout: TimeSpan.FromSeconds(2)) { ShowInTaskbar = false, Opacity = 0 };
        form.Show(); await Ready(form);
        await session.SelectAsync("input", "output"); await session.RecordAsync(); backend.Capture.Emit(400);
        backend.Capture.StopError = new InvalidOperationException("parada simulada");
        try
        {
            form.Close(); await PumpUntilAsync(() => form.CloseAttemptTask.IsCompleted);
            Assert.False(form.IsDisposed); Assert.True(form.ClosePending); Assert.True(form.Enabled);
            Assert.Contains("microfone", form.CloseDiagnostic); Assert.Contains("Encerramento não confirmado", form.CloseDiagnostic);
            Assert.True(session.Latest.ResourcesRetained); Assert.Equal(0, backend.Capture.Disposals);
            await session.RecordAsync(); Assert.Equal(1, backend.Capture.Starts);
        }
        finally
        {
            backend.Capture.StopError = null; backend.Capture.Completed = true;
            form.Close(); await PumpUntilAsync(() => form.IsDisposed);
            await session.DisposeAsync();
        }
        Assert.Equal(1, backend.Capture.Disposals);
    });

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public Task StorageWarningThrowDoesNotEscapeAsyncClosingOrSkipIndependentCleanup() => OnSta(async () =>
    {
        using var directory = new TemporarySettingsDirectory(); var writer = new FailingWriter();
        var backend = new MicrophoneTestTests.FakeBackend(); var session = new MicrophoneTestSession(backend);
        bool throwWarning = true;
        using var form = new MainForm(new SettingsStore(directory.FilePath, writer), _ =>
        { if (throwWarning) throw new InvalidOperationException("aviso simulado"); }, microphoneSession: session,
            workAreas: () => [new WindowArea(0, 0, 1920, 1080)],
            shutdownTimeout: TimeSpan.FromSeconds(2)) { ShowInTaskbar = false, Opacity = 0 };
        form.Show(); await Ready(form);
        form.Close(); await PumpUntilAsync(() => form.CloseAttemptTask.IsCompleted);
        Assert.False(form.IsDisposed); Assert.Contains("preferências", form.CloseDiagnostic);
        Assert.Equal(MicrophoneTestState.Disposed, session.Latest.State);
        Assert.False(File.Exists(directory.FilePath));
        throwWarning = false; writer.Fail = false;
        form.Close(); await PumpUntilAsync(() => form.IsDisposed); await session.DisposeAsync();
    });

    private static async Task Ready(MainForm form)
    {
        await PumpUntilAsync(() => Descendants(form).OfType<Label>().Any(label =>
            label.AccessibleName?.StartsWith("Estado do armazenamento de preferências", StringComparison.Ordinal) == true && label.Text.Length > 0));
        await form.StartupTask; await form.RefreshTask;
    }
    private sealed class FailingWriter : ISettingsWriter
    {
        private readonly SettingsWriter writer = new();
        public bool Fail = true;
        public void WriteNew(string path, byte[] bytes, CancellationToken token)
        { if (Fail) throw new IOException("escrita simulada"); writer.WriteNew(path, bytes, token); }
        public void Commit(string temporary, string target, bool replacing, string? backup) => writer.Commit(temporary, target, replacing, backup);
    }
}
