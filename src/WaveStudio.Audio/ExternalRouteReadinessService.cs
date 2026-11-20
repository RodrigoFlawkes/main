using WaveStudio.Audio.Engine;

namespace WaveStudio.Audio;

internal enum RouteMode { Single, Dual }
internal enum RouteEndpointKind { Other, Physical, Single, CableA, CableB }
internal enum RouteAvailability { NotDetected, Partial, Available, Unknown }
internal enum RouteCheck { Confirmed, Missing, Unavailable, Ambiguous, DriverFault, CableIsDefault, UnsupportedFormat, Changed }

internal sealed record RoutePort(DeviceSnapshot Device, RouteEndpointKind Kind, PnpAudioAdapter? Adapter, string? Connection);
internal sealed record RouteInventory(IReadOnlyList<RoutePort> Endpoints, IReadOnlyList<string?> WindowsDefaults,
    IReadOnlyList<PnpAudioAdapter>? SingleDrivers, bool Complete);
internal sealed record RouteSelections(RouteMode Mode, string? BrowserRender = null, string? BrowserCapture = null,
    string? MicrophoneRender = null, string? MicrophoneCapture = null, string? Headset = null, string? Microphone = null);
internal sealed record RouteReadiness(RouteInventory Inventory, long Generation, RouteAvailability SingleCable,
    RouteAvailability DualCable, RouteCheck Pair, RouteCheck Driver, uint? SelectedProblem, bool SecondaryCode10,
    RouteCheck WindowsDefaults, RouteCheck Headset, RouteCheck Mic, RouteCheck Browser, RouteCheck Microphone);

