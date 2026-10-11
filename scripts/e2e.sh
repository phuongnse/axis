#!/usr/bin/env bash
# End-to-end tests: real server, real PostgreSQL and the built SPA in Chromium (requires Docker).
# The server starts with two applications active: the generic E2E test application in
# tests/e2e/fixtures/e2e-app and the purchase request sample in samples/apps/purchase-requests.
# The worker runs against the same database while Playwright runs, with a 5 second lease. A spec
# can restart it: it writes the restart file, then stops the worker process. The run fails if the
# worker exits without a restart request, or never finds the tenant database migrated.
# A Compose smoke test then builds the image and runs the stack on free ports. It is skipped
# when $NEXKIT_BASE_SHA shows that nothing it covers changed.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh

image="postgres:18-alpine"
container="axis-e2e-postgres-$$"
password="e2e-$RANDOM$RANDOM"

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
  docker rm -f "$container" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker run -d --name "$container" -e POSTGRES_USER=axis -e POSTGRES_DB=axis \
  -e POSTGRES_PASSWORD="$password" -p 127.0.0.1::5432 "$image" >/dev/null
port="$(docker port "$container" 5432/tcp | head -n1 | sed 's/.*://')"

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
export AXIS_E2E_SERVER_LOG="$root/artifacts/logs/e2e-server.log"
export AXIS_E2E_JUNIT="$results_dir/playwright.xml"
export AXIS_E2E_WORKER_LOG="$root/artifacts/logs/e2e-worker.log"
: >"$AXIS_E2E_SERVER_LOG"
: >"$AXIS_E2E_WORKER_LOG"

export AXIS_E2E_DATABASE="Host=127.0.0.1;Port=$port;Database=axis;Username=axis;Password=$password"

# The worker waits until the server, which Playwright starts, has migrated the database. It runs
# from the built dll, because dotnet run starts a child process that kill would miss.
# The loop starts the worker again only when a spec wrote the restart file before it stopped the
# worker, so a worker that exits on its own still ends the loop and fails the run. Each worker
# writes its pid to the pid file for the spec to stop. A worker stopped in the middle of a step
# keeps its claim until the lease runs out, so the lease is 5 seconds instead of 30, and a restart
# delays the other specs' work only briefly.
run_worker() {
  local child
  while true; do
    DOTNET_ENVIRONMENT=Testing Tenants__default__Hosts__0=127.0.0.1 \
      Tenants__default__ConnectionString="$AXIS_E2E_DATABASE" Worker__LeaseDuration=00:00:05 \
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
