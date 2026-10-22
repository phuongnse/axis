#!/usr/bin/env bash
# Formatting, analyzers and type checks for backend, frontend and E2E code.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet format Axis.slnx --verify-no-changes
npm run lint --prefix web
npx --prefix tests/e2e tsc -p tests/e2e/tsconfig.json

# Specs find elements by role, accessible name and ARIA state, never by a class of the UI library (D58).
if git grep -nE --untracked '(^|[^[:alnum:]_-])ant-[[:alnum:]]' -- tests; then
  echo 'A test selects an Ant Design class. Use a role, an accessible name or an ARIA state.' >&2
  exit 1
fi

# A test outside a *PerformanceTests class passes or fails the same way on any runner, so it measures
# no elapsed time. A polling deadline built on DateTime.UtcNow only ends a wait and is allowed.
if git grep -nE --untracked '(^|[^[:alnum:]_])(Stopwatch|GetElapsedTime)([^[:alnum:]_]|$)|\.Elapsed([^[:alnum:]_]|$)' -- 'tests/*.cs' ':(exclude)tests/*PerformanceTests.cs'; then
  echo 'A test outside a *PerformanceTests class measures elapsed time. Assert a count or an outcome, or poll with a DateTime.UtcNow deadline.' >&2
  exit 1
fi

node scripts/check-links.mjs
node scripts/check-docs.mjs

# The Helm chart: lint, then validate the rendered manifests against the Kubernetes schemas that
# scripts/setup.sh downloads. No schema is missing-ignored, so a new kind needs its schema file.
source scripts/lib/tools.sh
chart=deploy/helm/axis
"$helm" lint "$chart" --strict -f "$chart/ci/test-values.yaml"
"$helm" template axis "$chart" -f "$chart/ci/test-values.yaml" |
  "$kubeconform" -strict -summary -kubernetes-version "$KUBERNETES_VERSION" \
    -schema-location "$kubernetes_schemas/{{ .ResourceKind }}{{ .KindSuffix }}.json"
