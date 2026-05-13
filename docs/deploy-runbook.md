---
title: Deploy Runbook (v3.0)
summary: Manual deploy steps for Portal + Worker to a single Linux host. Used by Slice 5 to exercise the first production deploy to Hetzner CPX31.
keywords: deploy, runbook, Hetzner, burrow, systemd, DataProtection, /health
category: Operations
---

# Meridian v3.0 Deploy Runbook (manual)

This is the **manual** deploy procedure. v3.0 ships without a blue-green
script (acknowledged SUPREME-0 violation; deferred to v3.1). Rollback is
"redeploy the previous artifact" with a short downtime window.

Companion to `docs/soft-launch-runbook.md`. Run the deploy steps first,
then the soft-launch checklist with the deployed host as the target.

## Target host

Hetzner CPX31 (4 vCPU / 8 GB RAM / 160 GB), Ubuntu 24.04 LTS, on the
operator's Tailnet at `100.99.141.72`. Worker + Portal co-locate on this
single host for v3.0. Splitting hosts is a v3.1 concern.

## Prerequisites on the host

```bash
# .NET 10 runtime
wget https://dot.net/v1/dotnet-install.sh
sudo bash dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /opt/dotnet
sudo ln -s /opt/dotnet/dotnet /usr/local/bin/dotnet

# Postgres 18
sudo apt-get install -y postgresql-18

# Shared DataProtection volume
sudo mkdir -p /var/lib/meridian/dp-keys
sudo chown meridian:meridian /var/lib/meridian/dp-keys

# Service user
sudo useradd --system --home /opt/meridian --shell /usr/sbin/nologin meridian
```

## Database

```bash
sudo -u postgres createuser meridian
sudo -u postgres createdb -O meridian meridian
sudo -u postgres psql -c "ALTER USER meridian WITH PASSWORD '<chosen>';"
```

Schema is applied by the Portal on startup (Portal-must-boot-first
coupling — known gap, deferred). For new hosts the first `dotnet Meridian.Portal.dll`
boot will run all migrations.

## Artifacts

From a workstation:

```bash
dotnet publish src/Meridian.Portal -c Release -r linux-x64 \
    --no-self-contained -o ./publish/portal
dotnet publish src/Meridian.Worker -c Release -r linux-x64 \
    --no-self-contained -o ./publish/worker

rsync -av ./publish/portal/  burrow:/opt/meridian/portal/
rsync -av ./publish/worker/  burrow:/opt/meridian/worker/
```

## Environment / appsettings overrides

Both processes read `appsettings.json` then `appsettings.<Env>.json` then
env vars. The production secrets go in env vars, NOT files:

```bash
# /etc/meridian/portal.env (chmod 600, owned by meridian)
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5000
ConnectionStrings__Meridian=Host=localhost;Database=meridian;Username=meridian;Password=<chosen>
# ...etc per appsettings.json schema...

# /etc/meridian/worker.env
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:9090
ConnectionStrings__Meridian=Host=localhost;Database=meridian;Username=meridian;Password=<chosen>
PostmarkInbound__Username=<chosen>
PostmarkInbound__Password=<chosen>
Health__Description=Meridian Worker — burrow
```

## DataProtection key sharing

Both processes MUST share the keyring or the Worker can't decrypt secrets
the Portal encrypted (per-tenant API keys, CRM tokens, OIDC client
secrets, webhook secrets). The path is set per process; the value must
match:

In each process's appsettings (or via env var
`DataProtection__KeyDirectory=...`):

```json
{
  "DataProtection": { "KeyDirectory": "/var/lib/meridian/dp-keys" }
}
```

The `SetApplicationName("Meridian")` call is already in both
Program.cs files and must NOT diverge.

## systemd units

`/etc/systemd/system/meridian-portal.service`:

```ini
[Unit]
Description=Meridian Portal
After=network.target postgresql.service
Requires=postgresql.service

[Service]
Type=notify
WorkingDirectory=/opt/meridian/portal
EnvironmentFile=/etc/meridian/portal.env
ExecStart=/usr/local/bin/dotnet Meridian.Portal.dll
Restart=on-failure
RestartSec=5
User=meridian
Group=meridian

[Install]
WantedBy=multi-user.target
```

`/etc/systemd/system/meridian-worker.service`:

```ini
[Unit]
Description=Meridian Worker
After=network.target meridian-portal.service
# Worker requires Portal to apply migrations first (known gap).

[Service]
Type=notify
WorkingDirectory=/opt/meridian/worker
EnvironmentFile=/etc/meridian/worker.env
ExecStart=/usr/local/bin/dotnet Meridian.Worker.dll
Restart=on-failure
RestartSec=5
User=meridian
Group=meridian

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now meridian-portal meridian-worker
```

## Smoke after deploy

1. **Portal listens.** `curl -i http://127.0.0.1:5000/` returns
   200 (login page) or a redirect.
2. **Worker /health.** `curl http://127.0.0.1:9090/health` returns
   JSON matching:
   ```json
   { "status": "healthy",
     "version": "YYYYMMDD.HHMM",
     "description": "Meridian Worker — burrow",
     "buildDate": "...",
     "timestamp": "..." }
   ```
3. **Migrations applied.**
   `sudo -u postgres psql meridian -c "\dt"` shows the full table set.
4. **Logs.**
   `journalctl -u meridian-portal -f` and
   `journalctl -u meridian-worker -f` — look for `Application started`
   and the per-job scheduling lines (`Job Ingestion scheduled for ...`).

Once smoke passes, follow `docs/soft-launch-runbook.md` against this
host as the target.

## Rollback

```bash
sudo systemctl stop meridian-portal meridian-worker
# Restore previous publish/ artifacts via rsync
sudo systemctl start meridian-portal meridian-worker
```

DB migrations are NOT auto-rolled-back. If a migration was the cause,
revert manually with `dotnet ef database update <previous-migration>`
from a workstation pointed at the prod connection.

## Known gaps explicit (carried from soft-launch runbook)

- No blue-green / zero-downtime deploy (SUPREME-0 violation, v3.1).
- No automated migration runner in Worker (Portal-first deploy order).
- `OpportunityType` field missing (sequence selection AgencyType-only).
- Role-based access deferred (all users full-access in v3.0).
