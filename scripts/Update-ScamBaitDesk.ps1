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
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $fileName
    $startInfo.Arguments = $arguments -join " "
    $startInfo.WorkingDirectory = $repository
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw "$failureMessage`r`n`r`nThe process could not be started." }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    while (-not $process.HasExited) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 100
        if ($progress.Value -lt $maximumPercent) { $progress.Value++ }
    }
    $process.WaitForExit()
    [int]$exitCode = $process.ExitCode
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $process.Dispose()
    "[$(Get-Date -Format o)] Exit ${exitCode}: $fileName $($arguments -join ' ')`r`n$stdout$stderr" | Add-Content -LiteralPath $logPath
    if ($exitCode -ne 0) {
        $detail = if ([string]::IsNullOrWhiteSpace($stderr)) { $stdout.Trim() } else { $stderr.Trim() }
        throw "$failureMessage$(if ($detail) { "`r`n`r`n$detail" })"
    }
}

function Get-SignToolPath {
    $candidates = @()

    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { $candidates += $command.Source }

    $sdkRoots = @()
    if (${env:ProgramFiles(x86)}) { $sdkRoots += Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin" }
    if (${env:ProgramFiles}) { $sdkRoots += Join-Path ${env:ProgramFiles} "Windows Kits\10\bin" }

    foreach ($root in $sdkRoots) {
        if (Test-Path -LiteralPath $root) {
            $candidates += Get-ChildItem -LiteralPath $root -Recurse -Filter signtool.exe -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\\(x64|x86)\\signtool\.exe$' } |
                Sort-Object FullName -Descending |
                Select-Object -ExpandProperty FullName
        }
    }

    $nugetRoot = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools"
    if (Test-Path -LiteralPath $nugetRoot) {
        $candidates += Get-ChildItem -LiteralPath $nugetRoot -Recurse -Filter signtool.exe -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\(x64|x86)\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -ExpandProperty FullName
    }

    $path = $candidates |
        Where-Object { $_ -and (Test-Path -LiteralPath $_) } |
        Select-Object -First 1

    if (-not $path) {
        throw "SignTool.exe was not found. Install the Windows 10/11 SDK, then run the updater again."
    }

    "[$(Get-Date -Format o)] Using SignTool: $path" | Add-Content -LiteralPath $logPath
    return $path
}

function Get-OrCreateSigningCertificate {
    $publisher = "CN=ScamBaitDesk"
    $existing = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $publisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
    if (-not $existing) {
        $existing = New-SelfSignedCertificate `
            -Type Custom `
            -KeyUsage DigitalSignature `
            -Subject $publisher `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
            -FriendlyName "ScamBaitDesk local MSIX signing"
        "[$(Get-Date -Format o)] Created local signing certificate: $($existing.Thumbprint)" | Add-Content -LiteralPath $logPath
    }

    $publicCert = Join-Path $logDirectory "ScamBaitDesk-signing.cer"
    Export-Certificate -Cert $existing -FilePath $publicCert -Force | Out-Null

    # Windows MSIX deployment validates self-signed package certificates against
    # the machine Trusted People store. Import the public certificate there with
    # elevation so Add-AppxPackage can trust the package.
    $machineTrusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
        Where-Object Thumbprint -EQ $existing.Thumbprint |
        Select-Object -First 1
    if (-not $machineTrusted) {
        Set-UpdateStage "Trusting the local signing certificate..." 84
        "[$(Get-Date -Format o)] Requesting administrator approval to trust certificate in LocalMachine\TrustedPeople." | Add-Content -LiteralPath $logPath
        $escapedCert = $publicCert.Replace("'", "''")
        $arguments = @(
            '-NoProfile',
            '-Command',
            "Import-Certificate -FilePath '$escapedCert' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null"
        )
        $elevated = Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $arguments -Wait -PassThru
        if ($elevated.ExitCode -ne 0) {
            throw "Windows administrator approval was required to trust the MSIX signing certificate."
        }
        $machineTrusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
            Where-Object Thumbprint -EQ $existing.Thumbprint |
            Select-Object -First 1
        if (-not $machineTrusted) {
            throw "The signing certificate could not be found in LocalMachine\TrustedPeople after elevation."
        }
        "[$(Get-Date -Format o)] Signing certificate trusted in LocalMachine\TrustedPeople." | Add-Content -LiteralPath $logPath
    }

    return $existing
}

$window.Show()
$window.Activate()
$window.BringToFront()
[System.Windows.Forms.Application]::DoEvents()

$manifestSource = $null
$originalManifestVersion = $null
try {
    $repository = Split-Path -Parent $PSScriptRoot
    Set-Location $repository

    Start-Sleep -Seconds 2

    Set-UpdateStage "Downloading the latest version..." 15
    Invoke-UpdateProcess "git.exe" @("pull", "origin", "main") 30 "Downloading the update failed."

    $now = Get-Date
    $monthVersion = [int]$now.ToString("yyMM")
    $dayHourVersion = [int]$now.ToString("ddHH")
    $minuteSecondVersion = [int]$now.ToString("mmss")
    $version = "1.$monthVersion.$dayHourVersion.$minuteSecondVersion"

    $manifestSource = Join-Path $repository "src\ScamBaitDesk\Package.appxmanifest"
    [xml]$sourceXml = Get-Content -LiteralPath $manifestSource
    $originalManifestVersion = $sourceXml.Package.Identity.Version
    $sourceXml.Package.Identity.Version = $version
    $sourceXml.Save($manifestSource)

    Set-UpdateStage "Building a self-contained MSIX package..." 40
    Invoke-UpdateProcess "dotnet.exe" @(
        "publish", ".\src\ScamBaitDesk\ScamBaitDesk.csproj",
        "-c", "Debug",
        "-p:Platform=x64",
        "-p:PublishProfile=",
        "-p:GenerateAppxPackageOnBuild=true",
        "-p:AppxPackageSigningEnabled=false",
        "-p:WindowsAppSDKSelfContained=true",
        "-p:SelfContained=true",
        "-p:PublishTrimmed=false",
        "-p:PublishReadyToRun=false"
    ) 70 "The MSIX build failed; the installed app was not changed."

    $packageRoots = @(
        (Join-Path $repository "src\ScamBaitDesk\AppPackages"),
        (Join-Path $repository "src\ScamBaitDesk\bin\x64\Debug")
    ) | Where-Object { Test-Path -LiteralPath $_ }

    $msix = Get-ChildItem -Path $packageRoots -Recurse -Filter *.msix -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $msix) {
        $existingPackages = ($packageRoots | ForEach-Object { "- $_" }) -join [Environment]::NewLine
        throw "The build completed but did not produce an MSIX package. Searched:`r`n$existingPackages`r`n`r`nCheck update.log for the publish output."
    }

    "[$(Get-Date -Format o)] MSIX package before signing: $($msix.FullName)" | Add-Content -LiteralPath $logPath

    Set-UpdateStage "Signing the MSIX package..." 78
    $signTool = Get-SignToolPath
    $certificate = Get-OrCreateSigningCertificate
    Invoke-UpdateProcess $signTool @("sign", "/fd", "SHA256", "/sha1", $certificate.Thumbprint, $msix.FullName) 86 "The MSIX was built but could not be signed."
    "[$(Get-Date -Format o)] MSIX signed with certificate $($certificate.Thumbprint)." | Add-Content -LiteralPath $logPath

    Set-UpdateStage "Installing the MSIX package..." 90
    Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown

    Set-UpdateStage "Reopening ScamBait Desk..." 95
    Start-Sleep -Seconds 2

    $installedPackage = Get-AppxPackage -Name "ScamBaitDesk" |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if (-not $installedPackage) {
        throw "The MSIX was installed, but Windows did not report the Scam Bait Desk package."
    }

    $installedManifest = Get-AppxPackageManifest -Package $installedPackage.PackageFullName
    $application = $installedManifest.Package.Applications.Application |
        Where-Object Id -EQ "ScamBaitDeskApp" |
        Select-Object -First 1
    if (-not $application) {
        throw "The MSIX was installed, but the Scam Bait Desk application entry could not be found."
    }

    $applicationUserModelId = "$($installedPackage.PackageFamilyName)!$($application.Id)"
    "[$(Get-Date -Format o)] Installed package: $($installedPackage.PackageFullName)`r`n[$(Get-Date -Format o)] Launching AUMID: $applicationUserModelId" | Add-Content -LiteralPath $logPath
    Start-Process -FilePath "explorer.exe" -ArgumentList "shell:AppsFolder\$applicationUserModelId"

    Start-Sleep -Seconds 2
    Set-UpdateStage "Update complete" 100
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
    if ($manifestSource -and $originalManifestVersion) {
        try {
            [xml]$restoreXml = Get-Content -LiteralPath $manifestSource
            $restoreXml.Package.Identity.Version = $originalManifestVersion
            $restoreXml.Save($manifestSource)
        }
        catch {
            "[$(Get-Date -Format o)] WARNING: Could not restore source manifest version: $($_.Exception)" | Add-Content -LiteralPath $logPath
        }
    }
    $window.Close()
    $window.Dispose()
}
