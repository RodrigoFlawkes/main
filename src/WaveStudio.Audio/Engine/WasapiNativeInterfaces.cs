using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace WaveStudio.Audio.Engine;

// Windows SDK mmdeviceapi.idl / Audioclient.idl; full inherited IUnknown method order.
// Signatures only, no third-party implementation. See docs/WASAPI_BACKEND_PLAN.md.
// ComObjectWrapper generates calls to native COM only, not a callable audio server.
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IPreparationEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int flow, uint states, out nint devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out nint device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out nint device);
    [PreserveSig] int RegisterEndpointNotificationCallback(nint callback);
    [PreserveSig] int UnregisterEndpointNotificationCallback(nint callback);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IPreparationDevice
{
    [PreserveSig] int Activate(in Guid iid, uint context, nint parameters, out nint instance);
    [PreserveSig] int OpenPropertyStore(uint access, out nint properties);
    [PreserveSig] int GetId(out nint id);
    [PreserveSig] int GetState(out uint state);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("1BE09788-6894-4089-8586-9A2A6C265AC5")]
internal partial interface IPreparationEndpoint
{
    [PreserveSig] int GetDataFlow(out int flow);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
internal partial interface IPreparationAudioClient
{
    [PreserveSig] int Initialize(int shareMode, uint flags, long duration100ns, long periodicity100ns, nint format, in Guid session);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency100ns);
    [PreserveSig] int GetCurrentPadding(out uint frames);
    [PreserveSig] int IsFormatSupported(int shareMode, nint format, out nint closest);
    [PreserveSig] int GetMixFormat(out nint format);
    [PreserveSig] int GetDevicePeriod(out long default100ns, out long minimum100ns);
    [PreserveSig] int Start(); // ABI slot only. Never called by preparation.
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(nint handle);
    [PreserveSig] int GetService(in Guid iid, out nint service);
}

[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("CD63314F-3FBA-4A1B-812C-EF96358728E7")]
internal partial interface IPreparationAudioClock
{
    [PreserveSig] int GetFrequency(out ulong frequency);
    [PreserveSig] int GetPosition(out ulong position, out ulong qpc100ns);
    [PreserveSig] int GetCharacteristics(out uint characteristics);
}
