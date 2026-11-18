namespace WaveStudio.Desktop;

internal sealed partial class MainForm
{
    private readonly TimeSpan closeTimeout;
    private CloseStep[] closeSteps = [];
    private bool closeAttemptRunning;
    private Task closeAttemptTask = Task.CompletedTask;
    internal Task CloseAttemptTask => closeAttemptTask;
    internal bool ClosePending => stopping && !closing;
    internal string CloseDiagnostic => status.Text;
    internal Task RefreshTask => refreshTask;

    private async Task FinishCloseAttemptAsync()
    {
        try
        {
            // Inicia cada limpeza independente, mesmo quando uma criação anterior lança uma exceção síncrona.
            foreach (var step in closeSteps) step.BeginOrRetry();
            await Task.WhenAll(closeSteps.Select(step => step.Task!)).WaitAsync(closeTimeout);
            closing = true;
            Close();
        }
        catch (Exception error) { ReportClosePending(error); }
    }

    private void ReportClosePending(Exception error)
    {
        if (IsDisposed) return;
        string pending = string.Join(", ", closeSteps.Where(step => step.Task?.IsCompletedSuccessfully != true).Select(step => step.Name));
        status.Text = $"Encerramento não confirmado ({pending}). A janela permanece aberta e novos comandos estão bloqueados. " +
            $"Aguarde e tente fechar novamente. {error.GetType().Name}";
        status.AccessibleDescription = status.Text;
        status.ForeColor = Color.Firebrick;
    }

    private sealed class CloseStep(string name, Func<Task> run)
    {
        public string Name => name;
        public Task? Task { get; private set; }
        public void BeginOrRetry()
        {
            // Mantém operações pendentes ou concluídas com sucesso. Nunca inicia um segundo descarte enquanto outro está em andamento.
            if (Task is null || Task.IsFaulted || Task.IsCanceled) Task = InvokeAsync();
        }
        private async Task InvokeAsync() => await run();
    }
}
