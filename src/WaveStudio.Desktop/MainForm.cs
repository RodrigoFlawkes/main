using WaveStudio.Audio;

namespace WaveStudio.Desktop;

internal sealed class MainForm : Form
{
    private readonly CoreAudioDeviceService audio = new();
    private readonly DeviceSelection selection = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly DevicePanel playback = new("Dispositivo de reprodução / headset");
    private readonly DevicePanel capture = new("Dispositivo de captura / microfone");
    private readonly Button refresh = new() { Text = "&Atualizar dispositivos", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private bool refreshing;
    private bool closing;
    private bool stopping;
    private Task refreshTask = Task.CompletedTask;

    public MainForm()
    {
        Text = "Wave Studio — v0.1.0 Alpha em desenvolvimento";
        MinimumSize = new Size(820, 760);
        ClientSize = new Size(1080, 860);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading = new Label { Text = "Wave Studio", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold) };
        var version = new Label { Text = "v0.1.0 Alpha em desenvolvimento", AutoSize = true, Margin = new Padding(3, 4, 3, 16) };
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 2);
        layout.Controls.Add(version, 0, 1);
        layout.SetColumnSpan(version, 2);
        layout.Controls.Add(playback, 0, 2);
        layout.Controls.Add(capture, 1, 2);
        layout.Controls.Add(refresh, 0, 3);
        layout.Controls.Add(status, 0, 4);
        layout.SetColumnSpan(status, 2);
        Controls.Add(layout);
        playback.DeviceSelected += selection.Select;
        capture.DeviceSelected += selection.Select;
        refresh.Click += (_, _) => refreshTask = RefreshDevicesAsync();
        Shown += (_, _) => refreshTask = RefreshDevicesAsync();
        FormClosing += async (_, args) =>
        {
            if (closing)
                return;
            args.Cancel = true;
            if (stopping)
                return;
            stopping = true;
            Enabled = false;
            lifetime.Cancel();
            await refreshTask;
            await Task.WhenAll(playback.StopAsync().AsTask(), capture.StopAsync().AsTask());
            closing = true;
            Close();
        };
        FormClosed += (_, _) => lifetime.Dispose();
    }

    private async Task RefreshDevicesAsync()
    {
        if (refreshing)
            return;
        refreshing = true;
        refresh.Enabled = false;
        playback.Enabled = false;
        capture.Enabled = false;
        status.Text = "Consultando dispositivos de áudio…";
        try
        {
            var catalog = await audio.QueryAsync(lifetime.Token);
            if (IsDisposed || lifetime.IsCancellationRequested)
                return;
            // Resolve both preferences before binding triggers selection events.
            var selectedPlayback = selection.Resolve(catalog, EndpointDirection.Playback);
            var selectedCapture = selection.Resolve(catalog, EndpointDirection.Capture);
            playback.ShowDevices(catalog, EndpointDirection.Playback, selectedPlayback);
            capture.ShowDevices(catalog, EndpointDirection.Capture, selectedCapture);
            status.Text = catalog.Warnings.Count > 0
                ? string.Join(Environment.NewLine, catalog.Warnings.Distinct())
                : $"Consulta concluída — {catalog.Devices.Count} endpoint(s) encontrado(s).";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (IsDisposed || lifetime.IsCancellationRequested)
                return;
            string message = AudioError.Describe(error);
            playback.ShowUnavailable(message);
            capture.ShowUnavailable(message);
            status.Text = message;
        }
        finally
        {
            refreshing = false;
            if (!IsDisposed)
            {
                refresh.Enabled = true;
                playback.Enabled = true;
                capture.Enabled = true;
            }
        }
    }
}
