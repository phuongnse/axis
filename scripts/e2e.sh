#!/usr/bin/env bash
# End-to-end tests: real server, real PostgreSQL and the built SPA in Chromium (requires Docker).
set -euo pipefail
cd "$(dirname "$0")/.."

image="postgres:18-alpine"
container="axis-e2e-postgres-$$"
password="e2e-$RANDOM$RANDOM"

cleanup() { docker rm -f "$container" >/dev/null 2>&1 || true; }
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

export AXIS_E2E_DATABASE="Host=127.0.0.1;Port=$port;Database=axis;Username=axis;Password=$password"
npm test --prefix tests/e2e
