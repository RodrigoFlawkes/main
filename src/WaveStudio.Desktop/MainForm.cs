using WaveStudio.Audio;
using Microsoft.Win32;
using System.Diagnostics;
using WaveStudio.Desktop.Settings;

namespace WaveStudio.Desktop;

internal sealed partial class MainForm : Form
{
    private readonly CoreAudioDeviceService audio = new();
    private readonly DeviceSelection selection = new();
    private readonly ApplicationMixerPanel mixer = new();
    private readonly MicrophoneTestPanel microphoneTest;
    private readonly ExternalRouteReadinessPanel routeReadiness;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DevicePanel playback = new(StudioStrings.Get("MainForm.DispositivoDeReproducaoHeadset"));
    private readonly DevicePanel capture = new(StudioStrings.Get("MainForm.DispositivoDeCapturaMicrofone"));
    private readonly Button refresh = new() { Text = StudioStrings.Get("MainForm.AtualizarDispositivos"), AutoSize = true };
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label watchStatus = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly CheckBox followPlayback = new() { AutoSize = true, Checked = true, Text = StudioStrings.Get("MainForm.SeguirPadraoMultimidiaDeReproducao") };
    private readonly CheckBox followCapture = new() { AutoSize = true, Checked = true, Text = StudioStrings.Get("MainForm.SeguirPadraoMultimidiaDeCaptura") };
    private readonly AudioWatchWorker watch;
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
    private readonly Icon brandIcon = StudioBrand.CreateIcon();
    private readonly Bitmap brandLogo = StudioBrand.CreateLogo();

