using WaveStudio.Audio;
using WaveStudio.Desktop.Settings;

namespace WaveStudio.Desktop;

internal sealed partial class MainForm
{
    private readonly SettingsStore store;
    private readonly Action<string> showStorageWarning;
    private readonly Func<WindowArea[]> readWorkAreas;
    private bool windowPlacementAvailable;
    private StudioSettings settings = new();
    private readonly Label storageStatus = new() { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(1000, 0) };
    private readonly Label preferencesSummary = new() { AutoSize = true, MaximumSize = new Size(800, 0) };
    private readonly Button resetPreferences = new() { AutoSize = true, Text = StudioStrings.Get("MainForm.Preferences.RedefinirPreferenciasLocais"), Enabled = false };
    private readonly Button recoverPreferences = new() { AutoSize = true, Text = StudioStrings.Get("MainForm.Preferences.RecuperarBackup"), Enabled = false };
    private Task startupTask = Task.CompletedTask;
    private Task settingsTask = Task.CompletedTask;
    private Task preferenceTask = Task.CompletedTask;
    private bool settingsLoaded;
    private bool settingsSaveRequested;
    private bool savingSettings;
    private bool preferenceOperation;
    private bool recoveringPreferences;
    private string? lastSaveError;
    internal Task StartupTask => startupTask;

