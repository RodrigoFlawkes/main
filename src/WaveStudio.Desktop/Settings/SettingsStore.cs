using System.Security.Cryptography;
using System.Text.Json;

namespace WaveStudio.Desktop.Settings;

internal enum SettingsFileState { Uninitialized, Missing, Ready, Corrupt, Unsupported, Oversized, Unreadable, Conflict }
internal sealed class SettingsFileTooLargeException() : IOException("Configuração excede o limite de 1 MiB.");
internal sealed record SettingsLoadResult(StudioSettings Settings, SettingsFileState State, string Message, bool CanRecoverBackup);
internal sealed record SettingsSaveResult(bool Success, string Message);

internal interface ISettingsWriter
{
    void WriteNew(string path, byte[] bytes, CancellationToken token);
    void Commit(string temporary, string target, bool replacing, string? backup);
}

internal sealed class SettingsWriter : ISettingsWriter
{
    public void WriteNew(string path, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        token.ThrowIfCancellationRequested();
        stream.Flush(flushToDisk: true);
    }

    public void Commit(string temporary, string target, bool replacing, string? backup)
    {
        if (replacing) File.Replace(temporary, target, backup);
        else File.Move(temporary, target);
    }
}

/// <summary>Serializa as operações de I/O fora da thread da interface. Um lease e o hash do conteúdo protegem as instâncias cooperantes.</summary>
internal sealed class SettingsStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ISettingsWriter writer;
    private byte[]? baseline;
    public string FilePath { get; }
    public string BackupPath => FilePath + ".bak";
    public SettingsFileState State { get; private set; }
    public bool CanSave => State == SettingsFileState.Ready || State == SettingsFileState.Missing && !CanRecoverBackup;
    public bool CanReset => CanSave || State is SettingsFileState.Corrupt or SettingsFileState.Missing;
    public bool CanRecoverBackup { get; private set; }
    public static string DefaultPath
    {
        get
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) throw new InvalidOperationException("Pasta local do usuário indisponível.");
            return Path.Combine(local, "WaveStudio", "settings.json");
        }
    }

    public SettingsStore(string path, ISettingsWriter? writer = null)
    {
        FilePath = Path.GetFullPath(path);
        if (string.IsNullOrEmpty(Path.GetFileName(FilePath)) || Path.GetDirectoryName(FilePath) is null)
            throw new ArgumentException("Informe o caminho de um arquivo de preferências.", nameof(path));
        this.writer = writer ?? new SettingsWriter();
    }

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                StudioSettings settings = new();
                string message;
                CanRecoverBackup = false;
                baseline = null;
                try
                {
                    baseline = ReadOptional(FilePath);
                    settings = baseline is null ? new() : SettingsJson.Decode(baseline);
                    State = baseline is null ? SettingsFileState.Missing : SettingsFileState.Ready;
                    message = baseline is null ? "Primeira execução. Preferências locais serão salvas após suas ações."
                        : "Preferências carregadas. Os níveis atuais são consultados no Windows.";
                }
                catch (SettingsFileTooLargeException)
                {
                    State = SettingsFileState.Oversized;
                    message = ProtectOversizedFile();
                }
                catch (NotSupportedException)
                {
                    State = SettingsFileState.Unsupported;
                    message = "Versão de configuração incompatível. Arquivo preservado e gravação bloqueada. Use uma versão compatível do Wave Studio.";
                }
                catch (Exception error) when (IsInvalid(error))
                {
                    State = SettingsFileState.Corrupt;
                    message = "Configuração inválida preservada. Padrões seguros estão em memória. Recupere o backup ou redefina explicitamente as preferências locais.";
                }
                catch (Exception error) when (IsStorageError(error))
                {
                    State = SettingsFileState.Unreadable;
                    message = "Não foi possível ler a configuração. Gravação bloqueada. Confira acesso à pasta e reinicie o aplicativo.";
                }
                if (State is SettingsFileState.Corrupt or SettingsFileState.Missing)
                    CanRecoverBackup = ReadValidBackup() is not null;
                if (State == SettingsFileState.Missing && CanRecoverBackup)
                    message = "Arquivo principal ausente. Backup válido disponível. Escolha Recuperar backup ou Redefinir preferências locais.";
                token.ThrowIfCancellationRequested();
                return new SettingsLoadResult(settings, State, message, CanRecoverBackup);
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public Task<SettingsSaveResult> SaveAsync(StudioSettings settings, CancellationToken token = default) =>
        WriteAsync(settings, recovery: false, useBackup: false, token);
    public Task<SettingsSaveResult> ResetAsync(CancellationToken token = default) =>
        WriteAsync(new StudioSettings(), recovery: true, useBackup: false, token);
    public Task<SettingsSaveResult> RecoverBackupAsync(CancellationToken token = default) =>
        WriteAsync(new StudioSettings(), recovery: true, useBackup: true, token);

    private async Task<SettingsSaveResult> WriteAsync(StudioSettings settings, bool recovery, bool useBackup, CancellationToken token)
    {
        // Copia e valida antes de aguardar, para que o chamador não altere um array em uma gravação enfileirada.
        byte[] encoded;
        try { encoded = SettingsJson.Encode(settings); }
        catch (Exception error) when (SettingsJson.IsValidationError(error))
        {
            return new(false, "Preferências inválidas. A configuração válida foi preservada. " + error.Message);
        }
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!(CanSave || recovery && (State is SettingsFileState.Corrupt or SettingsFileState.Missing)))
                return new(false, "Gravação protegida. O arquivo existente foi preservado.");
            return await Task.Run(() => Write(encoded, recovery, useBackup, token), token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private SettingsSaveResult Write(byte[] encoded, bool recovery, bool useBackup, CancellationToken token)
    {
        string directory = Path.GetDirectoryName(FilePath)!;
        string temporary = Path.Combine(directory, Path.GetFileName(FilePath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            using var lease = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            byte[]? current = ReadOptional(FilePath);
            if (!SameContent(current, baseline))
            {
                State = SettingsFileState.Conflict;
                return new(false, "A configuração foi modificada por outra instância. Arquivo preservado. Feche e reabra para carregar a versão atual.");
            }
            if (useBackup)
            {
                byte[]? backup = ReadValidBackup();
                if (backup is null) return new(false, "Backup ausente ou inválido. O arquivo original foi preservado.");
                encoded = backup;
            }
            string? archived = null;
            if (State == SettingsFileState.Corrupt && recovery && current is not null)
            {
                archived = FilePath + ".corrupt-" + Guid.NewGuid().ToString("N") + ".json";
                writer.WriteNew(archived, current, token);
            }
            writer.WriteNew(temporary, encoded, token);
            token.ThrowIfCancellationRequested();
            if (!SameContent(ReadOptional(FilePath), baseline))
            {
                State = SettingsFileState.Conflict;
                return new(false, "O arquivo mudou durante a gravação. Conteúdo atual preservado. Reabra o aplicativo para carregar a alteração.");
            }
            // Somente um arquivo principal reconhecidamente válido vira o próximo backup. A recuperação mantém o backup válido.
            string? backupPath = State == SettingsFileState.Ready ? BackupPath : null;
            writer.Commit(temporary, FilePath, current is not null, backupPath);
            baseline = encoded;
            State = SettingsFileState.Ready;
            CanRecoverBackup = false;
            return new(true, archived is null ? "Preferências salvas localmente."
                : "Preferências recuperadas. O original inválido foi preservado em " + Path.GetFileName(archived) + ".");
        }
        catch (SettingsFileTooLargeException)
        {
            return new(false, ProtectOversizedFile());
        }
        catch (Exception error) when (IsStorageError(error))
        {
            return new(false, "Falha ao gravar preferências. Confira permissão, espaço em disco e outra instância em execução. " + error.GetType().Name);
        }
        finally
        {
            // Este arquivo temporário exclusivo pertence a esta operação no diretório de configurações.
            try { File.Delete(temporary); }
            catch (Exception error) when (IsStorageError(error)) { /* Uma inspeção explícita posterior pode remover o arquivo órfão. */ }
        }
    }

    private string ProtectOversizedFile()
    {
        State = SettingsFileState.Oversized;
        baseline = null;
        CanRecoverBackup = false;
        return "Configuração maior que 1 MiB preservada. Gravação, redefinição e recuperação automáticas bloqueadas. "
            + "Inspecione o arquivo com o aplicativo fechado e reabra após corrigir seu tamanho.";
    }

    private byte[]? ReadValidBackup()
    {
        try
        {
            var bytes = ReadOptional(BackupPath);
            if (bytes is not null) SettingsJson.Decode(bytes);
            return bytes;
        }
        catch (Exception error) when (IsInvalid(error) || IsStorageError(error) || error is NotSupportedException) { return null; }
    }

    private static byte[]? ReadOptional(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = stream.Length;
            if (length > SettingsJson.MaximumBytes) throw new SettingsFileTooLargeException();
            var bytes = new byte[checked((int)length)];
            stream.ReadExactly(bytes);
            long finalLength = stream.Length;
            if (finalLength > SettingsJson.MaximumBytes) throw new SettingsFileTooLargeException();
            if (finalLength != length) throw new IOException("O arquivo mudou durante a leitura.");
            return bytes;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static bool SameContent(byte[]? left, byte[]? right) => left is null || right is null
        ? left is null && right is null
        : CryptographicOperations.FixedTimeEquals(SHA256.HashData(left), SHA256.HashData(right));
    private static bool IsInvalid(Exception error) => error is JsonException or InvalidDataException or ArgumentException;
    internal static bool IsStorageError(Exception error) => error is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
