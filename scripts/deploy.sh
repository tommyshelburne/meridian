#!/usr/bin/env bash
# Deploy the current checkout's Portal + Worker to burrow production.
#
# Prerequisite (one-time, on burrow): scoped passwordless sudo for the
# remote deploy script — see docs/deploy-runbook.md. Without it the
# `sudo` calls below will hang waiting for a password.
#
# Usage:  scripts/deploy.sh        # deploys whatever is checked out
set -euo pipefail
cd "$(dirname "$0")/.."

HOST=burrow
SSH=(ssh -o BatchMode=yes)

commit=$(git rev-parse --short HEAD)
branch=$(git rev-parse --abbrev-ref HEAD)
echo ">> deploying ${branch} (${commit}) to ${HOST}"
[[ "$branch" == "main" ]] || echo "!! warning: not on main — deploying ${branch}"

echo ">> [1/4] publish Portal + Worker"
dotnet publish src/Meridian.Portal -c Release -r linux-x64 --no-self-contained -o ./publish/portal -v q
dotnet publish src/Meridian.Worker -c Release -r linux-x64 --no-self-contained -o ./publish/worker -v q

echo ">> [2/4] stage artifacts to ${HOST}"
rsync -a --delete -e "${SSH[*]}" ./publish/portal/ "${HOST}:/home/claw/meridian-portal-stage/"
rsync -a --delete -e "${SSH[*]}" ./publish/worker/ "${HOST}:/home/claw/meridian-worker-stage/"

echo ">> [3/4] remote deploy (Portal first — it applies migrations)"
"${SSH[@]}" "$HOST" 'sudo /home/claw/meridian-deploy.sh portal'
"${SSH[@]}" "$HOST" 'sudo /home/claw/meridian-deploy.sh worker'

echo ">> [4/4] verify"
# The systemd units are Type=simple, so `systemctl start` returns before
# Kestrel finishes binding. Poll the public URL until it's actually up
# (or give up) rather than racing a single curl against a cold start.
site=000
for _ in $(seq 1 20); do
    site=$(curl -s --max-time 10 https://meridianbd.dev/ -o /dev/null -w '%{http_code}' || echo 000)
    [[ "$site" == "200" ]] && break
    sleep 3
done
worker=$("${SSH[@]}" "$HOST" 'curl -s --max-time 5 http://127.0.0.1:9090/health' || echo '{}')
echo "   meridianbd.dev/ -> ${site}"
echo "   worker /health  -> ${worker}"
if [[ "$site" != "200" ]]; then
    echo "!! meridianbd.dev/ never returned 200 — check: ssh ${HOST} 'systemctl status meridian-portal'"
    exit 1
fi
echo ">> done: ${branch} (${commit}) live"
