$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

docker compose --env-file .env.local -f docker-compose.local.yml up -d --build

Write-Host ""
Write-Host "Fulvero: http://localhost:8080"
Write-Host "Adminer: http://localhost:8082"
Write-Host ""
Write-Host "Local database:"
Write-Host "  Host: 127.0.0.1"
Write-Host "  Port: 5433"
Write-Host "  Database: Fulvero_ozon"
Write-Host "  User: Fulvero"
Write-Host "  Password: Fulvero_local_password_2026"
