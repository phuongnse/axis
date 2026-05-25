#!/usr/bin/env bash
# Per-module buf breaking vs BASE_REF. Skips proto roots that did not exist on the base
# branch (avoids workspace image-count mismatch when adding a new modules: entry).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

BASE_REF="${BASE_REF:?Set BASE_REF (e.g. origin/main)}"

if ! git rev-parse --verify "${BASE_REF}" >/dev/null 2>&1; then
  echo "buf-breaking-against-base FAIL: missing ${BASE_REF}" >&2
  exit 1
fi

if ! command -v buf >/dev/null 2>&1; then
  echo "buf-breaking-against-base FAIL: buf CLI not on PATH" >&2
  exit 1
fi

if [ ! -f buf.yaml ]; then
  echo "buf-breaking-against-base: no buf.yaml — skip"
  exit 0
fi

while read -r dir; do
  [ -z "${dir}" ] && continue
  if git ls-tree -r --name-only "${BASE_REF}" "${dir}" 2>/dev/null | grep -qE '\.proto$'; then
    echo "buf breaking ${dir} (vs ${BASE_REF})"
    buf breaking "${dir}" --against ".git#ref=${BASE_REF},subdir=${dir}"
  else
    echo "buf breaking: skip ${dir} (new on this branch — no baseline on ${BASE_REF})"
  fi
done < <(awk '/^[[:space:]]*- path: / { print $3 }' buf.yaml)

echo "buf-breaking-against-base: OK"
