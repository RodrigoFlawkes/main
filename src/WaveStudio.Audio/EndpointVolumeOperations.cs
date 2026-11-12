namespace WaveStudio.Audio;

public static class EndpointVolumeOperations
{
    public static EndpointVolumeState Read(IEndpointVolumeAccess endpoint)
    {
        double? percent = null;
        bool? muted = null;
        StereoLevels? stereo = null;
        string? volumeError = null;
        string? muteError = null;
        string? balanceError = null;
        try
        {
            double scalar = endpoint.VolumeScalar;
            StereoLevels.ValidateLevel(scalar);
            percent = scalar * 100;
        }
        catch (Exception error) when (IsAccessError(error)) { volumeError = Describe(error); }
        try { muted = endpoint.Muted; }
        catch (Exception error) when (IsAccessError(error)) { muteError = Describe(error); }
        try
        {
            stereo = endpoint.ReadStereo();
            StereoLevels.ValidateLevel(stereo.Left);
            StereoLevels.ValidateLevel(stereo.Right);
        }
        catch (Exception error) when (IsAccessError(error))
        {
            stereo = null;
            balanceError = Describe(error);
        }
        return new EndpointVolumeState(percent, muted, stereo, volumeError, muteError, balanceError);
    }

    public static void Apply(IEndpointVolumeAccess endpoint, EndpointCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (command)
        {
            case SetEndpointVolume volume:
                StereoLevels.ValidateLevel(volume.Percent / 100);
                endpoint.VolumeScalar = volume.Percent / 100;
                if (Math.Abs(endpoint.VolumeScalar - volume.Percent / 100) > 0.02)
                    throw new NotSupportedException("O driver não confirmou o volume solicitado. O controle foi desativado.");
                break;
            case SetEndpointMute mute:
                endpoint.Muted = mute.Muted;
                if (endpoint.Muted != mute.Muted)
                    throw new NotSupportedException("O driver não confirmou o estado de mute solicitado. O controle foi desativado.");
                break;
            case SetEndpointBalance balance:
                ApplyBalance(endpoint, balance.Position, cancellationToken);
                break;
            default:
                throw new ArgumentException("Comando de áudio desconhecido.", nameof(command));
        }
    }

    private static void ApplyBalance(IEndpointVolumeAccess endpoint, double position, CancellationToken cancellationToken)
    {
        var original = endpoint.ReadStereo();
        var requested = original.WithBalance(position);
        bool writeStarted = false;
        var expected = original;
        try
        {
            WriteChannels(endpoint, original, requested, cancellationToken, () => writeStarted = true,
                (channel, level) => expected = channel == 0 ? expected with { Left = level } : expected with { Right = level });
            var actual = endpoint.ReadStereo();
            if (actual.BaseLevel > original.BaseLevel + 0.00001
                || Math.Abs(actual.Left - requested.Left) > 0.02 || Math.Abs(actual.Right - requested.Right) > 0.02)
                throw new NotSupportedException("O driver não confirmou canais independentes. O balanço foi desativado.");
        }
        catch (Exception error) when (writeStarted)
        {
            // A two-channel update has two native calls. Restore a partial update on failure.
            try
            {
                var current = endpoint.ReadStereo();
                if (!MatchesKnownLevel(current.Left, original.Left, expected.Left)
                    || !MatchesKnownLevel(current.Right, original.Right, expected.Right))
                    throw new InvalidOperationException("Foi detectada alteração externa dos canais. Confira os níveis no Windows.");
                WriteChannels(endpoint, current, original, CancellationToken.None);
                var restored = endpoint.ReadStereo();
                if (Math.Abs(restored.Left - original.Left) > 0.02 || Math.Abs(restored.Right - original.Right) > 0.02)
                    throw new InvalidOperationException("O driver não confirmou a restauração dos canais.");
            }
            catch (Exception restoreError) when (IsAccessError(restoreError))
            {
                throw new InvalidOperationException("Falha no balanço e na restauração dos canais. Confira os níveis no Windows e atualize a lista.",
                    new AggregateException(error, restoreError));
            }
            throw;
        }
    }

    private static void WriteChannels(IEndpointVolumeAccess endpoint, StereoLevels current, StereoLevels target,
        CancellationToken cancellationToken, Action? startingWrite = null, Action<int, double>? writingChannel = null)
    {
        // Attenuate first when moving across the center, avoiding a temporary increase of both sides.
        int first = target.Left < current.Left ? 0 : 1;
        cancellationToken.ThrowIfCancellationRequested();
        startingWrite?.Invoke();
        writingChannel?.Invoke(first, first == 0 ? target.Left : target.Right);
        endpoint.SetChannelLevel(first, first == 0 ? target.Left : target.Right);
        cancellationToken.ThrowIfCancellationRequested();
        int second = 1 - first;
        writingChannel?.Invoke(second, second == 0 ? target.Left : target.Right);
        endpoint.SetChannelLevel(second, second == 0 ? target.Left : target.Right);
    }

    private static bool MatchesKnownLevel(double actual, double original, double expected) =>
        Math.Abs(actual - original) <= 0.00001 || Math.Abs(actual - expected) <= 0.00001;

    public static bool IsAccessError(Exception error) => AudioError.IsDeviceAccessError(error)
        || error is NotSupportedException or ArgumentOutOfRangeException;

    public static string Describe(Exception error) => error is NotSupportedException or InvalidOperationException
        ? error.Message : AudioError.Describe(error);
}
