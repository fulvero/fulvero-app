$ErrorActionPreference = "Stop"

Write-Host "This opens an interactive SSH command on the server."
Write-Host "Use it to find the production PostgreSQL env values."
Write-Host "You will be asked for the SSH password."
Write-Host ""

ssh root@31.129.96.196 @'
set -e
echo "Docker containers:"
docker ps --format "table {{.Names}}\t{{.Image}}\t{{.Ports}}"
echo
echo "Possible .env files:"
find /root /opt /var/www /srv -maxdepth 5 -name ".env" -type f 2>/dev/null || true
echo
echo "If you know the project directory, run:"
echo "  cd /path/to/fulvero-app && grep -E \"^(POSTGRES_DB|POSTGRES_USER|POSTGRES_PASSWORD|POSTGRES_PORT)=\" .env"
'@
