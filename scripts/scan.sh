#!/usr/bin/env bash
# Dependency scan: fails on a known high or critical vulnerability in the NuGet and npm packages.
set -euo pipefail
cd "$(dirname "$0")/.."

exec node scripts/scan.mjs "$@"
