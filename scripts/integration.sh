#!/usr/bin/env bash
# Integration tests against real PostgreSQL and Keycloak containers, and the smoke test of
# scripts/dev.sh (requires Docker with the compose plugin, and network access for the first SPA
# install).
# The integration tests also run the test scenarios of every application under samples/apps/ and of
# every sample template under samples/templates/.
# Each application's scenarios are a suite named scenarios-NAME, written by that test.
# The integration tests and the scenarios are skipped when $NEXKIT_BASE_SHA shows that every changed
# path is on the safe list in scripts/lib/affected.mjs. The smoke test is skipped when nothing it
# covers changed.
# Arguments go to dotnet test, for example --filter-class 'Axis.Integration.Tests.Studio*'. With
# arguments only the matching integration tests run and the summary lists only them: the scenario suites
# are not expected and the smoke test does not run. A change is checked in minutes, and the full run stays
# the check.
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

if [ "$#" -gt 0 ]; then
  run_suite Axis.Integration.Tests tests/Axis.Integration.Tests dotnet test --project tests/Axis.Integration.Tests -c Release \
    --report-xunit-junit --report-xunit-junit-filename Axis.Integration.Tests.xml --results-directory "$results_dir" "$@"
  finish_report
fi

# NexKit's runs leave the performance tests, whose classes end in PerformanceTests, to CI on main, where
# no $NEXKIT_BASE_SHA is set. They take about a third of the suite's time and depend on the runner's load.
performance=()
if [ -n "${NEXKIT_BASE_SHA:-}" ]; then
  echo "NEXKIT_BASE_SHA is set, so the performance tests are left to CI on main."
  performance=(--filter-not-class '*PerformanceTests')
fi

if affected integration; then
  for suite in "${scenario_suites[@]}"; do expect_suite "$suite" .; done
  run_suite Axis.Integration.Tests tests/Axis.Integration.Tests dotnet test --project tests/Axis.Integration.Tests -c Release \
    --report-xunit-junit --report-xunit-junit-filename Axis.Integration.Tests.xml --results-directory "$results_dir" "${performance[@]}"
else
  for suite in "${scenario_suites[@]}"; do skip_suite "$suite" .; done
  skip_suite Axis.Integration.Tests tests/Axis.Integration.Tests
fi

run_suite_if_affected dev-script . node --test --test-reporter=spec --test-reporter-destination=stdout \
  --test-reporter=junit --test-reporter-destination="$results_dir/dev-script.xml" scripts/dev.test.mjs

finish_report
