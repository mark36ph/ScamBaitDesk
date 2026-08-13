$ErrorActionPreference = "Stop"
$logDirectory = Join-Path $env:LOCALAPPDATA "ScamBaitDesk"
$logPath = Join-Path $logDirectory "update.log"
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
"[$(Get-Date -Format o)] Update started." | Set-Content -LiteralPath $logPath

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$window = New-Object System.Windows.Forms.Form
$window.Text = "Updating ScamBait Desk"
$window.Size = New-Object System.Drawing.Size(460, 180)
$window.StartPosition = "CenterScreen"
$window.FormBorderStyle = "FixedDialog"
$window.MaximizeBox = $false
$window.MinimizeBox = $false
$window.ControlBox = $false
$window.TopMost = $true

$title = New-Object System.Windows.Forms.Label
$title.Text = "Updating ScamBait Desk"
$title.Font = New-Object System.Drawing.Font("Segoe UI", 15, [System.Drawing.FontStyle]::Bold)
$title.AutoSize = $true
$title.Location = New-Object System.Drawing.Point(22, 18)
$window.Controls.Add($title)

$stage = New-Object System.Windows.Forms.Label
$stage.Text = "Preparing update..."
$stage.Font = New-Object System.Drawing.Font("Segoe UI", 10)
$stage.AutoSize = $true
$stage.Location = New-Object System.Drawing.Point(24, 62)
$window.Controls.Add($stage)

$progress = New-Object System.Windows.Forms.ProgressBar
$progress.Style = "Continuous"
$progress.Minimum = 0
$progress.Maximum = 100
$progress.Value = 5
$progress.Size = New-Object System.Drawing.Size(400, 20)
$progress.Location = New-Object System.Drawing.Point(24, 96)
$window.Controls.Add($progress)

function Set-UpdateStage([string]$message, [int]$percent) {
    $stage.Text = $message
    $progress.Value = [Math]::Max(0, [Math]::Min(100, $percent))
    $window.Refresh()
    [System.Windows.Forms.Application]::DoEvents()
}

function Invoke-UpdateProcess([string]$fileName, [string[]]$arguments, [int]$maximumPercent, [string]$failureMessage) {
    $stdoutPath = Join-Path $env:TEMP "ScamBaitDesk-update-stdout.txt"
    $stderrPath = Join-Path $env:TEMP "ScamBaitDesk-update-stderr.txt"
    Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath $fileName -ArgumentList $arguments -WorkingDirectory $repository -NoNewWindow -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
    while (-not $process.HasExited) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 100
        $process.Refresh()
        if ($progress.Value -lt $maximumPercent) { $progress.Value++ }
    }
    $stdout = if (Test-Path $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw } else { "" }
    $stderr = if (Test-Path $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { "" }
    "[$(Get-Date -Format o)] $fileName $($arguments -join ' ')`r`n$stdout$stderr" | Add-Content -LiteralPath $logPath
    if ($process.ExitCode -ne 0) {
        $detail = if ([string]::IsNullOrWhiteSpace($stderr)) { $stdout.Trim() } else { $stderr.Trim() }
        throw "$failureMessage$(if ($detail) { "`r`n`r`n$detail" })"
    }
}

$window.Show()
$window.Activate()
$window.BringToFront()
[System.Windows.Forms.Application]::DoEvents()

try {
$repository = Split-Path -Parent $PSScriptRoot
Set-Location $repository

# When launched from inside the app, give its process time to close and release build outputs.
Start-Sleep -Seconds 2

Set-UpdateStage "Downloading the latest version..." 15
Invoke-UpdateProcess "git.exe" @("pull", "origin", "main") 35 "Downloading the update failed."

Set-UpdateStage "Building the application..." 40
Invoke-UpdateProcess "dotnet.exe" @("build", ".\ScamBaitDesk.sln", "-c", "Debug", "-p:Platform=x64", "-p:PublishProfile=") 75 "The Windows build failed; the installed app was not changed."

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

Set-UpdateStage "Installing the update..." 80
Add-AppxPackage -Register $manifest.FullName -ForceApplicationShutdown

Set-UpdateStage "Reopening ScamBait Desk..." 95
Start-Sleep -Seconds 1
$installedApp = Get-StartApps | Where-Object Name -EQ "ScamBait Desk" | Select-Object -First 1
if ($installedApp) {
    Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$($installedApp.AppID)"
} else {
    throw "The update succeeded, but ScamBait Desk could not be found in Start Apps. Open it from Start manually."
}
}
catch {
    "[$(Get-Date -Format o)] FAILED: $($_.Exception)" | Add-Content -LiteralPath $logPath
    [System.Windows.Forms.MessageBox]::Show(
        "ScamBait Desk could not be updated.`r`n`r`n$($_.Exception.Message)`r`n`r`nLog: $logPath",
        "Update failed",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error
    ) | Out-Null
}
finally {
    $window.Close()
    $window.Dispose()
}
