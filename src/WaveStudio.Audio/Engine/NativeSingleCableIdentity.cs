using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi;

namespace WaveStudio.Audio.Engine;

internal readonly record struct PnpAudioAdapter(string InstanceId, uint Status, uint Problem, string Service, string[] HardwareIds)
{
    private const uint Started = 0x00000008;
    private const uint HasProblem = 0x00000400;
    private bool HasCableService => StringComparer.OrdinalIgnoreCase.Equals(Service, "VBAudioVACMME");
    private bool HasCableHardware => HardwareIds.Contains("VBAudioVACWDM", StringComparer.OrdinalIgnoreCase);
    internal bool HasCableMarker => HasCableService || HasCableHardware;

    internal string RequireRole(bool expectsCable)
    {
        if (string.IsNullOrWhiteSpace(InstanceId) || Problem != 0 || (Status & (Started | HasProblem)) != Started)
            throw new InvalidOperationException("Instância PnP do endpoint indisponível ou com falha.");
        if (expectsCable ? !(HasCableService && HasCableHardware) : HasCableMarker)
            throw new InvalidOperationException("O driver PnP do endpoint não corresponde ao papel esperado.");
        return InstanceId;
    }
}

internal static class NativeSingleCableIdentity
{
    private const uint HardwareIdsProperty = 2;
    private const uint ServiceProperty = 5;
    private const uint StringType = 1;
    private const uint MultiStringType = 7;
    private const uint BufferTooSmall = 0x1A;
    private const uint NoSuchValue = 0x25;

    internal static PnpAudioAdapter Read(MMDeviceEnumerator enumerator, MMDevice endpoint)
    {
        // O ID do conector é específico do subdispositivo. A propriedade do dispositivo conectado identifica o devnode.
        using var connected = enumerator.GetDevice(NativeExternalRoute.ConnectedAdapter(endpoint));
        var instance = connected.Properties[PropertyKeys.PKEY_Device_InstanceId].Value as string;
        if (string.IsNullOrWhiteSpace(instance) ||
            CM_Locate_DevNodeW(out uint node, instance, 0) != 0 ||
            CM_Get_DevNode_Status(out uint status, out uint problem, node, 0) != 0)
            throw new InvalidOperationException("Identidade PnP do dispositivo conectado indisponível.");

        var service = ReadProperty(node, ServiceProperty, StringType).TrimEnd('\0');
        var hardware = ReadProperty(node, HardwareIdsProperty, MultiStringType).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return new(instance, status, problem, service, hardware);
    }

    private static string ReadProperty(uint node, uint property, uint expectedType)
    {
        uint length = 0;
        uint result = CM_Get_DevNode_Registry_PropertyW(node, property, out _, null, ref length, 0);
        if (result == NoSuchValue)
            return string.Empty;
        if (result != BufferTooSmall || length == 0 || length % sizeof(char) != 0)
            throw new InvalidOperationException("Tamanho da propriedade PnP do dispositivo não confirmado.");

        var buffer = new byte[length];
        if (CM_Get_DevNode_Registry_PropertyW(node, property, out uint type, buffer, ref length, 0) != 0 ||
            type != expectedType || length > buffer.Length || length % sizeof(char) != 0)
            throw new InvalidOperationException("Propriedade PnP do dispositivo não confirmada.");
        return Encoding.Unicode.GetString(buffer, 0, (int)length);
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string instance, uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_Registry_PropertyW(uint node, uint property, out uint type,
        [Out] byte[]? buffer, ref uint length, uint flags);
}
