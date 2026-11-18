using WaveStudio.Desktop.Settings;

namespace WaveStudio.Desktop;

internal sealed partial class MainForm
{
    private readonly System.Windows.Forms.Timer equalizerSaveTimer = new() { Interval = 500 };
    private bool equalizerSavePending;

    private void InitializeEqualizerProfiles()
    {
        microphoneTest.Equalizer.BeginLoad();
        microphoneTest.Equalizer.PreferencesChanged += (values, saveNow) =>
        {
            if (stopping || !settingsLoaded || recoveringPreferences) return;
            if (!settings.Equalizer.SameAs(values))
            {
                settings = settings with { Equalizer = values };
                equalizerSavePending = true;
            }
            equalizerSaveTimer.Stop();
            if (!store.CanSave)
            {
                microphoneTest.Equalizer.SetSaveStatus("Arquivo protegido. Estes ajustes não serão gravados nesta abertura.");
                return;
            }
            if (!equalizerSavePending) return;
            microphoneTest.Equalizer.SetSaveStatus("Alterações aguardando salvamento…");
            if (microphoneTest.Equalizer.IsAdjusting) return;
            if (saveNow) FlushEqualizerPreferences();
            else equalizerSaveTimer.Start();
        };
        equalizerSaveTimer.Tick += (_, _) => FlushEqualizerPreferences();
    }
    private void LoadEqualizerPreferences()
    {
        equalizerSaveTimer.Stop(); equalizerSavePending = false;
        microphoneTest.Equalizer.LoadPreferences(settings.Equalizer, store.CanSave);
    }
    private void FlushEqualizerPreferences()
    {
        equalizerSaveTimer.Stop();
        if (!settingsLoaded) return;
        if (microphoneTest.Equalizer.CapturePreferences() is { } current && !settings.Equalizer.SameAs(current))
        {
            settings = settings with { Equalizer = current };
            equalizerSavePending = true;
        }
        if (!equalizerSavePending) return;
        if (!store.CanSave)
        {
            microphoneTest.Equalizer.SetSaveStatus("Não foi possível salvar: o arquivo está protegido. Confira Preferências locais.");
            return;
        }
        equalizerSavePending = false;
        QueueSettingsSave();
    }
    private void ShowEqualizerSaveResult(bool success, string message)
    {
        if (!success) equalizerSavePending = true; // Mantém o estado mais recente em memória disponível para nova tentativa explícita ou gravação no encerramento.
        microphoneTest.Equalizer.SetWritable(store.CanSave && !recoveringPreferences);
        microphoneTest.Equalizer.SetSaveStatus(success
            ? equalizerSavePending || settingsSaveRequested ? "Alterações aguardando salvamento…" : "Ajustes salvos neste computador. Perfis só mudam ao salvar alterações."
            : "Não foi possível salvar. " + message);
    }
}
