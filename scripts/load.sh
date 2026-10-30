#!/usr/bin/env bash
# The load test, first three parts: builds the three production images, writes the generated application of typical size
# (tests/Axis.LoadData, seed 1) into its run folder, starts the reference stack of deploy/compose/compose.yaml (two
# server replicas behind the TLS proxy, one worker, PostgreSQL over TLS), migrates and creates the tenant `load`. It then
# prepares the realm `load` with 50 users and a machine caller (scripts/lib/load-realm.mjs, which writes
# artifacts/load/k6/users.json and caller.json for k6), sets the tenant to accept the caller's tokens, activates the
# application in the tenant, which writes the seed records, and runs the k6 scenario tests/load/objectives.js through
# the proxy: 50 users sign in through the realm and loop over the reads and writes of the objectives. k6 writes its summary
# to artifacts/load-results, which scripts/lib/load-objectives.mjs checks against the objectives. The exit code is the
# checker's when it fails, otherwise k6's, and k6's when k6 wrote no summary. An OpenTelemetry Collector receives the
# worker's metrics over https and writes them to artifacts/load-results/worker-metrics.json. After k6 the script stops the
# worker and then the collector, so the last metrics reach the file. Removes the stack at the end, unless --keep
# is given. Arguments after -- go to k6.
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
seed=1
tenant_host=load.axis.test
identity_host=id.axis.test
root="$PWD"
folder="$root/artifacts/load"
results="$root/artifacts/load-results"

npm run build:keycloak-theme --prefix web
for target in axis-server axis-worker axis-admin; do
  retry 3 docker build --target "$target" --build-arg "AXIS_VERSION=$version" -t "$target:$version" .
done

rm -rf "$folder"
node scripts/lib/load-stack.mjs "$folder"
dotnet run --project tests/Axis.LoadData -c Release -- "$folder/apps/load" --seed "$seed"
# The containers read the application as another user.
chmod -R a+rX "$folder/apps"

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

# The collector writes the worker's metrics into the results folder, as the host user. The grafana/k6 image does not run as
# the host user either, so the folder must be writable by all. Files of an earlier run are removed.
mkdir -p "$results"
chmod a+rwx "$results"
rm -f "$results/summary.json" "$results/worker-metrics.json"

compose up -d server worker
wait_for 120 'The server' server_ready "$AXIS_SERVER_PORT"
wait_for 120 'The worker' worker_ready

compose --profile replicas up -d server-2
wait_for 120 'The second server' server_ready "$AXIS_SERVER_2_PORT"

compose --profile admin run --rm --no-TTY admin migrate
compose --profile admin run --rm --no-TTY admin tenant create load --host "$tenant_host"
wait_for 60 "The host $tenant_host" tenant_routed

# The realm comes before the activation, which copies the redirect addresses and mappers of the client to the site's.
node scripts/lib/load-realm.mjs "$folder" "$AXIS_HTTPS_PORT" "$seed"
# The tenant accepts the tokens of the machine caller, whose audience is axis-load-endpoints.
compose --profile admin run --rm --no-TTY admin tenant oidc set load --endpoint-audience axis-load-endpoints
compose --profile admin run --rm --no-TTY admin activate /apps/load --tenant load

set +e
compose --profile load run --rm --no-TTY -v "$results:/results" k6 run --summary-export=/results/summary.json ${k6_args[@]+"${k6_args[@]}"} /scripts/objectives.js
status=$?
# The worker flushes its last metrics when it stops, and the collector, which it depends on, stops after it.
compose stop worker collector
set -e
if [ ! -f "$results/summary.json" ]; then
  echo "k6 wrote no summary to $results/summary.json" >&2
  exit "$status"
fi
node scripts/lib/load-objectives.mjs "$results/summary.json" || exit $?
exit "$status"
