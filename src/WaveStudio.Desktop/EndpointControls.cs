using WaveStudio.Audio;

namespace WaveStudio.Desktop;

internal sealed class EndpointControls : UserControl
{
    private readonly EndpointVolumeSession session = new();
    private readonly System.Windows.Forms.Timer poll = new() { Interval = 600 };
    private readonly TrackBar volumeSlider = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Dock = DockStyle.Fill };
    private readonly NumericUpDown volumeNumber = new() { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Dock = DockStyle.Fill };
    private readonly TrackBar balanceSlider = new() { Minimum = -100, Maximum = 100, TickFrequency = 50, Dock = DockStyle.Fill };
    private readonly NumericUpDown balanceNumber = new() { Minimum = -100, Maximum = 100, Dock = DockStyle.Fill };
    private readonly Button mute = new() { AutoSize = true, Text = "Mute indisponível" };
    private readonly Button center = new() { AutoSize = true, Text = "Centralizar balanço" };
    private readonly Label message = new() { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(430, 0) };
    private EndpointSelection target = new(null, 0);
    private EndpointVolumeState? current;
    private string? volumeFailure;
    private string? muteFailure;
    private string? balanceFailure;
    private bool applyingSnapshot;
    private bool reading;
    private bool writing;
    private bool stopped;
    private long stateRevision;
    private bool volumeDirty;
    private bool balanceDirty;

    public EndpointControls()
    {
        Dock = DockStyle.Fill;
        AutoSize = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.Controls.Add(new Label { Text = "Volume (%)", AutoSize = true }, 0, 0);
        layout.Controls.Add(volumeSlider, 1, 0);
        layout.Controls.Add(volumeNumber, 2, 0);
        layout.Controls.Add(mute, 0, 1);
        layout.SetColumnSpan(mute, 3);
        layout.Controls.Add(new Label { Text = "Balanço L/R", AutoSize = true }, 0, 2);
        layout.Controls.Add(balanceSlider, 1, 2);
        layout.Controls.Add(balanceNumber, 2, 2);
        layout.Controls.Add(center, 0, 3);
        layout.SetColumnSpan(center, 3);
        layout.Controls.Add(message, 0, 4);
        layout.SetColumnSpan(message, 3);
        Controls.Add(layout);
        volumeSlider.AccessibleName = "Volume do endpoint do Windows em porcentagem";
        volumeNumber.AccessibleName = "Volume numérico do endpoint do Windows";
        balanceSlider.AccessibleName = "Balanço, negativo à esquerda e positivo à direita";
        balanceNumber.AccessibleName = "Balanço numérico";
        volumeSlider.Scroll += (_, _) => PreviewSlider(volumeSlider, volumeNumber);
        volumeSlider.MouseUp += async (_, _) => await CommitSliderAsync(true);
        volumeSlider.KeyUp += async (_, _) => await CommitSliderAsync(true);
        volumeSlider.MouseWheel += (_, _) => BeginInvoke(new Action(async () => await CommitSliderAsync(true)));
        volumeSlider.Leave += async (_, _) => await CommitSliderAsync(true);
        volumeNumber.ValueChanged += async (_, _) =>
        {
            if (!applyingSnapshot)
                await SendAsync(new SetEndpointVolume((double)volumeNumber.Value));
        };
        balanceSlider.Scroll += (_, _) => PreviewSlider(balanceSlider, balanceNumber);
        balanceSlider.MouseUp += async (_, _) => await CommitSliderAsync(false);
        balanceSlider.KeyUp += async (_, _) => await CommitSliderAsync(false);
        balanceSlider.MouseWheel += (_, _) => BeginInvoke(new Action(async () => await CommitSliderAsync(false)));
        balanceSlider.Leave += async (_, _) => await CommitSliderAsync(false);
        balanceNumber.ValueChanged += async (_, _) =>
        {
            if (!applyingSnapshot)
                await SendAsync(new SetEndpointBalance((double)balanceNumber.Value));
        };
        center.Click += async (_, _) => await SendAsync(new SetEndpointBalance(0));
        mute.Click += async (_, _) =>
        {
            if (current?.Muted is bool muted)
                await SendAsync(new SetEndpointMute(!muted));
        };
        poll.Tick += async (_, _) => await ReadAsync();
        DisableEditors();
    }

    public void SelectDevice(DeviceSnapshot? device)
    {
        if (stopped)
            return;
        target = session.Select(device?.State == EndpointState.Active ? device.Id : null);
        stateRevision++;
        volumeDirty = balanceDirty = false;
        current = null;
        volumeFailure = muteFailure = balanceFailure = null;
        DisableEditors();
        message.Text = target.DeviceId is null ? "Controles indisponíveis — selecione um dispositivo ativo." : "Consultando volume do Windows…";
        poll.Enabled = target.DeviceId is not null;
        _ = ReadAsync();
    }

    private async Task ReadAsync()
    {
        if (reading || writing || stopped || target.DeviceId is null || volumeSlider.Capture || balanceSlider.Capture || volumeDirty || balanceDirty)
            return;
        var requestedTarget = target;
        long requestedRevision = stateRevision;
        reading = true;
        try
        {
            var snapshot = await session.ReadAsync(requestedTarget);
            if (requestedTarget == target && requestedRevision == stateRevision && !stopped && !writing)
                ShowSnapshot(snapshot);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (requestedTarget == target && requestedRevision == stateRevision && !stopped && !writing)
                ShowError(error);
        }
        finally { reading = false; }
    }

    private async Task SendAsync(EndpointCommand command)
    {
        if (applyingSnapshot || writing || stopped || current is null || target.DeviceId is null)
            return;
        var requestedTarget = target;
        stateRevision++;
        writing = true;
        DisableEditors();
        message.Text = "Aplicando ao dispositivo selecionado…";
        try
        {
            var snapshot = await session.ApplyAsync(requestedTarget, command);
            if (requestedTarget == target && !stopped)
                ShowSnapshot(snapshot);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (requestedTarget == target && !stopped)
            {
                string reason = $"{EndpointVolumeOperations.Describe(error)} Use Atualizar dispositivos para tentar novamente.";
                switch (command)
                {
                    case SetEndpointVolume: volumeFailure = reason; break;
                    case SetEndpointMute: muteFailure = reason; break;
                    case SetEndpointBalance: balanceFailure = reason; break;
                }
                ShowError(error);
            }
        }
        finally { writing = false; }
    }

    private void ShowSnapshot(EndpointVolumeState snapshot)
    {
        current = snapshot;
        applyingSnapshot = true;
        try
        {
            if (snapshot.VolumePercent is double percent)
            {
                volumeSlider.Value = (int)Math.Round(percent);
                volumeNumber.Value = (decimal)Math.Round(percent, 1);
            }
            if (snapshot.Stereo is { } stereo)
            {
                int position = (int)Math.Round(stereo.Balance);
                balanceSlider.Value = position;
                balanceNumber.Value = position;
            }
            volumeSlider.Enabled = volumeNumber.Enabled = snapshot.VolumePercent.HasValue && volumeFailure is null;
            mute.Enabled = snapshot.Muted.HasValue && muteFailure is null;
            balanceSlider.Enabled = balanceNumber.Enabled = center.Enabled = snapshot.Stereo is not null && balanceFailure is null;
            mute.Text = snapshot.Muted switch { true => "Desativar mute", false => "Ativar mute", _ => "Mute indisponível" };
            var reasons = new[] { volumeFailure ?? snapshot.VolumeError, muteFailure ?? snapshot.MuteError,
                balanceFailure ?? snapshot.BalanceError }.Where(reason => reason is not null);
            message.Text = string.Join(Environment.NewLine, reasons);
            if (message.Text.Length == 0)
                message.Text = "Volume do Windows no endpoint. Balanço de −100 (esquerda) a +100 (direita).";
        }
        finally { applyingSnapshot = false; }
    }

    private void PreviewSlider(TrackBar slider, NumericUpDown number)
    {
        if (slider == volumeSlider) volumeDirty = true;
        else balanceDirty = true;
        stateRevision++;
        applyingSnapshot = true;
        try { number.Value = slider.Value; }
        finally { applyingSnapshot = false; }
    }

    private async Task CommitSliderAsync(bool isVolume)
    {
        if (isVolume && volumeDirty)
        {
            volumeDirty = false;
            await SendAsync(new SetEndpointVolume(volumeSlider.Value));
        }
        else if (!isVolume && balanceDirty)
        {
            balanceDirty = false;
            await SendAsync(new SetEndpointBalance(balanceSlider.Value));
        }
    }

    private void ShowError(Exception error)
    {
        current = null;
        DisableEditors();
        message.Text = EndpointVolumeOperations.Describe(error);
    }

    private void DisableEditors()
    {
        volumeSlider.Enabled = volumeNumber.Enabled = mute.Enabled = false;
        balanceSlider.Enabled = balanceNumber.Enabled = center.Enabled = false;
    }

    public async ValueTask StopAsync()
    {
        if (stopped)
        {
            await session.DisposeAsync();
            return;
        }
        stopped = true;
        poll.Stop();
        await session.DisposeAsync();
        poll.Dispose();
    }
}
