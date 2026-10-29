#!/usr/bin/env bash
# The load test, first part: builds the three production images, starts the reference stack of
# deploy/compose/compose.yaml (two server replicas behind the TLS proxy, one worker, PostgreSQL over TLS), migrates,
# creates the tenant `load` and runs the placeholder k6 scenario in tests/load through the proxy. Removes the stack
# at the end, unless --keep is given. Arguments after -- go to k6. The exit code is k6's.
# The files of the run, with the certificates and secret files, are in artifacts/load. See docs/reference/performance.md.
set -euo pipefail
cd "$(dirname "$0")/.."

keep=false
k6_args=()
while [ "$#" -gt 0 ]; do
  case "$1" in
    --keep) keep=true; shift ;;
    --) shift; k6_args=("$@"); break ;;
    *) echo 'Usage: scripts/load.sh [--keep] [-- <k6 arguments>]' >&2; exit 2 ;;
  esac
done

source scripts/lib/retry.sh

version=0.0.0-load
tenant_host=load.axis.test
identity_host=id.axis.test
root="$PWD"
folder="$root/artifacts/load"

npm run build:keycloak-theme --prefix web
for target in axis-server axis-worker axis-admin; do
  retry 3 docker build --target "$target" --build-arg "AXIS_VERSION=$version" -t "$target:$version" .
done

rm -rf "$folder"
node scripts/lib/load-stack.mjs "$folder"

export AXIS_VERSION="$version"
export AXIS_SECRETS_DIR="$folder/secrets"
export AXIS_EXTENSIONS_DIR="$folder/extensions"
export AXIS_MODULES_DIR="$folder/modules"
export AXIS_STUDIO_DIR="$folder/studio"
export AXIS_APPS_DIR="$folder/apps"
export AXIS_TLS_DIR="$folder/tls"
export AXIS_SERVER_TLS_DIR="$folder/server-tls"
export AXIS_IDENTITY_TLS_DIR="$folder/identity-tls"
export AXIS_IDENTITY_DB_CA_DIR="$folder/db-ca"
export AXIS_IDENTITY_THEME_DIR="$root/artifacts/keycloak-theme"
export AXIS_IDENTITY_HOST="$identity_host"
# Ports that differ from scripts/dev.sh, so both can run on one machine. The variables override them.
: "${AXIS_SERVER_PORT:=18080}"
: "${AXIS_SERVER_2_PORT:=18081}"
: "${AXIS_HTTPS_PORT:=18443}"
: "${AXIS_WORKER_HEALTH_PORT:=15208}"
export AXIS_SERVER_PORT AXIS_SERVER_2_PORT AXIS_HTTPS_PORT AXIS_WORKER_HEALTH_PORT
unset AXIS_OIDC_CLIENT_SECRET

compose() {
  docker compose -p axis-load -f "$root/deploy/compose/compose.yaml" -f "$folder/override.json" "$@"
}

down_command="COMPOSE_PROFILES=admin,replicas,load docker compose -p axis-load -f $root/deploy/compose/compose.yaml -f $folder/override.json down -v --remove-orphans"
cleanup() {
  if [ "$keep" = true ]; then
    echo "The stack and $folder are left in place. Remove them with:"
    echo "  $down_command && rm -rf $folder"
  else
    COMPOSE_PROFILES=admin,replicas,load compose down -v --remove-orphans || true
    rm -rf "$folder"
  fi
}
trap cleanup EXIT

# wait_for <seconds> <label> <command...>: polls once a second. On a timeout it prints the logs and fails.
wait_for() {
  local seconds="$1" label="$2" i
  shift 2
  for ((i = 0; i < seconds; i++)); do
    if "$@" >/dev/null 2>&1; then return 0; fi
    sleep 1
  done
  echo "$label was not ready after $seconds seconds" >&2
  COMPOSE_PROFILES=admin,replicas,load compose logs --no-color >&2 || true
  exit 1
}

ca="$folder/ca/ca.crt"
# The identity provider's discovery document, through the proxy.
identity_answers() {
  curl -fsS -o /dev/null --cacert "$ca" --resolve "$identity_host:$AXIS_HTTPS_PORT:127.0.0.1" \
    "https://$identity_host:$AXIS_HTTPS_PORT/realms/master/.well-known/openid-configuration"
}
# A server's health: it serves HTTPS with a certificate for the name servers.
server_ready() {
  curl -fsS -o /dev/null --cacert "$ca" --resolve "servers:$1:127.0.0.1" -H 'Host: localhost' "https://servers:$1/health/ready"
}
worker_ready() {
  curl -fsS -o /dev/null "http://127.0.0.1:$AXIS_WORKER_HEALTH_PORT/health/ready"
}
# The tenant's host answers 401 without a signed-in user. An unknown host would answer 404.
tenant_routed() {
  [ "$(curl -s -o /dev/null -w '%{http_code}' --cacert "$ca" --resolve "$tenant_host:$AXIS_HTTPS_PORT:127.0.0.1" \
    "https://$tenant_host:$AXIS_HTTPS_PORT/api/me")" = 401 ]
}

compose up -d postgres
# Over TCP: the initialization server listens only on the Unix socket, so this succeeds after the init script.
wait_for 60 PostgreSQL compose exec -T postgres pg_isready -h 127.0.0.1 -U postgres

compose up -d identity proxy
wait_for 240 'The identity provider' identity_answers

compose up -d server worker
wait_for 120 'The server' server_ready "$AXIS_SERVER_PORT"
wait_for 120 'The worker' worker_ready

compose --profile replicas up -d server-2
wait_for 120 'The second server' server_ready "$AXIS_SERVER_2_PORT"

compose --profile admin run --rm --no-TTY admin migrate
compose --profile admin run --rm --no-TTY admin tenant create load --host "$tenant_host"
wait_for 60 "The host $tenant_host" tenant_routed

set +e
compose --profile load run --rm --no-TTY k6 run ${k6_args[@]+"${k6_args[@]}"} /scripts/placeholder.js
status=$?
set -e
exit "$status"
