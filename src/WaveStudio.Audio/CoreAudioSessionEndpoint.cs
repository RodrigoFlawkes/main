using System.Diagnostics;
using System.ComponentModel;
using NAudio.CoreAudioApi;

namespace WaveStudio.Audio;

public sealed class CoreAudioSessionEndpoint : IApplicationSessionEndpoint
{
    private readonly MMDevice device;
    private readonly SessionCollection sessions;
    private readonly string endpointId;

    public CoreAudioSessionEndpoint(string endpointId)
    {
        this.endpointId = endpointId;
        using var enumerator = new MMDeviceEnumerator();
        device = enumerator.GetDevice(endpointId);
        try
        {
            if (device.State != DeviceState.Active || device.DataFlow != DataFlow.Render)
                throw new InvalidOperationException("Selecione um dispositivo de reprodução disponível para consultar o mixer.");
            sessions = device.AudioSessionManager.Sessions;
        }
        catch { device.Dispose(); throw; }
    }

    public int Count => sessions.Count;
    public IApplicationSessionAccess Open(int index) => new NativeSession(endpointId, sessions[index]);
    // The device owns the manager, which owns the collection and its internal notification registration.
    public void Dispose() => device.Dispose();

    private sealed class NativeSession(string endpointId, AudioSessionControl control) : IApplicationSessionAccess
    {
        public SessionKey Key => new(endpointId, control.GetSessionInstanceIdentifier);
        public SessionActivity Activity => (SessionActivity)(int)control.State;
        private SimpleAudioVolume Volume => control.SimpleAudioVolume ?? throw new NotSupportedException("Volume da sessão indisponível.");
        public double VolumeScalar { get => Volume.Volume; set => Volume.Volume = (float)value; }
        public bool Muted { get => Volume.Mute; set => Volume.Mute = value; }

        public ApplicationSessionSnapshot Read()
        {
            var key = Key;
            var activity = Activity;
            uint? pid = null;
            bool system = false;
            string name = "Aplicativo não identificado";
            try { pid = control.GetProcessID; }
            catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error)) { }
            try { system = control.IsSystemSoundsSession; }
            catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error)) { }
            try
            {
                if (!string.IsNullOrWhiteSpace(control.DisplayName))
                    name = control.DisplayName;
                else if (pid is > 0 && activity != SessionActivity.Expired)
                {
                    using var process = Process.GetProcessById(checked((int)pid.Value));
                    name = process.ProcessName;
                }
            }
            catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error)
                || error is ArgumentException or Win32Exception or OverflowException) { }
            if (system)
                name = "Sons do sistema";
            double? percent = null;
            bool? muted = null;
            var errors = new List<string>();
            try { double scalar = VolumeScalar; StereoLevels.ValidateLevel(scalar); percent = scalar * 100; }
            catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error)) { errors.Add(EndpointVolumeOperations.Describe(error)); }
            try { muted = Muted; }
            catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error)) { errors.Add(EndpointVolumeOperations.Describe(error)); }
            return new ApplicationSessionSnapshot(key, name, pid, system, activity, percent, muted,
                errors.Count == 0 ? null : string.Join(" ", errors));
        }

        public void Dispose()
        {
            control.SimpleAudioVolume?.Dispose();
            control.Dispose();
        }
    }
}
