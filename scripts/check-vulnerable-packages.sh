#!/usr/bin/env bash
# Fail when any project in Axis.sln has known vulnerable NuGet packages.
set -euo pipefail

cd "$(dirname "$0")/.." || {
  echo "check-vulnerable-packages: failed to locate repository root" >&2
  exit 1
}

report="$(mktemp)"
trap 'rm -f "${report}"' EXIT

if ! dotnet list Axis.sln package --vulnerable --include-transitive >"${report}"; then
  cat "${report}"
  echo "check-vulnerable-packages: FAIL - dotnet vulnerable package scan failed" >&2
  exit 1
fi
cat "${report}"

if grep -q "has the following vulnerable packages" "${report}"; then
  echo "check-vulnerable-packages: FAIL - vulnerable NuGet packages found" >&2
  exit 1
fi

echo "check-vulnerable-packages: OK"
