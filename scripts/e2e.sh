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

for _ in $(seq 1 60); do
  if docker exec "$container" pg_isready -U axis -d axis >/dev/null 2>&1; then break; fi
  sleep 1
done
docker exec "$container" pg_isready -U axis -d axis >/dev/null

scripts/build.sh

export AXIS_E2E_DATABASE="Host=127.0.0.1;Port=$port;Database=axis;Username=axis;Password=$password"
npm test --prefix tests/e2e
