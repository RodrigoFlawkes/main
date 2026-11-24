using WaveStudio.Audio;
using WaveStudio.Desktop.Settings;

namespace WaveStudio.Desktop;

internal sealed class DevicePanel : GroupBox
{
    private readonly ComboBox selector = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox information = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window
    };
    private readonly EndpointControls endpointControls;
    private bool binding;
    private DeviceSnapshot? displayed;
    private readonly ProgressBar peakBar = new() { Minimum = 0, Maximum = 1000, Dock = DockStyle.Fill, Height = 14 };
    private readonly Label peakText = new() { AutoSize = true, Text = StudioStrings.Get("DevicePanel.PicoIndisponivel"), Dock = DockStyle.Fill };
    private readonly Label savedText = new() { AutoSize = true, Text = StudioStrings.Get("DevicePanel.PreferenciaDeNiveisAindaNao"), Dock = DockStyle.Fill };
    private readonly Button savePreference = new() { AutoSize = true, Text = StudioStrings.Get("DevicePanel.SalvarValoresAtuais") };
    private readonly Button restorePreference = new() { AutoSize = true, Text = StudioStrings.Get("DevicePanel.RestaurarValoresSalvos") };
    private EndpointPreference? saved;
    private bool preferenceBusy;
    private bool preferenceWritable = true;

    public event Action<DeviceSnapshot>? DeviceSelected;
    public event Action? SavePreferenceRequested;
    public event Action? RestorePreferenceRequested;
    internal EndpointControls AudioControls => endpointControls;

    public DevicePanel(string title, EndpointControls? endpointControls = null)
    {
        this.endpointControls = endpointControls ?? new EndpointControls();
        Text = title;
        Dock = DockStyle.Fill;
        Padding = new Padding(12, 24, 12, 12);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 1, AutoScroll = true };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selector.DisplayMember = nameof(DeviceItem.Label);
        selector.AccessibleName = title;
        information.AccessibleName = StudioStrings.Format("DevicePanel.InformacoesDe0", title);
        layout.Controls.Add(selector, 0, 0);
        layout.Controls.Add(information, 0, 1);
        layout.Controls.Add(this.endpointControls, 0, 2);
        layout.Controls.Add(peakBar, 0, 3);
        layout.Controls.Add(peakText, 0, 4);
        layout.Controls.Add(savedText, 0, 5);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actions.Controls.Add(savePreference);
        actions.Controls.Add(restorePreference);
        layout.Controls.Add(actions, 0, 6);
        savedText.AccessibleName = StudioStrings.Format("DevicePanel.PreferenciasSalvasDe0", title);
        savePreference.Click += (_, _) => SavePreferenceRequested?.Invoke();
        restorePreference.Click += (_, _) => RestorePreferenceRequested?.Invoke();
        peakBar.AccessibleName = StudioStrings.Format("DevicePanel.PicoRealDoWindows0", title);
        Controls.Add(layout);
        selector.SelectedIndexChanged += (_, _) =>
        {
            if (binding)
                return;
            if (selector.SelectedItem is DeviceItem item)
            {
                ShowDevice(item.Device);
                DeviceSelected?.Invoke(item.Device);
            }
        };
    }

    public void ShowDevices(DeviceCatalog catalog, EndpointDirection direction, DeviceSnapshot? selected, bool resetControls = true, string? preferredId = null)
    {
        binding = true;
        try
        {
            selector.Items.Clear();
            foreach (var device in catalog.Devices.Where(device => device.Direction == direction)
                .OrderByDescending(device => device.State == EndpointState.Active)
                .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase))
                selector.Items.Add(new DeviceItem(device));

            selector.Enabled = selector.Items.Count > 0;
            selector.SelectedIndex = selector.Items.Cast<DeviceItem>().ToList()
                .FindIndex(item => item.Device.Id == selected?.Id);
        }
        finally { binding = false; }
        ShowDevice(selected, resetControls);
        if (selected is null && preferredId is not null)
            information.Text = StudioStrings.Get("DevicePanel.PreferenciaExplicitaPreservadaDispositivoIndisponivel") + preferredId;
    }

    public void ShowUnavailable(string message)
    {
        selector.Items.Clear();
        selector.Enabled = false;
        information.Text = message;
        endpointControls.SelectDevice(null);
        displayed = null;
        UpdatePreferenceButtons();
        ShowPeak(PeakReading.Unavailable);
    }

    private void ShowDevice(DeviceSnapshot? device, bool resetControls = true)
    {
        if (resetControls || displayed?.Id != device?.Id || displayed?.State != device?.State || displayed?.MixFormat != device?.MixFormat)
        {
            endpointControls.SelectDevice(device);
            ShowPeak(PeakReading.Unavailable);
        }
        displayed = device;
        UpdatePreferenceButtons();
        if (device is null)
        {
            information.Text = StudioStrings.Get("DevicePanel.DispositivoSelecionadoIndisponivelOuNenhum");
            return;
        }
        var format = device.MixFormat;
        string formatText = format is null
            ? StudioStrings.Get("DevicePanel.FormatoDeMixagemIndisponivel")
            : StudioStrings.Format("DevicePanel.0N0Hz1Canal", format.SampleRate, format.Channels, format.BitsPerSample, format.Encoding);
        information.Text = string.Join(Environment.NewLine + Environment.NewLine,
            device.Name,
            StudioStrings.Format("DevicePanel.Disponibilidade0", StateText(device.State)),
            StudioStrings.Format("DevicePanel.PadraoDeMultimidia0", (device.IsDefaultMultimedia ? StudioStrings.Get("DevicePanel.Sim") : StudioStrings.Get("DevicePanel.Nao"))),
            StudioStrings.Format("DevicePanel.PadraoDeComunicacoes0", (device.IsDefaultCommunications ? StudioStrings.Get("DevicePanel.Sim") : StudioStrings.Get("DevicePanel.Nao"))),
            formatText,
            StudioStrings.Format("DevicePanel.IDDoEndpoint0", device.Id),
            device.InformationError ?? "");
    }

    public ValueTask StopAsync() => endpointControls.StopAsync();

    public void ShowPreference(EndpointPreference? preference, bool busy = false, bool canSave = true)
    {
        saved = preference;
        preferenceBusy = busy;
        preferenceWritable = canSave;
        savedText.Text = preference is null ? StudioStrings.Get("DevicePanel.PreferenciaDeNiveisAindaNao")
            : StudioStrings.Format("DevicePanel.SalvoVolume0Mute1", Format(preference.VolumePercent, "%"), preference.Muted switch { true => StudioStrings.Get("DevicePanel.Ligado"), false => StudioStrings.Get("DevicePanel.Desligado"), _ => StudioStrings.Get("DevicePanel.Ausente") }, Format(preference.Balance, ""));
        savedText.AccessibleDescription = savedText.Text;
        savedText.AccessibleName = StudioStrings.Format("DevicePanel.PreferenciasSalvasDe01", Text, savedText.Text);
        UpdatePreferenceButtons();
    }

    private void UpdatePreferenceButtons()
    {
        bool available = displayed?.State == EndpointState.Active && !preferenceBusy;
        savePreference.Enabled = available && preferenceWritable;
        restorePreference.Enabled = available && saved is not null && saved.EndpointId == displayed?.Id && saved.Values.HasValues;
    }

    private static string Format(double? value, string suffix) => value is double number ? $"{number:F1}{suffix}" : StudioStrings.Get("DevicePanel.Ausente");

    public void ShowPeak(PeakReading reading)
    {
        peakBar.Enabled = reading.Scalar.HasValue;
        peakBar.Value = reading.Scalar is double value ? (int)Math.Round(value * 1000) : 0;
        string text = reading.Scalar switch
        {
            null => reading.Error ?? StudioStrings.Get("DevicePanel.PicoIndisponivel"),
            0 => StudioStrings.Get("DevicePanel.PicoDoWindowsSilencio0"),
            _ => StudioStrings.Format("DevicePanel.PicoDoWindows0F1", reading.Scalar * 100, reading.Decibels)
        };
        if (peakText.Text != text) peakText.Text = text;
    }

    private static string StateText(EndpointState state) => state switch
    {
        EndpointState.Active => StudioStrings.Get("DevicePanel.Disponivel"),
        EndpointState.Disabled => StudioStrings.Get("DevicePanel.DesabilitadoNoWindows"),
        EndpointState.NotPresent => "Ausente",
        EndpointState.Unplugged => StudioStrings.Get("DevicePanel.Desconectado"),
        _ => StudioStrings.Get("DevicePanel.EstadoDesconhecido")
    };

    private sealed record DeviceItem(DeviceSnapshot Device)
    {
        public string Label => $"{Device.Name} — {StateText(Device.State)}"
            + (Device.IsDefaultMultimedia ? StudioStrings.Get("DevicePanel.PadraoMultimidia") : "")
            + (Device.IsDefaultCommunications ? StudioStrings.Get("DevicePanel.PadraoComunicacoes") : "");
    }
}
