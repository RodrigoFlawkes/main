using System.Runtime.InteropServices;
using WaveStudio.Audio.Dsp;
using WaveStudio.Audio.Engine;

namespace WaveStudio.Audio;

// Máquina de estados da thread proprietária. Os callbacks só podem acrescentar dados ao clipe limitado e protegido pela geração.
internal sealed class MicrophoneTestEngine(IMicrophoneTestBackend backend, Func<long, bool> isCurrent, TransportActivityGuard? activityGuard = null)
{
    private readonly TransportActivityGuard activities = activityGuard ?? new();
    private IDisposable? activity;
    private ITestCapture? capture;
    private ITestPlayback? playback;
    private BoundedTestAudio? clip;
    private BoundedTestAudio.ClipProvider? playbackSource;
    private long limitedSamples, sanitizedSamples;
    private string? playbackMode;
    private string? captureId;
    private string? playbackId;
    private long generation;
    private TimeSpan startedAt;
    private TimeSpan elapsed;
    private bool discard;
    private bool captureStarted, playbackStarted, stopRequested;
    private long deliveryEpoch;
    private Exception? primaryError, cleanupError;
    private string message = StudioStrings.Get("MicrophoneTestEngine.SelecioneOMicrofoneEA");
    internal MicrophoneTestState State { get; private set; }
    internal bool Busy => capture is not null || playback is not null;
    internal MicrophoneTestSnapshot Snapshot => new(State, clip?.Duration ?? TimeSpan.Zero, elapsed, message,
        !Busy && cleanupError is null && captureId is not null && State != MicrophoneTestState.Disposed,
        !Busy && cleanupError is null && clip?.Length > 0 && playbackId is not null && State != MicrophoneTestState.Disposed,
        Busy || clip is not null, clip?.Format.SampleRate,
        clip is not null && GraphicEqualizerSettings.SupportsRate(clip.Format.SampleRate), playbackMode,
        playbackSource?.LimitedSamples ?? limitedSamples, playbackSource?.SanitizedSamples ?? sanitizedSamples,
        Busy && stopRequested, false, primaryError is null ? null : Describe(primaryError),
        cleanupError is null ? null : Describe(cleanupError));

    internal void Select(string? input, string? output, long revision)
    {
        generation = revision;
        captureId = input;
        playbackId = output;
        Clear(StudioStrings.Get("MicrophoneTestEngine.TesteDescartadoAposSelecaoAtualizacao"));
    }

    internal void Record(long revision, TimeSpan now, Func<bool>? requested = null)
    {
        long epoch = Volatile.Read(ref deliveryEpoch);
        bool Current() => Valid(revision) && epoch == Volatile.Read(ref deliveryEpoch) && (requested?.Invoke() ?? true);
        if (Busy || !Current() || captureId is null) return;
        if (!AcquireActivity()) return;
        ResetFailure();
        epoch = Interlocked.Increment(ref deliveryEpoch);
        DropClip();
        try
        {
            capture = backend.OpenCapture(captureId);
            if (!Current()) { ReleaseCapture(); Finish(); return; }
            var recording = new BoundedTestAudio(capture.Format);
            clip = recording;
            startedAt = now;
            elapsed = TimeSpan.Zero;
            discard = false;
            captureStarted = true; // Uma falha em Start pode ocorrer depois da criação da thread de callback.
            capture.Start(data => { if (Current()) recording.Append(data); });
            State = MicrophoneTestState.Recording;
            message = StudioStrings.Get("MicrophoneTestEngine.GRAVANDOFalePorAte10");
        }
        catch (Exception error) { Fail(error, true); }
    }

