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

## Routine deploy: `scripts/deploy.sh`

For an already-provisioned host, a deploy is one command from a
workstation:

```bash
scripts/deploy.sh        # publishes, stages, swaps, restarts, verifies
```

It publishes Portal + Worker, rsyncs them to the burrow staging dirs
`/home/claw/meridian-{portal,worker}-stage/`, runs the host-side
`meridian-deploy.sh` for each service (Portal first — it applies
migrations on startup), and polls `https://meridianbd.dev/` until it
serves 200. Deploys whatever commit is checked out; warns if not `main`.

The host-side `/home/claw/meridian-deploy.sh <portal|worker>` does the
privileged part: backup `→` stop `→` rsync swap `→` chown `→` start.

### One-time host setup: scoped passwordless sudo

`scripts/deploy.sh` needs to invoke the host-side script under `sudo`
without an interactive password. Grant that **once**, scoped to only
that script:

```bash
# on burrow
sudo chown root:root /home/claw/meridian-deploy.sh   # claw can no longer edit it
sudo chmod 755 /home/claw/meridian-deploy.sh
echo "claw ALL=(root) NOPASSWD: /home/claw/meridian-deploy.sh" \
  | sudo tee /etc/sudoers.d/meridian-deploy
sudo chmod 440 /etc/sudoers.d/meridian-deploy
sudo visudo -cf /etc/sudoers.d/meridian-deploy        # validate syntax
```

The `chown root:root` is the security crux: passwordless sudo on a
script the calling user can rewrite is a trivial root escalation. Root
ownership makes the script read-only to `claw`, so the NOPASSWD grant
only ever runs the reviewed deploy logic.

The sections below are the **first-time / from-scratch** provisioning
reference for a new host.

## Target host

Hetzner CPX31 (4 vCPU / 8 GB RAM / 160 GB), Ubuntu 24.04 LTS, on the
operator's Tailnet at `100.99.141.72`. Worker + Portal co-locate on this
single host for v3.0. Splitting hosts is a v3.1 concern.

**Co-tenant: openclaw.** Meridian is the second app on this box. As of
2026-05-13 the openclaw stack runs Next.js on :3000 fronted by nginx
on :80, plus PostgreSQL 17 on :5432 (loopback) and Redis on :6379/:6380.
Meridian uses the same Postgres instance (different db + role), reuses
nginx as its reverse proxy (different server block), and binds Portal
to :5000 + Worker to :9090 — all of which were free on 2026-05-13.
Re-run the host inventory in Phase 1 below to confirm before deploy.

## Prerequisites on the host

```bash
# Confirm what's already there (do NOT install duplicates):
ss -tlnp                              # which ports are bound
systemctl is-active nginx             # expect: active
systemctl is-active postgresql@17-main  # expect: active (or @18 if newer)
psql --version
dotnet --info | head -5

# Install only what's missing. On burrow as of 2026-05-13:
# - .NET 10 SDK: already there (10.0.107)
# - PostgreSQL: 17.x already there — DO NOT install 18, use existing
# - nginx: already there — DO NOT install Caddy
# If any of these are missing on your target host:

# .NET 10 runtime (if missing)
wget https://dot.net/v1/dotnet-install.sh
sudo bash dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /opt/dotnet
sudo ln -s /opt/dotnet/dotnet /usr/local/bin/dotnet

# Postgres 17+ (if missing — 17 is fine, EF Core + pgvector both support it)
sudo apt-get install -y postgresql

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

For a provisioned host, `scripts/deploy.sh` does publish + stage + swap
in one step — see **Routine deploy** above. The manual equivalent below
is the reference for a fresh host or a deploy without the script.

Publish from a workstation:

```bash
dotnet publish src/Meridian.Portal -c Release -r linux-x64 \
    --no-self-contained -o ./publish/portal
dotnet publish src/Meridian.Worker -c Release -r linux-x64 \
    --no-self-contained -o ./publish/worker
```

`rsync` straight into `/opt/meridian/...` does NOT work — that tree is
owned `meridian:meridian`, the SSH user `claw` can't write it, and
`sudo` needs a password (so `--rsync-path="sudo rsync"` can't run
non-interactively either). Stage into `claw`'s home, then let the
root-owned host-side `meridian-deploy.sh` do the privileged swap:

```bash
# stage to the host (workstation) — no privileges needed
rsync -a --delete ./publish/portal/ burrow:meridian-portal-stage/
rsync -a --delete ./publish/worker/ burrow:meridian-worker-stage/

# privileged swap (host) — backup → stop → swap → chown → start
ssh burrow 'sudo /home/claw/meridian-deploy.sh portal'
ssh burrow 'sudo /home/claw/meridian-deploy.sh worker'
```

Portal first — it applies the migrations the Worker depends on.

## Environment / appsettings overrides

Both processes read `appsettings.json` then `appsettings.<Env>.json` then
env vars. The production secrets go in env vars, NOT files:

```bash
# /etc/meridian/portal.env (chmod 600, owned by meridian)
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5000
ConnectionStrings__Meridian=Host=localhost;Database=meridian;Username=meridian;Password=<chosen>
PostmarkInbound__Username=<chosen>
PostmarkInbound__Password=<chosen>
# ...etc per appsettings.json schema...

# /etc/meridian/worker.env
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:9090
ConnectionStrings__Meridian=Host=localhost;Database=meridian;Username=meridian;Password=<chosen>
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

Every `meridian-deploy.sh` run first backs the live artifact up to
`/opt/meridian/<svc>.bak`. To roll back the last deploy, SSH in and run
as root (substitute `worker` for `portal` as needed):

```bash
ssh burrow                 # then, on the host:
sudo systemctl stop meridian-portal
sudo rsync -a --delete /opt/meridian/portal.bak/ /opt/meridian/portal/
sudo systemctl start meridian-portal
```

`.bak` holds only the *previous* deploy — it is overwritten on every
run, so this rolls back exactly one step.

DB migrations are NOT auto-rolled-back. If a migration was the cause,
revert manually with `dotnet ef database update <previous-migration>`
from a workstation pointed at the prod connection.

## Known gaps explicit (carried from soft-launch runbook)

- No blue-green / zero-downtime deploy (SUPREME-0 violation, v3.1).
- No automated migration runner in Worker (Portal-first deploy order).
- `OpportunityType` field missing (sequence selection AgencyType-only).
- Role-based access deferred (all users full-access in v3.0).
