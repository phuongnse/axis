#!/bin/sh
set -eu

lockfile="/app/package-lock.json"
hashfile="/app/node_modules/.axis-package-lock.sha256"

current_hash="$(sha256sum "$lockfile" | awk '{print $1}')"
installed_hash=""

if [ -f "$hashfile" ]; then
  installed_hash="$(cat "$hashfile")"
fi

if [ "$current_hash" != "$installed_hash" ]; then
  echo "package-lock.json changed; syncing frontend dependencies with npm ci"
  npm ci
  mkdir -p /app/node_modules
  echo "$current_hash" > "$hashfile"
fi

exec npm run dev -- --host 0.0.0.0 --port 3000
