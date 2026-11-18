using WaveStudio.Audio;

namespace WaveStudio.Desktop;

// Apenas apresentação. Recebe os estados do worker de endpoints existente, sem acessar o stream de captura.
internal sealed class MicrophoneInputMeter : GroupBox
{
    private readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100, Height = 24, Dock = DockStyle.Fill,
        AccessibleName = "Nível de entrada do microfone em percentual de pico" };
    private readonly Label reading = new() { AutoSize = true, Dock = DockStyle.Fill,
        MaximumSize = new Size(700, 0), AccessibleName = "Leitura do nível de entrada do microfone" };
    private AudioWatchSelection? selection;
    private string? inputId;
    private int invalidated = 1;
    private bool measuring;
    private bool stopped;
    internal ProgressBar PeakBar => bar;
    internal string ReadingText => reading.Text;
    internal bool MeteringEnabled => measuring && !stopped;

    public MicrophoneInputMeter()
    {
        Text = "Nível de entrada do microfone";
        AccessibleName = Text;
        Dock = DockStyle.Fill;
        AutoSize = true;
        Padding = new Padding(12, 24, 12, 12);
        Margin = new Padding(3, 12, 3, 12);
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 3 };
        layout.Controls.Add(bar, 0, 0);
        layout.Controls.Add(reading, 0, 1);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(700, 0), Dock = DockStyle.Fill,
            Text = "Pico de amplitude de 0 a 100%: não identifica voz, inteligibilidade ou zumbido. Alguns drivers só informam nível durante captura ativa; confira também ao clicar em Gravar teste." }, 0, 2);
        Controls.Add(layout);
        ShowSnapshot(null, 0);
    }

    public void BindSelection(AudioWatchSelection target, string? selectedInputId)
    {
        selection = target;
        inputId = selectedInputId;
        Volatile.Write(ref invalidated, 0);
        ShowSnapshot(null, 0);
    }

    // Callbacks de notificação ou de energia podem invalidar o estado sem acessar WinForms.
    public void InvalidateSelection() => Volatile.Write(ref invalidated, 1);
    public void RefreshInvalidation() { if (Volatile.Read(ref invalidated) != 0) ShowSnapshot(null, 0); }

    public void SetMeteringEnabled(bool enabled)
    {
        if (measuring == enabled) return;
        measuring = enabled;
        ShowSnapshot(null, 0);
    }

    public void ShowSnapshot(AudioWatchSnapshot? snapshot, long now)
    {
        if (stopped) { Render(null, "Medição encerrada."); return; }
        if (!measuring) { Render(null, "Medição pausada — abra esta aba com a janela restaurada."); return; }
        if (inputId is null) { Render(null, "Microfone selecionado indisponível."); return; }
        if (Volatile.Read(ref invalidated) != 0 || snapshot is null || snapshot.Selection != selection || selection?.CaptureId != inputId)
        {
            Render(null, "Nível indisponível — aguardando leitura da seleção atual.");
            return;
        }
        var current = snapshot.ForDisplay(now);
        var peak = current.Capture.Scalar is double scalar ? PeakReading.FromScalar(scalar) : current.Capture;
        string text = current.WorkerFailed ? current.NotificationStatus
            : peak.Scalar switch
            {
                null => peak.Error ?? "Nível de entrada indisponível.",
                0 => "Pico de entrada — 0% (nenhum pico detectado nesta leitura).",
                double value => $"Pico de entrada — {value * 100:F1}%"
            };
        Render(current.WorkerFailed ? null : peak.Scalar, text);
    }

    private void Render(double? value, string text)
    {
        bar.Enabled = value.HasValue;
        bar.Value = value is double scalar ? (int)Math.Round(scalar * 100) : 0;
        if (reading.Text != text) reading.Text = text;
        reading.AccessibleDescription = bar.AccessibleDescription = text;
    }

    public void Stop() { stopped = true; ShowSnapshot(null, 0); }
}
