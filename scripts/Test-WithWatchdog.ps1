param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [int]$Seed = 20261114,
    [ValidateRange(30, 180)][int]$TimeoutSeconds = 90,
    [string]$TestClass,
    [string]$RepositoryRoot
)

# Only this newly created dotnet process and its descendants can be terminated.
$ErrorActionPreference = 'Stop'
$repoRoot = if ($RepositoryRoot) { (Resolve-Path -LiteralPath $RepositoryRoot).Path }
    else { (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'WaveStudio.slnx'))) { throw 'Raiz do repositório inválida.' }
$logDirectory = Join-Path $repoRoot 'artifacts/clock-drift'
[System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null
$prefix = "$Configuration-$Seed-" + [guid]::NewGuid().ToString('N')
$processInfo = New-Object System.Diagnostics.ProcessStartInfo
$processInfo.FileName = (Get-Command dotnet -ErrorAction Stop).Source
$processInfo.WorkingDirectory = $repoRoot
$processInfo.UseShellExecute = $false
$processInfo.CreateNoWindow = $true
$processInfo.RedirectStandardOutput = $true
$processInfo.RedirectStandardError = $true
$arguments = @('test', '--solution', 'WaveStudio.slnx', '-c', $Configuration, '--no-build', '--',
    '--seed', "$Seed", '--timeout', "$($TimeoutSeconds - 10)s", '--long-running', '10',
    '--diagnostic', '--diagnostic-output-directory', (Join-Path $logDirectory $prefix),
    '--show-live-output', 'on')
if ($TestClass) { $arguments += @('--filter-class', $TestClass) }
# No shell interpretation. All arguments are quoted; none can contain quotes or
# end with a backslash (which would escape the closing quote on Windows).
foreach ($argument in $arguments) {
    if ($argument.Contains('"') -or $argument.EndsWith('\')) { throw 'Argumento não suportado pelo watchdog.' }
}
$processInfo.Arguments = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
$processInfo.EnvironmentVariables['TESTINGPLATFORM_TELEMETRY_OPTOUT'] = '1'
$runner = New-Object System.Diagnostics.Process
$runner.StartInfo = $processInfo
try {
    if (-not $runner.Start()) { throw 'Não foi possível iniciar o runner.' }
    $stdoutTask = $runner.StandardOutput.ReadToEndAsync()
    $stderrTask = $runner.StandardError.ReadToEndAsync()
    $finished = $runner.WaitForExit($TimeoutSeconds * 1000)
    if (-not $finished) {
        # .NET Framework/PowerShell 5 has no Kill(entireProcessTree). Snapshot
        # descendants of this PID only, then recheck parent/creation before kill.
        $inventory = @(Get-CimInstance Win32_Process)
        function Stop-RunnerChildren([int]$parentId) {
            foreach ($child in $inventory | Where-Object { $_.ParentProcessId -eq $parentId }) {
                Stop-RunnerChildren $child.ProcessId
                $current = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $child.ProcessId)
                if ($current -and $current.ParentProcessId -eq $parentId -and $current.CreationDate -eq $child.CreationDate) {
                    $childProcess = [System.Diagnostics.Process]::GetProcessById($child.ProcessId)
                    try { $childProcess.Kill() } finally { $childProcess.Dispose() }
                }
            }
        }
        Stop-RunnerChildren $runner.Id
        if (-not $runner.HasExited) { $runner.Kill() }
        if (-not $runner.WaitForExit(5000)) { throw 'O runner próprio não confirmou encerramento.' }
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    [System.IO.File]::WriteAllText((Join-Path $logDirectory "$prefix.stdout.txt"), $stdout)
    [System.IO.File]::WriteAllText((Join-Path $logDirectory "$prefix.stderr.txt"), $stderr)
    Write-Output $stdout
    if ($stderr) { Write-Output $stderr }
    Write-Output "Logs: artifacts/clock-drift/$prefix; watchdog=$(-not $finished)"
    if (-not $finished) { exit 124 }
    exit $runner.ExitCode
}
finally { $runner.Dispose() }
