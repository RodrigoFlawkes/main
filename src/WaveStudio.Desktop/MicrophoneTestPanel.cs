using WaveStudio.Audio;
using WaveStudio.Audio.Dsp;

namespace WaveStudio.Desktop;

internal sealed class MicrophoneTestPanel : UserControl
{
    private readonly MicrophoneTestSession session;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private readonly Label devices = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill, AccessibleName = "Estado do teste de microfone" };
    private readonly Label time = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Button record = new() { Text = "&Gravar teste", AutoSize = true };
    private readonly Button stop = new() { Text = "&Parar", AutoSize = true };
    private readonly Button play = new() { Text = "&Ouvir gravação", AutoSize = true };
    private readonly Button playEqualized = new() { Text = "Ouvir com &EQ", AutoSize = true };
    private readonly EqualizerPreviewPanel equalizer = new();
    private readonly Label protection = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private readonly Button stopPlayback = new() { Text = "Parar &reprodução", AutoSize = true };
    private readonly Button clear = new() { Text = "&Limpar teste", AutoSize = true };
    private readonly TrackBar level = new() { Minimum = 0, Maximum = 100, Value = 20, TickFrequency = 10, Width = 270, AccessibleName = "Nível digital da reprodução do teste" };
    private readonly Label levelValue = new() { Text = "20%", AutoSize = true };
    private readonly MicrophoneInputMeter inputMeter = new();
    private string? inputId;
    private string? outputId;
    private bool stopped;
    private bool timerDisposed;
    private int pending;
    private MixFormatSnapshot? inputFormat;
    private MixFormatSnapshot? outputFormat;
    internal MicrophoneTestSnapshot Snapshot => session.Latest;
    internal Button RecordButton => record;
    internal Button PlayButton => play;
    internal TrackBar TestLevel => level;
    internal Button PlayEqualizedButton => playEqualized;
    internal EqualizerPreviewPanel Equalizer => equalizer;
    internal MicrophoneInputMeter InputMeter => inputMeter;

    public MicrophoneTestPanel(MicrophoneTestSession? session = null)
    {
        this.session = session ?? new MicrophoneTestSession();
        Dock = DockStyle.Fill;
        AutoScroll = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(18), ColumnCount = 1, MinimumSize = new Size(480, 0) };
        void Add(Control control) => layout.Controls.Add(control, 0, layout.RowCount++);
        Add(new Label { Text = "Testar microfone", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        Add(new Label { Text = "Use fones e volume baixo. A gravação dura no máximo 10 segundos e só começa ao clicar em Gravar teste.", AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(3, 14, 3, 14) });
        Add(devices);
        Add(inputMeter);
        Add(new Label { Text = "Escolha entrada e saída na aba Dispositivos. Confira volume e mute do Windows antes de gravar/ouvir.", AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(3, 10, 3, 10) });
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        buttons.Controls.AddRange([record, stop, play, playEqualized, stopPlayback, clear]);
        Add(buttons);
        Add(new Label { Text = "Ouvir gravação = original (sem EQ). Ouvir com EQ = versão processada da mesma gravação, sem gravar novamente.", AutoSize = true, MaximumSize = new Size(700, 0) });
        Add(new Label { Text = "Nível digital para ouvir o teste (0–100%, sem amplificação). Não altera o volume do Windows.", AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(3, 16, 3, 3) });
        var levels = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        levels.Controls.AddRange([level, levelValue]);
        Add(levels);
        Add(time);
        Add(status);
        Add(protection);
        Add(equalizer);
        Add(new Label { Text = "Áudio somente em memória: apagado ao limpar, trocar/atualizar dispositivos, suspender ou fechar. Sem arquivo, envio ou reprodução automática. Não é monitoramento ao vivo.", AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(3, 20, 3, 3) });
        Controls.Add(layout);
        record.Click += async (_, _) => await RunAsync(this.session.RecordAsync);
        stop.Click += async (_, _) => await RunAsync(this.session.StopAsync);
        play.Click += async (_, _) => await RunAsync(() => this.session.PlayAsync(level.Value / 100f));
        playEqualized.Click += async (_, _) =>
        {
            var settings = equalizer.Settings;
            await RunAsync(() => this.session.PlayEqualizedAsync(level.Value / 100f, settings));
        };
        stopPlayback.Click += async (_, _) => await RunAsync(this.session.StopAsync);
        clear.Click += async (_, _) => await RunAsync(this.session.ClearAsync);
        level.ValueChanged += (_, _) => levelValue.Text = $"{level.Value}%";
        timer.Tick += (_, _) => { ShowSnapshot(); inputMeter.RefreshInvalidation(); };
        timer.Start();
        ShowSnapshot();
    }

    public void SelectDevices(DeviceSnapshot? input, DeviceSnapshot? output, bool invalidate = false)
    {
        if (stopped) return;
        string? nextInput = input?.State == EndpointState.Active ? input.Id : null;
        string? nextOutput = output?.State == EndpointState.Active ? output.Id : null;
        devices.Text = $"Microfone: {input?.Name ?? "indisponível"}{(nextInput is null ? " (indisponível)" : "")}\nSaída: {output?.Name ?? "indisponível"}{(nextOutput is null ? " (indisponível)" : "")}";
        if (!invalidate && nextInput == inputId && nextOutput == outputId && inputFormat == input?.MixFormat && outputFormat == output?.MixFormat) return;
        inputId = nextInput;
        outputId = nextOutput;
        inputFormat = input?.MixFormat;
        outputFormat = output?.MixFormat;
        inputMeter.InvalidateSelection();
        inputMeter.RefreshInvalidation();
        _ = RunAsync(() => session.SelectAsync(inputId, outputId));
    }

    // May be invoked directly by the notification/power callback. No WinForms access here.
    public Task InvalidateAsync()
    {
        inputMeter.InvalidateSelection();
        return session.SelectAsync(null, null);
    }

    public void BindInputMeter(AudioWatchSelection target) => inputMeter.BindSelection(target, inputId);
    public void EnableInputMeter(bool enabled) => inputMeter.SetMeteringEnabled(enabled);
    public void ShowInputPeak(AudioWatchSnapshot? snapshot, long now) => inputMeter.ShowSnapshot(snapshot, now);

    private async Task RunAsync(Func<Task> action)
    {
        if (stopped) return;
        pending++;
        ShowSnapshot();
        bool failed = false;
        try { await action(); }
        catch (Exception) { failed = true; }
        finally
        {
            pending--;
            if (!stopped)
            {
                ShowSnapshot();
                if (failed) status.Text = "Não foi possível completar a operação. Atualize os dispositivos e tente novamente.";
            }
        }
    }

    private void ShowSnapshot()
    {
        var value = session.Latest;
        bool busy = value.State is MicrophoneTestState.Recording or MicrophoneTestState.Playing or MicrophoneTestState.Stopping;
        record.Enabled = pending == 0 && !stopped && value.CanRecord;
        play.Enabled = pending == 0 && !stopped && value.CanPlay;
        bool eqSupported = value.RecordedSampleRate is not null ? value.EqualizerSupported
            : inputFormat is not null && GraphicEqualizerSettings.SupportsRate(inputFormat.SampleRate);
        equalizer.SetAvailability(eqSupported, stopped || pending != 0 || busy);
        playEqualized.Enabled = play.Enabled && eqSupported && equalizer.Activate.Checked;
        clear.Enabled = pending == 0 && !stopped && value.CanClear;
        stop.Enabled = !stopped && value.State == MicrophoneTestState.Recording;
        stopPlayback.Enabled = !stopped && value.State == MicrophoneTestState.Playing;
        level.Enabled = !stopped && pending == 0 && !busy;
        status.Text = value.Message;
        status.ForeColor = value.State == MicrophoneTestState.Recording ? Color.Firebrick : SystemColors.ControlText;
        time.Text = $"Gravado: {value.RecordedDuration.TotalSeconds:F1} / 10 s · Tempo da operação: {Math.Min(12, value.Elapsed.TotalSeconds):F1} s";
        protection.Text = $"Última reprodução: {value.PlaybackMode ?? "nenhuma"} · Amostras limitadas no EQ: {value.LimitedSamples} · Dados inválidos tratados: {value.SanitizedSamples}";
    }

    public async ValueTask StopAsync()
    {
        if (timerDisposed) return;
        stopped = true;
        inputMeter.Stop();
        timer.Stop();
        ShowSnapshot();
        try
        {
            await session.DisposeAsync();
            timer.Dispose();
            timerDisposed = true;
        }
        catch
        {
            if (!IsDisposed) status.Text = session.Latest.Message;
            throw;
        }
    }
}
