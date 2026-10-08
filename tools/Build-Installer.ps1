$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repoRoot 'src\SleepTimer.App\SleepTimer.App.csproj'
$publishDirectory = Join-Path $repoRoot 'dist\win-x64'
$iconSource = Join-Path $repoRoot 'src\SleepTimer.App\Assets\sleep-timer.ico'
$installerScript = Join-Path $PSScriptRoot 'SleepTimerSetup.iss'
$installerOutput = Join-Path $repoRoot 'dist\SleepTimerSetup.exe'

$compilerCandidates = @(
    (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

if (-not $compilerCandidates) {
    throw 'Inno Setup 6 or 7 is required to rebuild the installer. Install it from https://jrsoftware.org/isdl.php.'
}
$compiler = $compilerCandidates | Select-Object -First 1

dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishDirectory -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$publishedExecutable = Join-Path $publishDirectory 'SleepTimer.exe'
$publishedIcon = Join-Path $publishDirectory 'Assets\sleep-timer.ico'
if (-not (Test-Path -LiteralPath $publishedExecutable)) { throw "Published executable not found: $publishedExecutable" }
New-Item -ItemType Directory -Path (Split-Path -Parent $publishedIcon) -Force | Out-Null
Copy-Item -LiteralPath $iconSource -Destination $publishedIcon -Force

& $compiler /Qp $installerScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $installerOutput)) { throw "Installer not found: $installerOutput" }

$installerSize = (Get-Item -LiteralPath $installerOutput).Length
Write-Output "Installer: $installerOutput"
Write-Output ('Size: {0:N1} MB' -f ($installerSize / 1MB))
Write-Output "Portable app: $publishedExecutable"
