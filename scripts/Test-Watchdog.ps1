param([string]$RepositoryRoot, [ValidateSet('Timeout', 'Pipe')][string]$Mode = 'Timeout')
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'WaveStudio.slnx'))) { throw 'Raiz do repositório inválida.' }
$probeDirectory = Join-Path $repoRoot 'artifacts/watchdog-probe'
[System.IO.Directory]::CreateDirectory($probeDirectory) | Out-Null
$source = @'
// WaveStudio-WatchdogProbe-v1: harmless locally compiled process/pipe fixture.
using System.Diagnostics;
if (args[0] == "Child") { Console.WriteLine("owned child holds stdout"); Thread.Sleep(120000); return; }
var child = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
child.ArgumentList.Add("Child"); child.ArgumentList.Add(args[1]); child.ArgumentList.Add(args[2]);
using var process = Process.Start(child)!;
File.WriteAllText(Path.Combine(args[2], args[1] + ".probe-child.txt"), process.Id.ToString());
Console.WriteLine("probe root started owned child " + process.Id);
if (args[0] == "Timeout") Thread.Sleep(120000);
'@
$sourcePath = Join-Path $probeDirectory 'Program.cs'
if ((Test-Path $sourcePath) -and -not ([System.IO.File]::ReadAllText($sourcePath).Contains('WaveStudio-WatchdogProbe-v1'))) { throw 'Fonte de probe desconhecida; preservada.' }
[System.IO.File]::WriteAllText($sourcePath, $source)
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>'
$projectPath = Join-Path $probeDirectory 'WatchdogProbe.csproj'
if ((Test-Path $projectPath) -and [System.IO.File]::ReadAllText($projectPath) -ne $project) { throw 'Projeto de probe desconhecido; preservado.' }
[System.IO.File]::WriteAllText($projectPath, $project)
& dotnet build (Join-Path $probeDirectory 'WatchdogProbe.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& ([scriptblock]::Create((Get-Content -Raw -Encoding UTF8 (Join-Path $repoRoot 'scripts/Test-WithWatchdog.ps1')))) -RepositoryRoot $repoRoot -Probe $Mode -TimeoutSeconds 30
