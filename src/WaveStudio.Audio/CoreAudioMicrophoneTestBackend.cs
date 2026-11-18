using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WaveStudio.Audio;

// A criação, os comandos e o descarte ocorrem na thread MTA do worker de diagnóstico.
// A NAudio gerencia os clientes e threads do stream. Estes wrappers são responsáveis pelo MMDevice selecionado explicitamente.
internal sealed class CoreAudioMicrophoneTestBackend : IMicrophoneTestBackend
{
    private static MMDevice Open(string id, DataFlow direction)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(id); // Não selecionar o dispositivo padrão nem recorrer a um substituto.
        try
        {
            if (device.State != DeviceState.Active || device.DataFlow != direction)
                throw new TestEndpointUnavailableException();
            if (device.AudioEndpointVolume.Mute)
                throw new InvalidOperationException(direction == DataFlow.Capture
                    ? "O microfone selecionado está em mute no Windows. Confira o mute antes de gravar."
                    : "A saída selecionada está em mute no Windows. Confira o mute antes de ouvir.");
            return device;
        }
        catch { device.Dispose(); throw; }
    }

    public ITestCapture OpenCapture(string deviceId)
    {
        var device = Open(deviceId, DataFlow.Capture);
        WasapiRecorder? recorder = null;
        try
        {
            using var client = device.CreateAudioClient();
            var format = BoundedTestAudio.ValidateFormat(client.MixFormat);
            recorder = new WasapiRecorderBuilder().WithDevice(device).WithSharedMode()
                .WithFormat(format).WithBufferLength(100).Build();
            return new Capture(device, recorder);
        }
        catch { try { recorder?.Dispose(); } finally { device.Dispose(); } throw; }
    }

    public ITestPlayback OpenPlayback(string deviceId, IWaveProvider source)
    {
        var device = Open(deviceId, DataFlow.Render);
        WasapiPlayer? player = null;
        try
        {
            using var client = device.CreateAudioClient();
            // Evita mapeamento ambíguo de canais na prévia de diagnóstico.
            BoundedTestAudio.ValidateFormat(client.MixFormat);
            player = new WasapiPlayerBuilder().WithDevice(device).WithSharedMode().WithLatency(200).Build();
            player.Init(source); // Caminho público AutoConvertPcm/SrcDefaultQuality no modo compartilhado da NAudio 3.1.0.
            return new Playback(device, player);
        }
        catch { try { player?.Dispose(); } finally { device.Dispose(); } throw; }
    }

    private sealed class Capture : ITestCapture
    {
        private readonly MMDevice device;
        private readonly WasapiRecorder recorder;
        private TestAudioData? receive;
        private Exception? error;
        private int completed;
        private int startAttempted;
        public WaveFormat Format => recorder.WaveFormat;
        public bool Completed => Volatile.Read(ref completed) != 0;
        public Exception? Error => Volatile.Read(ref error);

        public Capture(MMDevice device, WasapiRecorder recorder)
        {
            this.device = device;
            this.recorder = recorder;
            recorder.DataAvailable += OnData;
            recorder.RecordingStopped += OnStopped;
        }
        private void OnData(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long position, long qpc) => receive?.Invoke(data);
        private void OnStopped(object? sender, StoppedEventArgs args)
        {
            Volatile.Write(ref error, args.Exception);
            Volatile.Write(ref completed, 1);
        }
        public void Start(TestAudioData receive)
        {
            if (Interlocked.Exchange(ref startAttempted, 1) != 0) throw new InvalidOperationException("Este stream de teste já foi iniciado.");
            this.receive = receive;
            try { recorder.StartRecording(); }
            catch
            {
                // Auditoria da versão 3.1.0: a primeira chamada a StartRecording inicializa de forma síncrona
                // e inicia sua thread como última ação. Uma falha no chamador
                // não deixa callback de captura ativo. Falhas assíncronas são comunicadas por RecordingStopped.
                Volatile.Write(ref completed, 1);
                throw;
            }
        }
        public void RequestStop()
        {
            // A versão 3.1.0 define Capturing na thread de captura. Evite disputar essa atribuição
            // com StopRecording durante Starting. O worker tenta novamente no próximo ciclo.
            if (recorder.CaptureState != CaptureState.Starting) recorder.StopRecording();
        }
        public void Dispose()
        {
            // Somente após RecordingStopped ou falha anterior à criação da thread por Start.
            try { recorder.Dispose(); }
            finally
            {
                recorder.DataAvailable -= OnData;
                recorder.RecordingStopped -= OnStopped;
                receive = null;
                device.Dispose();
            }
        }
    }

    private sealed class Playback : ITestPlayback
    {
        private readonly MMDevice device;
        private readonly WasapiPlayer player;
        private Exception? error;
        private int completed;
        private int startAttempted;
        public bool Completed => Volatile.Read(ref completed) != 0;
        public Exception? Error => Volatile.Read(ref error);
        public Playback(MMDevice device, WasapiPlayer player)
        {
            this.device = device;
            this.player = player;
            player.PlaybackStopped += OnStopped;
        }
        private void OnStopped(object? sender, StoppedEventArgs args)
        {
            Volatile.Write(ref error, args.Exception);
            Volatile.Write(ref completed, 1);
        }
        public void Start()
        {
            if (Interlocked.Exchange(ref startAttempted, 1) != 0) throw new InvalidOperationException("Este stream de teste já foi iniciado.");
            try { player.Play(); }
            catch
            {
                // Na versão inspecionada, a chamada única a Play termina em Thread.Start.
                // Falhas nos callbacks são comunicadas separadamente por PlaybackStopped.
                Volatile.Write(ref completed, 1);
                throw;
            }
        }
        public void RequestStop() => player.Stop();
        public void Dispose()
        {
            try { player.Dispose(); }
            finally { player.PlaybackStopped -= OnStopped; device.Dispose(); }
        }
    }
}
