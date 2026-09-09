<#
.SYNOPSIS
  Rebuilds the worker image and restarts the container: stops the running one, builds a
  fresh image from the current code, starts it detached. Run this after every code pull.
.PARAMETER Logs
  Follow the container log after starting.
#>
param(
    [switch]$Logs
)

$ErrorActionPreference = "Stop"
$compose = Join-Path $PSScriptRoot "docker-compose.yml"
$envFile = Join-Path $PSScriptRoot "worker.env"

if (-not (Test-Path $envFile)) {
    Copy-Item (Join-Path $PSScriptRoot "worker.env.example") $envFile
    throw "Created $envFile - fill in the Neon + R2 credentials, then run this again."
}

# Stop and remove the old container, then build a fresh image and start a new one.
docker compose -f $compose down
docker compose -f $compose up -d --build

if ($Logs) {
    docker compose -f $compose logs -f
} else {
    Write-Host ""
    Write-Host "Worker restarted. Follow the log with:  docker logs -f knowledgebase-worker"
    Write-Host "Stop it with:  infra/worker/stop-worker.ps1"
}
