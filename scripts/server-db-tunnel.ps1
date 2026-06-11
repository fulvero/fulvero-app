$ErrorActionPreference = "Stop"

$LocalPort = if ($args.Count -ge 1) { $args[0] } else { "5434" }
$ServerPostgresPort = if ($args.Count -ge 2) { $args[1] } else { "5433" }

Write-Host "Opening SSH tunnel to server database."
Write-Host "Keep this window open while you work with the server DB."
Write-Host ""
Write-Host "Local connection:"
Write-Host "  Host: 127.0.0.1"
Write-Host "  Port: $LocalPort"
Write-Host ""
Write-Host "You will be asked for the SSH password."
Write-Host ""

ssh -N -L "${LocalPort}:127.0.0.1:${ServerPostgresPort}" root@31.129.96.196
