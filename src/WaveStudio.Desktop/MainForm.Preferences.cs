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
    private readonly Button resetPreferences = new() { AutoSize = true, Text = "Redefinir preferências locais…", Enabled = false };
    private readonly Button recoverPreferences = new() { AutoSize = true, Text = "Recuperar backup…", Enabled = false };
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
        var page = new TabPage("Preferências locais") { AutoScroll = true };
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(12)
        };
        storageStatus.AccessibleName = "Estado do armazenamento de preferências";
        layout.Controls.Add(new TextBox { ReadOnly = true, Width = 720, Text = store.FilePath, AccessibleName = "Arquivo de preferências locais" });
        layout.Controls.Add(preferencesSummary);
        layout.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(800, 0),
            Text = "Seleções são salvas após sua escolha. Use Salvar valores atuais em Dispositivos para guardar níveis. Restaurar valores salvos apresenta uma prévia e pede confirmação."
        });
        layout.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(800, 0),
            Text = "Preferências de sessões indisponíveis — identidade durável e correspondência inequívoca de aplicativos ainda aguardam validação."
        });
        layout.Controls.Add(recoverPreferences);
        layout.Controls.Add(resetPreferences);
        page.Controls.Add(layout);
        resetPreferences.Click += (_, _) => RunPreferenceAction(() => RecoverOrResetAsync(useBackup: false));
        recoverPreferences.Click += (_, _) => RunPreferenceAction(() => RecoverOrResetAsync(useBackup: true));
        return page;
    }

    private async void RunPreferenceAction(Func<Task> action)
    {
        if (stopping || !preferenceTask.IsCompleted) return;
        preferenceTask = action();
        try { await preferenceTask; }
        catch (Exception error)
        {
            if (!stopping && !IsDisposed) ShowStorageStatus("Falha na operação de preferências. " + error.GetType().Name);
        }
    }

    private async Task InitializePreferencesAsync()
    {
        try
        {
            var loaded = await store.LoadAsync(lifetime.Token);
            if (stopping) return;
            settings = loaded.Settings;
            settingsLoaded = true;
            ApplySavedSelections();
            LoadEqualizerPreferences();
            windowPlacementAvailable = ApplyWindowPlacement();
            ShowStorageStatus(loaded.Message + (windowPlacementAvailable ? "" : " Área útil da tela indisponível; posição salva preservada nesta abertura."));
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
            lastSaveError = "A gravação das preferências foi cancelada. Confira o armazenamento antes de fechar.";
            if (!IsDisposed) ShowEqualizerSaveResult(false, lastSaveError);
            if (!IsDisposed) ShowStorageStatus(lastSaveError);
        }
        catch (Exception error) when (SettingsJson.IsValidationError(error) || SettingsStore.IsStorageError(error))
        {
            settingsSaveRequested = false;
            lastSaveError = "Falha ao salvar preferências. A configuração válida foi preservada. " + error.Message;
            if (!IsDisposed) ShowEqualizerSaveResult(false, lastSaveError);
            if (!IsDisposed) ShowStorageStatus(lastSaveError);
        }
        finally { savingSettings = false; }
    }

    private void ShowStorageStatus(string message)
    {
        storageStatus.Text = message;
        storageStatus.AccessibleName = "Estado do armazenamento de preferências — " + message;
        resetPreferences.Enabled = store.CanReset && !preferenceOperation;
        recoverPreferences.Enabled = store.CanRecoverBackup && !preferenceOperation;
        preferencesSummary.Text = $"Reprodução salva — {SelectionText(settings.Playback)}{Environment.NewLine}"
            + $"Captura salva — {SelectionText(settings.Capture)}{Environment.NewLine}"
            + $"{settings.Endpoints.Length} preferência(s) de níveis por endpoint.";
        ShowSavedPreferences();
    }

    private static string SelectionText(SelectionPreference preference) =>
        preference.FollowDefault ? "seguir padrão multimídia" : preference.EndpointId!;

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
            ShowStorageStatus("Não foi possível consultar os valores para salvar. " + EndpointVolumeOperations.Describe(error));
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
            string preview = $"Restaurar os valores salvos em {device.Name}?{Environment.NewLine}{plan.Selection.DeviceId}{Environment.NewLine}{Environment.NewLine}"
                + $"Volume atual {Level(plan.Original.VolumePercent)} → salvo {Level(plan.Values.VolumePercent)}{Environment.NewLine}"
                + $"Mute atual {MuteText(plan.Original.Muted)} → salvo {MuteText(plan.Values.Muted)}{Environment.NewLine}"
                + $"Balanço atual {Level(plan.Original.Stereo?.Balance)} → salvo {Level(plan.Values.Balance)}";
            if (plan.Values.VolumePercent > plan.Original.VolumePercent)
                preview += Environment.NewLine + "A confirmação aumentará o volume do endpoint.";
            if (plan.Values.Muted == false && plan.Original.Muted == true)
                preview += Environment.NewLine + "A confirmação desativará o mute e poderá tornar o áudio audível.";
            if (MessageBox.Show(this, preview, "Confirmar restauração de áudio", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var result = await panel.AudioControls.RestoreAsync(plan);
            if (!stopping) ShowStorageStatus(result.Complete ? "Preferência aplicada por sua confirmação. Valores atuais consultados no Windows."
                : result.Error!);
        }
        catch (OperationCanceledException)
        {
            if (!stopping) ShowStorageStatus("A seleção mudou. Confira os níveis do dispositivo anterior e solicite uma nova prévia para restaurar.");
        }
        catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error))
        {
            if (!stopping) ShowStorageStatus(EndpointVolumeOperations.Describe(error));
        }
        finally { preferenceOperation = false; if (!IsDisposed) ShowSavedPreferences(); }
    }

    private static string Level(double? value) => value is double number ? number.ToString("F1") : "ausente";
    private static string MuteText(bool? value) => value switch { true => "ligado", false => "desligado", _ => "ausente" };

    private async Task RecoverOrResetAsync(bool useBackup)
    {
        if (stopping || preferenceOperation || (useBackup ? !store.CanRecoverBackup : !store.CanReset)) return;
        string prompt = useBackup ? "Recuperar as preferências e os perfis do backup válido? A seleção será recarregada e os níveis atuais serão consultados."
            : "Redefinir seleções, níveis salvos e todos os perfis e ajustes do equalizador? A configuração válida anterior ficará no backup. Um original inválido será arquivado.";
        if (MessageBox.Show(this, prompt, "Preferências locais", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        preferenceOperation = true;
        recoveringPreferences = true;
        FlushEqualizerPreferences();
        microphoneTest.Equalizer.BeginLoad();
        EnablePreferenceSelections(false);
        ShowStorageStatus("Atualizando preferências locais…");
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
                    ApplySavedSelections();
                    LoadEqualizerPreferences();
                    RequestRefresh(true);
                }
            }
            if (!stopping) ShowStorageStatus(result.Message);
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException)
        {
            if (!stopping) ShowStorageStatus("A operação de armazenamento ainda não terminou. Aguarde e confira o arquivo antes de repetir.");
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
            showStorageWarning("A operação de preferências ficou pendente ou falhou. Confira o arquivo na próxima abertura. " + error.GetType().Name);
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
        catch (TimeoutException) { lastSaveError = "A gravação ainda está pendente. A próxima abertura deverá conferir o arquivo de preferências."; }
        catch (Exception error) when (SettingsJson.IsValidationError(error) || SettingsStore.IsStorageError(error) || error is OperationCanceledException)
        {
            lastSaveError = "Falha na gravação final. Confira a configuração preservada. " + error.GetType().Name;
        }
        if (lastSaveError is not null)
            showStorageWarning(lastSaveError);
    }

    private static void AddFallbackWarning(List<string> warnings, DeviceSnapshot? device, EndpointDirection direction, string? explicitId)
    {
        if (explicitId is null && device is { State: EndpointState.Active, IsDefaultMultimedia: false })
            warnings.Add($"Padrão multimídia de {(direction == EndpointDirection.Playback ? "reprodução" : "captura")} indisponível. Alternativa ativa exibida — {device.Name}.");
    }
}
