namespace WaveStudio.Audio.Engine;

[Flags]
internal enum TransportPacketFlags { None = 0, Discontinuity = 1, Silent = 2, TimestampError = 4 }
internal enum NativeClockQuality { Unavailable, Accurate, Uncertain }
internal enum NativePositionUnit { Frames, DeviceUnits }
internal readonly record struct NativeClockSample(TransportEndpoint Endpoint, ulong Position, ulong Frequency,
    ulong Qpc100ns, NativePositionUnit Unit, NativeClockQuality Quality, TransportPacketFlags Flags = TransportPacketFlags.None);
internal enum NativeClockResult { Valid, InvalidIdentity, Unavailable, TimestampError, Discontinuity, InvalidTime, InvalidPosition, Stalled, Overflow, RequiresReset }

// No native clock call is made here. Caller must supply audited accuracy/unit evidence.
internal sealed class WasapiClockAdapter
{
    private readonly TransportEndpoint endpoint;
    private readonly int rate;
    private readonly AudioClock clock;
    private bool anchored;
    private ulong nativePosition, nativeFrequency, qpc, lastProgressQpc, remainder;
    private NativePositionUnit unit;
    private long frames;
    public bool RequiresReset { get; private set; }
    public AudioClock Clock => clock;
    public WasapiClockAdapter(TransportEndpoint endpoint, int rate)
    {
        endpoint.Validate();
        if (rate is not (44100 or 48000)) throw new ArgumentOutOfRangeException(nameof(rate));
        this.endpoint = endpoint; this.rate = rate;
        // Bounded identity for the estimator; native identity is checked in full above conversion.
        string identity = ConvertIdentity(endpoint);
        clock = new(identity, endpoint.Generation, rate, "WASAPI-QPC-100ns", 10000000);
    }
    public NativeClockResult Convert(NativeClockSample sample, long now100ns, out ClockObservation observation)
    {
        observation = default;
        if (sample.Endpoint != endpoint) return NativeClockResult.InvalidIdentity;
        if (RequiresReset) return NativeClockResult.RequiresReset;
        NativeClockResult Reject(NativeClockResult result) { RequiresReset = true; return result; }
        if ((sample.Flags & TransportPacketFlags.Discontinuity) != 0) return Reject(NativeClockResult.Discontinuity);
        if ((sample.Flags & TransportPacketFlags.TimestampError) != 0) return Reject(NativeClockResult.TimestampError);
        if ((sample.Flags & ~(TransportPacketFlags.Discontinuity | TransportPacketFlags.TimestampError | TransportPacketFlags.Silent)) != 0)
            return Reject(NativeClockResult.Unavailable);
        if (sample.Quality != NativeClockQuality.Accurate || sample.Frequency == 0 || sample.Unit is not (NativePositionUnit.Frames or NativePositionUnit.DeviceUnits))
            return Reject(NativeClockResult.Unavailable);
        if (sample.Qpc100ns > long.MaxValue || now100ns < 0 || sample.Qpc100ns > (ulong)now100ns ||
            (ulong)now100ns - sample.Qpc100ns > 5000000 || (anchored && sample.Qpc100ns <= qpc))
            return Reject(NativeClockResult.InvalidTime);
        ulong frequency = sample.Unit == NativePositionUnit.Frames ? (ulong)rate : sample.Frequency;
        if (sample.Unit == NativePositionUnit.Frames && sample.Frequency != (ulong)rate) return Reject(NativeClockResult.Unavailable);
        if (anchored)
        {
            if (sample.Unit != unit || frequency != nativeFrequency || sample.Position < nativePosition) return Reject(NativeClockResult.InvalidPosition);
            if (sample.Position == nativePosition && sample.Qpc100ns - lastProgressQpc > 5000000) return Reject(NativeClockResult.Stalled);
            UInt128 delta = (UInt128)(sample.Position - nativePosition) * (uint)rate + remainder;
            UInt128 advance = delta / frequency;
            if (advance > long.MaxValue || advance > (UInt128)(ulong)(long.MaxValue - frames)) return Reject(NativeClockResult.Overflow);
            double seconds = (sample.Qpc100ns - qpc) / 10000000d;
            if (seconds > 2 || Math.Abs((double)advance - rate * seconds) > rate * seconds * .005 + 2)
                return Reject(NativeClockResult.InvalidPosition);
            frames += (long)advance; remainder = (ulong)(delta % frequency);
            if (sample.Position != nativePosition) lastProgressQpc = sample.Qpc100ns;
        }
        else { anchored = true; lastProgressQpc = sample.Qpc100ns; }
        nativePosition = sample.Position; nativeFrequency = frequency; qpc = sample.Qpc100ns; unit = sample.Unit;
        observation = new(clock, frames, (long)qpc, ClockQuality.Monotonic);
        return NativeClockResult.Valid;
    }
    private static string ConvertIdentity(TransportEndpoint endpoint) =>
        System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{endpoint.Direction}:{endpoint.Id}")));
}
