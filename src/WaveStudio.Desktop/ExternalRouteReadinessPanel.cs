using WaveStudio.Audio;

namespace WaveStudio.Desktop;

internal sealed class ExternalRouteReadinessPanel : UserControl
{
    private readonly ExternalRouteReadinessService service;
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 180 };
    private readonly Button refresh = new() { AutoSize = true, Text = ExternalRouteText.RefreshButton };
    private readonly ComboBox mode = NewChoice(ExternalRouteText.Mode);
    private readonly Label consultation = NewLabel(ExternalRouteText.Waiting);
    private readonly Label single = NewLabel(ExternalRouteText.SingleStatus);
    private readonly Label dual = NewLabel(ExternalRouteText.DualStatus);
    private readonly Label pair = NewLabel(ExternalRouteText.Pair);
    private readonly Label driver = NewLabel(ExternalRouteText.Driver);
    private readonly Label defaults = NewLabel(ExternalRouteText.Defaults);
    private readonly Label browser = NewLabel(ExternalRouteText.Browser);
    private readonly Label microphone = NewLabel(ExternalRouteText.Microphone);
    private readonly Label physical = NewLabel(ExternalRouteText.Headset);
    private readonly ListView inventory = new() { View = View.Details, FullRowSelect = true, MultiSelect = false,
        Height = 144, Dock = DockStyle.Top, AccessibleName = ExternalRouteText.Inventory };
    private readonly TextBox guide = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
        Height = 168, Dock = DockStyle.Top, AccessibleName = ExternalRouteText.GuideTitle };
    private readonly TableLayoutPanel layout = new() { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Top,
        Padding = new Padding(12), GrowStyle = TableLayoutPanelGrowStyle.AddRows };
    private readonly PortChoice[] choices;
    private RouteSelections selection = new(RouteMode.Single);
    private CancellationTokenSource? request;
    private Task query = Task.CompletedTask;
    private long generation;
    private bool active;
    private bool pending;
    private bool binding;
    private bool stopped;
    private bool dualAvailable;

    internal ExternalRouteReadinessPanel(ExternalRouteReadinessService service)
    {
        this.service = service;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        AccessibleName = ExternalRouteText.Title;

        choices = [
            new(ExternalRouteText.SingleRender, EndpointDirection.Playback),
            new(ExternalRouteText.SingleCapture, EndpointDirection.Capture),
            new(ExternalRouteText.MicRender, EndpointDirection.Playback),
            new(ExternalRouteText.MicCapture, EndpointDirection.Capture),
            new(ExternalRouteText.Headset, EndpointDirection.Playback),
            new(ExternalRouteText.Mic, EndpointDirection.Capture)
        ];
        var heading = NewLabel(ExternalRouteText.Title);
        heading.Font = new Font(Font, FontStyle.Bold);
        var commands = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
        commands.ColumnStyles.Add(new(SizeType.Percent, 100));
        commands.ColumnStyles.Add(new(SizeType.AutoSize));
        commands.Controls.Add(consultation, 0, 0);
        commands.Controls.Add(refresh, 1, 0);
        var selections = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
        selections.ColumnStyles.Add(new(SizeType.Percent, 50));
        selections.ColumnStyles.Add(new(SizeType.Percent, 50));
        selections.Controls.Add(Field(NewLabel(ExternalRouteText.Mode), mode), 0, 0);
        for (int i = 0; i < choices.Length; i++)
        {
            var choice = choices[i];
            int slot = i;
            selections.Controls.Add(choice.Field, i % 2, 1 + i / 2);
            choice.Input.SelectedIndexChanged += (_, _) => SelectPort(slot, choice.Input.SelectedItem as Option);
        }

        inventory.Columns.Add(ExternalRouteText.Device, 240);
        inventory.Columns.Add(ExternalRouteText.Direction, 85);
        inventory.Columns.Add(ExternalRouteText.State, 100);
        inventory.Columns.Add(ExternalRouteText.Format, 210);
        foreach (var control in new Control[] { heading, NewLabel(ExternalRouteText.ProofPending), commands, single, dual,
            selections, NewLabel(ExternalRouteText.LocalOnly), pair, driver, defaults, physical, browser, microphone,
            NewLabel(ExternalRouteText.Inventory), inventory, NewLabel(ExternalRouteText.GuideTitle), guide,
            NewLabel(ExternalRouteText.BootEvidence) })
            layout.Controls.Add(control);
        Controls.Add(layout);

        Layout += (_, _) => UpdateWidths();
        mode.SelectedIndexChanged += (_, _) => SelectMode();
        refresh.Click += (_, _) => InvalidateInventory();
        debounce.Tick += (_, _) => BeginQuery();
        BindModes();
        SetModeLabels();
        SetWaiting();
    }

    internal void SetActive(bool visible)
    {
        if (stopped || active == visible) return;
        active = visible;
        if (visible) InvalidateInventory();
        else
        {
            debounce.Stop();
            request?.Cancel();
        }
    }

    internal void InvalidateInventory()
    {
        if (stopped) return;
        ++generation;
        pending = true;
        request?.Cancel();
        if (active)
        {
            SetWaiting();
            Schedule();
        }
    }

    internal Task StopAsync()
    {
        if (!stopped)
        {
            stopped = true;
            debounce.Stop();
            refresh.Enabled = false;
            request?.Cancel();
        }
        return query;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ = StopAsync();
            debounce.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Schedule()
    {
        if (stopped) return;
        debounce.Stop();
        if (active && pending && request is null) debounce.Start();
    }

    private void BeginQuery()
    {
        debounce.Stop();
        if (!active || stopped || !pending || request is not null) return;
        pending = false;
        request = new CancellationTokenSource();
        query = QueryAsync(selection, generation, request);
    }

    private async Task QueryAsync(RouteSelections requested, long revision, CancellationTokenSource cancel)
    {
        try
        {
            var result = await service.QueryAsync(requested, revision, cancel.Token);
            if (Current()) ShowResult(result);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception)
        {
            if (Current())
            {
                consultation.Text = ExternalRouteText.QueryIssue;
                inventory.Items.Clear();
                refresh.Enabled = true;
            }
        }
        finally
        {
            request = null;
            cancel.Dispose();
            Schedule();
        }

        bool Current() => active && !stopped && !IsDisposed && !cancel.IsCancellationRequested && generation == revision;
    }

    private void SetWaiting()
    {
        consultation.Text = ExternalRouteText.Updating;
        refresh.Enabled = active && !stopped;
        foreach (var label in new[] { single, dual, pair, driver, defaults, physical, browser, microphone })
            ShowStatus(label, ExternalRouteText.Waiting);
    }

    private void ShowResult(RouteReadiness report)
    {
        consultation.Text = report.Inventory.Complete
            ? StudioStrings.Format("ExternalRouteReadinessPanel.01T", ExternalRouteText.Consulted, DateTime.Now) : ExternalRouteText.QueryIssue;
        ShowStatus(single, ExternalRouteText.Availability(report.SingleCable));
        ShowStatus(dual, ExternalRouteText.Availability(report.DualCable, dual: true));
        ShowStatus(pair, ExternalRouteText.Check(report.Pair));
        string driverText = ExternalRouteText.Check(report.Driver);
        if (report.SelectedProblem is uint problem) driverText += StudioStrings.Format("ExternalRouteReadinessPanel.Codigo0", problem);
        if (report.SecondaryCode10) driverText += " · " + ExternalRouteText.SeparateCode10;
        else if (report.SelectedProblem != 10u && report.Inventory.SingleDrivers?.Any(adapter => adapter.Problem == 10) == true)
            driverText += " · " + ExternalRouteText.UnlinkedCode10;
        ShowStatus(driver, driverText);
        ShowStatus(defaults, ExternalRouteText.Check(report.WindowsDefaults));
        ShowStatus(physical, $"{ExternalRouteText.Check(report.Headset)} · {ExternalRouteText.Mic}: {ExternalRouteText.Check(report.Mic)}");
        ShowStatus(browser, FlowStatus(report.Browser));
        ShowStatus(microphone, FlowStatus(report.Microphone));

        binding = true;
        try
        {
            dualAvailable = report.DualCable == RouteAvailability.Available;
            BindModes();
            BindPorts(report.Inventory.Endpoints);
        }
        finally { binding = false; }

        inventory.BeginUpdate();
        try
        {
            inventory.Items.Clear();
            foreach (var port in report.Inventory.Endpoints.OrderBy(p => p.Device.Direction).ThenBy(p => p.Device.Name))
            {
                var device = port.Device;
                inventory.Items.Add(new ListViewItem([device.Name, device.Direction == EndpointDirection.Playback
                    ? ExternalRouteText.DirectionPlayback : ExternalRouteText.DirectionCapture,
                    ExternalRouteText.DeviceState(device.State), ExternalRouteText.Mix(device.MixFormat)]));
            }
        }
        finally { inventory.EndUpdate(); }
    }

    private void BindModes()
    {
        bool wasBinding = binding;
        binding = true;
        try
        {
            mode.Items.Clear();
            mode.Items.Add(ExternalRouteText.SingleMode);
            if (dualAvailable || selection.Mode == RouteMode.Dual) mode.Items.Add(ExternalRouteText.DualMode);
            mode.SelectedIndex = selection.Mode == RouteMode.Single ? 0 : 1;
        }
        finally { binding = wasBinding; }
    }

    private void SetModeLabels()
    {
        bool simple = selection.Mode == RouteMode.Single;
        choices[0].Rename(simple ? ExternalRouteText.SingleRender : ExternalRouteText.BrowserRender);
        choices[1].Rename(simple ? ExternalRouteText.SingleCapture : ExternalRouteText.BrowserCapture);
        choices[2].Field.Visible = choices[3].Field.Visible = !simple;
        guide.Text = ExternalRouteText.Guide(selection.Mode).ReplaceLineEndings(Environment.NewLine);
    }

    private void SelectMode()
    {
        if (binding || stopped || mode.SelectedIndex < 0) return;
        var changed = mode.SelectedIndex == 0 ? RouteMode.Single : RouteMode.Dual;
        if (changed == selection.Mode) return;
        selection = new(changed, Headset: selection.Headset, Microphone: selection.Microphone);
        binding = true;
        try
        {
            foreach (var choice in choices.Take(4)) choice.Input.Items.Clear();
            SetModeLabels();
        }
        finally { binding = false; }
        InvalidateInventory();
    }

    private void SelectPort(int slot, Option? option)
    {
        if (binding || stopped || option is null) return;
        selection = slot switch
        {
            0 => selection with { BrowserRender = option.Id },
            1 => selection with { BrowserCapture = option.Id },
            2 => selection with { MicrophoneRender = option.Id },
            3 => selection with { MicrophoneCapture = option.Id },
            4 => selection with { Headset = option.Id },
            _ => selection with { Microphone = option.Id }
        };
        InvalidateInventory();
    }

    private void BindPorts(IReadOnlyList<RoutePort> endpoints)
    {
        bool simple = selection.Mode == RouteMode.Single;
        var kind = simple ? RouteEndpointKind.Single : RouteEndpointKind.CableA;
        choices[0].Bind(endpoints, kind, selection.BrowserRender);
        choices[1].Bind(endpoints, kind, selection.BrowserCapture);
        if (!simple)
        {
            choices[2].Bind(endpoints, RouteEndpointKind.CableB, selection.MicrophoneRender);
            choices[3].Bind(endpoints, RouteEndpointKind.CableB, selection.MicrophoneCapture);
        }
        choices[4].Bind(endpoints, RouteEndpointKind.Physical, selection.Headset);
        choices[5].Bind(endpoints, RouteEndpointKind.Physical, selection.Microphone);
    }

    private void UpdateWidths()
    {
        int width = Math.Max(230, ClientSize.Width - layout.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
        foreach (var label in layout.Controls.OfType<Label>())
            if (label.MaximumSize.Width != width) label.MaximumSize = new Size(width, 0);
        if (inventory.Columns.Count == 4)
        {
            inventory.Columns[0].Width = width * 38 / 100;
            inventory.Columns[1].Width = width * 14 / 100;
            inventory.Columns[2].Width = width * 17 / 100;
            inventory.Columns[3].Width = width * 28 / 100;
        }
    }

    private static string FlowStatus(RouteCheck check) => check == RouteCheck.Confirmed
        ? ExternalRouteText.FlowConfirmed : ExternalRouteText.Check(check);

    private static void ShowStatus(Label label, string text)
    {
        label.Text = $"{label.AccessibleName} — {text}";
        label.AccessibleDescription = text;
    }

    private static Label NewLabel(string text) => new() { Text = text, AccessibleName = text, AutoSize = true, Dock = DockStyle.Top };

    private static ComboBox NewChoice(string name) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top,
        AccessibleName = name, AccessibleDescription = ExternalRouteText.LocalChoice };

    private static Control Field(Label heading, ComboBox input)
    {
        var container = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1 };
        container.Controls.Add(heading);
        container.Controls.Add(input);
        return container;
    }

    private sealed record Option(string? Id, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed class PortChoice
    {
        private readonly Label heading;
        private readonly EndpointDirection direction;
        public ComboBox Input { get; }
        public Control Field { get; }

        public PortChoice(string name, EndpointDirection direction)
        {
            this.direction = direction;
            heading = NewLabel(name);
            Input = NewChoice(name);
            Field = ExternalRouteReadinessPanel.Field(heading, Input);
        }

        public void Rename(string name) => heading.Text = heading.AccessibleName = Input.AccessibleName = name;

        public void Bind(IReadOnlyList<RoutePort> ports, RouteEndpointKind kind, string? previous)
        {
            var available = ports.Where(port => port.Device.Direction == direction && port.Kind == kind)
                .Select(p => p.Device).OrderBy(d => d.Name).ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToArray();
            var items = new List<Option> { new(null, ExternalRouteText.Choose) };
            foreach (var group in available.GroupBy(ExternalRouteText.Option))
            {
                int number = 0;
                foreach (var device in group)
                    items.Add(new(device.Id, group.Count() == 1 ? group.Key : $"{group.Key} ({++number})"));
            }
            if (previous is not null && !items.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, previous)))
                items.Add(new(previous, ExternalRouteText.PreviousUnavailable));
            Input.BeginUpdate();
            try
            {
                Input.Items.Clear();
                Input.Items.AddRange(items.ToArray());
                Input.SelectedIndex = items.FindIndex(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, previous));
                int measured = items.Max(item => TextRenderer.MeasureText(item.Display, Input.Font).Width) + 38;
                Input.DropDownWidth = Math.Max(1, Math.Min(Screen.FromControl(Input).WorkingArea.Width - 20, Math.Max(Input.Width, measured)));
            }
            finally { Input.EndUpdate(); }
        }
    }
}
