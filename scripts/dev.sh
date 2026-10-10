#!/usr/bin/env bash
# Runs Axis locally: PostgreSQL in Docker, the server and the worker under dotnet watch and the
# SPA on Vite, in one terminal. The worker starts once the server has migrated the tenant
# databases. A change to a JSON file in an application folder that the server activates at
# startup restarts the server, so that it activates the edited configuration. The worker and
# the SPA keep running. Ctrl+C stops the server, the worker and the SPA. PostgreSQL keeps
# running.
# AXIS_POSTGRES_PORT, AXIS_SERVER_PORT and AXIS_WEB_PORT move the ports (5432, 5206, 5173).
# Works with the bash 3.2 that ships with macOS.
set -euo pipefail
cd "$(dirname "$0")/.."

postgres_port="${AXIS_POSTGRES_PORT:-5432}"
server_port="${AXIS_SERVER_PORT:-5206}"
web_port="${AXIS_WEB_PORT:-5173}"

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

log "Starting PostgreSQL on port $postgres_port"
AXIS_POSTGRES_PORT="$postgres_port" docker compose up -d --wait --wait-timeout 120 postgres
if [ -n "${AXIS_POSTGRES_PORT:-}" ]; then
  connection="Host=localhost;Port=$AXIS_POSTGRES_PORT;Database=axis;Username=axis;Password=axis"
  # The worker reads the same tenant connection string, so this export moves it too.
  export ConnectionStrings__Platform="$connection"
  export Tenants__default__ConnectionString="$connection"
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

# Stops the server, the worker and the SPA. Safe to call twice.
stopped=0
stop() {
  [ "$stopped" -eq 0 ] || return 0
  stopped=1
  stop_processes $server $worker $web
}

cleanup() {
  stop
  rm -rf "$state"
}
trap cleanup EXIT
trap 'log "Stopping"; stop; exit 130' INT TERM

# Starts the server under dotnet watch. dotnet watch reads the terminal unless it is
# non-interactive. The launch profile sets Development, and --urls overrides its URL. Each
# start counts as a generation, with its own exit marker.
generation=0
start_server() {
  generation=$((generation + 1))
  (cd src/Axis.Server && ASPNETCORE_ENVIRONMENT=Development DOTNET_WATCH_RESTART_ON_RUDE_EDIT=true \
    exec dotnet watch --non-interactive -- --urls "http://localhost:$server_port") \
    </dev/null > >(prefix server "$state/server-exited-$generation") 2>&1 &
  server=$!
  disown "$server"
}

watched="$(fingerprint)"
start_server

(cd web && AXIS_SERVER_URL="http://localhost:$server_port" \
  exec node_modules/.bin/vite --port "$web_port" --strictPort) \
  </dev/null > >(prefix web) 2>&1 &
web=$!
disown "$web"

log "Server on http://localhost:$server_port, SPA on http://localhost:$web_port. Press Ctrl+C to stop."

code=0
while :; do
  if [ -e "$state/server-exited-$generation" ] || ! kill -0 "$server" 2>/dev/null; then
    log "The server exited. Stopping the worker and the SPA."
    code=1
    break
  fi
  if ! kill -0 "$web" 2>/dev/null; then
    log "The SPA dev server exited. Stopping the server and the worker."
    code=1
    break
  fi
  if [ -n "$worker" ] && { [ -e "$state/worker-exited" ] || ! kill -0 "$worker" 2>/dev/null; }; then
    log "The worker exited. Stopping the server and the SPA."
    code=1
    break
  fi
  # Restart the server when the application configuration changes. An editor or git may write
  # several files in a row, so wait until two checks 0.5 seconds apart agree. The worker keeps
  # running, because it needs only the migrated databases.
  if [ "$(fingerprint)" != "$watched" ]; then
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
      exec dotnet watch --non-interactive) \
      </dev/null > >(prefix worker) 2>&1 &
    worker=$!
    disown "$worker"
  fi
  sleep 1
done
stop
exit "$code"
