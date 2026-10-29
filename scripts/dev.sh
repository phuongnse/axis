#!/usr/bin/env bash
# Runs Axis locally: PostgreSQL, Keycloak and the purchasing system stub in Docker, the server
# and the worker under dotnet watch, the publisher and the SPA on Vite, in one terminal. The server starts once
# PostgreSQL accepts connections, Keycloak's development realm answers and the stub answers. The
# server signs people in through that realm at /api/auth/sign-in. The SPA does not offer it yet. The realm signs
# people in on the Axis theme, which this script builds into artifacts/keycloak-theme before Keycloak starts.
# The worker and the publisher start once the server has migrated the tenant databases. The
# publisher runs the publish requests of Studio. A change to a JSON file
# in an application folder that the server activates at startup restarts the server, so that it
# activates the edited configuration. The worker, the publisher and the SPA keep running. Ctrl+C
# stops the server, the worker, the publisher and the SPA. PostgreSQL, Keycloak and the purchasing
# system stub keep running.
# The server runs startup activation as the database role axis_schema and serves requests as
# axis_runtime. The worker runs only as axis_runtime. The publisher runs its steps as axis_schema
# and seeds as axis_runtime. Docker Compose creates both roles. The publisher also gets the role
# axis_scenario, which has CREATEDB, to run test scenarios. The server turns on Studio's test controls
# through appsettings.Development.json.
# The server creates Studio's new applications in artifacts/studio-apps, which git ignores. An exported
# Studio__ApplicationsPath replaces that folder.
# The publisher runs from the built Axis.Admin.dll, not under dotnet watch, so a change to its code
# needs a restart of this script. The admin host reads no appsettings file, so this script sets the
# publisher's tenant settings as environment variables of its process only. It serves the tenant
# default and the preview tenant default-preview, so it runs preview requests too.
# The preview tenant default-preview serves at http://preview.localhost:5206 from the server and at
# http://preview.localhost:5173 through the SPA. It has its own database axis_preview and its own
# roles, axis_preview_schema and axis_preview_runtime, which Docker Compose creates, and no
# deployment bindings. The server activates both samples on it too.
# The sample extension package payment-references is built first, into artifacts/extensions,
# which the server and the worker read through Extensions:Path. The sample modules under
# samples/modules are installed into artifacts/modules, which they read through Modules:Path. A
# change to a module needs a restart of this script. The sample templates under samples/templates are
# installed into artifacts/templates, which the server reads through Templates:Path, and a template
# change needs a restart too.
# AXIS_POSTGRES_PORT, AXIS_KEYCLOAK_PORT, AXIS_PURCHASING_PORT, AXIS_SERVER_PORT and
# AXIS_WEB_PORT move the ports (5432, 8080, 5207, 5206, 5173).
# Works with the bash 3.2 that ships with macOS.
set -euo pipefail
cd "$(dirname "$0")/.."

postgres_port="${AXIS_POSTGRES_PORT:-5432}"
keycloak_port="${AXIS_KEYCLOAK_PORT:-8080}"
purchasing_port="${AXIS_PURCHASING_PORT:-5207}"
server_port="${AXIS_SERVER_PORT:-5206}"
web_port="${AXIS_WEB_PORT:-5173}"
studio_apps="${Studio__ApplicationsPath:-$PWD/artifacts/studio-apps}"

log() { printf '[dev] %s\n' "$*"; }

if ! command -v docker >/dev/null 2>&1; then
  log "Docker is not installed. Install Docker, then run scripts/dev.sh again." >&2
  exit 1
fi
if ! docker info >/dev/null 2>&1; then
  log "Docker is not running. Start Docker, then run scripts/dev.sh again." >&2
  exit 1
fi

# The copy of the lock file in node_modules records what the last install used.
marker=web/node_modules/.axis-dev-package-lock.json
if [ ! -d web/node_modules ] || ! cmp -s web/package-lock.json "$marker"; then
  log "Installing SPA packages"
  npm ci --prefix web
  cp web/package-lock.json "$marker"
fi

# Keycloak's compose service mounts the theme from artifacts/keycloak-theme.
log "Building the Keycloak theme"
npm run build:keycloak-theme --prefix web

# The health checks make --wait return once PostgreSQL accepts connections, the realm answers
# and the purchasing system stub answers. --build rebuilds the stub's image when it changed.
log "Starting PostgreSQL on port $postgres_port, Keycloak on port $keycloak_port and the purchasing system stub on port $purchasing_port"
AXIS_POSTGRES_PORT="$postgres_port" AXIS_KEYCLOAK_PORT="$keycloak_port" AXIS_PURCHASING_PORT="$purchasing_port" \
  docker compose up -d --build --wait --wait-timeout 180 postgres keycloak purchasing
