#!/usr/bin/env bash
# End-to-end tests: real server, real PostgreSQL and the built SPA in Chromium (requires Docker).
# Keycloak runs with the development realm from samples/keycloak, and Playwright starts the
# server only once the realm answers. The server signs people in through that realm at
# /api/auth/sign-in. The SPA does not offer it yet.
# The server starts with two applications active: the generic E2E test application in
# tests/e2e/fixtures/e2e-app and the purchase request sample in samples/apps/purchase-requests.
# A spec can activate another folder during the run: tests/e2e/activate.ts starts the built server
# once more on a spare port with that folder in ActivateOnStartup, waits until it is ready and
# stops it. The main server then serves the new release. The release-pinning spec activates
# tests/e2e/fixtures/release-app that way. The purchase request release spec activates a changed copy
# of the purchase request sample, and activates the committed sample again when it ends.
# The worker runs against the same database while Playwright runs, with a 5 second lease. A spec
# can restart it: it writes the restart file, then stops the worker process. The run fails if the
# worker exits without a restart request, or never finds the tenant database migrated.
# The purchasing system stub from tests/Axis.PurchasingStub runs from the build output on
# $AXIS_E2E_PURCHASING_PORT (default 5281). Specs find it at $AXIS_E2E_PURCHASING_URL. The run
# fails if it never answers. The server and the worker run in Development, because only
# Development lets a connector call the stub over http. Their purchasing system bindings point at
# the stub, so an approved purchase request creates its purchase order there.
# A Compose smoke test then builds the image and runs the stack on free ports. It is skipped
# when $NEXKIT_BASE_SHA shows that nothing it covers changed.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh

image="postgres:18-alpine"
container="axis-e2e-postgres-$$"
password="e2e-$RANDOM$RANDOM"
keycloak_image="quay.io/keycloak/keycloak:26.4"
keycloak_container="axis-e2e-keycloak-$$"

purchasing_pid=""
worker_pid=""
worker_dir="$(mktemp -d)"
export AXIS_E2E_WORKER_PID_FILE="$worker_dir/worker.pid"
export AXIS_E2E_WORKER_RESTART_FILE="$worker_dir/restart"

# stop_worker: stops the restart loop first, so it starts no new worker, then the worker itself.
stop_worker() {
  if [ -n "$worker_pid" ]; then
    kill "$worker_pid" 2>/dev/null || true
    if [ -s "$AXIS_E2E_WORKER_PID_FILE" ]; then kill "$(cat "$AXIS_E2E_WORKER_PID_FILE")" 2>/dev/null || true; fi
    wait "$worker_pid" 2>/dev/null || true
    worker_pid=""
  fi
  rm -rf "$worker_dir"
}

