using System.Globalization;
using WaveStudio.Audio;

namespace WaveStudio.Desktop;

internal sealed class ApplicationMixerPanel : UserControl
{
    private readonly ApplicationSessionService service = new();
    private readonly System.Windows.Forms.Timer poll = new() { Interval = 1000 };
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoGenerateColumns = false, RowHeadersVisible = false, MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly Label message = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label endpointName = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Button refresh = new() { Text = "Atualizar mixer", AutoSize = true };
    private EndpointSelection target = new(null, 0);
    private bool reading;
    private bool writing;
    private long revision;
    private bool stopped;
    private string? commandError;

    public ApplicationMixerPanel()
    {
        Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "App", HeaderText = "Aplicativo", ReadOnly = true, MinimumWidth = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "PID", HeaderText = "PID", ReadOnly = true, MinimumWidth = 55, FillWeight = 30 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "Estado", ReadOnly = true, MinimumWidth = 80, FillWeight = 45 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Volume", HeaderText = "Volume (%)", MinimumWidth = 85, FillWeight = 40 });
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "Mute", HeaderText = "Mute", MinimumWidth = 110, FillWeight = 65 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Instance", HeaderText = "Instância da sessão", ReadOnly = true, MinimumWidth = 150 });
        layout.Controls.Add(endpointName, 0, 0);
        layout.Controls.Add(refresh, 0, 1);
        layout.Controls.Add(grid, 0, 2);
        layout.Controls.Add(message, 0, 3);
        Controls.Add(layout);
        grid.AccessibleName = "Mixer de sessões do dispositivo de reprodução";
        grid.CellValidating += (_, args) =>
        {
            if (grid.Columns[args.ColumnIndex].Name == "Volume" && grid.IsCurrentCellInEditMode
                && (!double.TryParse(Convert.ToString(args.FormattedValue), out double value) || !double.IsFinite(value) || value < 0 || value > 100))
            {
                args.Cancel = true;
                message.Text = "Informe um volume entre 0 e 100%. Use Esc para cancelar a edição.";
            }
        };
        grid.CellEndEdit += async (_, args) =>
        {
            var row = grid.Rows[args.RowIndex];
            if (grid.Columns[args.ColumnIndex].Name == "Volume" && row.Tag is ApplicationSessionSnapshot snapshot
                && double.TryParse(Convert.ToString(row.Cells["Volume"].Value), out double value)
                && value != Math.Round(snapshot.VolumePercent ?? 0, 1))
                await ApplyAsync(snapshot, new SetSessionVolume(value));
        };
        grid.CellContentClick += async (_, args) =>
        {
            if (args.RowIndex >= 0 && grid.Columns[args.ColumnIndex].Name == "Mute"
                && grid.Rows[args.RowIndex].Tag is ApplicationSessionSnapshot { Muted: bool muted } snapshot
                && snapshot.Activity != SessionActivity.Expired)
                await ApplyAsync(snapshot, new SetSessionMute(!muted));
        };
        grid.DataError += (_, args) => { args.ThrowException = false; message.Text = "Valor inválido. Revise a edição ou pressione Esc."; };
        refresh.Click += async (_, _) => { commandError = null; await ReadAsync(); };
        poll.Tick += async (_, _) => { if (Visible) await ReadAsync(); };
    }

    public void SelectDevice(DeviceSnapshot? device)
    {
        if (stopped) return;
        // Cancel edits before changing the selection, so CellEndEdit cannot target the next endpoint.
        grid.CancelEdit();
        target = service.Select(device?.State == EndpointState.Active ? device.Id : null);
        revision++;
        commandError = null;
        grid.Rows.Clear();
        endpointName.Text = device is null ? "Reprodução indisponível" : $"Sessões de reprodução — {device.Name}";
        message.Text = "Selecione um dispositivo de reprodução disponível.";
        poll.Enabled = target.DeviceId is not null;
        _ = ReadAsync();
    }

    private async Task ReadAsync()
    {
        if (reading || writing || stopped || target.DeviceId is null || grid.IsCurrentCellInEditMode) return;
        reading = true;
        var request = target;
        long requestedRevision = revision;
        try
        {
            var catalog = await service.ReadAsync(request);
            if (request == target && requestedRevision == revision && !writing && !stopped && !grid.IsCurrentCellInEditMode)
                ShowCatalog(catalog);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (request == target && requestedRevision == revision && !writing && !stopped)
            {
                grid.Rows.Clear();
                message.Text = EndpointVolumeOperations.Describe(error);
            }
        }
        finally { reading = false; }
    }

    private void ShowCatalog(ApplicationSessionCatalog catalog)
    {
        SessionKey? focused = (grid.CurrentRow?.Tag as ApplicationSessionSnapshot)?.Key;
        int scroll = grid.FirstDisplayedScrollingRowIndex;
        grid.Rows.Clear();
        foreach (var snapshot in catalog.Sessions)
        {
            string state = snapshot.Activity switch { SessionActivity.Active => "Ativa", SessionActivity.Expired => "Encerrada", _ => "Inativa" };
            int index = grid.Rows.Add(snapshot.Name, snapshot.ProcessId?.ToString() ?? "Indisponível", state,
                snapshot.VolumePercent?.ToString("F1", CultureInfo.CurrentCulture) ?? "Indisponível",
                snapshot.Muted switch { true => "Desativar mute", false => "Ativar mute", _ => "Indisponível" }, snapshot.Key.InstanceId);
            var row = grid.Rows[index];
            row.Tag = snapshot;
            row.Cells["Volume"].ReadOnly = snapshot.VolumePercent is null || snapshot.Activity == SessionActivity.Expired;
            row.ErrorText = snapshot.Error ?? "";
            if (snapshot.Key == focused) grid.CurrentCell = row.Cells["Volume"];
        }
        if (scroll >= 0 && scroll < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = scroll;
        message.Text = catalog.Warnings.Count > 0 ? string.Join(Environment.NewLine, catalog.Warnings.Distinct())
            : catalog.Sessions.Count == 0 ? "Nenhuma sessão encontrada neste dispositivo."
            : $"{catalog.Sessions.Count} sessão(ões). Edite o volume e confirme com Enter. Volume da sessão do aplicativo.";
        if (commandError is not null) message.Text = commandError;
    }

    private async Task ApplyAsync(ApplicationSessionSnapshot snapshot, ApplicationSessionCommand command)
    {
        if (writing || stopped || snapshot.Key.EndpointId != target.DeviceId) return;
        writing = true;
        revision++;
        var request = target;
        grid.Enabled = false;
        try
        {
            await service.ApplyAsync(request, snapshot.Key, command);
            if (request == target && !stopped) commandError = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (request == target && !stopped)
                message.Text = commandError = EndpointVolumeOperations.Describe(error);
        }
        finally
        {
            writing = false;
            if (!stopped) grid.Enabled = true;
        }
        // Preserve command errors across polling until another explicit action.
    }

    public async ValueTask StopAsync()
    {
        stopped = true;
        poll.Stop();
        await service.DisposeAsync();
        poll.Dispose();
    }
}
