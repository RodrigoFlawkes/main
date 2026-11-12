namespace WaveStudio.Audio;

public sealed record EndpointRestoreValues(double? VolumePercent, bool? Muted, double? Balance)
{
    public bool HasValues => VolumePercent.HasValue || Muted.HasValue || Balance.HasValue;
    public void Validate()
    {
        if (VolumePercent is double volume) StereoLevels.ValidateLevel(volume / 100);
        if (Balance is double balance && (!double.IsFinite(balance) || balance < -100 || balance > 100))
            throw new ArgumentOutOfRangeException(nameof(Balance));
    }

    internal void RequireSupport(EndpointVolumeState state)
    {
        Validate();
        if (!HasValues) throw new InvalidOperationException("O endpoint ainda não possui valores salvos.");
        if (VolumePercent.HasValue && !state.VolumePercent.HasValue || Muted.HasValue && !state.Muted.HasValue
            || Balance.HasValue && state.Stereo is null)
            throw new NotSupportedException("Um controle salvo está indisponível neste dispositivo. Atualize e salve os valores suportados.");
    }
}

public sealed record EndpointRestorePlan(EndpointSelection Selection, EndpointRestoreValues Values, EndpointVolumeState Original);
public sealed record EndpointRestoreResult(EndpointVolumeState Actual, bool Complete, string? Error);

internal static class EndpointPreferenceRestore
{
    public static EndpointRestoreResult Apply(IEndpointVolumeAccess endpoint, EndpointRestorePlan plan, CancellationToken token)
    {
        var current = EndpointVolumeOperations.Read(endpoint);
        plan.Values.RequireSupport(current);
        if (!SameLevels(current, plan.Original))
            throw new InvalidOperationException("Os níveis mudaram após a prévia. Confira os valores e confirme novamente.");
        try
        {
            // Mute requested on is applied first, and unmute last. Each command confirms its result.
            if (plan.Values.Muted == true) EndpointVolumeOperations.Apply(endpoint, new SetEndpointMute(true), token);
            if (plan.Values.VolumePercent is double volume) EndpointVolumeOperations.Apply(endpoint, new SetEndpointVolume(volume), token);
            if (plan.Values.Balance is double balance) EndpointVolumeOperations.Apply(endpoint, new SetEndpointBalance(balance), token);
            if (plan.Values.Muted == false) EndpointVolumeOperations.Apply(endpoint, new SetEndpointMute(false), token);
            var actual = EndpointVolumeOperations.Read(endpoint);
            plan.Values.RequireSupport(actual);
            if (plan.Values.VolumePercent is double requested && Math.Abs(actual.VolumePercent!.Value - requested) > 2
                || plan.Values.Muted.HasValue && actual.Muted != plan.Values.Muted)
                throw new NotSupportedException("O Windows retornou níveis diferentes da preferência solicitada.");
            return new(actual, true, null);
        }
        catch (Exception error) when (EndpointVolumeOperations.IsAccessError(error))
        {
            return new(EndpointVolumeOperations.Read(endpoint), false,
                "A restauração pode ter sido parcial. Confira os níveis atuais. " + EndpointVolumeOperations.Describe(error));
        }
    }

    private static bool SameLevels(EndpointVolumeState left, EndpointVolumeState right) =>
        Equal(left.VolumePercent, right.VolumePercent) && left.Muted == right.Muted
        && Equal(left.Stereo?.Left, right.Stereo?.Left) && Equal(left.Stereo?.Right, right.Stereo?.Right);
    private static bool Equal(double? left, double? right) => left.HasValue == right.HasValue
        && (!left.HasValue || Math.Abs(left.Value - right!.Value) <= 0.00001);
}