schema_connection=""
# Bash cannot export a name with a hyphen, like Tenants__default-preview__ConnectionString. These
# overrides reach the server and the worker through env instead.
server_env=()
worker_env=()
if [ -n "${AXIS_POSTGRES_PORT:-}" ]; then
  runtime_connection="Host=localhost;Port=$AXIS_POSTGRES_PORT;Database=axis;Username=axis_runtime;Password=axis"
  # The worker reads the same tenant connection string, so this export moves it too.
  export ConnectionStrings__Platform="$runtime_connection"
  export Tenants__default__ConnectionString="$runtime_connection"
  # Only the server reads the schema connection string, and startup activation and the platform
  # database use it. It is not exported at the top, so the worker never holds the schema credentials.
  schema_connection="Host=localhost;Port=$AXIS_POSTGRES_PORT;Database=axis;Username=axis_schema;Password=axis"
  # The preview tenant has its own database and roles. The worker gets only its runtime connection string.
  preview_runtime="Host=localhost;Port=$AXIS_POSTGRES_PORT;Database=axis_preview;Username=axis_preview_runtime;Password=axis"
  preview_schema="Host=localhost;Port=$AXIS_POSTGRES_PORT;Database=axis_preview;Username=axis_preview_schema;Password=axis"
  server_env+=("Tenants__default-preview__ConnectionString=$preview_runtime" "Tenants__default-preview__SchemaConnectionString=$preview_schema")
  worker_env+=("Tenants__default-preview__ConnectionString=$preview_runtime")
fi
if [ -n "${AXIS_KEYCLOAK_PORT:-}" ]; then
  # The server signs people in at the moved Keycloak.
  export Tenants__default__Oidc__Authority="http://localhost:$AXIS_KEYCLOAK_PORT/realms/axis"
fi
if [ -n "${AXIS_PURCHASING_PORT:-}" ]; then
  # The server checks this binding when it activates the sample, and the worker calls it.
  export Tenants__default__Bindings__PurchasingSystemUrl__Value="http://localhost:$AXIS_PURCHASING_PORT"
fi

# Job control leaves SIGINT enabled for the background processes. Without it, a
# non-interactive bash starts them with SIGINT ignored. The jobs are disowned so that bash
# prints no status line when they stop.
set -m

# The application folders that the server activates at startup. A change to a JSON file in
# them restarts the server. dotnet watch does not watch these files itself.
folders=()
while IFS= read -r folder; do folders+=("$folder"); done < <(node scripts/lib/dev-watch-folders.mjs)

# A checksum of the names and contents of the JSON files in the application folders. Saving a
# file without changing it leaves it the same. It is empty when there are no folders.
fingerprint() {
  [ ${#folders[@]} -gt 0 ] || return 0
  find "${folders[@]}" -type f -name '*.json' -exec cksum {} + 2>/dev/null | LC_ALL=C sort | cksum || true
}

state="$(mktemp -d)"
server=""
worker=""
publisher=""
web=""

# Prefixes each line with the process name. It ignores SIGINT so it keeps draining output
# while the processes stop. It marks when the server listens, which it does only after it has
# migrated the tenant databases. It marks the server and worker exits in the file named by
# the optional second argument, which is $state/NAME-exited by default. Each server start has
# its own file, so a dotnet watch that is being stopped cannot end the session. When the app exits,
# dotnet watch prints "Exited" and then waits for a file change instead of exiting. When it
# restarts the app after an edit that hot reload cannot apply, it prints "Exited" too, and
# then builds. So only an "Exited" line that the waiting line follows at once counts as an exit.
prefix() {
  trap '' INT
  local exited=0
  while IFS= read -r line || [ -n "$line" ]; do
    printf '[%s] %s\n' "$1" "$line"
    if [ "$1" = server ] && [[ $line == *"Now listening on"* ]]; then
      : >"$state/server-listening"
    fi
    if [ "$1" != web ]; then
      if [ "$exited" -eq 1 ] && [[ $line == *"Waiting for a file to change before restarting"* ]]; then
        : >"${2:-$state/$1-exited}"
      fi
      exited=0
      if [[ $line == *"dotnet watch"*Exited* ]]; then exited=1; fi
    fi
  done
}

descendants() {
  local child
  for child in $(pgrep -P "$1" 2>/dev/null || true); do
    echo "$child"
    descendants "$child"
  done
}

# signal_and_wait SIGNAL SECONDS
# Sends the signal to every pid in $pids, then waits until every pid in $waiting has exited,
# or the time is up.
signal_and_wait() {
  local pid i alive
  for pid in $pids; do kill "-$1" "$pid" 2>/dev/null || true; done
  for i in $(seq 1 $(($2 * 4))); do
    alive=0
    for pid in $waiting; do
      if kill -0 "$pid" 2>/dev/null; then alive=1; fi
    done
    [ "$alive" -eq 1 ] || return 0
    sleep 0.25
  done
}

# stop_processes ROOT...
# Stops the processes and everything below them. The apps and Vite stop on SIGINT. dotnet
# watch does not: it waits for a file change after its app has stopped. So the wait covers
# the apps and Vite only, and dotnet watch gets killed after that. The output prefixers
# are not waited for either, because they end when the pipes close.
stop_processes() {
  local pid command root
  pids="" waiting=""
  for root in "$@"; do
    pids="$pids $root $(descendants "$root")"
  done
  for pid in $pids; do
    command="$(ps -o args= -p "$pid" 2>/dev/null || true)"
    case "$command" in
      *dotnet-watch* | *"dotnet watch"* | *dev.sh*) ;;
      *) waiting="$waiting $pid" ;;
    esac
  done
  signal_and_wait INT 10
  signal_and_wait TERM 5
  for pid in $pids; do kill -KILL "$pid" 2>/dev/null || true; done
}

