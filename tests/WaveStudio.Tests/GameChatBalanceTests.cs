using System.Windows.Forms;
using WaveStudio.Audio;
using WaveStudio.Audio.Localization;
using WaveStudio.Desktop;
using Xunit;

namespace WaveStudio.Tests;

public sealed class GameChatBalanceTests
{
    [Theory]
    [InlineData(-100, .8, 0)]
    [InlineData(-25, .8, .45)]
    [InlineData(45, .44, .6)]
    [InlineData(100, 0, .6)]
    public async Task AttenuatesOnlyOppositeGroupAndRestoresOriginal(int position, double gameLevel, double chatLevel)
    {
        var game = new Native("game", .8);
        var chat = new Native("chat", .6);
        var other = new Native("other", .7);
        await using var service = Service(game, chat, other);
        var selection = service.Select("out");
        var bindings = Map(game, chat);
        var neutral = await service.BalanceAsync(selection, bindings, null, 0);
        Assert.Null(neutral.Lease);
        Assert.Empty(game.Writes);
        var outcome = await service.BalanceAsync(selection, bindings, null, position);
        Assert.Equal(position, outcome.Lease!.Position);
        Assert.Equal(gameLevel, game.Level, 9);
        Assert.Equal(chatLevel, chat.Level, 9);
        Assert.Empty(other.Writes);
        outcome = await service.BalanceAsync(selection, [], outcome.Lease, 0);
        Assert.Null(outcome.Lease);
        Assert.Equal(0, outcome.Issues);
        Assert.Equal(.8, game.Level);
        Assert.Equal(.6, chat.Level);
    }

    [Fact]
    public async Task ReusesBaselinesAcrossBothSidesAndPreservesExternalEdits()
    {
        var game = new Native("game", .8);
        var chat = new Native("chat", .6);
        var extra = new Native("second-instance", .4);
        var order = new List<SessionKey>();
        game.OnWrite = chat.OnWrite = extra.OnWrite = order.Add;
        await using var service = Service(game, chat, extra);
        var selection = service.Select("out");
        var bindings = Map(game, chat, extra);
        var first = await service.BalanceAsync(selection, bindings, null, 70);
        order.Clear();
        var second = await service.BalanceAsync(selection, bindings, first.Lease, -60);
        Assert.Equal([chat.Key, extra.Key, game.Key], order);
        Assert.Equal(.8, game.Level);
        Assert.Equal(.24, chat.Level, 9);
        Assert.Equal(.16, extra.Level, 9);
        chat.Level = .3;
        var conflict = await service.BalanceAsync(selection, bindings, second.Lease, -80);
        Assert.True(conflict.Suspended);
        Assert.Equal(1, conflict.Issues);
        Assert.Null(conflict.Lease);
        Assert.Equal(.3, chat.Level);
        Assert.Equal(.4, extra.Level);
    }

    [Fact]
    public async Task PartialWriteRestoresConfirmedWritesEvenIfSecondSetterThrowsAfterChanging()
    {
        var game = new Native("game", .5);
        var firstChat = new Native("one", .6);
        var secondChat = new Native("two", .4) { ThrowAfterWrite = true };
        await using var service = Service(game, firstChat, secondChat);
        var selection = service.Select("out");
        var result = await service.BalanceAsync(selection, Map(game, firstChat, secondChat), null, -50);
        Assert.True(result.Suspended);
        Assert.Null(result.Lease);
        Assert.Equal(0, result.Issues);
        Assert.Equal(.6, firstChat.Level);
        Assert.Equal(.4, secondChat.Level);
    }

    [Fact]
    public async Task IncompatibleReadbackIsNotOverwrittenDuringRollback()
    {
        var game = new Native("game", .8);
        var chat = new Native("chat", .6) { RewriteApplied = .42 };
        await using var service = Service(game, chat);
        var selection = service.Select("out");
        var result = await service.BalanceAsync(selection, Map(game, chat), null, -75);
        Assert.True(result.Suspended);
        Assert.Equal(1, result.Issues);
        Assert.Null(result.Lease);
        Assert.Equal(.42, chat.Level);
        Assert.Single(chat.Writes);
    }

