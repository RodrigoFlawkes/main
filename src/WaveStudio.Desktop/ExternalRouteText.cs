using WaveStudio.Audio;

namespace WaveStudio.Desktop;

// Textos deste módulo reunidos para futura migração a recursos locais.
internal static class ExternalRouteText
{
    internal static string Tab => StudioStrings.Get("ExternalRouteText.Tab");
    internal static string Title => StudioStrings.Get("ExternalRouteText.Title");
    internal static string ProofPending => StudioStrings.Get("ExternalRouteText.ProofPending");
    internal static string BootEvidence => StudioStrings.Get("ExternalRouteText.BootEvidence");
    internal static string LocalOnly => StudioStrings.Get("ExternalRouteText.LocalOnly");
    internal static string RefreshButton => StudioStrings.Get("ExternalRouteText.RefreshButton");
    internal static string Mode => StudioStrings.Get("ExternalRouteText.Mode");
    internal static string SingleMode => StudioStrings.Get("ExternalRouteText.SingleMode");
    internal static string DualMode => StudioStrings.Get("ExternalRouteText.DualMode");
    internal static string SingleRender => StudioStrings.Get("ExternalRouteText.SingleRender");
    internal static string SingleCapture => StudioStrings.Get("ExternalRouteText.SingleCapture");
    internal static string BrowserRender => StudioStrings.Get("ExternalRouteText.BrowserRender");
    internal static string BrowserCapture => StudioStrings.Get("ExternalRouteText.BrowserCapture");
    internal static string MicRender => StudioStrings.Get("ExternalRouteText.MicRender");
    internal static string MicCapture => StudioStrings.Get("ExternalRouteText.MicCapture");
    internal static string Headset => StudioStrings.Get("ExternalRouteText.Headset");
    internal static string Mic => StudioStrings.Get("ExternalRouteText.Mic");
    internal static string Browser => StudioStrings.Get("ExternalRouteText.Browser");
    internal static string Microphone => StudioStrings.Get("ExternalRouteText.Microphone");
    internal static string Pair => StudioStrings.Get("ExternalRouteText.Pair");
    internal static string Driver => StudioStrings.Get("ExternalRouteText.Driver");
    internal static string Defaults => StudioStrings.Get("ExternalRouteText.Defaults");
    internal static string SingleStatus => StudioStrings.Get("ExternalRouteText.SingleStatus");
    internal static string DualStatus => StudioStrings.Get("ExternalRouteText.DualStatus");
    internal static string UnlinkedCode10 => StudioStrings.Get("ExternalRouteText.UnlinkedCode10");
    internal static string FlowConfirmed => StudioStrings.Get("ExternalRouteText.FlowConfirmed");
    internal static string SeparateCode10 => StudioStrings.Get("ExternalRouteText.SeparateCode10");
    internal static string DualNotDetected => StudioStrings.Get("ExternalRouteText.DualNotDetected");
    internal static string Confirmed => StudioStrings.Get("ExternalRouteText.Confirmed");
    internal static string Missing => StudioStrings.Get("ExternalRouteText.Missing");
    internal static string Waiting => StudioStrings.Get("ExternalRouteText.Waiting");
    internal static string Updating => StudioStrings.Get("ExternalRouteText.Updating");
    internal static string Consulted => StudioStrings.Get("ExternalRouteText.Consulted");
    internal static string QueryIssue => StudioStrings.Get("ExternalRouteText.QueryIssue");
    internal static string PreviousUnavailable => StudioStrings.Get("ExternalRouteText.PreviousUnavailable");
    internal static string Choose => StudioStrings.Get("ExternalRouteText.Choose");
    internal static string FormatUnknown => StudioStrings.Get("ExternalRouteText.FormatUnknown");
    internal static string Inventory => StudioStrings.Get("ExternalRouteText.Inventory");
    internal static string Device => StudioStrings.Get("ExternalRouteText.Device");
    internal static string Direction => StudioStrings.Get("ExternalRouteText.Direction");
    internal static string State => StudioStrings.Get("ExternalRouteText.State");
    internal static string Format => StudioStrings.Get("ExternalRouteText.Format");
    internal static string GuideTitle => StudioStrings.Get("ExternalRouteText.GuideTitle");
    internal static string LocalChoice => StudioStrings.Get("ExternalRouteText.LocalChoice");
    internal static string DirectionPlayback => StudioStrings.Get("ExternalRouteText.DirectionPlayback");
    internal static string DirectionCapture => StudioStrings.Get("ExternalRouteText.DirectionCapture");

    internal static string Check(RouteCheck value) => value switch
    {
        RouteCheck.Confirmed => Confirmed,
        RouteCheck.Missing => Missing,
        RouteCheck.Unavailable => StudioStrings.Get("ExternalRouteText.ConsultaOuDispositivoIndisponivel"),
        RouteCheck.Ambiguous => StudioStrings.Get("ExternalRouteText.IdentidadeDirecaoOuPareamentoIncompativel"),
        RouteCheck.DriverFault => StudioStrings.Get("ExternalRouteText.InstanciaPnPComFalhaOu"),
        RouteCheck.CableIsDefault => StudioStrings.Get("ExternalRouteText.UmPinoDoCaboE"),
        RouteCheck.UnsupportedFormat => StudioStrings.Get("ExternalRouteText.FormatoExatoIncompativelOuNao"),
        RouteCheck.Changed => StudioStrings.Get("ExternalRouteText.ODispositivoMudouDuranteA"),
        _ => Waiting
    };

    internal static string Availability(RouteAvailability value, bool dual = false) => value switch
    {
        RouteAvailability.Available => dual ? StudioStrings.Get("ExternalRouteText.DoisParesAtivosDetectados") : StudioStrings.Get("ExternalRouteText.ParAtivoDetectado"),
        RouteAvailability.NotDetected => dual ? DualNotDetected : StudioStrings.Get("ExternalRouteText.NenhumaInstanciaOuEndpointDetectado"),
        RouteAvailability.Partial => StudioStrings.Get("ExternalRouteText.DetectadoSemTodosOsPinos"),
        _ => StudioStrings.Get("ExternalRouteText.InventarioIndisponivelOuIncompleto")
    };

    internal static string DeviceState(EndpointState value) => value switch
    {
        EndpointState.Active => StudioStrings.Get("ExternalRouteText.Ativo"),
        EndpointState.Disabled => StudioStrings.Get("ExternalRouteText.Desabilitado"),
        EndpointState.NotPresent => "Ausente",
        EndpointState.Unplugged => StudioStrings.Get("ExternalRouteText.Desconectado"),
        _ => StudioStrings.Get("ExternalRouteText.Indeterminado")
    };

    internal static string Mix(MixFormatSnapshot? mix) => mix is null ? FormatUnknown :
        StudioStrings.Format("ExternalRouteText.0Hz1CanalIs", mix.SampleRate, mix.Channels, mix.BitsPerSample, mix.Encoding);

    internal static string Option(DeviceSnapshot device) => $"{device.Name} · {DeviceState(device.State)} · {Mix(device.MixFormat)}";

    internal static string Guide(RouteMode mode) => (mode == RouteMode.Single
        ? StudioStrings.Get("ExternalRouteText.CaboSimplesDoisFluxosSequenciais")
        : StudioStrings.Get("ExternalRouteText.CaboABDoisPares")) + StudioStrings.Get("ExternalRouteText.MantenhaOsPadroesDoWindows");
}
