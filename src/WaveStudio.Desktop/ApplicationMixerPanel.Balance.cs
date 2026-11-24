using WaveStudio.Audio;

namespace WaveStudio.Desktop;

internal sealed partial class ApplicationMixerPanel
{
    private readonly TrackBar gameChat = new()
    {
        Orientation = Orientation.Horizontal, Minimum = -100, Maximum = 100, TickFrequency = 50,
        SmallChange = 5, LargeChange = 20, Dock = DockStyle.Fill, Enabled = false
    };
    private readonly Button centerBalance = new() { AutoSize = true, Enabled = false };
    private readonly Label balancePosition = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label balanceScope = new() { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(530, 0) };
    private readonly Label balanceStatus = new() { AutoSize = true, Dock = DockStyle.Fill, MaximumSize = new Size(530, 0) };
    private readonly System.Windows.Forms.Timer balanceTimer = new() { Interval = 180 };
    private GameChatLease? balanceLease;
    private string? balanceNotice;
    private bool balanceCatalogValid;
    private bool balanceDragging;
    private bool balanceEditing;
    private bool balanceWriting;
    private bool exitBalanceRequested;
    private bool selectingDevice;
    private DeviceSnapshot? pendingDevice;
    private Task selectionTransition = Task.CompletedTask;
    private Task pendingWrite = Task.CompletedTask;
    private int retiredBalanceIssues;
    private string? closeBalanceWarning;

    internal string? BalanceCloseWarning => CombineWarnings(closeBalanceWarning, RetiredBalanceWarning());