    [Fact]
    public async Task MissingOrAmbiguousParticipantBlocksAllWritesAndRetiredGenerationCannotApply()
    {
        var game = new Native("game", .8);
        var chat = new Native("chat", .6);
        var duplicate = new Native("chat", .5);
        await using var service = Service(game, chat, duplicate);
        var selection = service.Select("out");
        Assert.True((await service.BalanceAsync(selection, Map(game, chat), null, -40)).Suspended);
        Assert.Empty(chat.Writes);
        var other = service.Select("other");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.BalanceAsync(selection, Map(game, chat), null, -40));
        Assert.True((await service.BalanceAsync(other, Map(game, chat), null, -40)).Suspended);
        Assert.Empty(game.Writes);
    }

    [Fact]
    public async Task MissingAttenuatedSessionRemainsUnconfirmedUntilItCanBeVerified()
    {
        var game = new Native("game", .8);
        var chat = new Native("chat", .6);
        var items = new List<Native> { game, chat };
        await using var service = new ApplicationSessionService(_ => new Endpoint(items));
        var selection = service.Select("out");
        var active = await service.BalanceAsync(selection, Map(game, chat), null, -50);
        items.Remove(chat);
        var missing = await service.BalanceAsync(selection, [], active.Lease, 0);
        Assert.Equal(1, missing.Issues);
        Assert.Equal(0, missing.Lease!.Position);
        items.Add(chat);
        var resolved = await service.BalanceAsync(selection, [], missing.Lease, 0);
        Assert.Null(resolved.Lease);
        Assert.Equal(.6, chat.Level);
    }

    [Fact]
    public async Task ConfirmedExpiredInstanceReleasesOwnershipAndLeavesReplacementUnchanged()
    {
        var game = new Native("game", .8);
        var chat = new Native("chat", .6);
        var items = new List<Native> { game, chat };
        await using var service = new ApplicationSessionService(_ => new Endpoint(items));
        var selection = service.Select("out");
        var active = await service.BalanceAsync(selection, Map(game, chat), null, -50);

        chat.Activity = SessionActivity.Expired;
        var replacement = new Native("chat-replacement", .7);
        items.Add(replacement);
        var result = await service.BalanceAsync(selection, [], active.Lease, 0);
        Assert.Null(result.Lease);
        Assert.Equal(1, result.Issues);
        Assert.Single(chat.Writes);
        Assert.Empty(replacement.Writes);
    }

    [Fact]
    public async Task DisposalFailureDoesNotEraseConfirmedAttenuation()
    {
        var game = new Native("game", .8);
        var chat = new Native("chat", .6) { ThrowOnDispose = true };
        await using var service = new ApplicationSessionService(_ => new Endpoint([game, chat], throwOnDispose: true));
        var selection = service.Select("out");

        var active = await service.BalanceAsync(selection, Map(game, chat), null, -50);
        Assert.Equal(-50, active.Lease!.Position);
        Assert.Equal(.3, chat.Level);
        var restored = await service.BalanceAsync(selection, [], active.Lease, 0);
        Assert.Null(restored.Lease);
        Assert.Equal(0, restored.Issues);
        Assert.Equal(.6, chat.Level);
    }

    [Fact]
    public Task BoardRestoresOnGroupChangeDeviceSelectionAndClosing() => PreferencesUiTests.OnSta(async () =>
    {
        using var host = new Form { ClientSize = new Size(800, 600), ShowInTaskbar = false, Opacity = 0 };
        var game = new Native("game", .8);
        var chat = new Native("chat", .6);
        var other = new Native("other", .2, "new");
        var system = new Native("system", .5) { IsSystemSounds = true };
        using var panel = new ApplicationMixerPanel(new ApplicationSessionService(id => new Endpoint(id == "out" ? [game, chat, system] : [other])),
            new EndpointVolumeSession(_ => new FakeEndpointVolume(new FakeEndpointState())));
        host.Controls.Add(panel);
        host.Show();
        try
        {
            var device = new DeviceSnapshot("out", "Fones", EndpointDirection.Playback, EndpointState.Active, false, false, null, null);
            panel.SelectDevice(device);
            await panel.PendingRead;
            var slider = Assert.Single(PreferencesUiTests.Descendants(panel).OfType<TrackBar>(), x => x.Minimum == -100);
            Assert.False(slider.Enabled);
            Assign(panel, game, MixerGroup.Game);
            Assert.False(slider.Enabled);
            Assign(panel, chat, MixerGroup.Chat);
            Assert.True(slider.Enabled);
            Assign(panel, system, MixerGroup.Game);
            Assert.False(slider.Enabled);
            Assign(panel, system, MixerGroup.System);
            Assert.True(slider.Enabled);

            await MoveBalance(slider, -50, () => chat.Level == .3 && panel.MixerViews.Enabled);
            await panel.PendingRead;
            Assign(panel, game, MixerGroup.Media);
            await PreferencesUiTests.PumpUntilAsync(() => chat.Level == .6 && panel.MixerViews.Enabled);
            Assert.Equal(0, slider.Value);
            Assert.False(slider.Enabled);
            await panel.PendingRead;

            Assign(panel, game, MixerGroup.Game);
            await MoveBalance(slider, -50, () => chat.Level == .3 && panel.MixerViews.Enabled);
            panel.SelectDevice(device with { Id = "new" });
            await PreferencesUiTests.PumpUntilAsync(() => panel.Selection.DeviceId == "new");
            await panel.PendingRead;
            Assert.Equal(.6, chat.Level);
            Assert.Empty(other.Writes);
            Assert.False(slider.Enabled);

            panel.SelectDevice(device);
            await panel.PendingRead;
            Assign(panel, game, MixerGroup.Game);
            Assign(panel, chat, MixerGroup.Chat);
            await MoveBalance(slider, -25, () => chat.Writes.Count == 5 && panel.MixerViews.Enabled);
        }
        finally { await panel.StopAsync(); }

        Assert.Equal(.8, game.Level);
        Assert.Equal(.6, chat.Level);
        Assert.Empty(system.Writes);
        Assert.Null(panel.BalanceCloseWarning);
    });

    [Fact]
    public Task MissingOutputKeepsRestorationWarningAfterAnotherBalanceAndClosing() => PreferencesUiTests.OnSta(async () =>
    {
        using var host = new Form { ClientSize = new Size(800, 600), ShowInTaskbar = false, Opacity = 0 };
        var game = new Native("game", .8);
        var chat = new Native("chat", .6);
        var newGame = new Native("game", .7, "new");
        var newChat = new Native("chat", .4, "new");
        bool firstOutputAvailable = true;
        using var panel = new ApplicationMixerPanel(new ApplicationSessionService(id =>
            id == "new" ? new Endpoint([newGame, newChat]) :
            firstOutputAvailable ? new Endpoint([game, chat]) : throw new System.Runtime.InteropServices.COMException()),
            new EndpointVolumeSession(_ => new FakeEndpointVolume(new FakeEndpointState())));
        host.Controls.Add(panel);
        host.Show();
        try
        {
            var device = new DeviceSnapshot("out", "Fones", EndpointDirection.Playback, EndpointState.Active, false, false, null, null);
            panel.SelectDevice(device);
            await panel.PendingRead;
            Assign(panel, game, MixerGroup.Game);
            Assign(panel, chat, MixerGroup.Chat);
            var slider = Assert.Single(PreferencesUiTests.Descendants(panel).OfType<TrackBar>(), x => x.Minimum == -100);
            await MoveBalance(slider, -50, () => chat.Writes.Count == 1 && panel.MixerViews.Enabled);

            firstOutputAvailable = false;
            panel.SelectDevice(device with { Id = "new" });
            await PreferencesUiTests.PumpUntilAsync(() => panel.Selection.DeviceId == "new");
            await panel.PendingRead;
            Assign(panel, newGame, MixerGroup.Game);
            Assign(panel, newChat, MixerGroup.Chat);
            await MoveBalance(slider, -50, () => newChat.Writes.Count == 1 && panel.MixerViews.Enabled);
            var expected = StudioStrings.Format("Balance.PreviousOutput", 1);
            Assert.Contains(PreferencesUiTests.Descendants(panel).OfType<Label>(), label => label.Text.Contains(expected, StringComparison.Ordinal));
        }
        finally { await panel.StopAsync(); }

        Assert.Equal(.3, chat.Level);
        Assert.Equal(.4, newChat.Level);
        Assert.Equal(StudioStrings.Format("Balance.PreviousOutput", 1), panel.BalanceCloseWarning);
    });

    private static void Assign(ApplicationMixerPanel panel, Native session, MixerGroup group)
    {
        var strip = Assert.Single(PreferencesUiTests.Descendants(panel).OfType<MixerStrip>(), s => s.Binding?.Snapshot.Key == session.Key);
        Assert.Single(strip.Controls.OfType<ComboBox>()).SelectedIndex = (int)group;
    }

    private static async Task MoveBalance(TrackBar slider, int position, Func<bool> applied)
    {
        var scroll = typeof(TrackBar).GetMethod("OnScroll", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        slider.Value = position;
        scroll.Invoke(slider, [EventArgs.Empty]);
        await PreferencesUiTests.PumpUntilAsync(applied);
    }

    private static GameChatMember[] Map(Native game, params Native[] chat) =>
        [new(game.Key, GameChatSide.Game), .. chat.Select(x => new GameChatMember(x.Key, GameChatSide.Chat))];

    private static ApplicationSessionService Service(params Native[] items) => new(_ => new Endpoint(items));

    private sealed class Endpoint(IReadOnlyList<Native> items, bool throwOnDispose = false) : IApplicationSessionEndpoint
    {
        public int Count => items.Count;
        public IApplicationSessionAccess Open(int index) => items[index];
        public void Dispose()
        {
            if (throwOnDispose) throw new System.Runtime.InteropServices.COMException();
        }
    }

    private sealed class Native(string id, double initial, string endpointId = "out") : IApplicationSessionAccess
    {
        public SessionKey Key { get; } = new(endpointId, id);
        public SessionActivity Activity { get; set; } = SessionActivity.Active;
        public double Level { get; set; } = initial;
        public List<double> Writes { get; } = [];
        public Action<SessionKey>? OnWrite { get; set; }
        public bool ThrowAfterWrite { get; set; }
        public bool ThrowOnDispose { get; set; }
        public bool IsSystemSounds { get; init; }
        public double? RewriteApplied { get; set; }
        public double VolumeScalar
        {
            get => Level;
            set
            {
                Writes.Add(value);
                OnWrite?.Invoke(Key);
                Level = RewriteApplied ?? value;
                if (ThrowAfterWrite)
                {
                    ThrowAfterWrite = false;
                    throw new System.Runtime.InteropServices.COMException();
                }
            }
        }
        public bool Muted { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public ApplicationSessionSnapshot Read() => new(Key, id, 123, IsSystemSounds, Activity, Level * 100, null, null);
        public void Dispose()
        {
            if (ThrowOnDispose) throw new System.Runtime.InteropServices.COMException();
        }
    }
}
