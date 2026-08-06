# RLogisticsGENIE — start Redis + Qdrant (+ optional RLogisticsGENIE container)
# Requires Docker Desktop on D:\Docker (see scripts/install-docker-desktop-d.ps1)

$ErrorActionPreference = "Stop"
$env:Path = "D:\Docker\Docker\resources\bin;$env:Path"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Error "docker.exe not found. Start Docker Desktop from D:\Docker\Docker\Docker Desktop.exe first."
}

# Ensure engine is up
$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    docker info 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Write-Host "Waiting for Docker engine... ($i)"
    Start-Sleep -Seconds 3
}
if (-not $ready) { throw "Docker engine not ready." }

$root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path (Join-Path $root "infra\docker-compose.yml"))) {
    $root = "d:\Praveen\Projects\RLogistics"
}

Write-Host "Starting redis + qdrant from $root ..."
Push-Location $root
try {
    docker compose -f infra/docker-compose.yml up -d redis qdrant
    docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
    Write-Host ""
    Write-Host "Redis:  localhost:6379"
    Write-Host "Qdrant: http://localhost:6333"
    Write-Host "Set Core Redis:Enabled=true then restart RLogistics."
    Write-Host "GENIE:  cd src\RLogisticsGENIE && uvicorn app.main:app --port 8090"
    Write-Host "Or full stack: docker compose -f infra/docker-compose.yml up -d --build"
}
finally {
    Pop-Location
}
