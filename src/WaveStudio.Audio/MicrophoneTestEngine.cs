using System.Runtime.InteropServices;

namespace WaveStudio.Audio;

// Owner-thread state machine; callbacks can only append to their bounded, generation-gated clip.
internal sealed class MicrophoneTestEngine(IMicrophoneTestBackend backend, Func<long, bool> isCurrent)
{
    private ITestCapture? capture;
    private ITestPlayback? playback;
    private BoundedTestAudio? clip;
    private string? captureId;
    private string? playbackId;
    private long generation;
    private TimeSpan startedAt;
    private TimeSpan elapsed;
    private bool discard;
    private string message = "Selecione o microfone e a saída na aba Dispositivos. Nenhuma gravação iniciada.";
    internal MicrophoneTestState State { get; private set; }
    internal bool Busy => capture is not null || playback is not null;
    internal MicrophoneTestSnapshot Snapshot => new(State, clip?.Duration ?? TimeSpan.Zero, elapsed, message,
        !Busy && captureId is not null && State != MicrophoneTestState.Disposed,
        !Busy && clip?.Length > 0 && playbackId is not null && State != MicrophoneTestState.Disposed,
        Busy || clip is not null);

    internal void Select(string? input, string? output, long revision)
    {
        generation = revision;
        captureId = input;
        playbackId = output;
        Clear("Teste descartado após seleção/atualização dos dispositivos. Grave um novo teste.");
    }

    internal void Record(long revision, TimeSpan now, Func<bool>? requested = null)
    {
        bool Current() => Valid(revision) && (requested?.Invoke() ?? true);
        if (Busy || !Current() || captureId is null) return;
        DropClip();
        try
        {
            capture = backend.OpenCapture(captureId);
            if (!Current()) { capture.Dispose(); capture = null; return; }
            var recording = new BoundedTestAudio(capture.Format);
            clip = recording;
            startedAt = now;
            elapsed = TimeSpan.Zero;
            discard = false;
            capture.Start(data => { if (isCurrent(revision) && (requested?.Invoke() ?? true)) recording.Append(data); });
            State = MicrophoneTestState.Recording;
            message = "GRAVANDO — fale por até 10 segundos. Clique em Parar quando terminar.";
        }
        catch (Exception error) { Fail(error, true); }
    }

    internal void Play(long revision, float level, TimeSpan now, Func<bool>? requested = null)
    {
        bool Current() => Valid(revision) && (requested?.Invoke() ?? true);
        if (!float.IsFinite(level) || level is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(level));
        if (Busy || !Current() || clip?.Length is not > 0 || playbackId is null) return;
        try
        {
            playback = backend.OpenPlayback(playbackId, clip.CreatePlayback(level, () => isCurrent(revision) && (requested?.Invoke() ?? true)));
            if (!Current()) { playback.Dispose(); playback = null; return; }
            startedAt = now;
            elapsed = TimeSpan.Zero;
            discard = false;
            playback.Start();
            State = MicrophoneTestState.Playing;
            message = "Reproduzindo o teste na saída selecionada. Captura desligada.";
        }
        catch (Exception error) { Fail(error, false); }
    }

    internal void Stop()
    {
        if (!Busy) return;
        clip?.Seal();
        State = MicrophoneTestState.Stopping;
        message = "Parando o teste…";
        RequestStop();
    }

    internal void Clear(string reason = "Teste apagado da memória. Nenhuma gravação iniciada.")
    {
        discard = true;
        clip?.Seal();
        if (Busy) Stop();
        else { DropClip(); State = MicrophoneTestState.Idle; }
        message = reason;
    }

    internal void Tick(TimeSpan now)
    {
        if (!Busy) return;
        elapsed = now - startedAt;
        if (!isCurrent(generation)) Clear("Operação cancelada: seleção ou disponibilidade mudou.");
        if (State == MicrophoneTestState.Recording &&
            (elapsed >= TimeSpan.FromSeconds(BoundedTestAudio.MaximumSeconds) || clip?.Full == true)) Stop();
        if (State == MicrophoneTestState.Playing && elapsed >= TimeSpan.FromSeconds(BoundedTestAudio.MaximumSeconds + 2)) Stop();
        if (State == MicrophoneTestState.Stopping) RequestStop();
        bool finished = capture?.Completed ?? playback?.Completed ?? false;
        if (!finished) return;
        var error = capture?.Error ?? playback?.Error;
        error ??= ReleaseStreams();
        clip?.Seal();
        if (discard) { DropClip(); State = MicrophoneTestState.Idle; return; }
        if (error is not null) { Fail(error, true); return; }
        State = clip?.Length > 0 ? MicrophoneTestState.Ready : MicrophoneTestState.Idle;
        message = clip?.Length > 0
            ? "Teste pronto em memória. Use fones e clique em Ouvir gravação. Não há reprodução automática."
            : "Nenhum áudio recebido. Confira permissões, conexão e mute do microfone.";
    }

    internal void MarkDisposed()
    {
        if (Busy) throw new InvalidOperationException("Encerre os streams antes do descarte.");
        DropClip();
        State = MicrophoneTestState.Disposed;
        message = "Teste encerrado e áudio descartado.";
    }

    private bool Valid(long revision) => generation == revision && isCurrent(revision) && State != MicrophoneTestState.Disposed;
    private void RequestStop() { capture?.RequestStop(); playback?.RequestStop(); }
    private void DropClip() { clip?.Dispose(); clip = null; elapsed = TimeSpan.Zero; }
    private Exception? ReleaseStreams()
    {
        Exception? failure = null;
        try { capture?.Dispose(); } catch (Exception error) { failure = error; }
        finally { capture = null; }
        try { playback?.Dispose(); } catch (Exception error) { failure ??= error; }
        finally { playback = null; }
        return failure;
    }
    private void Fail(Exception error, bool clearAudio)
    {
        ReleaseStreams();
        clearAudio |= error is TestEndpointUnavailableException || error is COMException
            { HResult: unchecked((int)0x88890004) or unchecked((int)0x80070490) };
        if (clearAudio) DropClip();
        State = MicrophoneTestState.Error;
        message = error switch
        {
            NotSupportedException or InvalidOperationException => error.Message,
            UnauthorizedAccessException => "O Windows negou acesso. Confira Privacidade > Microfone e permita aplicativos da área de trabalho.",
            COMException => $"O teste de áudio falhou (0x{error.HResult:X8}). Confira conexão, permissões e formato; atualize os dispositivos.",
            _ => "Não foi possível completar o teste. Confira os dispositivos e tente novamente."
        };
    }
}
