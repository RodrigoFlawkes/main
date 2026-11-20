using WaveStudio.Audio;
using WaveStudio.Desktop;
using Xunit;

namespace WaveStudio.Tests;

public sealed class ExternalRouteReadinessUiTests
{
    [Fact]
    public Task ShowsRealChoicesAndManualGuidanceWithoutOfferingAudioActions() => PreferencesUiTests.OnSta(async () =>
    {
        var inventory = ExternalRouteReadinessTests.Fixture();
        int queries = 0;
        var service = new ExternalRouteReadinessService(_ =>
        {
            queries++;
            return Task.FromResult(inventory);
        }, ExternalRouteReadinessTests.Probe);
        using var panel = new ExternalRouteReadinessPanel(service);
        using var host = Show(panel);
        panel.SetActive(true);
        await PreferencesUiTests.PumpUntilAsync(() => Choices(panel, ExternalRouteText.Headset).Items.Count > 1);
        SelectSingleCable(panel);
        await PreferencesUiTests.PumpUntilAsync(() => Status(panel, ExternalRouteText.Browser).Text.Contains(ExternalRouteText.FlowConfirmed));
        Assert.Equal(2, queries);
        Assert.Contains(ExternalRouteText.ProofPending, PreferencesUiTests.Descendants(panel).OfType<Label>().Select(x => x.Text));
        Assert.Contains(PreferencesUiTests.Descendants(panel).OfType<Label>(), x => x.Text.Contains(ExternalRouteText.DualNotDetected));
        Assert.Contains(PreferencesUiTests.Descendants(panel).OfType<Label>(), x => x.Text.Contains(ExternalRouteText.SeparateCode10));
        var instructions = Assert.Single(PreferencesUiTests.Descendants(panel).OfType<TextBox>());
        Assert.True(instructions.ReadOnly);
        Assert.Contains("quietude", instructions.Text);
        Assert.Contains("https://obsproject.com/", instructions.Text);
        Assert.Equal(ExternalRouteText.RefreshButton, Assert.Single(PreferencesUiTests.Descendants(panel).OfType<Button>()).Text);
        Assert.All(PreferencesUiTests.Descendants(panel).OfType<ComboBox>(), x => Assert.False(string.IsNullOrWhiteSpace(x.AccessibleName)));

        using var newPanel = new ExternalRouteReadinessPanel(service);
        Assert.Empty(Choices(newPanel, ExternalRouteText.Headset).Items);
        await panel.StopAsync();
    });

    [Fact]
    public Task HotplugCancelsTheOldQueryAndKeepsTheMissingSelectionInsteadOfFallingBack() => PreferencesUiTests.OnSta(async () =>
    {
        var available = ExternalRouteReadinessTests.Fixture();
        var missing = available with { Endpoints = available.Endpoints.Where(p => p.Device.Id != "headset").ToArray() };
        var pending = new TaskCompletionSource<RouteInventory>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        int calls = 0;
        var service = new ExternalRouteReadinessService(token => ++calls switch
        {
            1 => Task.FromResult(available),
            2 => Hold(token),
            _ => Task.FromResult(missing)
        }, ExternalRouteReadinessTests.Probe);
        Task<RouteInventory> Hold(CancellationToken token)
        {
            oldToken = token;
            return pending.Task;
        }
        using var panel = new ExternalRouteReadinessPanel(service);
        using var host = Show(panel);
        panel.SetActive(true);
        await PreferencesUiTests.PumpUntilAsync(() => Choices(panel, ExternalRouteText.Headset).Items.Count > 1);
        SelectSingleCable(panel);
        await PreferencesUiTests.PumpUntilAsync(() => calls == 2);
        panel.InvalidateInventory();
        Assert.True(oldToken.IsCancellationRequested);
        pending.SetResult(available);
        await PreferencesUiTests.PumpUntilAsync(() => calls == 3 &&
            Status(panel, ExternalRouteText.Browser).Text.Contains(ExternalRouteText.Missing));
        Assert.Contains(ExternalRouteText.PreviousUnavailable, Choices(panel, ExternalRouteText.Headset).Text);
        await panel.StopAsync();
    });

    [Fact]
    public Task StopCancelsAndAwaitsAnActiveQueryWithoutPublishingLateResults() => PreferencesUiTests.OnSta(async () =>
    {
        var pending = new TaskCompletionSource<RouteInventory>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken queryToken = default;
        var service = new ExternalRouteReadinessService(token =>
        {
            queryToken = token;
            return pending.Task;
        }, ExternalRouteReadinessTests.Probe);
        using var panel = new ExternalRouteReadinessPanel(service);
        using var host = Show(panel);
        panel.SetActive(true);
        await PreferencesUiTests.PumpUntilAsync(() => queryToken.CanBeCanceled);
        var stop = panel.StopAsync();
        Assert.True(queryToken.IsCancellationRequested);
        Assert.False(stop.IsCompleted);
        pending.SetResult(ExternalRouteReadinessTests.Fixture());
        await stop;
        Assert.Empty(Choices(panel, ExternalRouteText.Headset).Items);
        Assert.False(Assert.Single(PreferencesUiTests.Descendants(panel).OfType<Button>()).Enabled);
    });

    [Fact]
    public Task ExposesAbChoicesOnlyWhenBothRealPairsArePresent() => PreferencesUiTests.OnSta(async () =>
    {
        var service = ExternalRouteReadinessTests.Service(ExternalRouteReadinessTests.WithDualCable(distinct: true));
        using var panel = new ExternalRouteReadinessPanel(service);
        using var host = Show(panel);
        panel.SetActive(true);
        var mode = Choices(panel, ExternalRouteText.Mode);
        await PreferencesUiTests.PumpUntilAsync(() => mode.Items.Contains(ExternalRouteText.DualMode));
        mode.SelectedIndex = 1;
        await PreferencesUiTests.PumpUntilAsync(() => Choices(panel, ExternalRouteText.MicCapture).Items.Count == 2);
        var guide = Assert.Single(PreferencesUiTests.Descendants(panel).OfType<TextBox>());
        Assert.Contains("cabo A → DSP → headset", guide.Text);
        Assert.Contains("cabo B → fonte do OBS", guide.Text);
        Assert.Equal(0, Choices(panel, ExternalRouteText.BrowserCapture).SelectedIndex);
        await panel.StopAsync();
    });

    private static Form Show(Control panel)
    {
        var host = new Form { ClientSize = new Size(900, 620), ShowInTaskbar = false, Opacity = 0 };
        host.Controls.Add(panel);
        host.Show();
        return host;
    }

    private static void SelectSingleCable(Control panel)
    {
        Choices(panel, ExternalRouteText.SingleRender).SelectedIndex = 1;
        Choices(panel, ExternalRouteText.SingleCapture).SelectedIndex = 1;
        Choices(panel, ExternalRouteText.Headset).SelectedIndex = 1;
        Choices(panel, ExternalRouteText.Mic).SelectedIndex = 1;
    }

    private static ComboBox Choices(Control panel, string name) =>
        PreferencesUiTests.Descendants(panel).OfType<ComboBox>().Single(c => c.AccessibleName == name);

    private static Label Status(Control panel, string name) =>
        PreferencesUiTests.Descendants(panel).OfType<Label>().Single(c => c.AccessibleName == name);
}
