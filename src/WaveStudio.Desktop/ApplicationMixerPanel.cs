using System.Globalization;
using System.Diagnostics;
using WaveStudio.Audio;

namespace WaveStudio.Desktop;

internal sealed partial class ApplicationMixerPanel : UserControl
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
    private readonly Button refresh = new() { Text = StudioStrings.Get("ApplicationMixerPanel.AtualizarMixer"), AutoSize = true };
    private EndpointSelection target = new(null, 0);
    private bool reading;
    private bool readRequested;
    private bool writing;
    private long revision;
    private bool stopped;
    private string? commandError;
    private bool binding;
    private ApplicationSessionBinding? edit;
    internal DataGridView SessionGrid => grid;
    internal EndpointSelection Selection => target;
    internal Task PendingRead { get; private set; } = Task.CompletedTask;

    public ApplicationMixerPanel() : this(new ApplicationSessionService(), new EndpointVolumeSession()) { }
    internal ApplicationMixerPanel(ApplicationSessionService service, EndpointVolumeSession masterService)
    {
        this.service = service;
        this.masterService = masterService;
        Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "App", HeaderText = StudioStrings.Get("ApplicationMixerPanel.Aplicativo"), ReadOnly = true, MinimumWidth = 140 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "PID", HeaderText = StudioStrings.Get("ApplicationMixerPanel.PID"), ReadOnly = true, MinimumWidth = 55, FillWeight = 30 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = StudioStrings.Get("ApplicationMixerPanel.Estado"), ReadOnly = true, MinimumWidth = 80, FillWeight = 45 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Peak", HeaderText = StudioStrings.Get("ApplicationMixerPanel.Pico1S"), ReadOnly = true, MinimumWidth = 100, FillWeight = 45 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Volume", HeaderText = StudioStrings.Get("ApplicationMixerPanel.Volume"), MinimumWidth = 85, FillWeight = 40 });
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "Mute", HeaderText = StudioStrings.Get("ApplicationMixerPanel.Mute"), MinimumWidth = 110, FillWeight = 65 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Instance", HeaderText = StudioStrings.Get("ApplicationMixerPanel.InstanciaDaSessao"), ReadOnly = true, MinimumWidth = 150 });
        layout.Controls.Add(endpointName, 0, 0);
        layout.Controls.Add(refresh, 0, 1);
        layout.Controls.Add(CreateViews(), 0, 2);
        layout.Controls.Add(message, 0, 3);
        Controls.Add(layout);
        grid.AccessibleName = StudioStrings.Get("ApplicationMixerPanel.MixerDeSessoesDoDispositivo");
        grid.CellValidating += (_, args) =>
        {
            if (!binding && ValidCell(args.RowIndex, args.ColumnIndex) && grid.Columns[args.ColumnIndex].Name == "Volume" && grid.IsCurrentCellInEditMode
                && (!double.TryParse(Convert.ToString(args.FormattedValue), out double value) || !double.IsFinite(value) || value < 0 || value > 100))
            {
                args.Cancel = true;
                message.Text = StudioStrings.Get("ApplicationMixerPanel.InformeUmVolumeEntre0");
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
        grid.DataError += (_, args) => { args.ThrowException = false; message.Text = StudioStrings.Get("ApplicationMixerPanel.ValorInvalidoReviseAEdicao"); };
        refresh.Click += async (_, _) =>
        {
            commandError = null;
            ClearMasterBlocks();
            await (PendingRead = ReadAsync());
        };
        poll.Tick += async (_, _) =>
        {
            UpdatePeakCells(Stopwatch.GetTimestamp(), grid.IsCurrentCellInEditMode || writing || !Visible);
            if (Visible) await (PendingRead = ReadAsync());
        };
    }

    public void SelectDevice(DeviceSnapshot? device)
    {
        if (stopped) return;
        pendingDevice = device;
        if (selectingDevice) return;
        CancelBalanceEdit();
        balanceCatalogValid = false;
        if (balanceLease is null && !balanceWriting)
        {
            SelectDeviceCore(device);
            return;
        }
        selectingDevice = true;
        revision++;
        views.Enabled = refresh.Enabled = false;
        selectionTransition = SwitchAfterBalanceAsync();
    }

    private void SelectDeviceCore(DeviceSnapshot? device)
    {
        // Cancela as edições antes de mudar a seleção para que CellEndEdit não atue no próximo endpoint.
        binding = true;
        try
        {
            edit = null;
            grid.CancelEdit();
            grid.EndEdit();
            target = service.Select(device?.State == EndpointState.Active ? device.Id : null);
            SelectMaster(target.DeviceId);
            grid.Rows.Clear();
            rows.Clear();
            ClearStrips();
        }
        finally { binding = false; }
        revision++;
        commandError = null;
        endpointName.Text = device is null ? StudioStrings.Get("ApplicationMixerPanel.ReproducaoIndisponivel") : StudioStrings.Format("ApplicationMixerPanel.SessoesDeReproducao0", device.Name);
        message.Text = StudioStrings.Get("ApplicationMixerPanel.SelecioneUmDispositivoDeReproducao");
        poll.Enabled = target.DeviceId is not null;
        UpdateBalanceControls();
        PendingRead = ReadAsync();
    }

    private Task ReadAsync()
    {
        if (stopped || selectingDevice || target.DeviceId is null || grid.IsCurrentCellInEditMode) return Task.CompletedTask;
        readRequested = true;
        if (!reading && !writing) PendingRead = DrainReadsAsync();
        return PendingRead;
    }

    private async Task DrainReadsAsync()
    {
        reading = true;
        try
        {
            while (readRequested && !writing && !stopped && !selectingDevice && target.DeviceId is not null && !grid.IsCurrentCellInEditMode)
            {
                readRequested = false;
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
                        ObserveBalance(catalogValid: false);
                        message.Text = EndpointVolumeOperations.Describe(error);
                    }
                }
                if (request == target && requestedRevision == revision && !stopped && !writing)
                    await ReadMasterAsync(request, requestedRevision);
            }
        }
        finally { reading = false; }
    }

    internal void ShowCatalog(EndpointSelection request, ApplicationSessionCatalog catalog)
    {
        if (request != target || stopped || selectingDevice || grid.IsCurrentCellInEditMode) return;
        binding = true;
        try
        {
            var keys = catalog.Sessions.Select(item => item.Key).ToHashSet();
            foreach (var key in rows.Keys.Where(key => !keys.Contains(key)).ToArray())
            {
                grid.Rows.Remove(rows[key]);
                rows.Remove(key);
                RemoveStrip(key);
            }
            foreach (var snapshot in catalog.Sessions)
            {
                if (snapshot.Key.EndpointId != request.DeviceId) continue;
                string state = snapshot.Activity switch { SessionActivity.Active => StudioStrings.Get("ApplicationMixerPanel.Ativa"), SessionActivity.Expired => StudioStrings.Get("ApplicationMixerPanel.Encerrada"), _ => StudioStrings.Get("ApplicationMixerPanel.Inativa") };
                if (!rows.TryGetValue(snapshot.Key, out var row))
                {
                    row = grid.Rows[grid.Rows.Add()];
                    rows.Add(snapshot.Key, row);
                }
                SetCell(row, "App", snapshot.Name);
                SetCell(row, "PID", snapshot.ProcessId?.ToString() ?? StudioStrings.Get("ApplicationMixerPanel.Indisponivel"));
                SetCell(row, "State", state);
                SetCell(row, "Volume", snapshot.VolumePercent?.ToString("F1", CultureInfo.CurrentCulture) ?? StudioStrings.Get("ApplicationMixerPanel.Indisponivel"));
                SetCell(row, "Mute", snapshot.Muted switch { true => StudioStrings.Get("ApplicationMixerPanel.DesativarMute"), false => StudioStrings.Get("ApplicationMixerPanel.AtivarMute"), _ => StudioStrings.Get("ApplicationMixerPanel.Indisponivel") });
                SetCell(row, "Instance", snapshot.Key.InstanceId);
                var current = new ApplicationSessionBinding(request, snapshot);
                row.Tag = current;
                BindStrip(current, state);
                row.Cells["Volume"].ReadOnly = snapshot.VolumePercent is null || snapshot.Activity == SessionActivity.Expired;
                row.ErrorText = snapshot.Error ?? "";
            }
        }
        finally { binding = false; }
        FilterStrips();
        ObserveBalance(catalog.Warnings.Count == 0);
        UpdatePeakCells(Stopwatch.GetTimestamp(), paused: false);
        message.Text = catalog.Warnings.Count > 0 ? string.Join(Environment.NewLine, catalog.Warnings.Distinct())
            : catalog.Sessions.Count == 0 ? StudioStrings.Get("ApplicationMixerPanel.NenhumaSessaoEncontradaNesteDispositivo")
            : StudioStrings.Format(views.SelectedIndex == 0 ? "Mixer.ChannelCount" : "ApplicationMixerPanel.0SessaoOesEditeO", catalog.Sessions.Count);
        if (commandError is not null) message.Text = commandError;
    }

    internal void UpdatePeakCells(long now, bool paused)
    {
        foreach (var row in rows.Values)
        {
            if (row.Tag is not ApplicationSessionBinding bound || !bound.IsCurrent(target)) continue;
            var snapshot = bound.Snapshot;
            var peak = snapshot.Activity == SessionActivity.Expired ? null : snapshot.Peak;
            if (strips.TryGetValue(snapshot.Key, out var strip))
                strip.ShowPeak(peak, snapshot.PeakRecordedAt, now, paused);
            string value;
            if (paused) value = StudioStrings.Get("ApplicationMixerPanel.Pausado");
            else if (peak?.Scalar is not double scalar) value = StudioStrings.Get("ApplicationMixerPanel.Indisponivel");
            else if (snapshot.PeakRecordedAt == 0 || Stopwatch.GetElapsedTime(snapshot.PeakRecordedAt, now) > TimeSpan.FromSeconds(2))
                value = StudioStrings.Get("ApplicationMixerPanel.LeituraAtrasada");
            else value = scalar == 0 ? StudioStrings.Format("ApplicationMixerPanel.Silence", 0d) : (scalar * 100).ToString("F1", CultureInfo.CurrentCulture);
            SetCell(row, "Peak", value);
            row.Cells["Peak"].ToolTipText = peak?.Error ?? StudioStrings.Get("ApplicationMixerPanel.PicoInstantaneoDoWindowsConsulta");
        }
    }

    private bool ValidCell(int row, int column) => row >= 0 && row < grid.Rows.Count && column >= 0 && column < grid.Columns.Count;

    private static void SetCell(DataGridViewRow row, string name, string value)
    {
        if (!Equals(row.Cells[name].Value, value)) row.Cells[name].Value = value;
    }

    private void ClearRows()
    {
        binding = true;
        try { edit = null; grid.CancelEdit(); grid.EndEdit(); grid.Rows.Clear(); rows.Clear(); ClearStrips(); }
        finally { binding = false; }
    }

    private Task ApplyAsync(ApplicationSessionBinding bound, ApplicationSessionCommand command)
    {
        if (!bound.IsCurrent(target) || !rows.ContainsKey(bound.Snapshot.Key)) return Task.CompletedTask;
        return RunWriteAsync(bound.Selection, async () => { await service.ApplyAsync(bound.Selection, bound.Snapshot.Key, command); });
    }

    private Task RunWriteAsync(EndpointSelection request, Func<Task> action)
    {
        if (binding || writing || stopped || selectingDevice || request != target) return Task.CompletedTask;
        pendingWrite = WriteAsync(request, action);
        return pendingWrite;
    }

    private async Task WriteAsync(EndpointSelection request, Func<Task> action)
    {
        writing = true;
        revision++;
        CancelStripEdits();
        views.Enabled = false;
        try
        {
            await action();
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
            if (!stopped && !selectingDevice) views.Enabled = true;
        }
        if (!stopped && !selectingDevice)
        {
            if (exitBalanceRequested)
            {
                exitBalanceRequested = false;
                if (balanceLease is { Position: not 0 }) await ChangeBalanceAsync(0, suspended: true);
            }
            // Mantém erros de comando até outra ação explícita.
            PendingRead = ReadAsync();
        }
    }

    public async ValueTask StopAsync()
    {
        stopped = true;
        poll.Stop();
        CancelBalanceEdit();
        CancelStripEdits();
        views.Enabled = false;
        binding = true;
        try { edit = null; grid.CancelEdit(); grid.EndEdit(); }
        finally { binding = false; }
        await selectionTransition;
        await pendingWrite;
        await ReleaseBalanceAsync();
        await Task.WhenAll(service.DisposeAsync().AsTask(), masterService.DisposeAsync().AsTask());
        await PendingRead;
        poll.Dispose();
        balanceTimer.Dispose();
    }
}
