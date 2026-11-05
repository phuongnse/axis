#!/usr/bin/env bash
# The cell load test, run against a staging cell, never in CI: finds the number of tenants that the cell holds and prints it as
# Cell:TenantCap. It writes the generated application of typical size (tests/Axis.LoadData, seed 1) into artifacts/load-cell
# for its roles, and then scripts/lib/load-cell-run.mjs creates the 1,000 load tenants through the cell's provisioning API (only
# when artifacts/load-cell/k6/tenants.json is missing), runs tests/load/cell.js in k6 at 1,000 tenants and at each lower count
# in steps of 100, and checks each step against the pass criteria. The first passing count is the cap. Each step keeps its k6
# summary, worker metrics, usage and result in artifacts/load-results/cell-<count>.
# The cell address is the address of the provisioning API, such as https://console.cell.example.com. The other settings, and the
# three commands of the operator that make the backlog and collect the worker metrics and the cell's usage, come from the
# environment and are listed in scripts/lib/load-cell-tenants.mjs and scripts/lib/load-cell-run.mjs.
# Exit codes: 0 with a cap, 1 when no count passed or a step broke, 2 for a wrong argument or a missing setting.
# See docs/reference/performance.md#cell-load-test.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ "$#" -ne 1 ]; then
  echo 'Usage: scripts/load-cell.sh <cell address>' >&2
  exit 2
fi

export AXIS_CELL_URL="$1"
folder="$PWD/artifacts/load-cell"
seed="${AXIS_LOAD_SEED:-1}"

mkdir -p artifacts/load-results
# The tenants are created once. The generated application only gives the provisioner the names of its roles.
if [ ! -f "$folder/k6/tenants.json" ]; then
  rm -rf "$folder/apps"
  mkdir -p "$folder/apps"
  dotnet run --project tests/Axis.LoadData -c Release -- "$folder/apps/load" --seed "$seed"
fi

exec node scripts/lib/load-cell-run.mjs "$folder" "$seed"
