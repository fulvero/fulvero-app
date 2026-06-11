# Local Docker Runbook

## Start the project

From the repository root:

```powershell
.\scripts\local-up.ps1
```

Application:

```text
http://localhost:8080
```

Adminer:

```text
http://localhost:8082
```

## Local database connection

Use these settings in DataGrip, DBeaver, pgAdmin, Adminer, or another PostgreSQL client:

```text
Host: 127.0.0.1
Port: 5433
Database: Fulvero_ozon
User: Fulvero
Password: Fulvero_local_password_2026
```

In Adminer:

```text
System: PostgreSQL
Server: postgres
Username: Fulvero
Password: Fulvero_local_password_2026
Database: Fulvero_ozon
```

## Server database connection through SSH tunnel

Start the tunnel in a separate PowerShell window and keep it open:

```powershell
.\scripts\server-db-tunnel.ps1
```

Then connect your PostgreSQL client to:

```text
Host: 127.0.0.1
Port: 5434
Database: Fulvero_ozon
User: Fulvero
Password: use the production PostgreSQL password from the server .env
```

The script does not store the SSH password. SSH will ask for it interactively.

If the server PostgreSQL host port is not `5433`, pass it as the second argument:

```powershell
.\scripts\server-db-tunnel.ps1 5434 5432
```

To find the production PostgreSQL database name, user, password, and exposed server port, run:

```powershell
.\scripts\server-inspect-db.ps1
```

Then on the server, in the project directory:

```bash
grep -E "^(POSTGRES_DB|POSTGRES_USER|POSTGRES_PASSWORD|POSTGRES_PORT)=" .env
```

## Stop local containers

```powershell
.\scripts\local-down.ps1
```

## Useful Docker commands

```powershell
docker compose --env-file .env.local -f docker-compose.local.yml ps
docker compose --env-file .env.local -f docker-compose.local.yml logs -f app
docker compose --env-file .env.local -f docker-compose.local.yml logs -f postgres
```
