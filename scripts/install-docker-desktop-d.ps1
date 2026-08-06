# Install Docker Desktop on D: drive (WSL data also on D:)
# Run elevated PowerShell: .\scripts\install-docker-desktop-d.ps1

$ErrorActionPreference = "Stop"

$installRoot = "D:\Docker"
$installerDir = Join-Path $installRoot "Installer"
$installDir = Join-Path $installRoot "Docker"
$wslRoot = Join-Path $installRoot "wsl"
$installer = Join-Path $installerDir "DockerDesktopInstaller.exe"

New-Item -ItemType Directory -Force -Path $installerDir, $installDir, $wslRoot | Out-Null

if (-not (Test-Path $installer) -or (Get-Item $installer).Length -lt 100MB) {
    Write-Host "Downloading Docker Desktop installer..."
    Import-Module BitsTransfer
    Start-BitsTransfer -Source "https://desktop.docker.com/win/main/amd64/Docker%20Desktop%20Installer.exe" `
        -Destination $installer -DisplayName "DockerDesktop"
}

Write-Host "Installing to $installDir (WSL data: $wslRoot)..."
$p = Start-Process -FilePath $installer -ArgumentList @(
    "install",
    "--quiet",
    "--accept-license",
    "--installation-dir=$installDir",
    "--wsl-default-data-root=$wslRoot",
    "--backend=wsl-2",
    "--always-run-service"
) -Wait -PassThru

Write-Host "Installer exit code: $($p.ExitCode)"
if ($p.ExitCode -ne 0) { throw "Docker install failed: $($p.ExitCode)" }

$desktop = Join-Path $installDir "Docker Desktop.exe"
if (-not (Test-Path $desktop)) { throw "Docker Desktop.exe not found at $desktop" }

# User PATH for CLI
$bin = Join-Path $installDir "resources\bin"
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -notlike "*$bin*") {
    [Environment]::SetEnvironmentVariable("Path", "$bin;$userPath", "User")
    Write-Host "Added Docker bin to User PATH: $bin"
}

Write-Host "Starting Docker Desktop..."
Start-Process $desktop
Write-Host "Done. Sign-in may appear once. Then run: .\scripts\start-infra.ps1"