    public MainForm(SettingsStore? settingsStore = null, Action<string>? storageWarning = null,
        AudioWatchWorker? audioWatch = null, MicrophoneTestSession? microphoneSession = null,
        Func<WindowArea[]>? workAreas = null, TimeSpan? shutdownTimeout = null,
        SettingsLoadResult? initialSettings = null, System.Globalization.CultureInfo? windowsUiCulture = null)
    {
        this.initialSettings = initialSettings;
        this.windowsUiCulture = windowsUiCulture ?? System.Globalization.CultureInfo.CurrentUICulture;
        closeTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(5);
        if (closeTimeout <= TimeSpan.Zero || closeTimeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
        watch = audioWatch ?? new AudioWatchWorker();
        microphoneTest = new MicrophoneTestPanel(microphoneSession);
        routeReadiness = new ExternalRouteReadinessPanel(new ExternalRouteReadinessService());
        store = settingsStore ?? new SettingsStore(SettingsStore.DefaultPath);
        InitializeEqualizerProfiles();
        readWorkAreas = workAreas ?? (() => Screen.AllScreens.Select(screen => screen.WorkingArea)
            .Select(area => new WindowArea(area.X, area.Y, area.Width, area.Height)).ToArray());
        showStorageWarning = storageWarning ?? (message => MessageBox.Show(this, message, StudioStrings.Get("MainForm.PreferenciasLocais"), MessageBoxButtons.OK, MessageBoxIcon.Warning));
        showBalanceWarning = storageWarning ?? (message => MessageBox.Show(this, message, StudioStrings.Get("Balance.Title"), MessageBoxButtons.OK, MessageBoxIcon.Warning));
        Text = StudioStrings.Get("MainForm.WaveStudioV010");
        Icon = brandIcon;
        ShowIcon = true;
        MinimumSize = new Size(600, 400);
        ClientSize = new Size(1080, 860);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 7
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading = new Label { Text = "Wave Studio", AutoSize = true, Font = new Font(Font.FontFamily, 20, FontStyle.Bold) };
        var version = new Label { Text = StudioStrings.Get("MainForm.V010AlphaEm"), AutoSize = true, Margin = new Padding(3, 4, 3, 16) };
        var header = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        header.Controls.Add(new PictureBox { Image = brandLogo, SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(64, 64), Margin = new Padding(0, 0, 12, 0), AccessibleName = StudioStrings.Get("MainForm.LogoOficialDoWaveStudio"), TabStop = false });
        heading.Margin = new Padding(0, 12, 0, 0);
        header.Controls.Add(heading);
        layout.Controls.Add(header, 0, 0);
        layout.SetColumnSpan(header, 2);
        layout.Controls.Add(version, 0, 1);
        layout.SetColumnSpan(version, 2);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var devicesPage = new TabPage(StudioStrings.Get("MainForm.Dispositivos")) { AutoScroll = true };
        var devicesLayout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 820, MinimumSize = new Size(780, 820), ColumnCount = 2 };
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
        var mixerPage = new TabPage(StudioStrings.Get("Mixer.Tab"));
        mixerPage.Controls.Add(mixer);
        tabs.TabPages.Add(devicesPage);
        tabs.TabPages.Add(mixerPage);
        var microphoneTestPage = new TabPage(StudioStrings.Get("MainForm.TestarMicrofone"));
        microphoneTestPage.Controls.Add(microphoneTest);
        tabs.TabPages.Add(microphoneTestPage);
        var routePage = new TabPage(ExternalRouteText.Tab) { AccessibleDescription = ExternalRouteText.Title };
        routePage.Controls.Add(routeReadiness);
        tabs.TabPages.Add(routePage);
        tabs.TabPages.Add(CreatePreferencesPage());
        layout.Controls.Add(tabs, 0, 2);
        layout.SetColumnSpan(tabs, 2);
        layout.Controls.Add(refresh, 0, 3);
        layout.Controls.Add(status, 0, 4);
        layout.SetColumnSpan(status, 2);
        layout.Controls.Add(watchStatus, 0, 5);
        layout.SetColumnSpan(watchStatus, 2);
        layout.Controls.Add(storageStatus, 0, 6);
        layout.SetColumnSpan(storageStatus, 2);
        Controls.Add(layout);
        EnablePreferenceSelections(false);
        playback.DeviceSelected += device => SelectExplicit(device);
        capture.DeviceSelected += device => SelectExplicit(device);
        followPlayback.CheckedChanged += (_, _) => ChangeFollowMode(EndpointDirection.Playback, followPlayback);
        followCapture.CheckedChanged += (_, _) => ChangeFollowMode(EndpointDirection.Capture, followCapture);
        refresh.Click += (_, _) => { _ = microphoneTest.InvalidateAsync(); watch.Recover(); RequestRefresh(true); };
        playback.SavePreferenceRequested += () => RunPreferenceAction(() => SaveEndpointPreferenceAsync(EndpointDirection.Playback, playback));
        capture.SavePreferenceRequested += () => RunPreferenceAction(() => SaveEndpointPreferenceAsync(EndpointDirection.Capture, capture));
        playback.RestorePreferenceRequested += () => RunPreferenceAction(() => RestoreEndpointPreferenceAsync(EndpointDirection.Playback, playback));
        capture.RestorePreferenceRequested += () => RunPreferenceAction(() => RestoreEndpointPreferenceAsync(EndpointDirection.Capture, capture));
        Shown += async (_, _) =>
        {
            startupTask = InitializePreferencesAsync();
            await startupTask;
            if (!stopping) { UpdateMetering(); RequestRefresh(true); peakTimer.Start(); }
        };
        watch.CatalogChanged += OnCatalogChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        peakTimer.Tick += (_, _) =>
        {
            long now = Stopwatch.GetTimestamp();
            var snapshot = watch.Latest?.ForDisplay(now);
            microphoneTest.ShowInputPeak(snapshot, now);
            if (snapshot is null) return;
            watchStatus.Text = snapshot.NotificationStatus;
            if (snapshot.Selection != watchSelection) return;
            playback.ShowPeak(snapshot.Playback);
            capture.ShowPeak(snapshot.Capture);
            if (tabs.SelectedTab == mixerPage) mixer.ShowEndpointPeak(snapshot, now);
        };
        void UpdateMetering()
        {
            bool visible = !stopping && WindowState != FormWindowState.Minimized;
            watch.EnableMetering(visible && (tabs.SelectedTab == devicesPage || tabs.SelectedTab == microphoneTestPage || tabs.SelectedTab == mixerPage),
                includeCapture: tabs.SelectedTab != mixerPage);
            microphoneTest.EnableInputMeter(visible && tabs.SelectedTab == microphoneTestPage);
            if (!stopping && (!visible || tabs.SelectedTab != mixerPage)) mixer.RequestBalanceExit();
        }
        tabs.SelectedIndexChanged += (_, _) =>
        {
            bool showRoutes = tabs.SelectedTab == routePage;
            routeReadiness.SetActive(showRoutes);
            refresh.Visible = !showRoutes;
            UpdateMetering();
        };
        Resize += (_, _) => UpdateMetering();
        UpdateMetering();
        closeSteps = [
            new(StudioStrings.Get("MainForm.Microfone"), async () => await microphoneTest.StopAsync()),
            new(StudioStrings.Get("MainForm.Inicializacao"), () => startupTask),
            new(StudioStrings.Get("MainForm.Preferencias"), async () => { await startupTask; await SaveWindowOnCloseAsync(); }),
            new(StudioStrings.Get("MainForm.Catalogo"), () => refreshTask),
            new(StudioStrings.Get("MainForm.RoteamentoExterno"), routeReadiness.StopAsync),
            new(StudioStrings.Get("MainForm.Reproducao"), async () => await playback.StopAsync()),
            new(StudioStrings.Get("General.CaptureLower"), async () => await capture.StopAsync()),
            new(StudioStrings.Get("MainForm.Mixer"), async () => await mixer.StopAsync()),
            new(StudioStrings.Get("MainForm.Medidores"), async () => await watch.DisposeAsync())
        ];
        FormClosing += async (_, args) =>
        {
            if (closing)
                return;
            args.Cancel = true;
            if (closeAttemptRunning)
                return;
            closeAttemptRunning = true;
            try
            {
                if (!stopping)
                {
                    stopping = true;
                    UpdateMetering();
                    peakTimer.Stop();
                    watch.CatalogChanged -= OnCatalogChanged;
                    SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                    // Mantém a barra de título e o botão X disponíveis para nova tentativa, impedindo comandos conflitantes.
                    foreach (Control child in Controls) child.Enabled = false;
                    lifetime.Cancel();
                }
                closeAttemptTask = FinishCloseAttemptAsync();
                await closeAttemptTask;
            }
            catch (Exception error) { ReportClosePending(error); }
            finally { closeAttemptRunning = false; }
        };
        FormClosed += (_, _) => { lifetime.Dispose(); peakTimer.Dispose(); equalizerSaveTimer.Dispose(); };
    }

