param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [int]$Seed = 20261114,
    [ValidateRange(30, 180)][int]$TimeoutSeconds = 90,
    [string]$TestClass,
    [string]$RepositoryRoot,
    [ValidateSet('None', 'Timeout', 'Pipe')][string]$Probe = 'None'
)

# Somente o processo dotnet recém-criado e seus descendentes podem ser encerrados.
$ErrorActionPreference = 'Stop'
$repoRoot = if ($RepositoryRoot) { (Resolve-Path -LiteralPath $RepositoryRoot).Path }
    else { (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'WaveStudio.slnx'))) { throw 'Raiz do repositório inválida.' }
$logDirectory = Join-Path $repoRoot 'artifacts/clock-drift'
[System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null
$prefix = "$Configuration-$Seed-" + [guid]::NewGuid().ToString('N')
$budget = [System.Diagnostics.Stopwatch]::StartNew()
$processInfo = New-Object System.Diagnostics.ProcessStartInfo
$processInfo.FileName = (Get-Command dotnet -ErrorAction Stop).Source
$processInfo.WorkingDirectory = $repoRoot
$processInfo.UseShellExecute = $false
$processInfo.CreateNoWindow = $true
$processInfo.RedirectStandardOutput = $true
$processInfo.RedirectStandardError = $true
$arguments = @('test', '--solution', 'WaveStudio.slnx', '-c', $Configuration, '--no-build', '--',
    '--seed', "$Seed", '--timeout', "$($TimeoutSeconds - 15)s", '--long-running', '10',
    '--diagnostic', '--diagnostic-output-directory', (Join-Path $logDirectory $prefix),
    '--show-live-output', 'on')
if ($TestClass) { $arguments += @('--filter-class', $TestClass) }
if ($Probe -ne 'None') {
    $processInfo.FileName = Join-Path $repoRoot 'artifacts/watchdog-probe/bin/Release/net10.0-windows/WatchdogProbe.exe'
    if (-not (Test-Path -LiteralPath $processInfo.FileName)) { throw 'Compile primeiro o probe local revisado.' }
    $arguments = @($Probe, $prefix, $logDirectory)
}
# Sem interpretação pelo shell. Todos os argumentos são delimitados por aspas. Nenhum pode conter aspas ou
# terminar com barra invertida, que escaparia a aspa de fechamento no Windows.
foreach ($argument in $arguments) {
    if ($argument.Contains('"') -or $argument.EndsWith('\')) { throw 'Argumento não suportado pelo watchdog.' }
}
$processInfo.Arguments = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
$processInfo.EnvironmentVariables['TESTINGPLATFORM_TELEMETRY_OPTOUT'] = '1'
$runner = New-Object System.Diagnostics.Process
$runner.StartInfo = $processInfo
$watchdogState = @{ KilledChildren = 0; OwnershipRefused = $false; StreamFault = $false; Refused = [System.Collections.Generic.List[string]]::new() }
function Wait-CapturedStreams([int]$milliseconds) {
    try { return $streams.Wait($milliseconds) }
    catch { $watchdogState.StreamFault = $true; return $false }
}
function Stop-OwnedChildren([int]$parentId, [datetime]$parentCreated, [int]$depth = 0) {
    if ($depth -gt 8 -or $budget.Elapsed.TotalSeconds -gt ($TimeoutSeconds - 2)) { $watchdogState.OwnershipRefused = $true; return }
    try { $children = @(Get-CimInstance Win32_Process -Filter ('ParentProcessId = ' + $parentId) -OperationTimeoutSec 1) }
    catch { $watchdogState.OwnershipRefused = $true; $watchdogState.Refused.Add('CIM: ' + $_.Exception.GetType().Name); return }
    foreach ($child in $children) {
        if ($budget.Elapsed.TotalSeconds -gt ($TimeoutSeconds - 2)) { $watchdogState.OwnershipRefused = $true; break }
        # A relação entre PIDs não basta. O MTP e o probe precisam carregar o identificador único desta execução.
        if (-not $child.CommandLine -or -not $child.CommandLine.Contains($prefix) -or $child.CreationDate.ToUniversalTime() -lt $parentCreated) {
            $watchdogState.OwnershipRefused = $true; $watchdogState.Refused.Add('nonce/creation: ' + $child.Name); continue
        }
        $owned = $null
        try {
            $owned = [System.Diagnostics.Process]::GetProcessById($child.ProcessId)
            $null = $owned.Handle # Pin the process object before revalidating its CIM identity.
            if ($owned.HasExited) { continue }
            $current = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $child.ProcessId) -OperationTimeoutSec 1
            if (-not $current -or $current.ParentProcessId -ne $parentId -or $current.CreationDate -ne $child.CreationDate -or
                -not $current.CommandLine.Contains($prefix) -or
                [Math]::Abs(($owned.StartTime.ToUniversalTime() - $current.CreationDate.ToUniversalTime()).TotalMilliseconds) -gt 1) {
                $watchdogState.OwnershipRefused = $true; $watchdogState.Refused.Add('identity: ' + $child.Name); continue
            }
            Stop-OwnedChildren $child.ProcessId $current.CreationDate.ToUniversalTime() ($depth + 1)
            if (-not $owned.HasExited) { $owned.Kill(); $watchdogState.KilledChildren++ }
        }
        catch [System.ArgumentException] { } # Already exited before acquiring the handle.
        catch [System.InvalidOperationException] { }
        catch { $watchdogState.OwnershipRefused = $true; $watchdogState.Refused.Add($_.Exception.GetType().Name) }
        finally { if ($owned) { $owned.Dispose() } }
    }
}
try {
    if (-not $runner.Start()) { throw 'Não foi possível iniciar o runner.' }
    $stdoutTask = $runner.StandardOutput.ReadToEndAsync()
    $stderrTask = $runner.StandardError.ReadToEndAsync()
    $created = $runner.StartTime.ToUniversalTime()
    $finished = $runner.WaitForExit([int][Math]::Max(1, ($TimeoutSeconds - 10 - $budget.Elapsed.TotalSeconds) * 1000))
    $streams = [System.Threading.Tasks.Task]::WhenAll([System.Threading.Tasks.Task[]]@($stdoutTask, $stderrTask))
    $pipesClosed = $finished -and (Wait-CapturedStreams 1500)
    if (-not $finished -or -not $pipesClosed) {
        Stop-OwnedChildren $runner.Id $created
        if (-not $runner.HasExited) { $runner.Kill() }
        $null = $runner.WaitForExit([int][Math]::Max(1, [Math]::Min(1000, ($TimeoutSeconds - $budget.Elapsed.TotalSeconds) * 1000)))
    }
    # A espera é limitada mesmo quando um descendente não verificado mantém um pipe aberto. Nunca chame GetResult em tarefa pendente.
    $collected = Wait-CapturedStreams ([int][Math]::Max(1, [Math]::Min(2000, ($TimeoutSeconds - $budget.Elapsed.TotalSeconds) * 1000)))
    $stdout = if ($stdoutTask.Status -eq [System.Threading.Tasks.TaskStatus]::RanToCompletion) { $stdoutTask.Result } else { '[stdout incompleto: pipe não encerrou no orçamento ou falhou]' }
    $stderr = if ($stderrTask.Status -eq [System.Threading.Tasks.TaskStatus]::RanToCompletion) { $stderrTask.Result } else { '[stderr incompleto: pipe não encerrou no orçamento ou falhou]' }
    [System.IO.File]::WriteAllText((Join-Path $logDirectory "$prefix.stdout.txt"), $stdout)
    [System.IO.File]::WriteAllText((Join-Path $logDirectory "$prefix.stderr.txt"), $stderr)
    Write-Output $stdout
    if ($stderr) { Write-Output $stderr }
    $metadata = @{ Prefix = $prefix; Probe = $Probe; Watchdog = (-not $finished); PipeDeadline = (-not $pipesClosed);
        Collected = $collected; KilledChildren = $watchdogState.KilledChildren; OwnershipRefused = $watchdogState.OwnershipRefused;
        Refused = @($watchdogState.Refused); StreamFault = $watchdogState.StreamFault; ElapsedSeconds = $budget.Elapsed.TotalSeconds }
    [System.IO.File]::WriteAllText((Join-Path $logDirectory "$prefix.watchdog.json"), ($metadata | ConvertTo-Json))
    Write-Output "Logs: artifacts/clock-drift/$prefix; watchdog=$(-not $finished); pipes=$collected; ownershipRefused=$($watchdogState.OwnershipRefused)"
    if (-not $finished) { exit 124 }
    if (-not $pipesClosed -or -not $collected -or $watchdogState.OwnershipRefused) { exit 125 }
    exit $runner.ExitCode
}
finally { $runner.Dispose() }
