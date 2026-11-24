using WaveStudio.Audio.Dsp;
using WaveStudio.Desktop.Settings;

namespace WaveStudio.Desktop;

internal sealed class EqualizerPreviewPanel : UserControl
{
    internal CheckBox Activate { get; } = new() { Text = StudioStrings.Get("EqualizerPreviewPanel.AtivarEQNestaGravacao"), AutoSize = true };
    internal TrackBar[] Bands { get; } = new TrackBar[10];
    internal NumericUpDown Preamp { get; } = new() { Minimum = -30, Maximum = 0, DecimalPlaces = 1, Increment = 1, Width = 75, AccessibleName = StudioStrings.Get("EqualizerPreviewPanel.PreampDoEQEmDB") };
    internal Button ResetButton { get; } = new() { Text = StudioStrings.Get("EqualizerPreviewPanel.ZerarEQEPreamp"), AutoSize = true };
    internal ComboBox ProfileList { get; } = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, AccessibleName = StudioStrings.Get("EqualizerPreviewPanel.PerfilDoEqualizador") };
    internal TextBox ProfileName { get; } = new() { Width = 210, MaxLength = EqualizerPreferences.MaximumNameLength, PlaceholderText = StudioStrings.Get("EqualizerPreviewPanel.NomeDoPerfil"), AccessibleName = StudioStrings.Get("EqualizerPreviewPanel.NomeDoPerfil") };
    internal Button CreateProfile { get; } = new() { Text = StudioStrings.Get("EqualizerPreviewPanel.CriarPerfil"), AutoSize = true };
    internal Button SaveProfile { get; } = new() { Text = StudioStrings.Get("EqualizerPreviewPanel.SalvarAlteracoes"), AutoSize = true };
    internal Button RenameProfile { get; } = new() { Text = StudioStrings.Get("EqualizerPreviewPanel.Renomear"), AutoSize = true };
    internal Button DeleteProfile { get; } = new() { Text = StudioStrings.Get("EqualizerPreviewPanel.Excluir"), AutoSize = true };
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
        this.confirm = confirm ?? (message => MessageBox.Show(FindForm(), message, StudioStrings.Get("EqualizerPreviewPanel.PerfisDoEqualizador"), MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
        AutoSize = true; Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Padding = new Padding(0, 12, 0, 8) };
        void Add(Control item) => layout.Controls.Add(item, 0, layout.RowCount++);
        Add(new Label { Text = StudioStrings.Get("EqualizerPreviewPanel.EQDe10BandasPrevia"), AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        Add(new Label { Text = StudioStrings.Get("EqualizerPreviewPanel.CompareAMesmaGravacaoOriginal"), AutoSize = true, MaximumSize = new Size(700, 0) });
        var profileRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        profileRow.Controls.AddRange([new Label { Text = StudioStrings.Get("EqualizerPreviewPanel.ProfileLabel"), AutoSize = true }, ProfileList, ProfileName]);
        Add(profileRow);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        actions.Controls.AddRange([CreateProfile, SaveProfile, RenameProfile, DeleteProfile]); Add(actions);
        Add(profileStatus); Add(saveStatus);
        Add(Activate); Add(availability); Add(bands);
        for (int i = 0; i < Bands.Length; i++)
        {
            int frequency = GraphicEqualizerSettings.Frequencies[i];
            string name = frequency < 1000 ? StudioStrings.Format("EqualizerPreviewPanel.0Hz", frequency) : StudioStrings.Format("EqualizerPreviewPanel.0KHz", frequency / 1000);
            var column = new FlowLayoutPanel { Width = 60, Height = 172, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            var value = new Label { Text = StudioStrings.Get("EqualizerPreviewPanel.0DB"), AutoSize = true };
            var slider = new TrackBar { Orientation = Orientation.Vertical, Minimum = -15, Maximum = 15, TickFrequency = 5, Height = 118, Width = 48, AccessibleName = StudioStrings.Format("EqualizerPreviewPanel.EQ0EmDB", name) };
            Bands[i] = slider;
            column.Controls.AddRange([new Label { Text = name, AutoSize = true }, slider, value]);
            bands.Controls.Add(column);
            slider.ValueChanged += (_, _) => { value.Text = StudioStrings.Format("EqualizerPreviewPanel.0000DB", slider.Value); ShowSafety(); ParametersChanged(); };
            slider.MouseDown += (_, _) => BeginAdjustment(); slider.KeyDown += (_, _) => BeginAdjustment();
            slider.MouseUp += (_, _) => EndAdjustment(); slider.KeyUp += (_, _) => EndAdjustment(); slider.Leave += (_, _) => EndAdjustment();
        }
        var preampRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        preampRow.Controls.AddRange([new Label { Text = StudioStrings.Get("EqualizerPreviewPanel.EntradaPreamp30A0"), AutoSize = true }, Preamp, ResetButton]);
        Add(preampRow); Add(safety);
        Add(new Label { Text = StudioStrings.Get("EqualizerPreviewPanel.AjustesSaoCongeladosAoIniciar"), AutoSize = true, MaximumSize = new Size(700, 0) });
        Controls.Add(layout);
        Activate.CheckedChanged += (_, _) => { UpdateControls(); ShowSafety(); ParametersChanged(); };
        Preamp.ValueChanged += (_, _) => { ShowSafety(); ParametersChanged(); };
        ResetButton.Click += (_, _) => { foreach (var band in Bands) band.Value = 0; Preamp.Value = 0; ShowSafety(); };
        ProfileList.SelectedIndexChanged += (_, _) => SelectProfile();
        CreateProfile.Click += (_, _) => RunProfile(() => preferences.Create(ProfileName.Text), StudioStrings.Get("EqualizerPreviewPanel.PerfilCriado"));
        SaveProfile.Click += (_, _) => RunProfile(preferences.SaveSelected, StudioStrings.Get("EqualizerPreviewPanel.AlteracoesSalvasNoPerfil"));
        RenameProfile.Click += (_, _) => RunProfile(() => preferences.Rename(ProfileName.Text), StudioStrings.Get("EqualizerPreviewPanel.PerfilRenomeado"));
        DeleteProfile.Click += (_, _) =>
        {
            if (!CanEditProfiles || preferences.SelectedProfileId is null) return;
            if (this.confirm(StudioStrings.Format("EqualizerPreviewPanel.ExcluirOPerfil0Os", preferences.Profiles.Single(p => p.Id == preferences.SelectedProfileId).Name)))
                RunProfile(preferences.DeleteSelected, StudioStrings.Get("EqualizerPreviewPanel.PerfilExcluidoAjustesAtuaisMantidos"));
        };
        BindPreferences(); ShowSafety(); SetAvailability(false, false);
    }
    public void SetAvailability(bool supported, bool frozen)
    {
        Supported = supported;
        this.frozen = frozen;
        Enabled = !frozen && loaded;
        availability.Text = supported ? StudioStrings.Get("EqualizerPreviewPanel.DisponivelEm44148") : StudioStrings.Get("EqualizerPreviewPanel.EQIndisponivelNesteFormatoRequer");
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
        safety.Text = StudioStrings.Format("EqualizerPreviewPanel.AtenuacaoAutomaticaDeSeguranca0", settings.HeadroomDb, settings.EffectivePreampDb);
    }
    private bool CanEditProfiles => loaded && !frozen && writable;
    public void BeginLoad() { loaded = false; Enabled = false; }
    public void LoadPreferences(EqualizerPreferences values, bool canSave)
    {
        preferences = new(values.Current, values.Profiles, values.SelectedProfileId);
        loaded = true; writable = canSave; BindPreferences(); Enabled = !frozen; UpdateControls();
        SetSaveStatus(canSave ? StudioStrings.Get("EqualizerPreviewPanel.AjustesCarregadosAlteracoesSeraoLembradas") : StudioStrings.Get("EqualizerPreviewPanel.ArquivoProtegidoAjustesDestaAbertura"));
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
            ProfileList.Items.Clear(); ProfileList.Items.Add(new ProfileItem(null, StudioStrings.Get("EqualizerPreviewPanel.Padrao")));
            foreach (var profile in preferences.Profiles) ProfileList.Items.Add(new ProfileItem(profile.Id, profile.Name));
            ProfileList.SelectedIndex = preferences.SelectedProfileId is null ? 0 : preferences.Profiles.Select(p => p.Id).ToList().IndexOf(preferences.SelectedProfileId) + 1;
            ProfileName.Text = preferences.SelectedProfileId is null ? "" : preferences.Profiles.Single(p => p.Id == preferences.SelectedProfileId).Name;
        }
        finally { binding = false; }
        ShowSafety(); ShowProfileStatus(); UpdateControls();
    }
    private void ShowProfileStatus() => profileStatus.Text = preferences.HasUnsavedChanges()
        ? StudioStrings.Get("EqualizerPreviewPanel.AjustesDiferentesDoPerfilCrie")
        : StudioStrings.Get("EqualizerPreviewPanel.AjustesIguaisAoPerfilSelecionado");
    private void SelectProfile()
    {
        if (binding || !loaded || frozen || ProfileList.SelectedItem is not ProfileItem item) return;
        if (item.Id == preferences.SelectedProfileId) return;
        if (preferences.HasUnsavedChanges() && !confirm(StudioStrings.Get("EqualizerPreviewPanel.TrocarDePerfilSemSalvar")))
        { BindPreferences(); return; }
        preferences = preferences.Apply(item.Id); BindPreferences(); PreferencesChanged?.Invoke(preferences, true);
    }
    private void RunProfile(Func<EqualizerPreferences> action, string message)
    {
        if (!CanEditProfiles) return;
        try { preferences = action(); BindPreferences(); PreferencesChanged?.Invoke(preferences, true); profileStatus.Text = message + (preferences.HasUnsavedChanges() ? StudioStrings.Get("EqualizerPreviewPanel.AjustesDiferentesDoPerfil") : ""); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { profileStatus.Text = error.Message; }
    }
    private sealed record ProfileItem(string? Id, string Name) { public override string ToString() => Name; }
}
