#!/usr/bin/env bash
# Unit tests: no Docker or external services needed.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh

for project in Axis.Server.Tests Axis.Configuration.Tests Axis.Data.Tests Axis.Tenancy.Tests; do
  run_suite "$project" "tests/$project" dotnet test --project "tests/$project" -c Release \
    --report-xunit-junit --report-xunit-junit-filename "$project.xml" --results-directory "$results_dir"
done
run_suite web web npm test --prefix web -- \
  --reporter=default --reporter=junit --outputFile.junit="$results_dir/web.xml"

# The report and link check scripts' own tests.
node --test scripts/test-report.test.mjs || status=1
node --test scripts/check-links.test.mjs || status=1

finish_report
