using System.Runtime.InteropServices;

namespace WaveStudio.Audio;

public static class AudioError
{
    public static bool IsDeviceAccessError(Exception error) =>
        error is COMException or UnauthorizedAccessException or InvalidOperationException;

    public static string Describe(Exception error) => error switch
    {
        UnauthorizedAccessException => StudioStrings.Get("AudioError.OWindowsNegouAcessoAs"),
        COMException { HResult: unchecked((int)0x80070490) } => StudioStrings.Get("AudioError.ODispositivoNaoEstaMais"),
        COMException { HResult: unchecked((int)0x88890004) } => StudioStrings.Get("AudioError.ODispositivoFoiDesconectadoOu"),
        COMException => StudioStrings.Format("AudioError.NaoFoiPossivelConsultarO", error.HResult),
        _ => StudioStrings.Get("AudioError.NaoFoiPossivelConsultarAs")
    };
}
