#!/usr/bin/env bash
# End-to-end tests: real server, real PostgreSQL and the built SPA in Chromium (requires Docker).
# Keycloak runs with the development realm from samples/keycloak, and Playwright starts the
# server only once the realm answers. The server signs people in through that realm at
# /api/auth/sign-in. The SPA does not offer it yet.
# The server starts with four applications active: the generic E2E test application in
# tests/e2e/fixtures/e2e-app, the purchase request sample in samples/apps/purchase-requests, the
# leave request sample in samples/apps/leave-requests and the service desk sample in
# samples/apps/service-desk.
# A spec can activate another folder during the run: tests/e2e/activate.ts starts the built server
# once more on a spare port with that folder in ActivateOnStartup, waits until it is ready and
# stops it. The main server then serves the new release. The release-pinning spec activates
# tests/e2e/fixtures/release-app that way. The purchase request release spec activates a changed copy
# of the purchase request sample, and activates the committed sample again when it ends.
# The main E2E server's Studio edits a copy of the purchase request sample, which this script makes in a temporary
# folder for each run and deletes on exit, so a saved resource never changes a file under samples/. No spec writes to
# that copy.
# The worker runs against the same database while Playwright runs, with a 5 second lease. A spec
# can restart it: it writes the restart file, then stops the worker process. The run fails if the
# worker exits without a restart request, or never finds the tenant database migrated.
# One publisher, `axis-admin publisher`, runs against the same database and writes
# artifacts/logs/e2e-publisher.log. It waits until the server has migrated the database, then runs the
# publish requests of Studio. The run fails if it exits during the run or never finds the database migrated.
# The purchase request release spec also holds the Studio journey: edit, validate, preview on localhost, publish and
# rollback, all through the publisher and with no admin command.
# The purchasing system stub from tests/Axis.PurchasingStub runs from the build output on
# $AXIS_E2E_PURCHASING_PORT (default 5281). Specs find it at $AXIS_E2E_PURCHASING_URL. The run
# fails if it never answers. The server and the worker run in Development, because only
# Development lets a connector call the stub over http. Their purchasing system bindings point at
# the stub, so an approved purchase request creates its purchase order there.
# PostgreSQL loads samples/postgres/init-roles.sql on its first start, which creates the database
# roles axis_schema and axis_runtime. The server runs startup activation as axis_schema and serves
# requests as axis_runtime. The worker runs only as axis_runtime and gets no schema connection string.
# The same file creates the preview tenant's database axis_preview and its roles axis_preview_schema and
# axis_preview_runtime. The server and the worker serve the preview tenant default-preview on localhost,
# while the main tenant stays on 127.0.0.1, so one server serves both. The preview tenant has no
# deployment bindings, and the worker gets only its runtime connection string.
# The same file creates axis_scenario, a role with CREATEDB that owns no database. Only the publisher gets it, as
# Scenarios__ServerConnectionString, to run test scenarios, and the server sets Studio__Tests=true.
# A third tenant, journey, is for Studio journeys. This script creates its database axis_journey and its roles
# axis_journey_schema and axis_journey_runtime through psql, because the init script is shared with Compose. A
# second server serves it on 127.0.0.2, in the Testing environment, with Studio:ApplicationsPath set to a
# temporary folder. Only the empty application tests/e2e/fixtures/journey-base is active at start, because only
# startup activation migrates a tenant database. Playwright starts that server after the main one, which
# shares the platform database. The worker and the publisher serve the journey tenant too, and the run fails if
# either log never reports it ready. The setup step signs the author, the requester, the service desk lead and
# the agent in on its host.
# A Compose smoke test then builds the image and runs the stack on free ports. An images smoke test
# then builds the production images axis-server, axis-worker and axis-admin and runs them read-only
# against a throwaway PostgreSQL. An on-premises smoke test then installs deploy/compose/compose.yaml
# from the production images as docs/reference/installation.md says, against a test PostgreSQL and Keycloak over
# HTTPS. It creates two tenants, activates the leave request sample, checks each tenant's site and
# sign-in, suspend and resume, and an unknown host. Each is skipped when $NEXKIT_BASE_SHA shows that
# nothing it covers changed. Playwright and the services it starts are skipped when every changed path
# is under docs/ or is a Markdown file. The sample modules are still installed then, for the on-premises
# smoke test.
# The design-system preview page is built into artifacts/design-system-app before Playwright runs. Its spec serves
# that build from disk and needs no server.
# Arguments go to Playwright, for example tests/e2e/specs/studio.spec.ts or --grep 'publishes'. A spec path
# is relative to the repository root. With arguments only the matching specs run, after the setup project,
# and the three smoke tests do not run. A change is checked in minutes, and the full run stays the check.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh

