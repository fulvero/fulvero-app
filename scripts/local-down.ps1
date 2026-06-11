$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

docker compose --env-file .env.local -f docker-compose.local.yml down
