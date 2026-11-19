using System.Buffers.Binary;

namespace WaveStudio.Audio.Engine;

internal sealed record CableQuietReport(long Frames, long Packets, long Observation100ns, bool PacketRateConfirmed,
    bool ResourcesReleased, string? Failure, int? HResult)
{
    public bool Passed => ResourcesReleased && Failure is null && PacketRateConfirmed;
}

// Apenas captura do cabo. Examina buffers emprestados no MTA, sem FIFO, gravação ou render.
internal static class WasapiCableQuietProbe
{
    private const long Observation = 30_000_000;
    private const uint Silent = (uint)TransportPacketFlags.Silent;
    private const uint Discontinuity = (uint)TransportPacketFlags.Discontinuity;
    private const int BufferEmpty = 0x08890001;

    public static async Task<CableQuietReport> ObserveAsync(WasapiEndpointInfo endpoint, TransportActivityGuard guard,
        Func<bool> noProducer, CancellationToken token, Func<TransportEndpoint, IWasapiPreparationDevice>? open = null,
        Func<long>? clock = null, Action<CancellationToken>? wait = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        var probeToken = deadline.Token;
        var work = Task.Run(() => Observe(endpoint, guard, noProducer, probeToken,
            open ?? NativeWasapiPreparationDevice.Open, clock ?? WasapiTime.Now100ns, wait ?? WaitForPacket));
        try { return await work.WaitAsync(TimeSpan.FromSeconds(7)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            // A chamada pendente conserva os recursos no seu proprietário. O lote não libera o guard externo.
            deadline.Cancel();
            WasapiResourceScope.Retain(work);
            return new(0, 0, 0, false, false, "Quiet/CleanupDeadline", null);
        }
    }

    private static void WaitForPacket(CancellationToken token) => token.WaitHandle.WaitOne(5);

    private static unsafe CableQuietReport Observe(WasapiEndpointInfo endpoint, TransportActivityGuard guard,
        Func<bool> noProducer, CancellationToken token, Func<TransportEndpoint, IWasapiPreparationDevice> open,
        Func<long> now, Action<CancellationToken> wait)
    {
        var lease = guard.TryAcquire(AudioActivity.IsolatedTransport);
        if (lease is null) return new(0, 0, 0, false, false, "Quiet/ActivityBusy", null);
        var resources = new WasapiResourceScope(lease);
        IWasapiStreamClient? client = null;
        bool attemptedStart = false, bufferUncertain = false, stopConfirmed = false, rateConfirmed = false;
        long frames = 0, packetsSeen = 0, start = 0, end = 0, nextProducerCheck = 0;
        string? failure = null;
        int? hresult = null;
        ulong lastPacketQpc = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!noProducer()) throw new InvalidOperationException("Quiet/ActiveProducer");
            var format = endpoint.Mix!;
            var encoding = format.Validate();
            var device = resources.Add(open(endpoint.Endpoint));
            device.ValidateIdentity(endpoint.Endpoint);
            client = (IWasapiStreamClient)resources.Add(device.Activate());
            if (client.ReadMixFormat() != format) throw new InvalidOperationException("Quiet/Format");
            client.RequireExactSupport();
            var (period, minimum) = client.ReadPeriods();
            if (period is <= 0 or > 200_000 || minimum <= 0) throw new InvalidOperationException("Quiet/Period");
            token.ThrowIfCancellationRequested();
            client.Initialize();
            var (capacity, latency) = client.ReadBufferInfo();
            if (capacity is 0 or > 2048 || latency < 0) throw new InvalidOperationException("Quiet/Capacity");
            var timing = new WasapiCaptureTiming(format.SampleRate, period, capacity);
            var packets = resources.Add(client.OpenCapture());
            device.ValidateIdentity(endpoint.Endpoint);
            client.RevalidateFormat(format);
            token.ThrowIfCancellationRequested();
            if (!noProducer()) throw new InvalidOperationException("Quiet/ActiveProducer");
            start = now();
            attemptedStart = true;
            WasapiCallException.RequireSuccess(client.Start(), "Quiet/Start");
            while ((end = now()) - start < Observation)
            {
                token.ThrowIfCancellationRequested();
                if (end >= nextProducerCheck)
                {
                    if (!noProducer()) throw new InvalidOperationException("Quiet/ActiveProducer");
                    nextProducerCheck = end + 200_000;
                }
                for (int i = 0; i < 32 && now() - start < Observation; i++)
                {
                    token.ThrowIfCancellationRequested();
                    uint expected = packets.NextPacketSize();
                    if (expected == 0) break;
                    var packet = packets.GetBuffer();
                    if (packet.HResult == BufferEmpty) break;
                    WasapiCallException.RequireSuccess(packet.HResult, "Quiet/GetBuffer");
                    packetsSeen++;
                    bufferUncertain = true;
                    bool discard = (packet.Flags & Discontinuity) != 0;
                    uint release = 0;
                    try
                    {
                        long reading = now();
                        if (packet.Frames != expected || packet.Frames > capacity ||
                            (packet.Flags & ~(Silent | Discontinuity)) != 0 ||
                            (discard && (packetsSeen != 1 || reading - start > 5_000_000)) ||
                            packet.Qpc100ns == 0 || packet.Qpc100ns < (ulong)start || packet.Qpc100ns > (ulong)reading ||
                            (ulong)reading - packet.Qpc100ns > 1_000_000)
                            throw new InvalidOperationException("Quiet/Packet");
                        if (!discard) timing.ValidatePacket(packet.Position, packet.Frames, packet.Qpc100ns);
                        bool silentFlag = (packet.Flags & Silent) != 0;
                        if (!silentFlag && packet.Data == 0) throw new InvalidOperationException("Quiet/Pointer");
                        release = packet.Frames;
                        if (!silentFlag && !IsDigitalSilence(new ReadOnlySpan<byte>((void*)packet.Data,
                            checked((int)packet.Frames * format.BlockAlign)), encoding))
                            throw new InvalidOperationException("Quiet/ResidualSignal");
                    }
                    finally
                    {
                        WasapiCallException.RequireSuccess(packets.ReleaseBuffer(release), "Quiet/ReleaseBuffer");
                        bufferUncertain = false;
                    }
                    if (discard) continue;
                    timing.AcceptReleasedPacket(packet.Position, release, packet.Qpc100ns);
                    frames += release;
                    lastPacketQpc = packet.Qpc100ns;
                }
                wait(token);
            }
            token.ThrowIfCancellationRequested();
            if (!noProducer()) throw new InvalidOperationException("Quiet/ActiveProducer");
            rateConfirmed = frames >= format.SampleRate * 2L && timing.HasNominalPackets(end) &&
                lastPacketQpc <= (ulong)end && (ulong)end - lastPacketQpc <= 1_000_000;
            if (!rateConfirmed) failure = "Quiet/InsufficientEvidence";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { failure = "Quiet/Canceled"; }
        catch (WasapiCallException error) { failure = error.Stage; hresult = error.Result; }
        catch (Exception error)
        {
            failure = error is InvalidOperationException && error.Message.StartsWith("Quiet/", StringComparison.Ordinal)
                ? error.Message : "Quiet/Backend";
        }
        finally
        {
            if (attemptedStart)
            {
                end = now();
                try
                {
                    int hr = client!.Stop();
                    stopConfirmed = hr is 0 or 1;
                    if (!stopConfirmed) { failure = "Quiet/Stop"; hresult = hr; }
                }
                catch { failure = "Quiet/Stop"; }
            }
            if (bufferUncertain || (attemptedStart && !stopConfirmed)) WasapiResourceScope.Retain(resources);
            else resources.Dispose();
        }

        if (resources.CleanupError is not null) failure = "Quiet/Cleanup";
        if (token.IsCancellationRequested) failure ??= "Quiet/Canceled";
        return new(frames, packetsSeen, attemptedStart ? Math.Max(0, end - start) : 0,
            rateConfirmed, resources.Released, failure, hresult);
    }

    internal static bool IsDigitalSilence(ReadOnlySpan<byte> bytes, PcmEncoding encoding)
    {
        if (encoding == PcmEncoding.Pcm16LittleEndian) return bytes.IndexOfAnyExcept((byte)0) == -1;
        for (int offset = 0; offset < bytes.Length; offset += sizeof(float))
            if ((BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]) & 0x7fff_ffff) != 0) return false;
        return true;
    }
}
