using WaveStudio.Audio.Dsp;

namespace WaveStudio.Desktop;

internal sealed class EqualizerPreviewPanel : UserControl
{
    internal CheckBox Activate { get; } = new() { Text = "Ativar EQ nesta gravação", AutoSize = true };
    internal TrackBar[] Bands { get; } = new TrackBar[10];
    internal NumericUpDown Preamp { get; } = new() { Minimum = -30, Maximum = 0, DecimalPlaces = 1, Increment = 1, Width = 75, AccessibleName = "Preamp do EQ em dB" };
    internal Button ResetButton { get; } = new() { Text = "Zerar EQ e preamp", AutoSize = true };
    private readonly FlowLayoutPanel bands = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
    private readonly Label safety = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private readonly Label availability = new() { AutoSize = true };
    internal bool Supported { get; private set; }
    public GraphicEqualizerSettings Settings => new(Activate.Checked, Bands.Select(b => (double)b.Value), (double)Preamp.Value);

    public EqualizerPreviewPanel()
    {
        AutoSize = true; Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Padding = new Padding(0, 12, 0, 8) };
        void Add(Control item) => layout.Controls.Add(item, 0, layout.RowCount++);
        Add(new Label { Text = "EQ de 10 bandas — prévia da gravação", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        Add(new Label { Text = "Compare a mesma gravação original e processada. Não afeta o áudio de outros aplicativos.", AutoSize = true, MaximumSize = new Size(700, 0) });
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
            slider.ValueChanged += (_, _) => { value.Text = $"{slider.Value:+0;-0;0} dB"; ShowSafety(); };
        }
        var preampRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        preampRow.Controls.AddRange([new Label { Text = "Preamp (−30 a 0 dB):", AutoSize = true }, Preamp, ResetButton]);
        Add(preampRow); Add(safety);
        Add(new Label { Text = "Ajustes são congelados ao iniciar. Pare para alterar. A proteção final é hard clip, não um limiter.", AutoSize = true, MaximumSize = new Size(700, 0) });
        Controls.Add(layout);
        Activate.CheckedChanged += (_, _) => { UpdateControls(); ShowSafety(); };
        Preamp.ValueChanged += (_, _) => ShowSafety();
        ResetButton.Click += (_, _) => { foreach (var band in Bands) band.Value = 0; Preamp.Value = 0; ShowSafety(); };
        ShowSafety(); SetAvailability(false, false);
    }
    public void SetAvailability(bool supported, bool frozen)
    {
        Supported = supported;
        Enabled = !frozen;
        availability.Text = supported ? "Disponível em 44,1/48 kHz. Original continua sem EQ." : "EQ indisponível neste formato: requer gravação em 44,1 ou 48 kHz. Ouvir original permanece disponível.";
        UpdateControls();
    }
    private void UpdateControls()
    {
        Activate.Enabled = Supported;
        bands.Enabled = Preamp.Enabled = ResetButton.Enabled = Supported && Activate.Checked;
    }
    private void ShowSafety()
    {
        var settings = Settings;
        safety.Text = $"Atenuação automática de segurança: −{settings.HeadroomDb:F1} dB (soma dos boosts). Preamp efetivo com EQ: {settings.EffectivePreampDb:F1} dB. Pode soar mais baixo; não aumente o volume do Windows para compensar.";
    }
}
