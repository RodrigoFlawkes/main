using System.Runtime.InteropServices;
using WaveStudio.Audio;
using WaveStudio.Audio.Engine;
using Xunit;

namespace WaveStudio.Tests;

public sealed class ExternalRouteReadinessTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly PnpAudioAdapter Single = new("vb", 8, 0, "VBAudioVACMME", ["VBAudioVACWDM"]);
    private static readonly PnpAudioAdapter Secondary = Single with { InstanceId = "vb-other", Status = 0x400, Problem = 10 };
    private static readonly PnpAudioAdapter CableA = new("a", 8, 0, "cable-a", ["cable-a"]);
    private static readonly PnpAudioAdapter CableB = new("b", 8, 0, "cable-b", ["cable-b"]);
    private static readonly PnpAudioAdapter Usb = new("usb-headset", 8, 0, "usbaudio", ["USB"]);
    private static readonly PnpAudioAdapter Mic = Usb with { InstanceId = "usb-mic" };
    internal static readonly RouteSelections SingleChoices = new(RouteMode.Single, "single-in", "single-out", null, null, "headset", "mic");
    private static readonly RouteSelections DualChoices = new(RouteMode.Dual, "a-in", "a-out", "b-in", "b-out", "headset", "mic");

    [Fact]
    public async Task RecognizesOneHealthyInstanceWithThreePinsAndSeparatesCode10()
    {
        var result = await Service(Fixture()).QueryAsync(SingleChoices, 4, Token);
        Assert.Equal(4, result.Generation);
        Assert.Equal(RouteAvailability.Available, result.SingleCable);
        Assert.Equal(RouteAvailability.NotDetected, result.DualCable);
        Assert.True(result.SecondaryCode10);
        Assert.Equal(RouteCheck.Confirmed, result.Pair);
        Assert.Equal(RouteCheck.Confirmed, result.Driver);
        Assert.Equal(RouteCheck.Confirmed, result.WindowsDefaults);
        Assert.Equal(RouteCheck.Confirmed, result.Browser);
        Assert.Equal(RouteCheck.Confirmed, result.Microphone);
    }

    [Fact]
    public async Task CableWithoutEndpointsIsNotReportedAsAvailable()
    {
        var inventory = Fixture();
        inventory = inventory with { Endpoints = inventory.Endpoints.Where(e => e.Kind == RouteEndpointKind.Physical).ToArray() };
        var result = await Service(inventory).QueryAsync(SingleChoices, 1, Token);
        Assert.Equal(RouteAvailability.Partial, result.SingleCable);
        Assert.Equal(RouteCheck.Missing, result.Pair);

        inventory = inventory with { SingleDrivers = [] };
        result = await Service(inventory).QueryAsync(SingleChoices, 2, Token);
        Assert.Equal(RouteAvailability.NotDetected, result.SingleCable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AbRequiresTwoDistinctVerifiedAdapterPairs(bool distinct)
    {
        var result = await Service(WithDualCable(distinct)).QueryAsync(DualChoices, 7, Token);
        Assert.Equal(distinct ? RouteAvailability.Available : RouteAvailability.Partial, result.DualCable);
        Assert.Equal(distinct ? RouteCheck.Confirmed : RouteCheck.Ambiguous, result.Browser);
        Assert.Equal(distinct ? RouteCheck.Confirmed : RouteCheck.Ambiguous, result.Microphone);
    }

    [Theory]
    [InlineData("headset", EndpointState.Unplugged)]
    [InlineData("mic", EndpointState.NotPresent)]
    public async Task PhysicalEndpointLossBlocksItsFlowWithoutSelectingAnother(string id, EndpointState state)
    {
        var inventory = ChangePort(Fixture(), id, p => p with { Device = p.Device with { State = state } });
        var result = await Service(inventory).QueryAsync(SingleChoices, 5, Token);
        Assert.Equal(id == "headset" ? RouteCheck.Unavailable : RouteCheck.Confirmed, result.Browser);
        Assert.Equal(id == "mic" ? RouteCheck.Unavailable : RouteCheck.Confirmed, result.Microphone);
    }

    [Fact]
    public async Task RejectsVirtualHardwareAndWrongDirectionForPhysicalSelections()
    {
        var service = Service(Fixture());
        Assert.Equal(RouteCheck.Ambiguous, (await service.QueryAsync(SingleChoices with { Headset = "single-in" }, 1, Token)).Headset);
        Assert.Equal(RouteCheck.Ambiguous, (await service.QueryAsync(SingleChoices with { Microphone = "headset" }, 2, Token)).Mic);
    }

    [Fact]
    public async Task MarksCode10OnSelectedInstanceAsTheDriverBlocker()
    {
        var original = Fixture();
        var fault = Single with { Problem = 10, Status = 0x400 };
        var inventory = original with { SingleDrivers = [fault],
            Endpoints = original.Endpoints.Select(p => p.Kind == RouteEndpointKind.Single ? p with { Adapter = fault } : p).ToArray() };
        var result = await Service(inventory).QueryAsync(SingleChoices, 2, Token);
        Assert.Equal(RouteCheck.Confirmed, result.Pair);
        Assert.Equal(RouteCheck.DriverFault, result.Driver);
        Assert.Equal(10u, result.SelectedProblem);
        Assert.False(result.SecondaryCode10);
        Assert.Equal(RouteCheck.DriverFault, result.Browser);
        Assert.Equal(RouteCheck.DriverFault, result.Microphone);
    }

    [Fact]
    public async Task DetectsCableDefaultThroughTheAdditionalPlaybackPinAndFailsClosedOnUnknownRole()
    {
        var inventory = Fixture();
        foreach (var (replacement, expected) in new[] { ("single-extra", RouteCheck.CableIsDefault), (null, RouteCheck.Unavailable) })
        {
            var result = await Service(inventory with { WindowsDefaults = ["headset", replacement, "headset", "mic", "mic", "mic"] })
                .QueryAsync(SingleChoices, 1, Token);
            Assert.Equal(expected, result.WindowsDefaults);
            Assert.Equal(expected, result.Browser);
        }
    }

    [Fact]
    public async Task RejectsMissingMetadataAndDuplicateEndpointIdentity()
    {
        var inventory = Fixture();
        var missing = ChangePort(inventory, "single-out", p => p with { Adapter = null });
        var unknown = await Service(missing).QueryAsync(SingleChoices, 1, Token);
        Assert.Equal(RouteCheck.Unavailable, unknown.Pair);
        var duplicate = inventory with { Endpoints = [.. inventory.Endpoints, inventory.Endpoints[0]] };
        Assert.Equal(RouteCheck.Ambiguous, (await Service(duplicate).QueryAsync(SingleChoices, 2, Token)).Pair);
    }

    [Fact]
    public async Task RejectsUnsupportedFormatStaleGenerationAndProbeErrorWithoutCallingStreams()
    {
        var inventory = Fixture();
        foreach (var expected in new[] { RouteCheck.UnsupportedFormat, RouteCheck.Changed, RouteCheck.Unavailable })
        {
            var service = new ExternalRouteReadinessService(_ => Task.FromResult(inventory), (endpoint, _) =>
            {
                if (endpoint.Id != "headset") return Task.FromResult(Exact(endpoint));
                if (expected == RouteCheck.Unavailable) throw new COMException("Identificador privado");
                return Task.FromResult(expected == RouteCheck.Changed
                    ? Exact(endpoint with { Generation = endpoint.Generation - 1 })
                    : Exact(endpoint) with { Mix = new WasapiPcm(3, 96000, 1, 32, 32, 4, 384000, 0, WasapiPcm.FloatSubFormat, null) });
            });
            var result = await service.QueryAsync(SingleChoices, 5, Token);
            Assert.Equal(expected, result.Browser);
            Assert.Equal(RouteCheck.Confirmed, result.Microphone);
        }
    }

    [Fact]
    public async Task FailedOrCanceledInventoryDoesNotProduceAReadyRoute()
    {
        var service = new ExternalRouteReadinessService(_ => throw new COMException("Dado privado"), Probe);
        var result = await service.QueryAsync(SingleChoices, 1, Token);
        Assert.False(result.Inventory.Complete);
        Assert.Equal(RouteCheck.Unavailable, result.Browser);
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.QueryAsync(SingleChoices, 2, stopped.Token));
    }

    internal static RouteInventory WithDualCable(bool distinct)
    {
        var inventory = Fixture();
        var b = distinct ? CableB : CableA;
        return inventory with { Endpoints = [.. inventory.Endpoints,
            Port("a-in", EndpointDirection.Playback, RouteEndpointKind.CableA, CableA),
            Port("a-out", EndpointDirection.Capture, RouteEndpointKind.CableA, CableA),
            Port("b-in", EndpointDirection.Playback, RouteEndpointKind.CableB, b),
            Port("b-out", EndpointDirection.Capture, RouteEndpointKind.CableB, b)] };
    }

    internal static RouteInventory Fixture() => new([
        Port("single-in", EndpointDirection.Playback, RouteEndpointKind.Single, Single, "render-topology"),
        Port("single-extra", EndpointDirection.Playback, RouteEndpointKind.Single, Single, "render-topology"),
        Port("single-out", EndpointDirection.Capture, RouteEndpointKind.Single, Single, "capture-topology"),
        Port("headset", EndpointDirection.Playback, RouteEndpointKind.Physical, Usb),
        Port("mic", EndpointDirection.Capture, RouteEndpointKind.Physical, Mic)],
        ["headset", "headset", "headset", "mic", "mic", "mic"], [Single, Secondary], Complete: true);

    internal static ExternalRouteReadinessService Service(RouteInventory inventory) => new(_ => Task.FromResult(inventory), Probe);
    internal static Task<WasapiEndpointInfo> Probe(TransportEndpoint endpoint, CancellationToken _) => Task.FromResult(Exact(endpoint));
    private static WasapiEndpointInfo Exact(TransportEndpoint endpoint) => new(endpoint, EndpointState.Active,
        new WasapiPcm(3, 48000, 2, 32, 32, 8, 384000, 0, WasapiPcm.FloatSubFormat, null),
        100000, 50000, null, null, FormatEvidence.ExactCapability, false, null);

    private static RouteInventory ChangePort(RouteInventory inventory, string id, Func<RoutePort, RoutePort> change) =>
        inventory with { Endpoints = inventory.Endpoints.Select(p => p.Device.Id == id ? change(p) : p).ToArray() };

    private static RoutePort Port(string id, EndpointDirection direction, RouteEndpointKind kind, PnpAudioAdapter adapter,
        string? connector = null) => new(new(id, id, direction, EndpointState.Active, false, false,
            new(48000, 2, 32, "Extensible"), null), kind, adapter, connector ?? adapter.InstanceId);
}