# Stops the server, the worker, the publisher and the SPA. Safe to call twice.
stopped=0
stop() {
  [ "$stopped" -eq 0 ] || return 0
  stopped=1
  stop_processes $server $worker $publisher $web
}

cleanup() {
  # A failure here must not replace the exit code. A stopping prefixer can write a marker into the state folder.
  set +e
  stop
  rm -rf "$state"
}
trap cleanup EXIT
# The purchase request sample declares this package, so the server cannot activate it without the
# build in artifacts/extensions. A fresh clone has none. A change to the package's C# code needs a
# restart of this script.
log "Building the sample extension package"
dotnet build samples/packages/PaymentReferences/PaymentReferences.csproj --nologo -v quiet

# The server reads the module catalog only when it starts, so this runs once, before the server.
log "Installing the sample modules"
bash scripts/lib/install-sample-modules.sh

# The server reads the template catalog only when it starts, so this runs once, before the server.
log "Installing the sample templates"
bash scripts/lib/install-sample-templates.sh

# The publisher runs from this build. dotnet watch would build the shared projects at the same time as
# the worker's and risk file-lock failures, so it is built here, before the server starts.
log "Building the publisher"
dotnet build src/Axis.Admin/Axis.Admin.csproj --nologo -v quiet

# A stop signal only sets a flag. The main loop stops everything, so a signal cannot arrive
# between starting a process and saving its pid.
interrupted=0
trap 'interrupted=1' INT TERM

# Starts the server under dotnet watch. dotnet watch reads the terminal unless it is
# non-interactive. The launch profile sets Development, and --urls overrides its URL. Each
# start counts as a generation, with its own exit marker.
generation=0
start_server() {
  generation=$((generation + 1))
  # With the default port the server reads the schema connection string from appsettings.Development.json.
  (cd src/Axis.Server && export ASPNETCORE_ENVIRONMENT=Development DOTNET_WATCH_RESTART_ON_RUDE_EDIT=true Studio__ApplicationsPath="$studio_apps"
    if [ -n "$schema_connection" ]; then
      export Tenants__default__SchemaConnectionString="$schema_connection" ConnectionStrings__PlatformSchema="$schema_connection"
    fi
    exec env ${server_env[@]+"${server_env[@]}"} dotnet watch --non-interactive -- --urls "http://localhost:$server_port") \
    </dev/null > >(prefix server "$state/server-exited-$generation") 2>&1 &
  server=$!
  # A stop signal can end a job before it is disowned. Then disown fails, and that must not end the script.
  disown "$server" 2>/dev/null || true
}

watched="$(fingerprint)"
start_server

(cd web && AXIS_SERVER_URL="http://localhost:$server_port" \
  exec node_modules/.bin/vite --port "$web_port" --strictPort) \
  </dev/null > >(prefix web) 2>&1 &
web=$!
disown "$web" 2>/dev/null || true

log "Server on http://localhost:$server_port, preview on http://preview.localhost:$server_port, SPA on http://localhost:$web_port (preview on http://preview.localhost:$web_port), Keycloak on http://localhost:$keycloak_port, purchasing system stub on http://localhost:$purchasing_port. Press Ctrl+C to stop."