    private TabPage CreatePreferencesPage()
    {
        var page = new TabPage(StudioStrings.Get("Settings.Tab")) { AutoScroll = true };
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(12)
        };
        storageStatus.AccessibleName = StudioStrings.Get("MainForm.Preferences.EstadoDoArmazenamentoDePreferencias");
        var settingsPath = new TextBox { ReadOnly = true, Text = store.FilePath, AccessibleName = StudioStrings.Get("MainForm.Preferences.ArquivoDePreferenciasLocais") };
        layout.Controls.Add(settingsPath);
        layout.Controls.Add(preferencesSummary);
        layout.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(800, 0),
            Text = StudioStrings.Get("MainForm.Preferences.SelecoesSaoSalvasAposSua")
        });
        layout.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(800, 0),
            Text = StudioStrings.Get("MainForm.Preferences.PreferenciasDeSessoesIndisponiveisIdentidade")
        });
        AddLanguageControls(layout);
        layout.Controls.Add(recoverPreferences);
        layout.Controls.Add(resetPreferences);
        page.Controls.Add(layout);
        page.ClientSizeChanged += (_, _) => FitPreferencesWidth();
        FitPreferencesWidth();
        resetPreferences.Click += (_, _) => RunPreferenceAction(() => RecoverOrResetAsync(useBackup: false));
        recoverPreferences.Click += (_, _) => RunPreferenceAction(() => RecoverOrResetAsync(useBackup: true));
        return page;

        void FitPreferencesWidth()
        {
            // Reserva espaço para a rolagem vertical e permite quebrar o texto na largura visível.
            int available = page.ClientSize.Width - layout.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth;
            if (available <= 0) return;
            layout.SuspendLayout();
            try
            {
                foreach (var label in layout.Controls.OfType<Label>())
                    label.MaximumSize = new Size(Math.Clamp(available - label.Margin.Horizontal, 1, 800), 0);
                settingsPath.Width = Math.Clamp(available - settingsPath.Margin.Horizontal, 1, 720);
                language.Width = Math.Clamp(available - language.Margin.Horizontal, 1, 260);
            }
            finally { layout.ResumeLayout(); }
        }
    }

    private async void RunPreferenceAction(Func<Task> action)
    {
        if (stopping || !preferenceTask.IsCompleted) return;
        preferenceTask = action();
        try { await preferenceTask; }
        catch (Exception error)
        {
            if (!stopping && !IsDisposed) ShowStorageStatus(StudioStrings.Get("MainForm.Preferences.FalhaNaOperacaoDePreferencias") + error.GetType().Name);
        }
    }

    private async Task InitializePreferencesAsync()
    {
        try
        {
            var loaded = initialSettings ?? await store.LoadAsync(lifetime.Token);
            if (stopping) return;
            settings = loaded.Settings;
            settingsLoaded = true;
            ShowLanguagePreference();
            ApplySavedSelections();
            LoadEqualizerPreferences();
            windowPlacementAvailable = ApplyWindowPlacement();
            ShowStorageStatus(loaded.Message + (windowPlacementAvailable ? "" : StudioStrings.Get("MainForm.Preferences.AreaUtilDaTelaIndisponivel")));
            EnablePreferenceSelections(true);
        }
        catch (OperationCanceledException) when (stopping) { }
    }

    private void ApplySavedSelections()
    {
        selection.SetPreference(EndpointDirection.Playback, settings.Playback.EndpointId);
        selection.SetPreference(EndpointDirection.Capture, settings.Capture.EndpointId);
        bindingFollow = true;
        try
        {
            followPlayback.Checked = settings.Playback.FollowDefault;
            followCapture.Checked = settings.Capture.FollowDefault;
        }
        finally { bindingFollow = false; }
    }

    private bool ApplyWindowPlacement()
    {
        // Transições de tela ou sessão remota podem expor áreas de trabalho com dimensão zero.
        // Ignora monitores inválidos. Se nenhum puder ser usado, preserva os limites e o posicionamento salvo.
        var areas = readWorkAreas().Where(area => area.Width > 0 && area.Height > 0).ToArray();
        if (areas.Length == 0) return false;
        var initial = settings.Window ?? new WindowPlacement(Left, Top, Width, Height);
        var placement = initial.Fit(areas);
        MinimumSize = new Size(Math.Min(600, placement.Width), Math.Min(400, placement.Height));
        SetBounds(placement.X, placement.Y, placement.Width, placement.Height);
        if (placement.Maximized) WindowState = FormWindowState.Maximized;
        return true;
    }

    private void SaveSelectionPreferences()
    {
        if (!settingsLoaded) return;
        settings = settings with
        {
            Playback = new SelectionPreference(selection.PlaybackId is null, selection.PlaybackId),
            Capture = new SelectionPreference(selection.CaptureId is null, selection.CaptureId)
        };
        QueueSettingsSave();
    }

    private void QueueSettingsSave()
    {
        if (!settingsLoaded || !store.CanSave) return;
        settingsSaveRequested = true;
        if (!savingSettings) settingsTask = DrainSettingsSavesAsync();
    }

    private async Task DrainSettingsSavesAsync()
    {
        savingSettings = true;
        try
        {
            while (settingsSaveRequested && store.CanSave)
            {
                settingsSaveRequested = false;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var result = await store.SaveAsync(settings, timeout.Token);
                lastSaveError = result.Success ? null : result.Message;
                if (!IsDisposed) ShowEqualizerSaveResult(result.Success, result.Message);
                if (!IsDisposed) ShowStorageStatus(result.Message);
                if (!result.Success) { settingsSaveRequested = false; break; }
            }
        }
        catch (OperationCanceledException)
        {
            lastSaveError = StudioStrings.Get("MainForm.Preferences.AGravacaoDasPreferenciasFoi");
            if (!IsDisposed) ShowEqualizerSaveResult(false, lastSaveError);
            if (!IsDisposed) ShowStorageStatus(lastSaveError);
        }
        catch (Exception error) when (SettingsJson.IsValidationError(error) || SettingsStore.IsStorageError(error))
        {
            settingsSaveRequested = false;
            lastSaveError = StudioStrings.Get("MainForm.Preferences.FalhaAoSalvarPreferenciasA") + error.Message;
            if (!IsDisposed) ShowEqualizerSaveResult(false, lastSaveError);
            if (!IsDisposed) ShowStorageStatus(lastSaveError);
        }
        finally { savingSettings = false; }
    }

    private void ShowStorageStatus(string message)
    {
        storageStatus.Text = message;
        storageStatus.AccessibleName = StudioStrings.Get("MainForm.Preferences.EstadoDoArmazenamentoDePreferencias2") + message;
        language.Enabled = settingsLoaded && store.CanSave && !stopping && !preferenceOperation;
        resetPreferences.Enabled = store.CanReset && !preferenceOperation;
        recoverPreferences.Enabled = store.CanRecoverBackup && !preferenceOperation;
        preferencesSummary.Text = StudioStrings.Format("MainForm.Preferences.ReproducaoSalva01", SelectionText(settings.Playback), Environment.NewLine)
            + StudioStrings.Format("MainForm.Preferences.CapturaSalva01", SelectionText(settings.Capture), Environment.NewLine)
            + StudioStrings.Format("MainForm.Preferences.0PreferenciaSDeNiveis", settings.Endpoints.Length);
        ShowSavedPreferences();
    }

    private static string SelectionText(SelectionPreference preference) =>
        preference.FollowDefault ? StudioStrings.Get("MainForm.Preferences.SeguirPadraoMultimidia") : preference.EndpointId!;

    private EndpointPreference? SavedFor(EndpointDirection direction, DeviceSnapshot? device)
    {
        string? id = device?.Id ?? (direction == EndpointDirection.Playback ? selection.PlaybackId : selection.CaptureId);
        return settings.Endpoints.FirstOrDefault(item => item.Direction == direction && item.EndpointId == id);
    }

    private void ShowSavedPreferences()
    {
        playback.ShowPreference(SavedFor(EndpointDirection.Playback, currentPlayback), preferenceOperation, store.CanSave);
        capture.ShowPreference(SavedFor(EndpointDirection.Capture, currentCapture), preferenceOperation, store.CanSave);
    }

    private async Task SaveEndpointPreferenceAsync(EndpointDirection direction, DevicePanel panel)
    {
        if (stopping || preferenceOperation || !store.CanSave) return;
        preferenceOperation = true;
        ShowSavedPreferences();
        try
        {
            var captured = await panel.AudioControls.ReadPreferenceAsync();
            if (stopping) return;
            settings = settings.WithEndpoint(EndpointPreference.FromSnapshot(captured.Selection.DeviceId!, direction, captured.State));
            QueueSettingsSave();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error))
        {
            ShowStorageStatus(StudioStrings.Get("MainForm.Preferences.NaoFoiPossivelConsultarOs") + EndpointVolumeOperations.Describe(error));
        }
        finally { preferenceOperation = false; if (!IsDisposed) ShowSavedPreferences(); }
    }

    private async Task RestoreEndpointPreferenceAsync(EndpointDirection direction, DevicePanel panel)
    {
        var device = direction == EndpointDirection.Playback ? currentPlayback : currentCapture;
        var preference = SavedFor(direction, device);
        if (stopping || preferenceOperation || preference is null || device is null) return;
        preferenceOperation = true;
        ShowSavedPreferences();
        try
        {
            var plan = await panel.AudioControls.PreviewRestoreAsync(preference.Values);
            if (stopping) return;
            string preview = StudioStrings.Format("MainForm.Preferences.RestaurarOsValoresSalvosEm", device.Name, Environment.NewLine, plan.Selection.DeviceId, Environment.NewLine, Environment.NewLine)
                + StudioStrings.Format("MainForm.Preferences.VolumeAtual0Salvo1", Level(plan.Original.VolumePercent), Level(plan.Values.VolumePercent), Environment.NewLine)
                + StudioStrings.Format("MainForm.Preferences.MuteAtual0Salvo1", MuteText(plan.Original.Muted), MuteText(plan.Values.Muted), Environment.NewLine)
                + StudioStrings.Format("MainForm.Preferences.BalancoAtual0Salvo1", Level(plan.Original.Stereo?.Balance), Level(plan.Values.Balance));
            if (plan.Values.VolumePercent > plan.Original.VolumePercent)
                preview += Environment.NewLine + StudioStrings.Get("MainForm.Preferences.AConfirmacaoAumentaraOVolume");
            if (plan.Values.Muted == false && plan.Original.Muted == true)
                preview += Environment.NewLine + StudioStrings.Get("MainForm.Preferences.AConfirmacaoDesativaraOMute");
            if (MessageBox.Show(this, preview, StudioStrings.Get("MainForm.Preferences.ConfirmarRestauracaoDeAudio"), MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var result = await panel.AudioControls.RestoreAsync(plan);
            if (!stopping) ShowStorageStatus(result.Complete ? StudioStrings.Get("MainForm.Preferences.PreferenciaAplicadaPorSuaConfirmacao")
                : result.Error!);
        }
        catch (OperationCanceledException)
        {
            if (!stopping) ShowStorageStatus(StudioStrings.Get("MainForm.Preferences.ASelecaoMudouConfiraOs"));
        }
        catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error))
        {
            if (!stopping) ShowStorageStatus(EndpointVolumeOperations.Describe(error));
        }
        finally { preferenceOperation = false; if (!IsDisposed) ShowSavedPreferences(); }
    }

    private static string Level(double? value) => value is double number ? number.ToString("F1") : StudioStrings.Get("MainForm.Preferences.Ausente");
    private static string MuteText(bool? value) => value switch { true => StudioStrings.Get("MainForm.Preferences.Ligado"), false => StudioStrings.Get("MainForm.Preferences.Desligado"), _ => StudioStrings.Get("MainForm.Preferences.Ausente") };

    private async Task RecoverOrResetAsync(bool useBackup)
    {
        if (stopping || preferenceOperation || (useBackup ? !store.CanRecoverBackup : !store.CanReset)) return;
        string prompt = useBackup ? StudioStrings.Get("MainForm.Preferences.RecuperarAsPreferenciasEOs")
            : StudioStrings.Get("MainForm.Preferences.RedefinirSelecoesNiveisSalvosE");
        if (MessageBox.Show(this, prompt, StudioStrings.Get("MainForm.Preferences.PreferenciasLocais"), MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        preferenceOperation = true;
        recoveringPreferences = true;
        FlushEqualizerPreferences();
        microphoneTest.Equalizer.BeginLoad();
        EnablePreferenceSelections(false);
        ShowStorageStatus(StudioStrings.Get("MainForm.Preferences.AtualizandoPreferenciasLocais"));
        try
        {
            await settingsTask.WaitAsync(TimeSpan.FromSeconds(5));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var result = useBackup ? await store.RecoverBackupAsync(timeout.Token) : await store.ResetAsync(timeout.Token);
            if (result.Success)
            {
                var loaded = await store.LoadAsync(timeout.Token);
                settings = loaded.Settings;
                lastSaveError = null;
                if (!stopping)
                {
                    ShowLanguagePreference();
                    ApplySavedSelections();
                    LoadEqualizerPreferences();
                    RequestRefresh(true);
                }
            }
            if (!stopping) ShowStorageStatus(result.Message);
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException)
        {
            if (!stopping) ShowStorageStatus(StudioStrings.Get("MainForm.Preferences.AOperacaoDeArmazenamentoAinda"));
        }
        finally
        {
            preferenceOperation = recoveringPreferences = false;
            if (!stopping && !IsDisposed)
            {
                EnablePreferenceSelections(!refreshing);
                LoadEqualizerPreferences();
                ShowStorageStatus(storageStatus.Text);
            }
        }
    }

    private void EnablePreferenceSelections(bool enabled)
    {
        playback.Enabled = capture.Enabled = refresh.Enabled = enabled;
        followPlayback.Enabled = followCapture.Enabled = enabled;
    }

    private async Task SaveWindowOnCloseAsync()
    {
        if (!settingsLoaded) return;
        try { await preferenceTask.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception error)
        {
            showStorageWarning(StudioStrings.Get("MainForm.Preferences.AOperacaoDePreferenciasFicou") + error.GetType().Name);
            return;
        }
        FlushEqualizerPreferences();
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        var placement = new WindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height, WindowState == FormWindowState.Maximized);
        if (windowPlacementAvailable && settings.Window != placement && store.CanSave)
        {
            settings = settings with { Window = placement };
            QueueSettingsSave();
        }
        try { await settingsTask.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { lastSaveError = StudioStrings.Get("MainForm.Preferences.AGravacaoAindaEstaPendente"); }
        catch (Exception error) when (SettingsJson.IsValidationError(error) || SettingsStore.IsStorageError(error) || error is OperationCanceledException)
        {
            lastSaveError = StudioStrings.Get("MainForm.Preferences.FalhaNaGravacaoFinalConfira") + error.GetType().Name;
        }
        if (lastSaveError is not null)
            showStorageWarning(lastSaveError);
    }

    private static void AddFallbackWarning(List<string> warnings, DeviceSnapshot? device, EndpointDirection direction, string? explicitId)
    {
        if (explicitId is null && device is { State: EndpointState.Active, IsDefaultMultimedia: false })
            warnings.Add(StudioStrings.Format("MainForm.Preferences.PadraoMultimidiaDe0Indisponivel", (direction == EndpointDirection.Playback ? StudioStrings.Get("MainForm.Preferences.Reproducao") : StudioStrings.Get("General.CaptureLower")), device.Name));
    }
}
