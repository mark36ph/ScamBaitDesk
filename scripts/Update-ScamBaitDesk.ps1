$ErrorActionPreference = "Stop"

$repository = Split-Path -Parent $PSScriptRoot
Set-Location $repository

# When launched from inside the app, give its process time to close and release build outputs.
Start-Sleep -Seconds 2

git pull
if ($LASTEXITCODE -ne 0) { throw "git pull failed." }

dotnet build .\ScamBaitDesk.sln -c Debug -p:Platform=x64 -p:PublishProfile=
if ($LASTEXITCODE -ne 0) { throw "The Windows build failed; the installed app was not changed." }

$manifest = Get-ChildItem ".\src\ScamBaitDesk\bin\x64\Debug" -Recurse -Filter AppxManifest.xml |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $manifest) { throw "The generated AppxManifest.xml was not found." }

# Loose development packages require a monotonically increasing version.
# Use local date/time components, each safely below the MSIX 65535 limit.
$now = Get-Date
$monthVersion = [int]$now.ToString("yyMM")
$dayHourVersion = [int]$now.ToString("ddHH")
$minuteSecondVersion = [int]$now.ToString("mmss")
$version = "1.$monthVersion.$dayHourVersion.$minuteSecondVersion"
[xml]$xml = Get-Content -LiteralPath $manifest.FullName
$xml.Package.Identity.Version = $version
$xml.Save($manifest.FullName)

Add-AppxPackage -Register $manifest.FullName -ForceApplicationShutdown
Write-Host "ScamBait Desk updated and registered as version $version." -ForegroundColor Green
