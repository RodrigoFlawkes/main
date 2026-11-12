using WaveStudio.Audio;
using Microsoft.Win32;

namespace WaveStudio.Desktop;

internal sealed class MainForm : Form
{
    private readonly CoreAudioDeviceService audio = new();
    private readonly DeviceSelection selection = new();
    private readonly ApplicationMixerPanel mixer = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly DevicePanel playback = new("Dispositivo de reprodução / headset");
    private readonly DevicePanel capture = new("Dispositivo de captura / microfone");
    private readonly Button refresh = new() { Text = "&Atualizar dispositivos", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label watchStatus = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly CheckBox followPlayback = new() { AutoSize = true, Checked = true, Text = "Seguir padrão multimídia de reprodução" };
    private readonly CheckBox followCapture = new() { AutoSize = true, Checked = true, Text = "Seguir padrão multimídia de captura" };
    private readonly AudioWatchWorker watch = new();
    private readonly System.Windows.Forms.Timer peakTimer = new() { Interval = 100 };
    private AudioWatchSelection watchSelection = new(null, null, 0);
    private DeviceSnapshot? currentPlayback;
    private DeviceSnapshot? currentCapture;
    private bool bindingFollow;
    private bool refreshRequested;
    private bool resetRequested;
    private bool refreshing;
    private bool closing;
    private bool stopping;
    private Task refreshTask = Task.CompletedTask;

    public MainForm()
    {
        Text = "Wave Studio — v0.1.0 Alpha em desenvolvimento";
        MinimumSize = new Size(600, 400);
        ClientSize = new Size(1080, 860);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading = new Label { Text = "Wave Studio", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold) };
        var version = new Label { Text = "v0.1.0 Alpha em desenvolvimento", AutoSize = true, Margin = new Padding(3, 4, 3, 16) };
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 2);
        layout.Controls.Add(version, 0, 1);
        layout.SetColumnSpan(version, 2);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var devicesPage = new TabPage("Dispositivos") { AutoScroll = true };
        var devicesLayout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 700, MinimumSize = new Size(780, 700), ColumnCount = 2 };
        devicesLayout.RowCount = 2;
        devicesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        devicesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        devicesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        devicesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        devicesLayout.Controls.Add(followPlayback, 0, 0);
        devicesLayout.Controls.Add(followCapture, 1, 0);
        devicesLayout.Controls.Add(playback, 0, 1);
        devicesLayout.Controls.Add(capture, 1, 1);
        devicesPage.Controls.Add(devicesLayout);
        var mixerPage = new TabPage("Mixer de aplicativos");
        mixerPage.Controls.Add(mixer);
        tabs.TabPages.Add(devicesPage);
        tabs.TabPages.Add(mixerPage);
        layout.Controls.Add(tabs, 0, 2);
        layout.SetColumnSpan(tabs, 2);
        layout.Controls.Add(refresh, 0, 3);
        layout.Controls.Add(status, 0, 4);
        layout.SetColumnSpan(status, 2);
        layout.Controls.Add(watchStatus, 0, 5);
        layout.SetColumnSpan(watchStatus, 2);
        Controls.Add(layout);
        playback.DeviceSelected += device => SelectExplicit(device);
        capture.DeviceSelected += device => SelectExplicit(device);
        followPlayback.CheckedChanged += (_, _) => ChangeFollowMode(EndpointDirection.Playback, followPlayback);
        followCapture.CheckedChanged += (_, _) => ChangeFollowMode(EndpointDirection.Capture, followCapture);
        refresh.Click += (_, _) => RequestRefresh(true);
        Shown += (_, _) => { RequestRefresh(true); peakTimer.Start(); };
        watch.CatalogChanged += OnCatalogChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        peakTimer.Tick += (_, _) =>
        {
            if (watch.Latest is not { } snapshot) return;
            watchStatus.Text = snapshot.NotificationStatus;
            if (snapshot.Selection != watchSelection) return;
            playback.ShowPeak(snapshot.Playback);
            capture.ShowPeak(snapshot.Capture);
        };
        tabs.SelectedIndexChanged += (_, _) => watch.EnableMetering(tabs.SelectedTab == devicesPage && WindowState != FormWindowState.Minimized);
        Resize += (_, _) => watch.EnableMetering(tabs.SelectedTab == devicesPage && WindowState != FormWindowState.Minimized);
        Shown += (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(600, area.Width), Math.Min(400, area.Height));
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        };
        FormClosing += async (_, args) =>
        {
            if (closing)
                return;
            args.Cancel = true;
            if (stopping)
                return;
            stopping = true;
            peakTimer.Stop();
            watch.CatalogChanged -= OnCatalogChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            Enabled = false;
            lifetime.Cancel();
            await refreshTask;
            await Task.WhenAll(playback.StopAsync().AsTask(), capture.StopAsync().AsTask(), mixer.StopAsync().AsTask(), watch.DisposeAsync().AsTask());
            closing = true;
            Close();
        };
        FormClosed += (_, _) => { lifetime.Dispose(); peakTimer.Dispose(); };
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Resume) watch.Recover();
    }

    private void OnCatalogChanged()
    {
        try
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(() => RequestRefresh(false)));
        }
        catch (InvalidOperationException) { /* Closing may destroy the handle before dispatch. */ }
    }

    private void SelectExplicit(DeviceSnapshot device)
    {
        selection.Select(device);
        bindingFollow = true;
        try
        {
            if (device.Direction == EndpointDirection.Playback) followPlayback.Checked = false;
            else followCapture.Checked = false;
        }
        finally { bindingFollow = false; }
        if (device.Direction == EndpointDirection.Playback)
        {
            currentPlayback = device;
            mixer.SelectDevice(device);
        }
        else currentCapture = device;
        UpdateWatchSelection();
    }

    private void ChangeFollowMode(EndpointDirection direction, CheckBox check)
    {
        if (bindingFollow) return;
        if (check.Checked) selection.FollowDefault(direction);
        else
        {
            var current = direction == EndpointDirection.Playback ? currentPlayback : currentCapture;
            if (current is not null) selection.Select(current);
            else
            {
                bindingFollow = true;
                check.Checked = true;
                bindingFollow = false;
            }
        }
        RequestRefresh(true);
    }

    private void UpdateWatchSelection()
    {
        watchSelection = watch.Select(currentPlayback?.State == EndpointState.Active ? currentPlayback.Id : null,
            currentCapture?.State == EndpointState.Active ? currentCapture.Id : null);
        playback.ShowPeak(PeakReading.Unavailable);
        capture.ShowPeak(PeakReading.Unavailable);
    }

    private void RequestRefresh(bool resetControls)
    {
        if (stopping) return;
        refreshRequested = true;
        resetRequested |= resetControls;
        if (!refreshing) refreshTask = DrainRefreshAsync();
    }

    private async Task DrainRefreshAsync()
    {
        while (refreshRequested && !stopping)
        {
            refreshRequested = false;
            bool reset = resetRequested;
            resetRequested = false;
            await RefreshDevicesAsync(reset);
        }
    }

    private async Task RefreshDevicesAsync(bool resetControls)
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
            bool playbackChanged = currentPlayback?.Id != selectedPlayback?.Id || currentPlayback?.State != selectedPlayback?.State;
            currentPlayback = selectedPlayback;
            currentCapture = selectedCapture;
            playback.ShowDevices(catalog, EndpointDirection.Playback, selectedPlayback, resetControls);
            capture.ShowDevices(catalog, EndpointDirection.Capture, selectedCapture, resetControls);
            if (resetControls || playbackChanged) mixer.SelectDevice(selectedPlayback);
            UpdateWatchSelection();
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
            mixer.SelectDevice(null);
            capture.ShowUnavailable(message);
            currentPlayback = currentCapture = null;
            UpdateWatchSelection();
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