# Playwright runs in tests/e2e, so a spec path given from the repository root is made relative to it.
playwright_args=()
for argument in "$@"; do
  case "$argument" in
    tests/e2e/*) playwright_args+=("${argument#tests/e2e/}") ;;
    *) playwright_args+=("$argument") ;;
  esac
done

image="postgres:18-alpine"
container="axis-e2e-postgres-$$"
password="e2e-$RANDOM$RANDOM"
keycloak_image="quay.io/keycloak/keycloak:26.4"
keycloak_container="axis-e2e-keycloak-$$"

purchasing_pid=""
publisher_pid=""
worker_pid=""
worker_dir="$(mktemp -d)"
export AXIS_E2E_WORKER_PID_FILE="$worker_dir/worker.pid"
export AXIS_E2E_WORKER_RESTART_FILE="$worker_dir/restart"
studio_dir="$(mktemp -d)"
cp -R samples/apps/purchase-requests "$studio_dir/purchase-requests"
export AXIS_E2E_STUDIO_FOLDER="$studio_dir/purchase-requests"
# Studio creates the journey tenant's applications folder when its server starts.
journey_dir="$(mktemp -d)"
export AXIS_E2E_JOURNEY_APPLICATIONS="$journey_dir/applications"

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
  if [ -n "$publisher_pid" ]; then
    kill "$publisher_pid" 2>/dev/null || true
    wait "$publisher_pid" 2>/dev/null || true
  fi
  rm -rf "$studio_dir" "$journey_dir"
  if [ -n "$purchasing_pid" ]; then
    kill "$purchasing_pid" 2>/dev/null || true
    wait "$purchasing_pid" 2>/dev/null || true
  fi
  docker rm -f "$container" >/dev/null 2>&1 || true
  docker rm -f "$keycloak_container" >/dev/null 2>&1 || true
}
trap cleanup EXIT

if affected playwright; then
  docker run -d --name "$container" -e POSTGRES_USER=axis -e POSTGRES_DB=axis \
    -e POSTGRES_PASSWORD="$password" -p 127.0.0.1::5432 \
    -v "$PWD/samples/postgres/init-roles.sql:/docker-entrypoint-initdb.d/init-roles.sql:ro" "$image" >/dev/null
  port="$(docker port "$container" 5432/tcp | head -n1 | sed 's/.*://')"

  # Keycloak starts now and imports the realm while PostgreSQL and the build get ready.
  docker run -d --name "$keycloak_container" \
    -e KC_BOOTSTRAP_ADMIN_USERNAME=admin -e KC_BOOTSTRAP_ADMIN_PASSWORD=admin \
    -v "$PWD/samples/keycloak:/opt/keycloak/data/import:ro" -p 127.0.0.1::8080 \
    "$keycloak_image" start-dev --import-realm >/dev/null
  keycloak_port="$(docker port "$keycloak_container" 8080/tcp | head -n1 | sed 's/.*://')"

  # Check over TCP: the image's initialization server listens only on the Unix socket, so the check
  # succeeds only after the init scripts have run.
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

  # The journey tenant's database and roles. CREATE DATABASE cannot run in a multi-statement transaction, so each
  # statement has its own -c.
  docker exec "$container" psql -U axis -d axis -v ON_ERROR_STOP=1 \
    -c "CREATE ROLE axis_journey_schema LOGIN PASSWORD 'axis'" \
    -c "CREATE ROLE axis_journey_runtime LOGIN PASSWORD 'axis'" \
    -c "CREATE DATABASE axis_journey OWNER axis_journey_schema" >/dev/null

  scripts/build.sh
  # The design-system spec draws the preview build, which the server never ships.
  npm run build:design-system --prefix web

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
  export AXIS_E2E_JOURNEY_SERVER_LOG="$root/artifacts/logs/e2e-journey-server.log"
  publisher_log="$root/artifacts/logs/e2e-publisher.log"
  : >"$AXIS_E2E_SERVER_LOG"
  : >"$AXIS_E2E_WORKER_LOG"
  : >"$AXIS_E2E_JOURNEY_SERVER_LOG"
  : >"$publisher_log"

  # The init script gives both roles the development password axis. The bootstrap superuser axis keeps $password.
  export AXIS_E2E_DATABASE="Host=127.0.0.1;Port=$port;Database=axis;Username=axis_runtime;Password=axis"
  # Only Playwright, which starts the server, gets the schema role. It is not exported here, so the
  # worker below never inherits it.
  schema_database="Host=127.0.0.1;Port=$port;Database=axis;Username=axis_schema;Password=axis"
  # The preview tenant has its own database and roles. Only Playwright gets its schema role as well.
  export AXIS_E2E_PREVIEW_DATABASE="Host=127.0.0.1;Port=$port;Database=axis_preview;Username=axis_preview_runtime;Password=axis"
  preview_schema_database="Host=127.0.0.1;Port=$port;Database=axis_preview;Username=axis_preview_schema;Password=axis"
  # The journey tenant has its own database and roles too. Only Playwright gets its schema role.
  export AXIS_E2E_JOURNEY_DATABASE="Host=127.0.0.1;Port=$port;Database=axis_journey;Username=axis_journey_runtime;Password=axis"
  journey_schema_database="Host=127.0.0.1;Port=$port;Database=axis_journey;Username=axis_journey_schema;Password=axis"
  # The worker and the publisher reject two tenants that share a host, so the journey tenant takes the loopback
  # address 127.0.0.2, which needs no DNS.
  export AXIS_E2E_JOURNEY_URL="http://127.0.0.2:${AXIS_E2E_JOURNEY_PORT:-5282}"
  export AXIS_E2E_KEYCLOAK_URL="http://127.0.0.1:$keycloak_port"
  # The role spec asks PostgreSQL for the roles in this container.
  export AXIS_E2E_POSTGRES_CONTAINER="$container"
  # The role that creates the databases of test scenarios. Only the publisher gets it, below, and it connects to
  # the postgres maintenance database because it owns no database.
  scenario_database="Host=127.0.0.1;Port=$port;Database=postgres;Username=axis_scenario;Password=axis"

  # The worker waits until the server, which Playwright starts, has migrated the database. It runs
  # from the built dll, because dotnet run starts a child process that kill would miss.
  # The loop starts the worker again only when a spec wrote the restart file before it stopped the
  # worker, so a worker that exits on its own still ends the loop and fails the run. Each worker
  # writes its pid to the pid file for the spec to stop. A worker stopped in the middle of a step
  # keeps its claim until the lease runs out, so the lease is 5 seconds instead of 30, and a restart
  # delays the other specs' work only briefly.
  # The worker runs in Development, as the server does, because only Development lets a connector
  # call the stub over http. It gets the same purchasing system bindings as the server.
  # Its health endpoints listen on a free port, so a worker under scripts/dev.sh does not hold the address.
  # Bash cannot export a name with a hyphen, so the preview tenant's settings go through env.
  # It serves the journey tenant with only its runtime connection string and no bindings.
  run_worker() {
    local child
    while true; do
      env 'Tenants__default-preview__PreviewOf=default' 'Tenants__default-preview__Hosts__0=localhost' \
        "Tenants__default-preview__ConnectionString=$AXIS_E2E_PREVIEW_DATABASE" \
        Tenants__journey__Hosts__0=127.0.0.2 Tenants__journey__ConnectionString="$AXIS_E2E_JOURNEY_DATABASE" \
        DOTNET_ENVIRONMENT=Development Health__Urls=http://127.0.0.1:0 Tenants__default__Hosts__0=127.0.0.1 \
        Tenants__default__ConnectionString="$AXIS_E2E_DATABASE" Worker__LeaseDuration=00:00:05 \
        Extensions__Path="$root/artifacts/extensions" Modules__Path="$root/artifacts/modules" \
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

  # The publisher waits for the server's migration as the worker does. The admin host reads no appsettings
  # file, so the tenant settings are here. It gets the schema role for its steps and the runtime role for
  # seeding, and runs in Development for the http stub, as the worker does. It runs from the built dll, so
  # that kill stops it. It serves the preview tenant default-preview too, so it runs preview requests, and the
  # journey tenant. Bash
  # cannot export a name with a hyphen, so the preview tenant's settings go through env.
  env 'Tenants__default-preview__PreviewOf=default' 'Tenants__default-preview__Hosts__0=localhost' \
    "Tenants__default-preview__ConnectionString=$AXIS_E2E_PREVIEW_DATABASE" \
    "Tenants__default-preview__SchemaConnectionString=$preview_schema_database" \
    Tenants__journey__Hosts__0=127.0.0.2 Tenants__journey__ConnectionString="$AXIS_E2E_JOURNEY_DATABASE" \
    Tenants__journey__SchemaConnectionString="$journey_schema_database" \
    DOTNET_ENVIRONMENT=Development Tenants__default__Hosts__0=127.0.0.1 \
    Tenants__default__ConnectionString="$AXIS_E2E_DATABASE" \
    Tenants__default__SchemaConnectionString="$schema_database" \
    Scenarios__ServerConnectionString="$scenario_database" \
    Extensions__Path="$root/artifacts/extensions" Modules__Path="$root/artifacts/modules" \
    Tenants__default__Bindings__PurchasingSystemUrl__Kind=url \
    Tenants__default__Bindings__PurchasingSystemUrl__Value="$AXIS_E2E_PURCHASING_URL" \
    Tenants__default__Bindings__PurchasingSystemToken__Kind=secret \
    Tenants__default__Bindings__PurchasingSystemToken__Value=e2e-token \
    dotnet src/Axis.Admin/bin/Release/net10.0/Axis.Admin.dll publisher >>"$publisher_log" 2>&1 &
  publisher_pid=$!


  AXIS_E2E_SCHEMA_DATABASE="$schema_database" AXIS_E2E_PREVIEW_SCHEMA_DATABASE="$preview_schema_database" \
    AXIS_E2E_JOURNEY_SCHEMA_DATABASE="$journey_schema_database" \
    run_suite playwright tests/e2e npm test --prefix tests/e2e -- "${playwright_args[@]}"
  playwright_status=$status

  worker_failed=0
  if ! kill -0 "$worker_pid" 2>/dev/null; then
    echo "The Axis worker exited before Playwright finished, with no restart requested." >&2
    worker_failed=1
  elif ! grep -qF 'Tenant default is ready for work' "$AXIS_E2E_WORKER_LOG"; then
    echo "The Axis worker never found the E2E database migrated." >&2
    worker_failed=1
  elif ! grep -qF 'Tenant journey is ready for work' "$AXIS_E2E_WORKER_LOG"; then
    echo "The Axis worker never found the journey tenant database migrated." >&2
    worker_failed=1
  fi
  if [ "$worker_failed" -ne 0 ]; then status=1; fi
  stop_worker

  publisher_failed=0
  if ! kill -0 "$publisher_pid" 2>/dev/null; then
    echo "The Axis publisher exited before Playwright finished." >&2
    publisher_failed=1
  elif ! grep -qF 'default ready' "$publisher_log"; then
    echo "The Axis publisher never found the E2E database migrated." >&2
    publisher_failed=1
  elif ! grep -qF 'journey ready' "$publisher_log"; then
    echo "The Axis publisher never found the journey tenant database migrated." >&2
    publisher_failed=1
  fi
  if [ "$publisher_failed" -ne 0 ]; then status=1; fi

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
    print_log "Axis journey server log" "$AXIS_E2E_JOURNEY_SERVER_LOG"
  fi
  if [ "$playwright_status" -ne 0 ] || [ "$worker_failed" -ne 0 ]; then
    print_log "Axis worker log" "$AXIS_E2E_WORKER_LOG"
  fi
  if [ "$playwright_status" -ne 0 ] || [ "$publisher_failed" -ne 0 ]; then
    print_log "Axis publisher log" "$publisher_log"
  fi
else
  bash scripts/lib/install-sample-modules.sh
  skip_suite playwright tests/e2e
fi

if [ "$#" -gt 0 ]; then
  finish_report
fi

run_suite_if_affected compose . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/compose.xml" scripts/compose.test.mjs

run_suite_if_affected images . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/images.xml" scripts/images.test.mjs

run_suite_if_affected onprem . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/onprem.xml" scripts/onprem.test.mjs

finish_report