    protected override void Dispose(bool disposing)
    {
        try { base.Dispose(disposing); }
        finally { if (disposing) { brandIcon.Dispose(); brandLogo.Dispose(); } }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode is PowerModes.Suspend or PowerModes.Resume) _ = microphoneTest.InvalidateAsync();
        if (args.Mode == PowerModes.Resume) watch.Recover();
    }

    private void OnCatalogChanged(bool resetControls)
    {
        if (resetControls) _ = microphoneTest.InvalidateAsync();
        try
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(() =>
                {
                    if (stopping) return;
                    routeReadiness.InvalidateInventory();
                    RequestRefresh(resetControls);
                }));
        }
        catch (InvalidOperationException) { /* O fechamento pode destruir o handle antes do despacho. */ }
    }

    private void SelectExplicit(DeviceSnapshot device)
    {
        if (!settingsLoaded || stopping || recoveringPreferences) return;
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
        SaveSelectionPreferences();
        ShowSavedPreferences();
    }

    private void ChangeFollowMode(EndpointDirection direction, CheckBox check)
    {
        if (!settingsLoaded || stopping || bindingFollow || recoveringPreferences) return;
        _ = microphoneTest.InvalidateAsync();
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
        SaveSelectionPreferences();
    }

    private void UpdateWatchSelection(bool invalidateTest = false)
    {
        microphoneTest.SelectDevices(currentCapture, currentPlayback, invalidateTest);
        watchSelection = watch.Select(currentPlayback?.State == EndpointState.Active ? currentPlayback.Id : null,
            currentCapture?.State == EndpointState.Active ? currentCapture.Id : null);
        microphoneTest.BindInputMeter(watchSelection);
        playback.ShowPeak(PeakReading.Unavailable);
        capture.ShowPeak(PeakReading.Unavailable);
    }

    private void RequestRefresh(bool resetControls)
    {
        if (stopping || !settingsLoaded) return;
        refreshRequested = true;
        resetRequested |= resetControls;
        if (!refreshing)
        {
            refreshing = true;
            refreshTask = DrainRefreshAsync();
        }
    }

    private async Task DrainRefreshAsync()
    {
        try
        {
            while (refreshRequested && !stopping)
            {
                refreshRequested = false;
                bool reset = resetRequested;
                resetRequested = false;
                await RefreshDevicesAsync(reset);
            }
        }
        finally { refreshing = false; }
    }

    private async Task RefreshDevicesAsync(bool resetControls)
    {
        refresh.Enabled = false;
        playback.Enabled = false;
        capture.Enabled = false;
        status.Text = StudioStrings.Get("MainForm.ConsultandoDispositivosDeAudio");
        try
        {
            var catalog = await audio.QueryAsync(lifetime.Token);
            if (IsDisposed || lifetime.IsCancellationRequested)
                return;
            // Resolve as duas preferências antes que a vinculação dispare eventos de seleção.
            var selectedPlayback = selection.Resolve(catalog, EndpointDirection.Playback);
            var selectedCapture = selection.Resolve(catalog, EndpointDirection.Capture);
            bool playbackChanged = currentPlayback?.Id != selectedPlayback?.Id || currentPlayback?.State != selectedPlayback?.State;
            currentPlayback = selectedPlayback;
            currentCapture = selectedCapture;
            playback.ShowDevices(catalog, EndpointDirection.Playback, selectedPlayback, resetControls, selection.PlaybackId);
            capture.ShowDevices(catalog, EndpointDirection.Capture, selectedCapture, resetControls, selection.CaptureId);
            if (resetControls || playbackChanged) mixer.SelectDevice(selectedPlayback);
            UpdateWatchSelection(resetControls);
            ShowSavedPreferences();
            var warnings = catalog.Warnings.ToList();
            AddFallbackWarning(warnings, selectedPlayback, EndpointDirection.Playback, selection.PlaybackId);
            AddFallbackWarning(warnings, selectedCapture, EndpointDirection.Capture, selection.CaptureId);
            status.Text = warnings.Count > 0 ? string.Join(Environment.NewLine, warnings.Distinct())
                : StudioStrings.Format("MainForm.ConsultaConcluida0EndpointS", catalog.Devices.Count);
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
            if (!IsDisposed)
            {
                refresh.Enabled = !recoveringPreferences;
                playback.Enabled = !recoveringPreferences;
                capture.Enabled = !recoveringPreferences;
                followPlayback.Enabled = followCapture.Enabled = !recoveringPreferences;
            }
        }
    }
}
