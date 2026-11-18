using WaveStudio.Audio.Dsp;
using WaveStudio.Desktop.Settings;

namespace WaveStudio.Desktop;

internal sealed class EqualizerPreviewPanel : UserControl
{
    internal CheckBox Activate { get; } = new() { Text = "Ativar EQ nesta gravação", AutoSize = true };
    internal TrackBar[] Bands { get; } = new TrackBar[10];
    internal NumericUpDown Preamp { get; } = new() { Minimum = -30, Maximum = 0, DecimalPlaces = 1, Increment = 1, Width = 75, AccessibleName = "Preamp do EQ em dB" };
    internal Button ResetButton { get; } = new() { Text = "Zerar EQ e preamp", AutoSize = true };
    internal ComboBox ProfileList { get; } = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, AccessibleName = "Perfil do equalizador" };
    internal TextBox ProfileName { get; } = new() { Width = 210, MaxLength = EqualizerPreferences.MaximumNameLength, PlaceholderText = "Nome do perfil", AccessibleName = "Nome do perfil" };
    internal Button CreateProfile { get; } = new() { Text = "Criar perfil", AutoSize = true };
    internal Button SaveProfile { get; } = new() { Text = "Salvar alterações", AutoSize = true };
    internal Button RenameProfile { get; } = new() { Text = "Renomear", AutoSize = true };
    internal Button DeleteProfile { get; } = new() { Text = "Excluir", AutoSize = true };
    private readonly Label profileStatus = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private readonly Label saveStatus = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private EqualizerPreferences preferences = new();
    private bool binding, loaded = true, frozen, writable = true;
    private readonly Func<string, bool> confirm;
    public bool IsAdjusting { get; private set; }
    internal EqualizerPreferences Preferences => preferences;
    public EqualizerPreferences? CapturePreferences()
    {
        if (!loaded) return null;
        // Value valida qualquer edição numérica pendente antes que a gravação de encerramento capture o estado.
        _ = Preamp.Value;
        return preferences;
    }
    internal string ProfileStatus => profileStatus.Text;
    internal string SaveStatus => saveStatus.Text;
    public event Action<EqualizerPreferences, bool>? PreferencesChanged;
    private readonly FlowLayoutPanel bands = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
    private readonly Label safety = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private readonly Label availability = new() { AutoSize = true };
    internal bool Supported { get; private set; }
    internal bool PreferencesLoaded => loaded;
    public GraphicEqualizerSettings Settings => new(Activate.Checked, Bands.Select(b => (double)b.Value), (double)Preamp.Value);

    public EqualizerPreviewPanel(Func<string, bool>? confirm = null)
    {
        this.confirm = confirm ?? (message => MessageBox.Show(FindForm(), message, "Perfis do equalizador", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
        AutoSize = true; Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Padding = new Padding(0, 12, 0, 8) };
        void Add(Control item) => layout.Controls.Add(item, 0, layout.RowCount++);
        Add(new Label { Text = "EQ de 10 bandas — prévia da gravação", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        Add(new Label { Text = "Compare a mesma gravação original e processada. Não afeta o áudio de outros aplicativos.", AutoSize = true, MaximumSize = new Size(700, 0) });
        var profileRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        profileRow.Controls.AddRange([new Label { Text = "Perfil:", AutoSize = true }, ProfileList, ProfileName]);
        Add(profileRow);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        actions.Controls.AddRange([CreateProfile, SaveProfile, RenameProfile, DeleteProfile]); Add(actions);
        Add(profileStatus); Add(saveStatus);
        Add(Activate); Add(availability); Add(bands);
        for (int i = 0; i < Bands.Length; i++)
        {
            int frequency = GraphicEqualizerSettings.Frequencies[i];
            string name = frequency < 1000 ? $"{frequency} Hz" : $"{frequency / 1000} kHz";
            var column = new FlowLayoutPanel { Width = 60, Height = 172, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            var value = new Label { Text = "0 dB", AutoSize = true };
            var slider = new TrackBar { Orientation = Orientation.Vertical, Minimum = -15, Maximum = 15, TickFrequency = 5, Height = 118, Width = 48, AccessibleName = $"EQ {name} em dB" };
            Bands[i] = slider;
            column.Controls.AddRange([new Label { Text = name, AutoSize = true }, slider, value]);
            bands.Controls.Add(column);
            slider.ValueChanged += (_, _) => { value.Text = $"{slider.Value:+0;-0;0} dB"; ShowSafety(); ParametersChanged(); };
            slider.MouseDown += (_, _) => BeginAdjustment(); slider.KeyDown += (_, _) => BeginAdjustment();
            slider.MouseUp += (_, _) => EndAdjustment(); slider.KeyUp += (_, _) => EndAdjustment(); slider.Leave += (_, _) => EndAdjustment();
        }
        var preampRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        preampRow.Controls.AddRange([new Label { Text = "Entrada (preamp, −30 a 0 dB):", AutoSize = true }, Preamp, ResetButton]);
        Add(preampRow); Add(safety);
        Add(new Label { Text = "Ajustes são congelados ao iniciar. Pare para alterar. A proteção final é hard clip, não um limiter.", AutoSize = true, MaximumSize = new Size(700, 0) });
        Controls.Add(layout);
        Activate.CheckedChanged += (_, _) => { UpdateControls(); ShowSafety(); ParametersChanged(); };
        Preamp.ValueChanged += (_, _) => { ShowSafety(); ParametersChanged(); };
        ResetButton.Click += (_, _) => { foreach (var band in Bands) band.Value = 0; Preamp.Value = 0; ShowSafety(); };
        ProfileList.SelectedIndexChanged += (_, _) => SelectProfile();
        CreateProfile.Click += (_, _) => RunProfile(() => preferences.Create(ProfileName.Text), "Perfil criado.");
        SaveProfile.Click += (_, _) => RunProfile(preferences.SaveSelected, "Alterações salvas no perfil.");
        RenameProfile.Click += (_, _) => RunProfile(() => preferences.Rename(ProfileName.Text), "Perfil renomeado.");
        DeleteProfile.Click += (_, _) =>
        {
            if (!CanEditProfiles || preferences.SelectedProfileId is null) return;
            if (this.confirm($"Excluir o perfil {preferences.Profiles.Single(p => p.Id == preferences.SelectedProfileId).Name}? Os ajustes atuais serão mantidos."))
                RunProfile(preferences.DeleteSelected, "Perfil excluído. Ajustes atuais mantidos.");
        };
        BindPreferences(); ShowSafety(); SetAvailability(false, false);
    }
    public void SetAvailability(bool supported, bool frozen)
    {
        Supported = supported;
        this.frozen = frozen;
        Enabled = !frozen && loaded;
        availability.Text = supported ? "Disponível em 44,1/48 kHz. Original continua sem EQ." : "EQ indisponível neste formato: requer gravação em 44,1 ou 48 kHz. Ouvir original permanece disponível.";
        UpdateControls();
    }
    private void UpdateControls()
    {
        Activate.Enabled = Supported;
        bands.Enabled = Preamp.Enabled = ResetButton.Enabled = Supported && Activate.Checked;
        ProfileList.Enabled = loaded && !frozen;
        ProfileName.Enabled = CreateProfile.Enabled = CanEditProfiles;
        RenameProfile.Enabled = DeleteProfile.Enabled = CanEditProfiles && preferences.SelectedProfileId is not null;
        SaveProfile.Enabled = RenameProfile.Enabled;
    }
    private void ShowSafety()
    {
        var settings = Settings;
        safety.Text = $"Atenuação automática de segurança: −{settings.HeadroomDb:F1} dB (soma dos boosts). Preamp efetivo com EQ: {settings.EffectivePreampDb:F1} dB. Pode soar mais baixo; não aumente o volume do Windows para compensar.";
    }
    private bool CanEditProfiles => loaded && !frozen && writable;
    public void BeginLoad() { loaded = false; Enabled = false; }
    public void LoadPreferences(EqualizerPreferences values, bool canSave)
    {
        preferences = new(values.Current, values.Profiles, values.SelectedProfileId);
        loaded = true; writable = canSave; BindPreferences(); Enabled = !frozen; UpdateControls();
        SetSaveStatus(canSave ? "Ajustes carregados. Alterações serão lembradas automaticamente; perfis só mudam ao salvar." : "Arquivo protegido. Ajustes desta abertura não serão gravados.");
    }
    public void SetWritable(bool value) { writable = value; UpdateControls(); }
    public void SetSaveStatus(string message) => saveStatus.Text = message;
    internal void BeginAdjustment() { if (!binding && loaded && !frozen) { IsAdjusting = true; PreferencesChanged?.Invoke(preferences, false); } }
    internal void EndAdjustment() { if (IsAdjusting) { IsAdjusting = false; PreferencesChanged?.Invoke(preferences, false); } }
    private void ParametersChanged()
    {
        if (binding || !loaded) return;
        preferences = preferences.WithCurrent(new(Activate.Checked, Bands.Select(b => (double)b.Value).ToArray(), (double)Preamp.Value));
        ShowProfileStatus(); UpdateControls(); PreferencesChanged?.Invoke(preferences, false);
    }
    private void BindPreferences()
    {
        binding = true;
        try
        {
            Activate.Checked = preferences.Current.Enabled;
            for (int i = 0; i < Bands.Length; i++) Bands[i].Value = (int)preferences.Current.GainsDb[i];
            Preamp.Value = (decimal)preferences.Current.PreampDb;
            ProfileList.Items.Clear(); ProfileList.Items.Add(new ProfileItem(null, "Padrão"));
            foreach (var profile in preferences.Profiles) ProfileList.Items.Add(new ProfileItem(profile.Id, profile.Name));
            ProfileList.SelectedIndex = preferences.SelectedProfileId is null ? 0 : preferences.Profiles.Select(p => p.Id).ToList().IndexOf(preferences.SelectedProfileId) + 1;
            ProfileName.Text = preferences.SelectedProfileId is null ? "" : preferences.Profiles.Single(p => p.Id == preferences.SelectedProfileId).Name;
        }
        finally { binding = false; }
        ShowSafety(); ShowProfileStatus(); UpdateControls();
    }
    private void ShowProfileStatus() => profileStatus.Text = preferences.HasUnsavedChanges()
        ? "Ajustes diferentes do perfil. Crie um perfil ou use Salvar alterações para guardá-los nele."
        : "Ajustes iguais ao perfil selecionado.";
    private void SelectProfile()
    {
        if (binding || !loaded || frozen || ProfileList.SelectedItem is not ProfileItem item) return;
        if (item.Id == preferences.SelectedProfileId) return;
        if (preferences.HasUnsavedChanges() && !confirm("Trocar de perfil sem salvar as alterações nele? Os ajustes atuais serão substituídos."))
        { BindPreferences(); return; }
        preferences = preferences.Apply(item.Id); BindPreferences(); PreferencesChanged?.Invoke(preferences, true);
    }
    private void RunProfile(Func<EqualizerPreferences> action, string message)
    {
        if (!CanEditProfiles) return;
        try { preferences = action(); BindPreferences(); PreferencesChanged?.Invoke(preferences, true); profileStatus.Text = message + (preferences.HasUnsavedChanges() ? " Ajustes diferentes do perfil." : ""); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { profileStatus.Text = error.Message; }
    }
    private sealed record ProfileItem(string? Id, string Name) { public override string ToString() => Name; }
}
