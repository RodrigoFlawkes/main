using WaveStudio.Audio;

namespace WaveStudio.Desktop;

internal sealed class DevicePanel : GroupBox
{
    private readonly ComboBox selector = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox information = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window
    };
    private readonly EndpointControls endpointControls = new();
    private bool binding;
    private DeviceSnapshot? displayed;
    private readonly ProgressBar peakBar = new() { Minimum = 0, Maximum = 1000, Dock = DockStyle.Fill, Height = 14 };
    private readonly Label peakText = new() { AutoSize = true, Text = "Pico indisponível", Dock = DockStyle.Fill };

    public event Action<DeviceSnapshot>? DeviceSelected;

    public DevicePanel(string title)
    {
        Text = title;
        Dock = DockStyle.Fill;
        Padding = new Padding(12, 24, 12, 12);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, AutoScroll = true };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selector.DisplayMember = nameof(DeviceItem.Label);
        selector.AccessibleName = title;
        information.AccessibleName = $"Informações de {title}";
        layout.Controls.Add(selector, 0, 0);
        layout.Controls.Add(information, 0, 1);
        layout.Controls.Add(endpointControls, 0, 2);
        layout.Controls.Add(peakBar, 0, 3);
        layout.Controls.Add(peakText, 0, 4);
        peakBar.AccessibleName = $"Pico real do Windows — {title}";
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

    public void ShowDevices(DeviceCatalog catalog, EndpointDirection direction, DeviceSnapshot? selected, bool resetControls = true)
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
    }

    public void ShowUnavailable(string message)
    {
        selector.Items.Clear();
        selector.Enabled = false;
        information.Text = message;
        endpointControls.SelectDevice(null);
        displayed = null;
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
        if (device is null)
        {
            information.Text = "Dispositivo selecionado indisponível ou nenhum dispositivo encontrado. Escolha uma opção da lista ou atualize.";
            return;
        }
        var format = device.MixFormat;
        string formatText = format is null
            ? "Formato de mixagem indisponível"
            : $"{format.SampleRate:N0} Hz • {format.Channels} canal(is) • {format.BitsPerSample} bits • {format.Encoding}";
        information.Text = string.Join(Environment.NewLine + Environment.NewLine,
            device.Name,
            $"Disponibilidade — {StateText(device.State)}",
            $"Padrão de multimídia — {(device.IsDefaultMultimedia ? "Sim" : "Não")}",
            $"Padrão de comunicações — {(device.IsDefaultCommunications ? "Sim" : "Não")}",
            formatText,
            $"ID do endpoint — {device.Id}",
            device.InformationError ?? "");
    }

    public ValueTask StopAsync() => endpointControls.StopAsync();

    public void ShowPeak(PeakReading reading)
    {
        peakBar.Enabled = reading.Scalar.HasValue;
        peakBar.Value = reading.Scalar is double value ? (int)Math.Round(value * 1000) : 0;
        string text = reading.Scalar switch
        {
            null => reading.Error ?? "Pico indisponível",
            0 => "Pico do Windows — silêncio (0%)",
            _ => $"Pico do Windows — {reading.Scalar * 100:F1}% ({reading.Decibels:F1} dBFS)"
        };
        if (peakText.Text != text) peakText.Text = text;
    }

    private static string StateText(EndpointState state) => state switch
    {
        EndpointState.Active => "Disponível",
        EndpointState.Disabled => "Desabilitado no Windows",
        EndpointState.NotPresent => "Ausente",
        EndpointState.Unplugged => "Desconectado",
        _ => "Estado desconhecido"
    };

    private sealed record DeviceItem(DeviceSnapshot Device)
    {
        public string Label => $"{Device.Name} — {StateText(Device.State)}"
            + (Device.IsDefaultMultimedia ? " • Padrão multimídia" : "")
            + (Device.IsDefaultCommunications ? " • Padrão comunicações" : "");
    }
}
