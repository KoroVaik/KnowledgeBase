<#
.SYNOPSIS
  Stops and removes the worker container. The image stays; run-worker.ps1 brings it back.
#>
$ErrorActionPreference = "Stop"
docker compose -f (Join-Path $PSScriptRoot "docker-compose.yml") down
Write-Host "Worker stopped."
