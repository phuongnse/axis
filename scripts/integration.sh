#!/usr/bin/env bash
# Integration tests against real PostgreSQL and Keycloak containers (requires Docker).
# The integration tests also run the test scenarios of every application under samples/apps/ and of
# every sample template under samples/templates/.
# The Keycloak theme is built first, because the tests' Keycloak loads it.
# Each application's scenarios are a suite named scenarios-NAME, written by that test.
# The integration tests and the scenarios are skipped when every path changed since NexKit's commit is
# on the safe list in scripts/lib/affected.mjs. The smoke test of scripts/dev.sh runs in
# scripts/smoke.sh.
# The control plane has an integration suite of its own, Axis.ControlPlane.Integration.Tests. It is built
# first and then runs in the background beside Axis.Integration.Tests, so it adds no time to the run, and
# it is skipped with it. Its output is printed when Axis.Integration.Tests ends.
# Arguments go to dotnet test, for example --filter-class 'Axis.Integration.Tests.Studio*'. With
# arguments only the matching integration tests run and the summary lists only them: the scenario suites
# and the control plane suite are not expected. A filtered run of the control plane suite goes through
# dotnet test --project controlplane/tests/Axis.ControlPlane.Integration.Tests. A change is checked in
# minutes, and the full run stays the check.
# Without arguments the performance tests, whose classes end in PerformanceTests, never run here:
# scripts/performance.sh runs them, in a workflow of its own on main.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh

scenario_suites=()
for manifest in samples/apps/*/application.json; do
  name="$(node -p "JSON.parse(require('fs').readFileSync('$manifest','utf8')).name")"
  scenario_suites+=("scenarios-$name")
done

for manifest in samples/templates/*/application.json; do
  name="$(node -p "JSON.parse(require('fs').readFileSync('$manifest','utf8')).name")"
  scenario_suites+=("scenarios-$name")
done

# The integration tests' Keycloak loads the Axis theme from the test output, which copies artifacts/keycloak-theme.
build_theme() { npm run build:keycloak-theme --prefix web; }

if [ "$#" -gt 0 ]; then
  build_theme
  run_suite Axis.Integration.Tests tests/Axis.Integration.Tests dotnet test --project tests/Axis.Integration.Tests -c Release \
    --report-xunit-junit --report-xunit-junit-filename Axis.Integration.Tests.xml --results-directory "$results_dir" "$@"
  finish_report
fi

if affected integration; then
  build_theme
  for suite in "${scenario_suites[@]}"; do expect_suite "$suite" .; done
  # Build the control plane suite first, so the two dotnet test runs never build the shared projects at once.
  dotnet build controlplane/tests/Axis.ControlPlane.Integration.Tests -c Release
  control_plane_log="$(mktemp)"
  rm -f "$results_dir/Axis.ControlPlane.Integration.Tests.xml"
  suites+=("Axis.ControlPlane.Integration.Tests:controlplane/tests/Axis.ControlPlane.Integration.Tests")
  dotnet test --project controlplane/tests/Axis.ControlPlane.Integration.Tests -c Release --no-build \
    --report-xunit-junit --report-xunit-junit-filename Axis.ControlPlane.Integration.Tests.xml --results-directory "$results_dir" \
    >"$control_plane_log" 2>&1 &
  control_plane_pid=$!
  run_suite Axis.Integration.Tests tests/Axis.Integration.Tests dotnet test --project tests/Axis.Integration.Tests -c Release \
    --report-xunit-junit --report-xunit-junit-filename Axis.Integration.Tests.xml --results-directory "$results_dir" --filter-not-class '*PerformanceTests'
  wait "$control_plane_pid" || status=1
  cat "$control_plane_log"
  rm -f "$control_plane_log"
else
  for suite in "${scenario_suites[@]}"; do skip_suite "$suite" .; done
  skip_suite Axis.Integration.Tests tests/Axis.Integration.Tests
  skip_suite Axis.ControlPlane.Integration.Tests controlplane/tests/Axis.ControlPlane.Integration.Tests
fi

finish_report