    private Control CreateBalanceControl()
    {
        var section = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Padding = new Padding(4, 0, 4, 0) };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        section.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        string title = StudioStrings.Get("Balance.Title");
        section.Controls.Add(new Label { Text = title, AutoSize = true, AccessibleName = title }, 0, 0);
        section.Controls.Add(gameChat, 0, 1);
        section.Controls.Add(centerBalance, 1, 1);
        section.Controls.Add(balancePosition, 0, 2);
        section.Controls.Add(balanceStatus, 0, 3);
        section.SetColumnSpan(balancePosition, 2);
        section.SetColumnSpan(balanceStatus, 2);
        section.Controls.Add(balanceScope, 0, 4);
        section.SetColumnSpan(balanceScope, 2);
        balanceScope.Text = StudioStrings.Get("Balance.Scope");
        balanceScope.AccessibleName = balanceScope.Text;
        section.ClientSizeChanged += (_, _) =>
        {
            var limit = new Size(Math.Max(80, section.ClientSize.Width - section.Padding.Horizontal - balanceScope.Margin.Horizontal), 0);
            if (balanceScope.MaximumSize == limit) return;
            balanceScope.MaximumSize = balanceStatus.MaximumSize = limit;
        };
        centerBalance.Text = StudioStrings.Get("Balance.CenterButton");
        gameChat.AccessibleName = title;
        gameChat.AccessibleDescription = StudioStrings.Get("Balance.SliderHelp");
        gameChat.MouseDown += (_, args) =>
        {
            if (args.Button != MouseButtons.Left) return;
            balanceTimer.Stop();
            balanceDragging = balanceEditing = true;
        };
        gameChat.MouseUp += async (_, args) =>
        {
            if (args.Button == MouseButtons.Left && balanceDragging) await ChangeBalanceAsync(gameChat.Value);
        };
        gameChat.MouseCaptureChanged += (_, _) =>
        {
            if (balanceDragging && gameChat.IsHandleCreated && !gameChat.Capture)
                gameChat.BeginInvoke((Action)(() => { if (!IsDisposed && balanceDragging && !gameChat.Capture) CancelBalanceEdit(); }));
        };
        gameChat.Scroll += (_, _) =>
        {
            balanceEditing = true;
            ShowBalancePosition(gameChat.Value);
            if (balanceDragging) return;
            balanceTimer.Stop();
            balanceTimer.Start();
        };
        gameChat.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Escape) return;
            CancelBalanceEdit();
            args.Handled = args.SuppressKeyPress = true;
        };
        centerBalance.Click += async (_, _) => await ChangeBalanceAsync(0);
        balanceTimer.Tick += async (_, _) => await ChangeBalanceAsync(gameChat.Value);
        UpdateBalanceControls();
        return section;
    }

    private GameChatMember[] BalanceMembers() => strips.Values.Where(s => s.Group is MixerGroup.Game or MixerGroup.Chat &&
            s.Binding?.IsCurrent(target) == true && s.Binding.Snapshot is
                { IsSystemSounds: false, Activity: SessionActivity.Active, VolumePercent: >= 0 and <= 100 } && s.Binding.Snapshot.Error is null)
        .Select(s => new GameChatMember(s.Binding!.Snapshot.Key, s.Group == MixerGroup.Game ? GameChatSide.Game : GameChatSide.Chat))
        .ToArray();

    private bool CanBalance(GameChatMember[] members) =>
        balanceCatalogValid && GameChatBalance.Complete(target, members) &&
        strips.Values.Count(s => s.Group is MixerGroup.Game or MixerGroup.Chat) == members.Length &&
        balanceLease is not { Position: 0 };

    private void ObserveBalance(bool catalogValid)
    {
        balanceCatalogValid = catalogValid;
        var members = BalanceMembers();
        if (balanceLease is { Position: not 0 } active &&
            (!CanBalance(members) || !GameChatBalance.Matches(active, members) || active.Levels.Any(level =>
                !strips.TryGetValue(level.Member.Key, out var strip) ||
                strip.Binding?.Snapshot.VolumePercent is not double current ||
                !GameChatBalance.Same(current / 100, level.Applied))))
            RequestBalanceExit();
        UpdateBalanceControls();
    }

    internal void RequestBalanceExit()
    {
        if (stopped || IsDisposed) return;
        CancelBalanceEdit();
        if (selectingDevice || balanceLease is null && !balanceWriting) return;
        if (writing) { exitBalanceRequested = true; return; }
        if (balanceLease is { Position: not 0 })
            _ = ChangeBalanceAsync(0, suspended: true);
    }

    private void CancelBalanceEdit()
    {
        balanceTimer.Stop();
        balanceDragging = balanceEditing = false;
        PresentBalancePosition();
    }

    private void PresentBalancePosition()
    {
        if (balanceEditing) return;
        gameChat.Value = balanceLease?.Position ?? 0;
        ShowBalancePosition(gameChat.Value);
    }

    private void ShowBalancePosition(int position)
    {
        balancePosition.Text = position == 0 ? StudioStrings.Get("Balance.Center") :
            StudioStrings.Format(position < 0 ? "Balance.GamePosition" : "Balance.ChatPosition", Math.Abs(position));
        balancePosition.AccessibleName = balancePosition.Text;
    }

    private void UpdateBalanceControls()
    {
        if (IsDisposed) return;
        bool available = CanBalance(BalanceMembers());
        if (!available && balanceEditing) CancelBalanceEdit();
        gameChat.Enabled = available && !stopped && !selectingDevice;
        centerBalance.Enabled = balanceLease is not null && !stopped && !selectingDevice;
        var status = balanceNotice ?? (available ? null : StudioStrings.Get("Balance.ChooseGroups"));
        balanceStatus.Text = CombineWarnings(status, RetiredBalanceWarning()) ?? string.Empty;
        balanceStatus.AccessibleName = balanceStatus.Text;
        PresentBalancePosition();
    }

    private Task ChangeBalanceAsync(int position, bool suspended = false)
    {
        CancelBalanceEdit();
        var members = BalanceMembers();
        if (position != 0 && !CanBalance(members) || position == 0 && balanceLease is null)
            return Task.CompletedTask;

        return RunWriteAsync(target, async () =>
        {
            balanceWriting = true;
            try
            {
                var result = await service.BalanceAsync(target, members, balanceLease, position);
                PublishBalance(result, suspended);
            }
            finally { balanceWriting = false; }
        });
    }

    private void PublishBalance(GameChatResult result, bool suspended)
    {
        balanceLease = result.Lease;
        balanceNotice = result.Issues != 0 ? StudioStrings.Format("Balance.Incomplete", result.Issues) :
            (result.Suspended || suspended) ? StudioStrings.Get("Balance.Suspended") : null;
        if (stopped) closeBalanceWarning = result.Issues != 0 ? balanceNotice : null;
        else UpdateBalanceControls();
    }

    private string? RetiredBalanceWarning() => retiredBalanceIssues == 0
        ? null : StudioStrings.Format("Balance.PreviousOutput", retiredBalanceIssues);

    private static string? CombineWarnings(string? current, string? previous) =>
        current is null ? previous : previous is null ? current : current + Environment.NewLine + previous;

    private async Task<int> ReleaseBalanceAsync()
    {
        if (balanceLease is not { } active) return 0;
        var result = await service.BalanceAsync(active.Selection, [], active, 0);
        PublishBalance(result, suspended: true);
        return result.Issues;
    }

    private async Task SwitchAfterBalanceAsync()
    {
        try
        {
            await pendingWrite;
            int issues = await ReleaseBalanceAsync();
            if (stopped) return;

            // Guarda o aviso durante a sessão, sem aplicar a posse antiga a outra geração.
            retiredBalanceIssues += issues;
            balanceNotice = null;
            balanceLease = null;
            SelectDeviceCore(pendingDevice);
        }
        finally
        {
            selectingDevice = false;
            exitBalanceRequested = false;
            if (!stopped)
            {
                views.Enabled = refresh.Enabled = true;
                UpdateBalanceControls();
                PendingRead = ReadAsync();
            }
        }
    }
}