cleanup() {
  stop_worker
  if [ -n "$purchasing_pid" ]; then
    kill "$purchasing_pid" 2>/dev/null || true
    wait "$purchasing_pid" 2>/dev/null || true
  fi
  docker rm -f "$container" >/dev/null 2>&1 || true
  docker rm -f "$keycloak_container" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker run -d --name "$container" -e POSTGRES_USER=axis -e POSTGRES_DB=axis \
  -e POSTGRES_PASSWORD="$password" -p 127.0.0.1::5432 "$image" >/dev/null
port="$(docker port "$container" 5432/tcp | head -n1 | sed 's/.*://')"

# Keycloak starts now and imports the realm while PostgreSQL and the build get ready.
docker run -d --name "$keycloak_container" \
  -e KC_BOOTSTRAP_ADMIN_USERNAME=admin -e KC_BOOTSTRAP_ADMIN_PASSWORD=admin \
  -v "$PWD/samples/keycloak:/opt/keycloak/data/import:ro" -p 127.0.0.1::8080 \
  "$keycloak_image" start-dev --import-realm >/dev/null
keycloak_port="$(docker port "$keycloak_container" 8080/tcp | head -n1 | sed 's/.*://')"

# Check over TCP: the image's initialization server listens only on the Unix socket.
timeout="${AXIS_E2E_POSTGRES_TIMEOUT:-60}"
ready=0
reason="was not ready after $timeout seconds"
last=""
for _ in $(seq 1 "$timeout"); do
  if last="$(docker exec "$container" pg_isready -h 127.0.0.1 -U axis -d axis 2>&1)"; then
    ready=1
    break
  fi
  if [ "$(docker inspect -f '{{.State.Running}}' "$container" 2>/dev/null)" != "true" ]; then
    reason="stopped before it was ready"
    break
  fi
  sleep 1
done

if [ "$ready" -ne 1 ]; then
  {
    echo "PostgreSQL in $container $reason."
    echo "Last pg_isready output: ${last:-<none>}"
    echo "Container logs:"
    docker logs "$container" 2>&1 || true
  } >&2
  exit 1
fi

scripts/build.sh

mkdir -p artifacts/logs
purchasing_log="$root/artifacts/logs/e2e-purchasing.log"
: >"$purchasing_log"
export AXIS_E2E_PURCHASING_URL="http://127.0.0.1:${AXIS_E2E_PURCHASING_PORT:-5281}"
ASPNETCORE_URLS="$AXIS_E2E_PURCHASING_URL" \
  dotnet tests/Axis.PurchasingStub/bin/Release/net10.0/Axis.PurchasingStub.dll >>"$purchasing_log" 2>&1 &
purchasing_pid=$!

ready=0
reason="did not answer after 30 seconds"
for _ in $(seq 1 30); do
  if curl -fs -o /dev/null "$AXIS_E2E_PURCHASING_URL/health"; then
    ready=1
    break
  fi
  if ! kill -0 "$purchasing_pid" 2>/dev/null; then
    reason="stopped before it answered"
    break
  fi
  sleep 1
done

if [ "$ready" -ne 1 ]; then
  {
    echo "The purchasing system stub at $AXIS_E2E_PURCHASING_URL $reason."
    echo "Last 100 lines of artifacts/logs/e2e-purchasing.log:"
    tail -n 100 "$purchasing_log"
  } >&2
  exit 1
fi

discovery="http://127.0.0.1:$keycloak_port/realms/axis/.well-known/openid-configuration"
timeout="${AXIS_E2E_KEYCLOAK_TIMEOUT:-180}"
ready=0
reason="did not answer after $timeout seconds"
for _ in $(seq 1 "$timeout"); do
  if curl -fs -o /dev/null "$discovery"; then
    ready=1
    break
  fi
  if [ "$(docker inspect -f '{{.State.Running}}' "$keycloak_container" 2>/dev/null)" != "true" ]; then
    reason="stopped before it answered"
    break
  fi
  sleep 1
done

if [ "$ready" -ne 1 ]; then
  {
    echo "The axis realm of Keycloak in $keycloak_container $reason."
    echo "Container logs:"
    docker logs "$keycloak_container" 2>&1 || true
  } >&2
  exit 1
fi

export AXIS_E2E_SERVER_LOG="$root/artifacts/logs/e2e-server.log"
export AXIS_E2E_JUNIT="$results_dir/playwright.xml"
export AXIS_E2E_WORKER_LOG="$root/artifacts/logs/e2e-worker.log"
: >"$AXIS_E2E_SERVER_LOG"
: >"$AXIS_E2E_WORKER_LOG"

export AXIS_E2E_DATABASE="Host=127.0.0.1;Port=$port;Database=axis;Username=axis;Password=$password"
export AXIS_E2E_KEYCLOAK_URL="http://127.0.0.1:$keycloak_port"

# The worker waits until the server, which Playwright starts, has migrated the database. It runs
# from the built dll, because dotnet run starts a child process that kill would miss.
# The loop starts the worker again only when a spec wrote the restart file before it stopped the
# worker, so a worker that exits on its own still ends the loop and fails the run. Each worker
# writes its pid to the pid file for the spec to stop. A worker stopped in the middle of a step
# keeps its claim until the lease runs out, so the lease is 5 seconds instead of 30, and a restart
# delays the other specs' work only briefly.
# The worker runs in Development, as the server does, because only Development lets a connector
# call the stub over http. It gets the same purchasing system bindings as the server.
run_worker() {
  local child
  while true; do
    DOTNET_ENVIRONMENT=Development Tenants__default__Hosts__0=127.0.0.1 \
      Tenants__default__ConnectionString="$AXIS_E2E_DATABASE" Worker__LeaseDuration=00:00:05 \
      Extensions__Path="$root/artifacts/extensions" \
      Tenants__default__Bindings__PurchasingSystemUrl__Kind=url \
      Tenants__default__Bindings__PurchasingSystemUrl__Value="$AXIS_E2E_PURCHASING_URL" \
      Tenants__default__Bindings__PurchasingSystemToken__Kind=secret \
      Tenants__default__Bindings__PurchasingSystemToken__Value=e2e-token \
      dotnet src/Axis.Worker/bin/Release/net10.0/Axis.Worker.dll >>"$AXIS_E2E_WORKER_LOG" 2>&1 &
    child=$!
    echo "$child" >"$AXIS_E2E_WORKER_PID_FILE"
    wait "$child" || true
    if [ ! -e "$AXIS_E2E_WORKER_RESTART_FILE" ]; then break; fi
    rm -f "$AXIS_E2E_WORKER_RESTART_FILE"
  done
}
run_worker &
worker_pid=$!

run_suite playwright tests/e2e npm test --prefix tests/e2e
playwright_status=$status

worker_failed=0
if ! kill -0 "$worker_pid" 2>/dev/null; then
  echo "The Axis worker exited before Playwright finished, with no restart requested." >&2
  worker_failed=1
elif ! grep -qF 'Tenant default is ready for work' "$AXIS_E2E_WORKER_LOG"; then
  echo "The Axis worker never found the E2E database migrated." >&2
  worker_failed=1
fi
if [ "$worker_failed" -ne 0 ]; then status=1; fi
stop_worker

# print_log TITLE FILE
print_log() {
  if [ "${GITHUB_ACTIONS:-}" = "true" ]; then
    echo "::group::$1 (last 100 lines)"
  else
    echo "$1 (last 100 lines of ${2#"$root/"}):"
  fi
  tail -n 100 "$2"
  if [ "${GITHUB_ACTIONS:-}" = "true" ]; then
    echo "::endgroup::"
  fi
}

if [ "$playwright_status" -ne 0 ]; then
  print_log "Axis server log" "$AXIS_E2E_SERVER_LOG"
fi
if [ "$playwright_status" -ne 0 ] || [ "$worker_failed" -ne 0 ]; then
  print_log "Axis worker log" "$AXIS_E2E_WORKER_LOG"
fi

run_suite_if_affected compose . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/compose.xml" scripts/compose.test.mjs

finish_report