    internal void Play(long revision, float level, TimeSpan now, Func<bool>? requested = null, GraphicEqualizerSettings? equalizer = null)
    {
        long epoch = Volatile.Read(ref deliveryEpoch);
        bool Current() => Valid(revision) && epoch == Volatile.Read(ref deliveryEpoch) && (requested?.Invoke() ?? true);
        if (!float.IsFinite(level) || level is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(level));
        if (Busy || !Current() || clip?.Length is not > 0 || playbackId is null) return;
        if (!AcquireActivity()) return;
        ResetFailure();
        epoch = Interlocked.Increment(ref deliveryEpoch);
        try
        {
            playbackSource = clip.CreatePlayback(level, Current, equalizer);
            limitedSamples = sanitizedSamples = 0;
            playbackMode = equalizer?.Enabled == true ? StudioStrings.Get("MicrophoneTestEngine.ProcessadoComEQ") : StudioStrings.Get("MicrophoneTestEngine.OriginalSemEQ");
            playback = backend.OpenPlayback(playbackId, playbackSource);
            if (!Current()) { ReleasePlayback(); ResetPlayback(); Finish(); return; }
            startedAt = now;
            elapsed = TimeSpan.Zero;
            discard = false;
            playbackStarted = true;
            playback.Start();
            State = MicrophoneTestState.Playing;
            message = StudioStrings.Format("MicrophoneTestEngine.0ReproduzindoNaSaidaSelecionada", playbackMode);
        }
        catch (Exception error) { Fail(error, false); }
    }

    internal void Stop()
    {
        if (!Busy) return;
        Interlocked.Increment(ref deliveryEpoch);
        stopRequested = true;
        clip?.Seal();
        State = MicrophoneTestState.Stopping;
        message = StudioStrings.Get("MicrophoneTestEngine.ParandoOTeste");
        RequestStop();
    }

    internal void Clear(string? reason = null)
    {
        reason ??= StudioStrings.Get("MicrophoneTestEngine.TesteApagadoDaMemoriaNenhuma");
        discard = true;
        clip?.Seal();
        if (Busy)
        {
            Stop();
            message = StudioStrings.Get("MicrophoneTestEngine.DescartePendenteParadaAindaNao") + FailureMessage();
        }
        else { DropClip(); State = MicrophoneTestState.Idle; message = reason; }
    }

    internal void Tick(TimeSpan now)
    {
        if (!Busy) return;
        elapsed = now - startedAt;
        if (!isCurrent(generation)) Clear(StudioStrings.Get("MicrophoneTestEngine.OperacaoCanceladaSelecaoOuDisponibilidade"));
        if (State == MicrophoneTestState.Recording &&
            (elapsed >= TimeSpan.FromSeconds(BoundedTestAudio.MaximumSeconds) || clip?.Full == true)) Stop();
        if (State == MicrophoneTestState.Playing && elapsed >= TimeSpan.FromSeconds(BoundedTestAudio.MaximumSeconds + 2)) Stop();
        if (stopRequested) RequestStop();
        // Um erro no stream não interrompe a limpeza dos recursos.
        // Cada recurso é liberado uma única vez, somente após a conclusão ou antes de Start.
        if (capture is not null && (!captureStarted || capture.Completed))
        {
            try { if (capture.Error is { } error) { primaryError ??= error; discard = true; } }
            catch (Exception error) { primaryError ??= error; discard = true; }
            ReleaseCapture();
        }
        if (playback is not null && (!playbackStarted || playback.Completed))
        {
            try { if (playback.Error is { } error) { primaryError ??= error; discard = true; } }
            catch (Exception error) { primaryError ??= error; discard = true; }
            ReleasePlayback();
        }
        if (Busy) return;
        Finish();
    }

    private void Finish()
    {
        if (Busy) throw new InvalidOperationException(StudioStrings.Get("MicrophoneTestEngine.TerminoExigeQuiescenciaDoDiagnostico"));
        // A conclusão confirma que os callbacks estão inativos. Uma falha em Dispose deixa incerta a propriedade do recurso nativo.
        if (cleanupError is null)
        {
            activity?.Dispose();
            activity = null;
        }
        Interlocked.Increment(ref deliveryEpoch);
        stopRequested = false;
        clip?.Seal();
        if (discard) DropClip();
        if (primaryError is not null || cleanupError is not null)
        {
            State = MicrophoneTestState.Error;
            message = FailureMessage();
            return;
        }
        State = clip?.Length > 0 ? MicrophoneTestState.Ready : MicrophoneTestState.Idle;
        message = clip?.Length > 0
            ? StudioStrings.Get("MicrophoneTestEngine.TesteProntoEmMemoriaUse")
            : StudioStrings.Get("MicrophoneTestEngine.NenhumAudioRecebidoConfiraPermissoes");
    }
    private bool AcquireActivity()
    {
        if (cleanupError is not null)
        {
            message = FailureMessage();
            return false;
        }
        activity = activities.TryAcquire(AudioActivity.ShortDiagnostic);
        if (activity is not null) return true;
        message = StudioStrings.Get("MicrophoneTestEngine.OutraOperacaoDeAudioEsta");
        return false;
    }