internal sealed class ExternalRouteReadinessService(
    Func<CancellationToken, Task<RouteInventory>> inventorySource,
    Func<TransportEndpoint, CancellationToken, Task<WasapiEndpointInfo>> formatProbe)
{
    private static readonly StringComparer Ids = StringComparer.OrdinalIgnoreCase;

    internal ExternalRouteReadinessService() : this(NativeRouteInventory.QueryAsync, WasapiEndpointProbe.QueryAsync) { }

    internal async Task<RouteReadiness> QueryAsync(RouteSelections selection, long generation, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        token.ThrowIfCancellationRequested();
        RouteInventory inventory;
        try { inventory = await inventorySource(token).ConfigureAwait(false); }
        catch (Exception error) when (AudioError.IsDeviceAccessError(error))
        {
            inventory = new([], [], null, Complete: false);
        }
        token.ThrowIfCancellationRequested();
        bool single = selection.Mode == RouteMode.Single;
        var singlePorts = FindPair(inventory, RouteEndpointKind.Single);
        var aPorts = FindPair(inventory, RouteEndpointKind.CableA);
        var bPorts = FindPair(inventory, RouteEndpointKind.CableB);
        var simple = Availability(inventory, singlePorts is not null, inventory.SingleDrivers is not { Count: 0 } ||
            inventory.Endpoints.Any(p => p.Kind == RouteEndpointKind.Single), inventory.SingleDrivers is null);
        bool twoPairs = aPorts is not null && bPorts is not null && DistinctPairs(aPorts, bPorts);
        var dual = Availability(inventory, twoPairs,
            inventory.Endpoints.Any(p => p.Kind is RouteEndpointKind.CableA or RouteEndpointKind.CableB), false);
        if (!inventory.Complete)
            return new(inventory, generation, simple, dual, RouteCheck.Unavailable, RouteCheck.Unavailable, null, false,
                RouteCheck.Unavailable, RouteCheck.Unavailable, RouteCheck.Unavailable, RouteCheck.Unavailable, RouteCheck.Unavailable);

        var first = new[]
        {
            Find(inventory, selection.BrowserRender, EndpointDirection.Playback, single ? RouteEndpointKind.Single : RouteEndpointKind.CableA),
            Find(inventory, selection.BrowserCapture, EndpointDirection.Capture, single ? RouteEndpointKind.Single : RouteEndpointKind.CableA)
        };
        var second = single ? first : new[]
        {
            Find(inventory, selection.MicrophoneRender, EndpointDirection.Playback, RouteEndpointKind.CableB),
            Find(inventory, selection.MicrophoneCapture, EndpointDirection.Capture, RouteEndpointKind.CableB)
        };
        var head = Find(inventory, selection.Headset, EndpointDirection.Playback, RouteEndpointKind.Physical);
        var mic = Find(inventory, selection.Microphone, EndpointDirection.Capture, RouteEndpointKind.Physical);
        var cable = single ? first : [.. first, .. second];
        var pair = FirstIssue(cable.Select(p => p.Check));
        var driver = pair;
        var defaults = pair;
        uint? problem = null;
        bool secondaryCode10 = false;
        if (pair == RouteCheck.Confirmed)
        {
            var chosen = cable.Select(p => p.Port!).ToArray();
            var adapter = chosen[0].Adapter!.Value;
            pair = SamePair(chosen[0], chosen[1], single) &&
                (single || SamePair(chosen[2], chosen[3], false) &&
                    DistinctPairs(chosen[..2], chosen[2..])) ? RouteCheck.Confirmed : RouteCheck.Ambiguous;
            if (pair == RouteCheck.Confirmed)
            {
                var faulty = chosen.FirstOrDefault(p => !p.Adapter!.Value.IsHealthy);
                if (faulty is not null && faulty.Adapter!.Value.Problem != 0) problem = faulty.Adapter.Value.Problem;
                driver = faulty is null ? RouteCheck.Confirmed : RouteCheck.DriverFault;
                var activePorts = inventory.Endpoints.Where(e => e.Device.State == EndpointState.Active).ToArray();
                secondaryCode10 = single && activePorts.All(e => e.Adapter is not null) &&
                    inventory.SingleDrivers?.Any(p => p.Problem == 10 && !Ids.Equals(p.InstanceId, adapter.InstanceId) &&
                        !activePorts.Any(e => Ids.Equals(e.Adapter?.InstanceId, p.InstanceId))) == true;
                // Uma instância que falhou ainda pode ter uma identidade observada. A saúde bloqueia a rota separadamente.
                if (driver == RouteCheck.Confirmed)
                    pair = CheckPins(inventory, chosen, single, ref defaults);
                else defaults = CheckDefaults(inventory, chosen);
            }
            else driver = defaults = pair;
        }

        var shared = FirstIssue([pair, driver, defaults]);
        var headCheck = PhysicalCheck(head, cable);
        var micCheck = PhysicalCheck(mic, cable);
        var browser = FirstIssue([shared, headCheck]);
        var microphone = FirstIssue([shared, micCheck]);
        var formats = new Dictionary<string, (WasapiEndpointInfo? Info, RouteCheck Check)>(Ids);
        if (browser == RouteCheck.Confirmed)
            browser = await CheckFlowAsync(first[1].Port!, head.Port!, first[0].Port!).ConfigureAwait(false);
        if (microphone == RouteCheck.Confirmed)
            microphone = await CheckFlowAsync(mic.Port!, second[0].Port!, second[1].Port!).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return new(inventory, generation, simple, dual, pair, driver, problem, secondaryCode10, defaults, headCheck,
            micCheck, browser, microphone);

        async Task<RouteCheck> CheckFlowAsync(RoutePort input, RoutePort output, RoutePort appPort)
        {
            var capture = await CheckFormatAsync(input).ConfigureAwait(false);
            var render = await CheckFormatAsync(output).ConfigureAwait(false);
            var app = await CheckFormatAsync(appPort).ConfigureAwait(false);
            var result = FirstIssue([capture.Check, render.Check, app.Check]);
            if (result != RouteCheck.Confirmed) return result;
            try { new TransportRoute(capture.Info!, render.Info!).Validate(); }
            catch (NotSupportedException) { return RouteCheck.UnsupportedFormat; }
            return RouteCheck.Confirmed;
        }

        async Task<(WasapiEndpointInfo? Info, RouteCheck Check)> CheckFormatAsync(RoutePort port)
        {
            if (formats.TryGetValue(port.Device.Id, out var cached)) return cached;
            token.ThrowIfCancellationRequested();
            var requested = new TransportEndpoint(port.Device.Id, port.Device.Direction, generation);
            (WasapiEndpointInfo? Info, RouteCheck Check) result;
            try
            {
                var info = await formatProbe(requested, token).ConfigureAwait(false);
                result = info switch
                {
                    { Endpoint: var actual } when actual != requested => (null, RouteCheck.Changed),
                    { State: EndpointState.Unknown } => (null, RouteCheck.Unavailable),
                    { State: not EndpointState.Active } => (null, RouteCheck.Changed),
                    { Mix: null } => (null, RouteCheck.Unavailable),
                    { Evidence: not FormatEvidence.ExactCapability } or { StreamInitialized: true } or { Error: not null } =>
                        (null, RouteCheck.UnsupportedFormat),
                    _ => (info, RouteCheck.Confirmed)
                };
                if (result.Info is not null) result.Info.Mix!.Validate();
            }
            catch (NotSupportedException) { result = (null, RouteCheck.UnsupportedFormat); }
            catch (Exception error) when (AudioError.IsDeviceAccessError(error)) { result = (null, RouteCheck.Unavailable); }
            token.ThrowIfCancellationRequested();
            formats.Add(port.Device.Id, result);
            return result;
        }
    }

    private static (RoutePort? Port, RouteCheck Check) Find(RouteInventory inventory, string? id,
        EndpointDirection direction, RouteEndpointKind kind)
    {
        if (id is null) return (null, RouteCheck.Missing);
        var matches = inventory.Endpoints.Where(p => Ids.Equals(id, p.Device.Id)).Take(2).ToArray();
        if (matches.Length == 0) return (null, RouteCheck.Missing);
        if (matches.Length != 1) return (null, RouteCheck.Ambiguous);
        var port = matches[0];
        if (port.Device.State != EndpointState.Active || port.Adapter is null || string.IsNullOrWhiteSpace(port.Connection))
            return (port, RouteCheck.Unavailable);
        if (port.Device.Direction != direction || port.Kind != kind)
            return (port, RouteCheck.Ambiguous);
        return (port, kind == RouteEndpointKind.Physical && !port.Adapter.Value.IsHealthy
            ? RouteCheck.DriverFault : RouteCheck.Confirmed);
    }

    private static RouteCheck PhysicalCheck((RoutePort? Port, RouteCheck Check) physical,
        (RoutePort? Port, RouteCheck Check)[] cable) =>
        physical.Check == RouteCheck.Confirmed && cable.Any(p => p.Port is { } virtualPort &&
            (Ids.Equals(virtualPort.Adapter?.InstanceId, physical.Port?.Adapter?.InstanceId) ||
             Ids.Equals(virtualPort.Connection, physical.Port?.Connection)))
            ? RouteCheck.Ambiguous : physical.Check;

    private static bool SamePair(RoutePort render, RoutePort capture, bool single) =>
        !Ids.Equals(render.Device.Id, capture.Device.Id) && render.Adapter is { } a && capture.Adapter is { } b &&
        Ids.Equals(a.InstanceId, b.InstanceId) && (single || Ids.Equals(render.Connection, capture.Connection));

    private static bool DistinctPairs(RoutePort[] a, RoutePort[] b) =>
        !Ids.Equals(a[0].Adapter?.InstanceId, b[0].Adapter?.InstanceId) && !Ids.Equals(a[0].Connection, b[0].Connection) &&
        a.Concat(b).Select(p => p.Device.Id).Distinct(Ids).Count() == 4;

    private static RoutePort[]? FindPair(RouteInventory inventory, RouteEndpointKind kind)
    {
        var candidates = inventory.Endpoints.Where(p => p.Kind == kind && p.Device.State == EndpointState.Active).ToArray();
        var capture = candidates.Where(p => p.Device.Direction == EndpointDirection.Capture).Take(2).ToArray();
        if (capture.Length != 1 || candidates.Any(p => p.Adapter is not { IsHealthy: true } || p.Connection is null)) return null;
        var render = candidates.Where(p => p.Device.Direction == EndpointDirection.Playback).ToArray();
        bool single = kind == RouteEndpointKind.Single;
        if (render.Length == 0 || !render.All(p => SamePair(p, capture[0], single))) return null;
        if (single && candidates.Any(p => !p.Adapter!.Value.IsSingleCable)) return null;
        return [render[0], capture[0]];
    }

    private static RouteAvailability Availability(RouteInventory inventory, bool available, bool detected, bool unknown) =>
        !inventory.Complete ? RouteAvailability.Unknown : available ? RouteAvailability.Available :
        unknown ? RouteAvailability.Unknown : detected ? RouteAvailability.Partial : RouteAvailability.NotDetected;

    private static RouteCheck CheckPins(RouteInventory inventory, RoutePort[] cable, bool single, ref RouteCheck defaults)
    {
        defaults = CheckDefaults(inventory, cable);
        var active = inventory.Endpoints.Where(e => e.Device.State == EndpointState.Active).ToArray();
        if (active.Any(p => p.Adapter is null)) return RouteCheck.Unavailable;
        if (!single)
            return FindPair(inventory, RouteEndpointKind.CableA) is not null &&
                FindPair(inventory, RouteEndpointKind.CableB) is not null ? RouteCheck.Confirmed : RouteCheck.Ambiguous;
        try
        {
            var pins = active.Select(p => new SingleCablePin(p.Device.Id, p.Device.Direction,
                p.Kind == RouteEndpointKind.Single, p.Adapter!.Value));
            NativeCableIsolation.IdentifyPins(cable[0].Adapter!.Value.InstanceId, cable[0].Device.Id, cable[1].Device.Id, pins);
            return RouteCheck.Confirmed;
        }
        catch (InvalidOperationException) { return RouteCheck.Ambiguous; }
    }

    private static RouteCheck CheckDefaults(RouteInventory inventory, RoutePort[] cable)
    {
        if (inventory.WindowsDefaults.Count != 6 || inventory.WindowsDefaults.Any(string.IsNullOrWhiteSpace))
            return RouteCheck.Unavailable;
        var cableInstances = cable.Select(p => p.Adapter!.Value.InstanceId).ToHashSet(Ids);
        var cableIds = inventory.Endpoints.Where(p => p.Adapter is { } adapter && cableInstances.Contains(adapter.InstanceId))
            .Select(p => p.Device.Id).ToHashSet(Ids);
        return inventory.WindowsDefaults.Any(id => cableIds.Contains(id!)) ? RouteCheck.CableIsDefault : RouteCheck.Confirmed;
    }

    private static RouteCheck FirstIssue(IEnumerable<RouteCheck> checks) =>
        checks.FirstOrDefault(check => check != RouteCheck.Confirmed, RouteCheck.Confirmed);
}
