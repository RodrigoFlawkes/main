using System.Diagnostics;
using WaveStudio.Audio;
using WaveStudio.Desktop;
using Xunit;

namespace WaveStudio.Tests;

public sealed class MixerGridTests
{
    private sealed class TestForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }
    private sealed class EmptyEndpoint : IApplicationSessionEndpoint
    {
        public int Count => 0;
        public IApplicationSessionAccess Open(int index) => throw new InvalidOperationException();
        public void Dispose() { }
    }

    private static ApplicationSessionSnapshot Session(string id, double volume = 50) =>
        new(new("out", id), "Aplicativo", 3, false, SessionActivity.Active, volume, false, null);
    private static ApplicationSessionCatalog Catalog(params ApplicationSessionSnapshot[] sessions) => new(sessions, []);
    private static DeviceSnapshot Device => new("out", "Saída", EndpointDirection.Playback, EndpointState.Active, true, true, null, null);

    private static Task OnSta(Action<ApplicationMixerPanel> test, Func<string, IApplicationSessionEndpoint>? openEndpoint = null)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new TestForm { ClientSize = new Size(1000, 700), ShowInTaskbar = false, Opacity = 0 };
                using var panel = new ApplicationMixerPanel(new ApplicationSessionService(openEndpoint ?? (_ => new EmptyEndpoint())),
                    new EndpointVolumeSession(_ => throw new NotSupportedException()));
                form.Controls.Add(panel);
                form.Show();
                panel.MixerViews.SelectedIndex = 1;
                _ = panel.SessionGrid.Handle;
                panel.SelectDevice(Device);
                Pump(panel.PendingRead);
                try { test(panel); }
                finally { Pump(panel.StopAsync().AsTask()); }
                completion.TrySetResult();
            }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void Pump(Task pending)
    {
        var watch = Stopwatch.StartNew();
        while (!pending.IsCompleted)
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("A operação da grid não terminou.");
            Application.DoEvents();
            Thread.Sleep(5);
        }
        pending.GetAwaiter().GetResult();
    }

    [Fact]
    public Task ReconciliationKeepsRowIdentityCurrentCellAndOtherColumns() => OnSta(panel =>
    {
        var target = panel.Selection;
        panel.ShowCatalog(target, Catalog(Session("a"), Session("b")));
        var grid = panel.SessionGrid;
        var row = grid.Rows[0];
        var focused = row.Cells["Mute"];
        grid.CurrentCell = focused;
        panel.ShowCatalog(target, Catalog(Session("a", 30), Session("b", 40), Session("c")));
        Assert.Same(row, grid.Rows[0]);
        Assert.Same(focused, grid.CurrentCell);
        Assert.Equal("30.0", Convert.ToString(row.Cells["Volume"].Value, System.Globalization.CultureInfo.InvariantCulture)?.Replace(',', '.'));
        panel.ShowCatalog(target, Catalog(Session("a", 35), Session("c")));
        Assert.Same(row, grid.Rows[0]);
        Assert.Equal(2, grid.Rows.Count);
    });

    [Fact]
    public Task EditingIsPreservedAndCancelRestoresValueWithoutWriting() => OnSta(panel =>
    {
        var target = panel.Selection;
        panel.ShowCatalog(target, Catalog(Session("a")));
        var grid = panel.SessionGrid;
        grid.CurrentCell = grid.Rows[0].Cells["Volume"];
        Assert.True(grid.BeginEdit(false));
        ((TextBox)grid.EditingControl!).Text = "25";
        panel.ShowCatalog(target, Catalog(Session("a", 75)));
        Assert.True(grid.IsCurrentCellInEditMode);
        Assert.Equal("25", ((TextBox)grid.EditingControl!).Text);
        grid.CancelEdit();
        Assert.True(grid.EndEdit());
        Assert.False(grid.IsCurrentCellInEditMode);
        Assert.Equal(50, ((ApplicationSessionBinding)grid.Rows[0].Tag!).Snapshot.VolumePercent);
        panel.ShowCatalog(target, Catalog(Session("a", 75)));
        Assert.Equal(75, ((ApplicationSessionBinding)grid.Rows[0].Tag!).Snapshot.VolumePercent);
    });

    [Fact]
    public Task ReselectionInvalidatesOldRowsAndLateSnapshotEvenForSameEndpoint() => OnSta(panel =>
    {
        var old = panel.Selection;
        panel.ShowCatalog(old, Catalog(Session("a")));
        panel.SessionGrid.CurrentCell = panel.SessionGrid.Rows[0].Cells["Volume"];
        panel.SessionGrid.BeginEdit(false);
        panel.SelectDevice(Device);
        Pump(panel.PendingRead);
        Assert.NotEqual(old, panel.Selection);
        panel.ShowCatalog(old, Catalog(Session("a")));
        Assert.Empty(panel.SessionGrid.Rows.Cast<DataGridViewRow>());
    });

    [Fact]
    public async Task ReconciliationRequestedDuringReadRunsImmediatelyAfterIt()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int reads = 0;
        try
        {
            await OnSta(panel =>
            {
                var button = panel.Controls[0].Controls.OfType<Button>().Single();
                try
                {
                    button.PerformClick();
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                    button.PerformClick();
                }
                finally
                {
                    // Solta o produtor mesmo se alguma asserção falhar;
                    // o fechamento da janela não pode ficar aguardando essa leitura.
                    release.Set();
                }
                Pump(panel.PendingRead);
                // O poll de 1 segundo pode produzir leituras extras durante o teste;
                // importa provar que a reconciliação enfileirada foi executada.
                Assert.True(Volatile.Read(ref reads) >= 3, "A reconciliação solicitada não foi executada.");
            }, _ =>
            {
                if (Interlocked.Increment(ref reads) == 2)
                {
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                }
                return new EmptyEndpoint();
            });
        }
        finally { release.Set(); }
    }

    [Fact]
    public Task SessionPeaksKeepRowsAndFocusAndExposePauseStaleAndUnavailable() => OnSta(panel =>
    {
        long now = Stopwatch.GetTimestamp();
        var target = panel.Selection;
        var first = Session("a") with { Peak = PeakReading.FromScalar(0.25), PeakRecordedAt = now };
        panel.ShowCatalog(target, Catalog(first));
        var grid = panel.SessionGrid;
        var row = grid.Rows[0];
        grid.CurrentCell = row.Cells["Mute"];
        Assert.StartsWith("25", Convert.ToString(row.Cells["Peak"].Value));
        panel.ShowCatalog(target, Catalog(first with { Peak = PeakReading.FromScalar(0) }));
        Assert.Same(row, grid.Rows[0]);
        Assert.Same(row.Cells["Mute"], grid.CurrentCell);
        Assert.Contains("silêncio", Convert.ToString(row.Cells["Peak"].Value));
        panel.UpdatePeakCells(now, paused: true);
        Assert.Equal("Pausado", row.Cells["Peak"].Value);
        panel.UpdatePeakCells(now + Stopwatch.Frequency * 3, paused: false);
        Assert.Equal("Leitura atrasada", row.Cells["Peak"].Value);
        panel.ShowCatalog(target, Catalog(first with { Peak = PeakReading.Unavailable }));
        Assert.Equal("Indisponível", row.Cells["Peak"].Value);
        var old = target;
        panel.SelectDevice(Device);
        Pump(panel.PendingRead);
        panel.ShowCatalog(old, Catalog(first));
        Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
    });
}