    internal void MarkDisposed()
    {
        if (Busy) throw new InvalidOperationException(StudioStrings.Get("MicrophoneTestEngine.EncerreOsStreamsAntesDo"));
        DropClip();
        State = MicrophoneTestState.Disposed;
        message = StudioStrings.Get("MicrophoneTestEngine.TesteEncerradoEAudioDescartado") + (primaryError is not null || cleanupError is not null ? " " + FailureMessage() : "");
    }

    private bool Valid(long revision) => generation == revision && isCurrent(revision) && State != MicrophoneTestState.Disposed;
    private void RequestStop()
    {
        try { if (captureStarted) capture?.RequestStop(); } catch (Exception error) { ReportControlFailure(error); }
        try { if (playbackStarted) playback?.RequestStop(); } catch (Exception error) { ReportControlFailure(error); }
    }
    private void DropClip() { clip?.Dispose(); clip = null; playbackSource = null; playbackMode = null; limitedSamples = sanitizedSamples = 0; elapsed = TimeSpan.Zero; }
    private void ReleaseCapture()
    {
        var released = capture; capture = null; captureStarted = false;
        DisposeDiagnosticResource(released);
    }
    private void ReleasePlayback()
    {
        var released = playback; playback = null; playbackStarted = false;
        DisposeDiagnosticResource(released);
        limitedSamples = playbackSource?.LimitedSamples ?? limitedSamples;
        sanitizedSamples = playbackSource?.SanitizedSamples ?? sanitizedSamples;
        playbackSource = null;
    }
    private void DisposeDiagnosticResource(IDisposable? resource)
    {
        if (resource is null) return;
        try { resource.Dispose(); }
        catch (Exception error)
        {
            cleanupError ??= error;
            WasapiResourceScope.Retain(resource);
        }
    }
    private void ResetPlayback() { playbackSource = null; playbackMode = null; limitedSamples = sanitizedSamples = 0; }
    private void ResetFailure() { primaryError = cleanupError = null; stopRequested = discard = false; }
    internal void ReportControlFailure(Exception error)
    {
        primaryError ??= error; discard = true; stopRequested = true;
        Interlocked.Increment(ref deliveryEpoch); clip?.Seal();
        State = Busy ? MicrophoneTestState.Stopping : MicrophoneTestState.Error;
        message = Busy ? StudioStrings.Get("MicrophoneTestEngine.ParadaAindaNaoConfirmadaRecursos") + FailureMessage() : FailureMessage();
    }
    private void Fail(Exception error, bool clearAudio)
    {
        primaryError ??= error;
        Interlocked.Increment(ref deliveryEpoch); clip?.Seal();
        clearAudio |= error is TestEndpointUnavailableException || error is COMException
            { HResult: unchecked((int)0x88890004) or unchecked((int)0x80070490) };
        discard |= clearAudio;
        if (!captureStarted) ReleaseCapture();
        if (!playbackStarted) { ReleasePlayback(); ResetPlayback(); }
        if (Busy)
        {
            stopRequested = true; State = MicrophoneTestState.Stopping;
            RequestStop(); message = StudioStrings.Get("MicrophoneTestEngine.ParadaAindaNaoConfirmadaRecursos") + FailureMessage();
            try { Tick(startedAt); } catch (Exception secondary) { ReportControlFailure(secondary); }
        }
        else Finish();
    }
    private string FailureMessage() => (primaryError is null ? "" : Describe(primaryError)) +
        (cleanupError is null ? "" : StudioStrings.Get("MicrophoneTestEngine.LiberacaoDoDispositivoNaoConfirmada") + Describe(cleanupError));
    private static string Describe(Exception error) => error switch
        {
            NotSupportedException or InvalidOperationException => error.Message,
            UnauthorizedAccessException => StudioStrings.Get("MicrophoneTestEngine.OWindowsNegouAcessoConfira"),
            COMException => StudioStrings.Format("MicrophoneTestEngine.OTesteDeAudioFalhou", error.HResult),
            _ => StudioStrings.Get("MicrophoneTestEngine.NaoFoiPossivelCompletarO")
        };
}
