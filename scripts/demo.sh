#!/usr/bin/env bash
# Provision or reset the isolated demo tenant on burrow production.
#
#   scripts/demo.sh provision <slug> <email> ["Tenant Name"]
#   scripts/demo.sh reset <slug>
#
# The slug MUST start with "demo-" — the Worker refuses anything else, so
# neither command can touch a real tenant. Provision prompts for the demo
# operator password unless MERIDIAN_DEMO_PASSWORD is set (min 12 chars).
# The operator email never receives mail (outbound is Console-sandboxed and
# the login is pre-verified), so it does not need a real mailbox.
#
# Runs the Worker one-shot on the host via the same systemd-run pattern as
# the soft-launch runbook's --run-job. sudo on burrow may prompt — hence
# `ssh -t`, not BatchMode.
set -euo pipefail
cd "$(dirname "$0")/.."

HOST=burrow

usage() {
    sed -n '2,7p' "$0" | sed 's/^# \{0,1\}//'
    exit 1
}

[[ $# -ge 1 ]] || usage
cmd=$1

run_worker() {
    # shellcheck disable=SC2029 — args are expanded client-side on purpose.
    ssh -t "$HOST" "sudo systemd-run --pipe --wait --collect --uid=meridian --gid=meridian \
      -p EnvironmentFile=/etc/meridian/worker.env \
      --working-directory=/opt/meridian/worker \
      /usr/bin/env dotnet Meridian.Worker.dll $*"
}

case "$cmd" in
provision)
    [[ $# -ge 3 ]] || usage
    slug=$2 email=$3 name=${4:-"Meridian Demo"}
    if [[ -z "${MERIDIAN_DEMO_PASSWORD:-}" ]]; then
        read -rsp "Demo operator password (min 12 chars): " MERIDIAN_DEMO_PASSWORD
        echo
    fi
    echo ">> provisioning demo tenant '${slug}' on ${HOST}"
    run_worker --demo-provision "'$slug'" "'$email'" "'$MERIDIAN_DEMO_PASSWORD'" "'$name'"
    echo ">> done — log in at https://meridianbd.dev/login as ${email}"
    ;;
reset)
    [[ $# -ge 2 ]] || usage
    slug=$2
    echo ">> resetting demo tenant '${slug}' on ${HOST}"
    run_worker --demo-reset "'$slug'"
    echo ">> done — '${slug}' is back to the pristine demo story"
    ;;
*)
    usage
    ;;
esac
