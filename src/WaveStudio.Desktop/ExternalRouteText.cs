using WaveStudio.Audio;

namespace WaveStudio.Desktop;

// Textos deste módulo reunidos para futura migração a recursos locais.
internal static class ExternalRouteText
{
    internal const string Tab = "Roteamento externo";
    internal const string Title = "Roteamento externo (experimental)";
    internal const string ProofPending = "Áudio das rotas inativo. Prova física instrumentada pendente.";
    internal const string BootEvidence = "O registro da prova instrumentada documenta Inicialização Rápida. O boot completo pós-instalação segue sem comprovação nesse registro.";
    internal const string LocalOnly = "As escolhas valem para esta sessão do Wave Studio. Configure Windows e aplicativos manualmente em uma prova autorizada.";
    internal const string RefreshButton = "&Atualizar prontidão";
    internal const string Mode = "Modo de cabo";
    internal const string SingleMode = "VB-CABLE simples";
    internal const string DualMode = "VB-CABLE A+B";
    internal const string SingleRender = "Cabo simples — reprodução";
    internal const string SingleCapture = "Cabo simples — captura";
    internal const string BrowserRender = "Cabo A — reprodução";
    internal const string BrowserCapture = "Cabo A — captura";
    internal const string MicRender = "Cabo B — reprodução";
    internal const string MicCapture = "Cabo B — captura";
    internal const string Headset = "Headset físico — reprodução";
    internal const string Mic = "Microfone físico — captura";
    internal const string Browser = "Navegador → headset";
    internal const string Microphone = "Microfone → OBS";
    internal const string Pair = "Identidade e pareamento";
    internal const string Driver = "Driver selecionado";
    internal const string Defaults = "Padrões do Windows";
    internal const string SingleStatus = "VB-CABLE simples";
    internal const string DualStatus = "Disponibilidade A+B";
    internal const string UnlinkedCode10 = "Código 10 observado no inventário do cabo simples. O vínculo ao par selecionado não foi confirmado.";
    internal const string FlowConfirmed = "formato exato e geração confirmados em leitura";
    internal const string SeparateCode10 = "Código 10 em outra instância, sem pino ativo associado.";
    internal const string DualNotDetected = "A+B não detectado como instalado no inventário de endpoints.";
    internal const string Confirmed = "confirmado em leitura";
    internal const string Missing = "seleção ausente ou dispositivo removido";
    internal const string Waiting = "Aguardando consulta";
    internal const string Updating = "Atualizando dispositivos e prontidão…";
    internal const string Consulted = "Pré-verificação atualizada";
    internal const string QueryIssue = "Consulta incompleta. Atualize para verificar de novo.";
    internal const string PreviousUnavailable = "Escolha anterior indisponível";
    internal const string Choose = "Selecione um dispositivo";
    internal const string FormatUnknown = "Formato indisponível";
    internal const string Inventory = "Dispositivos encontrados";
    internal const string Device = "Dispositivo";
    internal const string Direction = "Direção";
    internal const string State = "Estado";
    internal const string Format = "Formato do dispositivo";
    internal const string GuideTitle = "Configuração manual para uma futura prova autorizada";
    internal const string LocalChoice = "Escolha de diagnóstico válida só nesta sessão do Wave Studio.";
    internal const string DirectionPlayback = "Reprodução";
    internal const string DirectionCapture = "Captura";

    internal static string Check(RouteCheck value) => value switch
    {
        RouteCheck.Confirmed => Confirmed,
        RouteCheck.Missing => Missing,
        RouteCheck.Unavailable => "consulta ou dispositivo indisponível",
        RouteCheck.Ambiguous => "identidade, direção ou pareamento incompatível",
        RouteCheck.DriverFault => "instância PnP com falha ou fora de operação",
        RouteCheck.CableIsDefault => "um pino do cabo é padrão do Windows",
        RouteCheck.UnsupportedFormat => "formato exato incompatível ou não confirmado",
        RouteCheck.Changed => "o dispositivo mudou durante a consulta",
        _ => Waiting
    };

    internal static string Availability(RouteAvailability value, bool dual = false) => value switch
    {
        RouteAvailability.Available => dual ? "dois pares ativos detectados." : "par ativo detectado.",
        RouteAvailability.NotDetected => dual ? DualNotDetected : "nenhuma instância ou endpoint detectado.",
        RouteAvailability.Partial => "detectado sem todos os pinos ativos e confirmados.",
        _ => "inventário indisponível ou incompleto."
    };

    internal static string DeviceState(EndpointState value) => value switch
    {
        EndpointState.Active => "Ativo",
        EndpointState.Disabled => "Desabilitado",
        EndpointState.NotPresent => "Ausente",
        EndpointState.Unplugged => "Desconectado",
        _ => "Indeterminado"
    };

    internal static string Mix(MixFormatSnapshot? mix) => mix is null ? FormatUnknown :
        $"{mix.SampleRate} Hz · {mix.Channels} canal(is) · {mix.BitsPerSample} bits · {mix.Encoding}";

    internal static string Option(DeviceSnapshot device) => $"{device.Name} · {DeviceState(device.State)} · {Mix(device.MixFormat)}";

    internal static string Guide(RouteMode mode) => (mode == RouteMode.Single
        ? """
          Cabo simples — dois fluxos sequenciais. Simultaneidade indisponível.
          1. Guarde as seleções originais. No mixer de volume do Windows 11, ou em Preferências de volume do aplicativo e do dispositivo no Windows 10, selecione só para o navegador a reprodução do cabo. O caminho planejado é navegador → cabo → DSP → headset.
          2. Depois da parada confirmada, pare o estímulo e restaure a saída original do navegador. Confirme a quietude do cabo e a ausência de outros produtores antes da troca.
          3. No OBS, use uma única fonte Captura de entrada de áudio para a captura do cabo. O caminho planejado é microfone → DSP → cabo → fonte do OBS. Mantenha a monitoração desligada, sem duplicar a fonte nas configurações globais. Depois da parada, restaure a seleção original.
          """
        : """
          Cabo A+B — dois pares independentes. Simultaneidade indisponível.
          1. Guarde as seleções originais. No mixer de volume do Windows 11, ou em Preferências de volume do aplicativo e do dispositivo no Windows 10, selecione só para o navegador a reprodução do cabo A. O caminho planejado é navegador → cabo A → DSP → headset.
          2. No OBS, use uma única fonte Captura de entrada de áudio para a captura do cabo B. O caminho planejado é microfone → DSP → cabo B → fonte do OBS. Mantenha a monitoração desligada, sem duplicar a fonte nas configurações globais.
          3. Confirme parada e liberação em cada fluxo e restaure as seleções originais.
          """) + """



          Mantenha os padrões do Windows fora dos cabos. A consulta não mede áudio, latência ou atividade do OBS.

          Windows — saída por aplicativo
          https://support.microsoft.com/en-au/windows/hardware/audio/fix-app-audio-not-working-while-system-sounds-work-in-windows
          OBS — fontes de áudio
          https://obsproject.com/kb/audio-sources/
          Manual do VB-CABLE
          https://vb-audio.com/Cable/VBCABLE_ReferenceManual.pdf
          """;
}
