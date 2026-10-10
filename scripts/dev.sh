#!/usr/bin/env bash
# Runs Axis locally: PostgreSQL in Docker, the server under dotnet watch and the SPA on Vite,
# in one terminal. Ctrl+C stops the server and the SPA. PostgreSQL keeps running.
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
  export ConnectionStrings__Platform="$connection"
  export Tenants__default__ConnectionString="$connection"
fi

# Job control leaves SIGINT enabled for the background processes. Without it, a
# non-interactive bash starts them with SIGINT ignored. The jobs are disowned so that bash
# prints no status line when they stop.
set -m

state="$(mktemp -d)"
server=""
web=""

# Prefixes each line with the process name. It ignores SIGINT so it keeps draining output
# while the processes stop. It marks the server exit: when the app exits, dotnet watch
# waits for a file change instead of exiting.
prefix() {
  trap '' INT
  while IFS= read -r line || [ -n "$line" ]; do
    printf '[%s] %s\n' "$1" "$line"
    if [ "$1" = server ] && [[ $line == *"dotnet watch"*Exited* ]]; then
      : >"$state/server-exited"
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

# Stops both processes and everything below them. The app and Vite stop on SIGINT. dotnet
# watch does not: it waits for a file change after its app has stopped. So the wait covers
# the app and Vite only, and dotnet watch gets killed after that. The output prefixers
# are not waited for either, because they end when the pipes close. Safe to call twice.
stopped=0
stop() {
  [ "$stopped" -eq 0 ] || return 0
  stopped=1
  local pid command
  pids="" waiting=""
  for pid in $server $web; do
    pids="$pids $pid $(descendants "$pid")"
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

cleanup() {
  stop
  rm -rf "$state"
}
trap cleanup EXIT
trap 'log "Stopping"; stop; exit 130' INT TERM

# dotnet watch reads the terminal unless it is non-interactive. The launch profile sets
# Development, and --urls overrides its URL.
(cd src/Axis.Server && ASPNETCORE_ENVIRONMENT=Development DOTNET_WATCH_RESTART_ON_RUDE_EDIT=true \
  exec dotnet watch --non-interactive -- --urls "http://localhost:$server_port") \
  </dev/null > >(prefix server) 2>&1 &
server=$!
disown "$server"

(cd web && AXIS_SERVER_URL="http://localhost:$server_port" \
  exec node_modules/.bin/vite --port "$web_port" --strictPort) \
  </dev/null > >(prefix web) 2>&1 &
web=$!
disown "$web"

log "Server on http://localhost:$server_port, SPA on http://localhost:$web_port. Press Ctrl+C to stop."

code=0
while :; do
  if [ -e "$state/server-exited" ] || ! kill -0 "$server" 2>/dev/null; then
    log "The server exited. Stopping the SPA."
    code=1
    break
  fi
  if ! kill -0 "$web" 2>/dev/null; then
    log "The SPA dev server exited. Stopping the server."
    code=1
    break
  fi
  sleep 1
done
stop
exit "$code"
