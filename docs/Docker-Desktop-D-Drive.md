# Docker Desktop (local on D:) + RLogistics RLogisticsGENIE infra

## Layout on this machine

| Path | Purpose |
|------|---------|
| `D:\Docker\Docker\` | Docker Desktop app + CLI |
| `D:\Docker\wsl\` | WSL2 disk data root (large images) |
| `D:\Docker\Installer\` | Offline installer cache |

CLI is at: `D:\Docker\Docker\resources\bin\docker.exe`

## Scripts

```powershell
# One-time (admin): install Docker to D: if needed
.\scripts\install-docker-desktop-d.ps1

# Every session: Redis + Qdrant
.\scripts\start-infra.ps1
```

## Compose services (`infra/docker-compose.yml`)

| Service | Port | Role |
|---------|------|------|
| redis | 6379 | Core session/cache + RLogisticsGENIE cache |
| qdrant | 6333/6334 | RLogisticsGENIE SOP vector store |
| genie | 8090 | RLogisticsGENIE API (optional container) |

## After infra is up

1. `appsettings.json` → `"Redis": { "Enabled": true }` (already set after install)  
2. Restart Core: `dotnet run --project src/RLogistics --urls http://localhost:5088`  
3. RLogisticsGENIE: `uvicorn` on 8090 (or `docker compose up genie`)  
4. Check: `docker ps`, `http://localhost:6333/dashboard`, RLogisticsGENIE `/health` redis/qdrant true  

**S: drive:** not used — **D:** has more free space for VM disks.
