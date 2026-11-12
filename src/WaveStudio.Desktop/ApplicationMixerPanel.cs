using System.Globalization;
using WaveStudio.Audio;

namespace WaveStudio.Desktop;

internal sealed class ApplicationMixerPanel : UserControl
{
    private readonly ApplicationSessionService service;
    private readonly Dictionary<SessionKey, DataGridViewRow> rows = new();
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
    private bool binding;
    private ApplicationSessionBinding? edit;
    internal DataGridView SessionGrid => grid;
    internal EndpointSelection Selection => target;
    internal Task PendingRead { get; private set; } = Task.CompletedTask;

    public ApplicationMixerPanel() : this(new ApplicationSessionService()) { }
    internal ApplicationMixerPanel(ApplicationSessionService service)
    {
        this.service = service;
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
            if (!binding && ValidCell(args.RowIndex, args.ColumnIndex) && grid.Columns[args.ColumnIndex].Name == "Volume" && grid.IsCurrentCellInEditMode
                && (!double.TryParse(Convert.ToString(args.FormattedValue), out double value) || !double.IsFinite(value) || value < 0 || value > 100))
            {
                args.Cancel = true;
                message.Text = "Informe um volume entre 0 e 100%. Use Esc para cancelar a edição.";
            }
        };
        grid.CellBeginEdit += (_, args) =>
        {
            edit = ValidCell(args.RowIndex, args.ColumnIndex) ? grid.Rows[args.RowIndex].Tag as ApplicationSessionBinding : null;
            args.Cancel = binding || edit is null || !edit.IsCurrent(target);
        };
        grid.CellEndEdit += async (_, args) =>
        {
            var editing = edit;
            edit = null;
            if (binding || !ValidCell(args.RowIndex, args.ColumnIndex) || editing is null || !editing.IsCurrent(target)) return;
            var row = grid.Rows[args.RowIndex];
            if (grid.Columns[args.ColumnIndex].Name == "Volume" && row.Tag is ApplicationSessionBinding bound
                && bound.Snapshot.Key == editing.Snapshot.Key && bound.IsCurrent(target)
                && double.TryParse(Convert.ToString(row.Cells["Volume"].Value), out double value)
                && value != Math.Round(editing.Snapshot.VolumePercent ?? 0, 1))
                await ApplyAsync(editing, new SetSessionVolume(value));
        };
        grid.CellContentClick += async (_, args) =>
        {
            if (!binding && ValidCell(args.RowIndex, args.ColumnIndex) && grid.Columns[args.ColumnIndex].Name == "Mute"
                && grid.Rows[args.RowIndex].Tag is ApplicationSessionBinding bound && bound.IsCurrent(target)
                && bound.Snapshot is { Muted: bool muted, Activity: not SessionActivity.Expired })
                await ApplyAsync(bound, new SetSessionMute(!muted));
        };
        grid.DataError += (_, args) => { args.ThrowException = false; message.Text = "Valor inválido. Revise a edição ou pressione Esc."; };
        refresh.Click += async (_, _) => { commandError = null; await (PendingRead = ReadAsync()); };
        poll.Tick += async (_, _) => { if (Visible) await (PendingRead = ReadAsync()); };
    }

    public void SelectDevice(DeviceSnapshot? device)
    {
        if (stopped) return;
        // Cancel edits before changing the selection, so CellEndEdit cannot target the next endpoint.
        binding = true;
        try
        {
            edit = null;
            grid.CancelEdit();
            grid.EndEdit();
            target = service.Select(device?.State == EndpointState.Active ? device.Id : null);
            grid.Rows.Clear();
            rows.Clear();
        }
        finally { binding = false; }
        revision++;
        commandError = null;
        endpointName.Text = device is null ? "Reprodução indisponível" : $"Sessões de reprodução — {device.Name}";
        message.Text = "Selecione um dispositivo de reprodução disponível.";
        poll.Enabled = target.DeviceId is not null;
        PendingRead = ReadAsync();
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
                ShowCatalog(request, catalog);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (request == target && requestedRevision == revision && !writing && !stopped)
            {
                ClearRows();
                message.Text = EndpointVolumeOperations.Describe(error);
            }
        }
        finally { reading = false; }
    }

    internal void ShowCatalog(EndpointSelection request, ApplicationSessionCatalog catalog)
    {
        if (request != target || stopped || grid.IsCurrentCellInEditMode) return;
        binding = true;
        try
        {
            var keys = catalog.Sessions.Select(item => item.Key).ToHashSet();
            foreach (var key in rows.Keys.Where(key => !keys.Contains(key)).ToArray())
            {
                grid.Rows.Remove(rows[key]);
                rows.Remove(key);
            }
            foreach (var snapshot in catalog.Sessions)
            {
                if (snapshot.Key.EndpointId != request.DeviceId) continue;
                string state = snapshot.Activity switch { SessionActivity.Active => "Ativa", SessionActivity.Expired => "Encerrada", _ => "Inativa" };
                if (!rows.TryGetValue(snapshot.Key, out var row))
                {
                    row = grid.Rows[grid.Rows.Add()];
                    rows.Add(snapshot.Key, row);
                }
                SetCell(row, "App", snapshot.Name);
                SetCell(row, "PID", snapshot.ProcessId?.ToString() ?? "Indisponível");
                SetCell(row, "State", state);
                SetCell(row, "Volume", snapshot.VolumePercent?.ToString("F1", CultureInfo.CurrentCulture) ?? "Indisponível");
                SetCell(row, "Mute", snapshot.Muted switch { true => "Desativar mute", false => "Ativar mute", _ => "Indisponível" });
                SetCell(row, "Instance", snapshot.Key.InstanceId);
                row.Tag = new ApplicationSessionBinding(request, snapshot);
                row.Cells["Volume"].ReadOnly = snapshot.VolumePercent is null || snapshot.Activity == SessionActivity.Expired;
                row.ErrorText = snapshot.Error ?? "";
            }
        }
        finally { binding = false; }
        message.Text = catalog.Warnings.Count > 0 ? string.Join(Environment.NewLine, catalog.Warnings.Distinct())
            : catalog.Sessions.Count == 0 ? "Nenhuma sessão encontrada neste dispositivo."
            : $"{catalog.Sessions.Count} sessão(ões). Edite o volume e confirme com Enter. Volume da sessão do aplicativo.";
        if (commandError is not null) message.Text = commandError;
    }

    private bool ValidCell(int row, int column) => row >= 0 && row < grid.Rows.Count && column >= 0 && column < grid.Columns.Count;

    private static void SetCell(DataGridViewRow row, string name, string value)
    {
        if (!Equals(row.Cells[name].Value, value)) row.Cells[name].Value = value;
    }

    private void ClearRows()
    {
        binding = true;
        try { edit = null; grid.CancelEdit(); grid.EndEdit(); grid.Rows.Clear(); rows.Clear(); }
        finally { binding = false; }
    }

    private async Task ApplyAsync(ApplicationSessionBinding bound, ApplicationSessionCommand command)
    {
        if (binding || writing || stopped || !bound.IsCurrent(target)) return;
        writing = true;
        revision++;
        var request = target;
        grid.Enabled = false;
        try
        {
            await service.ApplyAsync(request, bound.Snapshot.Key, command);
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
        if (!stopped) PendingRead = ReadAsync();
    }

    public async ValueTask StopAsync()
    {
        stopped = true;
        poll.Stop();
        binding = true;
        try { edit = null; grid.CancelEdit(); grid.EndEdit(); }
        finally { binding = false; }
        await service.DisposeAsync();
        poll.Dispose();
    }
}
