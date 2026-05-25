#!/usr/bin/env bash
# Ensures every Contracts/Protos tree that contains .proto files is registered in buf.yaml.
# Run via check-doc-drift.sh and locally before pushing proto changes.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

BUF_CONFIG="${ROOT}/buf.yaml"
if [ ! -f "${BUF_CONFIG}" ]; then
  echo "check-buf-modules FAIL: missing ${BUF_CONFIG}" >&2
  exit 1
fi

ERR=0
fail() {
  echo "check-buf-modules FAIL: $1" >&2
  ERR=1
}

# Discover module proto roots: src/Modules/{Module}/Axis.{Module}.Contracts/Protos with >=1 .proto
REGISTERED="$(awk '/^[[:space:]]*- path: / { print $3 }' "${BUF_CONFIG}" | sort -u)"
DISCOVERED="$(find src/Modules -type f -name '*.proto' -printf '%h\n' 2>/dev/null \
  | sed 's|/axis/.*||' | sort -u)"

if [ -z "${DISCOVERED}" ]; then
  echo "check-buf-modules: no .proto files under src/Modules — skip"
  exit 0
fi

while IFS= read -r protos_dir; do
  [ -z "${protos_dir}" ] && continue
  if ! echo "${REGISTERED}" | grep -Fxq "${protos_dir}"; then
    fail "Protos directory ${protos_dir} has .proto files but is not listed under modules: in buf.yaml"
  fi
done <<< "${DISCOVERED}"

# Warn on stale buf.yaml entries (registered path with no .proto yet is OK for scaffolded modules)
while IFS= read -r registered_path; do
  [ -z "${registered_path}" ] && continue
  if [ ! -d "${registered_path}" ]; then
    fail "buf.yaml lists missing directory: ${registered_path}"
  fi
done <<< "${REGISTERED}"

if [ "${ERR}" -ne 0 ]; then
  echo "" >&2
  echo "Add the path under modules: in buf.yaml (see docs/playbooks/patterns.md § gRPC contract + Buf)." >&2
  exit 1
fi

echo "check-buf-modules: OK"
