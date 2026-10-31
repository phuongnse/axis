#!/usr/bin/env bash
# Unit tests: no Docker or external services needed.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/test-results.sh
source scripts/lib/tools.sh

# The server tests start the server, which reads the sample modules and templates from artifacts/. NexKit
# runs this script in a job without scripts/build.sh, which installs them too.
bash scripts/lib/install-sample-modules.sh
bash scripts/lib/install-sample-templates.sh

for project in Axis.Server.Tests Axis.Configuration.Tests Axis.Data.Tests Axis.Expressions.Tests Axis.Tenancy.Tests; do
  run_suite "$project" "tests/$project" dotnet test --project "tests/$project" -c Release \
    --report-xunit-junit --report-xunit-junit-filename "$project.xml" --results-directory "$results_dir"
done
run_suite PaymentReferences.Tests samples/packages/PaymentReferences.Tests dotnet test --project samples/packages/PaymentReferences.Tests -c Release \
  --report-xunit-junit --report-xunit-junit-filename PaymentReferences.Tests.xml --results-directory "$results_dir"
run_suite web web npm test --prefix web -- \
  --reporter=default --reporter=junit --outputFile.junit="$results_dir/web.xml"

# The report, smoke test selection, link check, docs structure check, locator check, raw HTML check, prototype pollution check, bundle budget check,
# font check, design-system bundle check, dependency scan, image scan, setup retries, image build retries, E2E shard
# split, load script, load objectives, E2E folder comparison, hang guard, dialog and drag helpers, TOTP function, load test routes and TOTP module, Compose status
# helper, dev script stop helper, development realm script and Helm chart scripts' own tests.
node --test scripts/test-report.test.mjs || status=1
node --test scripts/affected.test.mjs || status=1
node --test scripts/check-links.test.mjs || status=1
node --test scripts/check-docs.test.mjs || status=1
node --test scripts/check-locators.test.mjs || status=1
node --test scripts/check-raw-html.test.mjs || status=1
node --test scripts/check-prototype-pollution.test.mjs || status=1
node --test scripts/check-bundle.test.mjs || status=1
node --test scripts/check-fonts.test.mjs || status=1
node --test scripts/check-design-system.test.mjs || status=1
node --test scripts/scan.test.mjs || status=1
node --test scripts/scan-images.test.mjs || status=1
node --test scripts/dependabot-issues.test.mjs || status=1
node --test scripts/setup.test.mjs || status=1
node --test scripts/e2e.test.mjs || status=1
node --test scripts/e2e-shard.test.mjs || status=1
node --test scripts/load-objectives.test.mjs || status=1
node --test scripts/load.test.mjs || status=1
node --test scripts/smoke.test.mjs || status=1
node --test scripts/compose-status.test.mjs || status=1
node --test scripts/dev-stop.test.mjs || status=1
node --test scripts/development-realm.test.mjs || status=1
node --test scripts/docker-build.test.mjs || status=1
node --test tests/e2e/folder-comparison.test.ts || status=1
node --test tests/e2e/hang-guard.test.ts || status=1
node --test tests/e2e/dialogs.test.ts || status=1
node --test tests/e2e/design-drag.test.ts || status=1
node --test tests/e2e/policy-violations.test.ts || status=1
node --test tests/e2e/startup-activation.test.ts || status=1
node --test tests/e2e/totp.test.ts || status=1
node --test tests/load/objectives.test.mjs || status=1
node --test tests/load/totp.test.mjs || status=1
HELM="$helm" node --test scripts/helm.test.mjs || status=1

finish_report
