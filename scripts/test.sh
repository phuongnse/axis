#!/usr/bin/env bash
# Unit tests: no Docker or external services needed.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh
source scripts/lib/tools.sh

for project in Axis.Server.Tests Axis.Configuration.Tests Axis.Data.Tests Axis.Expressions.Tests Axis.Tenancy.Tests; do
  run_suite "$project" "tests/$project" dotnet test --project "tests/$project" -c Release \
    --report-xunit-junit --report-xunit-junit-filename "$project.xml" --results-directory "$results_dir"
done
run_suite PaymentReferences.Tests samples/packages/PaymentReferences.Tests dotnet test --project samples/packages/PaymentReferences.Tests -c Release \
  --report-xunit-junit --report-xunit-junit-filename PaymentReferences.Tests.xml --results-directory "$results_dir"
run_suite web web npm test --prefix web -- \
  --reporter=default --reporter=junit --outputFile.junit="$results_dir/web.xml"

# The report, smoke test selection, link check, docs structure check, locator check, bundle budget check,
# dependency scan, E2E folder comparison and hang guard helpers and Helm chart scripts' own tests.
node --test scripts/test-report.test.mjs || status=1
node --test scripts/affected.test.mjs || status=1
node --test scripts/check-links.test.mjs || status=1
node --test scripts/check-docs.test.mjs || status=1
node --test scripts/check-locators.test.mjs || status=1
node --test scripts/check-bundle.test.mjs || status=1
node --test scripts/scan.test.mjs || status=1
node --test tests/e2e/folder-comparison.test.ts || status=1
node --test tests/e2e/hang-guard.test.ts || status=1
HELM="$helm" node --test scripts/helm.test.mjs || status=1

finish_report
