using System.Runtime.InteropServices;

namespace WaveStudio.Audio;

public static class AudioError
{
    public static bool IsDeviceAccessError(Exception error) =>
        error is COMException or UnauthorizedAccessException or InvalidOperationException;

    public static string Describe(Exception error) => error switch
    {
        UnauthorizedAccessException => "O Windows negou acesso às informações de áudio. Confira as permissões do dispositivo.",
        COMException { HResult: unchecked((int)0x80070490) } => "O dispositivo não está mais disponível. Atualize a lista.",
        COMException { HResult: unchecked((int)0x88890004) } => "O dispositivo foi desconectado ou reconfigurado. Atualize a lista.",
        COMException => $"Não foi possível consultar o áudio do Windows (código 0x{error.HResult:X8}). Confira a conexão e tente atualizar.",
        _ => "Não foi possível consultar as informações do dispositivo. Tente atualizar a lista."
    };
}
