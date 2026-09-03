$ErrorActionPreference = "Stop"

$repositoryUrl = "https://github.com/mark36ph/ScamBaitDesk.git"
$updaterRoot = Join-Path $env:LOCALAPPDATA "ScamBaitDesk\UpdaterRepo"
$updaterScript = Join-Path $updaterRoot "scripts\Update-ScamBaitDesk.ps1"

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $updaterRoot) | Out-Null

if (-not (Test-Path -LiteralPath (Join-Path $updaterRoot ".git"))) {
    if (Test-Path -LiteralPath $updaterRoot) {
        Remove-Item -LiteralPath $updaterRoot -Recurse -Force
    }
    & git.exe clone --depth 1 --branch main $repositoryUrl $updaterRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Could not download the ScamBait Desk update repository."
    }
}
else {
    & git.exe -C $updaterRoot fetch origin main --depth 1
    if ($LASTEXITCODE -ne 0) {
        throw "Could not check the ScamBait Desk update repository."
    }
    & git.exe -C $updaterRoot reset --hard origin/main
    if ($LASTEXITCODE -ne 0) {
        throw "Could not refresh the ScamBait Desk update repository."
    }
}

if (-not (Test-Path -LiteralPath $updaterScript)) {
    throw "The downloaded update repository does not contain the updater script."
}

& powershell.exe -NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File $updaterScript
exit $LASTEXITCODE
