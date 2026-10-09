$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repoRoot 'src\SleepTimer.App\SleepTimer.App.csproj'
$output = Join-Path $repoRoot 'dist\win-x64'
$icon = Join-Path $output 'Assets\sleep-timer.ico'
$executable = Join-Path $output 'SleepTimer.exe'

dotnet publish $project -c Release -r win-x64 --self-contained true -o $output -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $executable)) { throw "Published executable not found: $executable" }
$assetDirectory = Split-Path -Parent $icon
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\SleepTimer.App\Assets\sleep-timer.ico') -Destination $icon -Force
if (-not (Test-Path -LiteralPath $icon)) { throw "Published icon not found: $icon" }

$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$shortcutPath = Join-Path $desktop 'Bedtime Timer.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = $output
$shortcut.IconLocation = "$icon,0"
$shortcut.Description = 'Start your saved Bedtime Timer.'
$shortcut.Save()

Write-Output "Published: $executable"
Write-Output "Shortcut:  $shortcutPath"

