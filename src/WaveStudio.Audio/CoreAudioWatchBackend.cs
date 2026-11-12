using NAudio.CoreAudioApi;

namespace WaveStudio.Audio;

/// <summary>Created, read and disposed only on its owning MTA worker.</summary>
public sealed class CoreAudioWatchBackend : IAudioWatchBackend
{
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly MMDeviceNotificationClient? notifications;
    private long recoveryRevision;
    public string? NotificationError { get; }
    public long RecoveryRevision => Interlocked.Read(ref recoveryRevision);

    public CoreAudioWatchBackend(Action signal)
    {
        try
        {
            notifications = enumerator.CreateNotificationClient(useSynchronizationContext: false);
            notifications.DeviceAdded += (_, _) => SignalRecovery();
            notifications.DeviceRemoved += (_, _) => SignalRecovery();
            notifications.DeviceStateChanged += (_, _) => SignalRecovery();
            notifications.DefaultDeviceChanged += (_, _) => signal();
            notifications.PropertyValueChanged += (_, args) =>
            {
                if (args.PropertyKey.Equals(PropertyKeys.PKEY_Device_FriendlyName)
                    || args.PropertyKey.Equals(PropertyKeys.PKEY_DeviceInterface_FriendlyName)
                    || args.PropertyKey.Equals(PropertyKeys.PKEY_AudioEngine_DeviceFormat))
                    signal();
            };
            void SignalRecovery()
            {
                Interlocked.Increment(ref recoveryRevision);
                signal();
            }
        }
        catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error))
        {
            NotificationError = EndpointVolumeOperations.Describe(error);
        }
    }

    public IAudioPeakSource OpenPeak(string endpointId)
    {
        var device = enumerator.GetDevice(endpointId);
        try
        {
            if (device.State != DeviceState.Active) throw new InvalidOperationException("Dispositivo indisponível.");
            return new PeakSource(device, device.AudioMeterInformation);
        }
        catch { device.Dispose(); throw; }
    }

    private sealed class PeakSource(MMDevice device, AudioMeterInformation meter) : IAudioPeakSource
    {
        public double ReadPeak() => meter.MasterPeakValue;
        public void Dispose() => device.Dispose();
    }

    public void Dispose()
    {
        try { notifications?.Dispose(); }
        finally { enumerator.Dispose(); }
    }
}