code=0
while :; do
  if [ "$interrupted" -eq 1 ]; then
    log "Stopping"
    code=130
    break
  fi
  if [ -e "$state/server-exited-$generation" ] || ! kill -0 "$server" 2>/dev/null; then
    log "The server exited. Stopping the worker, the publisher and the SPA."
    code=1
    break
  fi
  if ! kill -0 "$web" 2>/dev/null; then
    log "The SPA dev server exited. Stopping the server, the worker and the publisher."
    code=1
    break
  fi
  if [ -n "$worker" ] && { [ -e "$state/worker-exited" ] || ! kill -0 "$worker" 2>/dev/null; }; then
    log "The worker exited. Stopping the server, the publisher and the SPA."
    code=1
    break
  fi
  if [ -n "$publisher" ] && ! kill -0 "$publisher" 2>/dev/null; then
    log "The publisher exited. Stopping the server, the worker and the SPA."
    code=1
    break
  fi
  # Restart the server when the application configuration changes. An editor or git may write
  # several files in a row, so wait until two checks 0.5 seconds apart agree. The worker and the
  # publisher keep running, because they need only the migrated databases.
  # A signal sent to the process group also ends the fingerprint subshell, which must not count
  # as a change.
  if [ "$(fingerprint)" != "$watched" ] && [ "$interrupted" -eq 0 ]; then
    current="$(fingerprint)"
    while sleep 0.5; do
      latest="$(fingerprint)"
      [ "$latest" != "$current" ] || break
      current="$latest"
    done
    log "Application configuration changed. Restarting the server."
    stop_processes $server
    watched="$current"
    start_server
    continue
  fi
  # The worker waits for the migrations itself, but starting it later keeps its output quiet. It
  # is not restarted with the server, because it needs only the migrated databases.
  if [ -z "$worker" ] && [ -e "$state/server-listening" ]; then
    log "The server has migrated the tenant databases. Starting the worker."
    (cd src/Axis.Worker && DOTNET_WATCH_RESTART_ON_RUDE_EDIT=true \
      exec env ${worker_env[@]+"${worker_env[@]}"} dotnet watch --non-interactive) \
      </dev/null > >(prefix worker) 2>&1 &
    worker=$!
    disown "$worker" 2>/dev/null || true
  fi
  # The publisher waits for the migrations too, and is not restarted with the server. Its settings are
  # set in this subshell only: the schema role is for its steps, and the runtime role is for seeding.
  if [ -z "$publisher" ] && [ -e "$state/server-listening" ]; then
    log "The server has migrated the tenant databases. Starting the publisher."
    # Bash cannot export a name with a hyphen, so the settings go through env. The realm settings only let the
    # tenant catalog pass validation: the server alone brings the realm in step.
    (exec env DOTNET_ENVIRONMENT=Development \
      Extensions__Path="$PWD/artifacts/extensions" \
      Modules__Path="$PWD/artifacts/modules" \
      Tenants__default__Hosts__0=localhost \
      Tenants__default__Oidc__Authority="http://localhost:$keycloak_port/realms/axis" \
      Tenants__default__Realm__Name=axis \
      Tenants__default__Realm__ActivationClientSecret=axis-activation-development-secret \
      Tenants__default__ConnectionString="Host=localhost;Port=$postgres_port;Database=axis;Username=axis_runtime;Password=axis" \
      Tenants__default__SchemaConnectionString="Host=localhost;Port=$postgres_port;Database=axis;Username=axis_schema;Password=axis" \
      Tenants__default__Bindings__PurchasingSystemUrl__Kind=url \
      Tenants__default__Bindings__PurchasingSystemUrl__Value="http://localhost:$purchasing_port" \
      Tenants__default__Bindings__PurchasingSystemToken__Kind=secret \
      Tenants__default__Bindings__PurchasingSystemToken__Value=axis-development-purchasing-token \
      'Tenants__default-preview__PreviewOf=default' \
      'Tenants__default-preview__Hosts__0=preview.localhost' \
      "Tenants__default-preview__ConnectionString=Host=localhost;Port=$postgres_port;Database=axis_preview;Username=axis_preview_runtime;Password=axis" \
      "Tenants__default-preview__SchemaConnectionString=Host=localhost;Port=$postgres_port;Database=axis_preview;Username=axis_preview_schema;Password=axis" \
      "Scenarios__ServerConnectionString=Host=localhost;Port=$postgres_port;Database=postgres;Username=axis_scenario;Password=axis" \
      dotnet src/Axis.Admin/bin/Debug/net10.0/Axis.Admin.dll publisher) \
      </dev/null > >(prefix publisher) 2>&1 &
    publisher=$!
    disown "$publisher" 2>/dev/null || true
  fi
  sleep 1 || true
done
stop
exit "$code"
