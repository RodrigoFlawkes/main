using WaveStudio.Audio;

namespace WaveStudio.Desktop;

internal sealed partial class ApplicationMixerPanel
{
    private readonly EndpointVolumeSession masterService;
    private readonly MixerStrip master = new(master: true) { Dock = DockStyle.Top };
    private readonly FlowLayoutPanel stripsLayout = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
    private readonly ComboBox filter = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label emptyGroup = new() { AutoSize = true, Visible = false, Text = StudioStrings.Get("Mixer.NoChannelsInGroup") };
    private readonly Dictionary<SessionKey, MixerStrip> strips = [];
    private readonly TabControl views = new() { Dock = DockStyle.Fill };
    private EndpointSelection masterTarget = new(null, 0);
    private EndpointVolumeState? masterState;
    private bool masterVolumeBlocked;
    private bool masterMuteBlocked;
    private int nextChannel;

    internal TabControl MixerViews => views;

    private Control CreateViews()
    {
        var board = new TabPage(StudioStrings.Get("Mixer.Board"));
        var gridView = new TabPage(StudioStrings.Get("Mixer.Details"));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(4) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        filter.AccessibleName = StudioStrings.Get("Mixer.GroupFilter");
        filter.Items.Add(new GroupFilter(null));
        foreach (var category in Enum.GetValues<MixerGroup>()) filter.Items.Add(new GroupFilter(category));
        filter.SelectedIndex = 0;
        filter.SelectedIndexChanged += (_, _) => FilterStrips();
        toolbar.Controls.Add(filter);
        toolbar.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(410, 0), Text = StudioStrings.Get("Mixer.BoardHint") });
        layout.Controls.Add(toolbar, 0, 0);
        layout.SetColumnSpan(toolbar, 2);
        var masterArea = new Panel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMinSize = new Size(0, master.Height) };
        masterArea.Controls.Add(master);
        layout.Controls.Add(masterArea, 0, 1);
        stripsLayout.Controls.Add(emptyGroup);
        layout.Controls.Add(stripsLayout, 1, 1);
        board.Controls.Add(layout);
        gridView.Controls.Add(grid);
        views.TabPages.Add(board);
        views.TabPages.Add(gridView);
        views.SelectedIndexChanged += (_, _) => CancelStripEdits();
        VisibleChanged += (_, _) => { if (!Visible) CancelStripEdits(); };
        master.Fader.Committed += async percent => await ApplyMasterAsync(new SetEndpointVolume(percent));
        master.Mute.Click += async (_, _) =>
        {
            if (masterState?.Muted is bool muted) await ApplyMasterAsync(new SetEndpointMute(!muted));
        };
        master.ShowMaster(null);
        return views;
    }

    private void BindStrip(ApplicationSessionBinding current, string state)
    {
        if (!strips.TryGetValue(current.Snapshot.Key, out var strip))
        {
            var channel = ++nextChannel;
            strip = new MixerStrip { Tag = channel };
            strips.Add(current.Snapshot.Key, strip);
            stripsLayout.Controls.Add(strip);
            strip.GroupChanged += FilterStrips;
            strip.Fader.Committed += async value =>
            {
                if (strip.Binding is { } bound) await ApplyAsync(bound, new SetSessionVolume(value));
            };
            strip.Mute.Click += async (_, _) =>
            {
                if (strip.Binding is { Snapshot.Muted: bool muted } bound) await ApplyAsync(bound, new SetSessionMute(!muted));
            };
        }
        strip.ShowSession(current, state, (int)strip.Tag!);
    }

    private void FilterStrips()
    {
        var group = (filter.SelectedItem as GroupFilter)?.Group;
        bool hasVisibleChannel = false;
        foreach (var strip in strips.Values)
        {
            bool visible = group is null || strip.Group == group;
            if (!visible) strip.Fader.CancelPendingEdit();
            strip.Visible = visible;
            hasVisibleChannel |= visible;
        }
        emptyGroup.Visible = group is not null && !hasVisibleChannel;
    }

    private void RemoveStrip(SessionKey key)
    {
        if (!strips.Remove(key, out var strip)) return;
        stripsLayout.Controls.Remove(strip);
        strip.Dispose();
    }

    private void ClearStrips()
    {
        foreach (var key in strips.Keys.ToArray()) RemoveStrip(key);
        nextChannel = 0;
        FilterStrips();
    }

    private void CancelStripEdits()
    {
        master.Fader.CancelPendingEdit();
        foreach (var strip in strips.Values) strip.Fader.CancelPendingEdit();
    }

    private void SelectMaster(string? device)
    {
        masterTarget = masterService.Select(device);
        ClearMasterBlocks();
        master.Fader.CancelEdit();
        SetMasterState(null);
        master.ShowPeak(null, 0, 0);
    }

    private void ClearMasterBlocks() => masterVolumeBlocked = masterMuteBlocked = false;

    private async Task ReadMasterAsync(EndpointSelection request, long requestedRevision)
    {
        var owner = masterTarget;
        try
        {
            var state = await masterService.ReadAsync(owner);
            if (Current()) SetMasterState(state);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error))
        {
            if (Current()) SetMasterState(new(null, null, null, EndpointVolumeOperations.Describe(error), null, null));
        }

        bool Current() => owner == masterTarget && request == target && requestedRevision == revision && !stopped && !writing;
    }

    private void SetMasterState(EndpointVolumeState? state)
    {
        masterState = state is null ? null : state with
        {
            VolumePercent = masterVolumeBlocked ? null : state.VolumePercent,
            Muted = masterMuteBlocked ? null : state.Muted
        };
        master.ShowMaster(masterState);
    }

    private Task ApplyMasterAsync(EndpointCommand command)
    {
        if (target.DeviceId is null || masterTarget.DeviceId != target.DeviceId || masterState is null) return Task.CompletedTask;
        var request = target;
        var owner = masterTarget;
        return RunWriteAsync(request, async () =>
        {
            try
            {
                var state = await masterService.ApplyAsync(owner, command);
                if (!stopped && owner == masterTarget) SetMasterState(state);
            }
            catch (NotSupportedException)
            {
                if (!stopped && owner == masterTarget)
                {
                    masterVolumeBlocked |= command is SetEndpointVolume;
                    masterMuteBlocked |= command is SetEndpointMute;
                    SetMasterState(masterState);
                }
                throw;
            }
        });
    }

    internal void ShowEndpointPeak(AudioWatchSnapshot snapshot, long now)
    {
        if (!stopped && target.DeviceId is { } device && snapshot.Selection.PlaybackId == device)
            master.ShowPeak(snapshot.Playback, snapshot.RecordedAt, now);
    }

    private sealed record GroupFilter(MixerGroup? Group)
    {
        public override string ToString() => Group is { } value ? MixerStrip.GroupName(value) : StudioStrings.Get("Mixer.GroupAll");
    }
}
