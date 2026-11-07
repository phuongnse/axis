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

# Specs match accessible names and texts exactly, and only a pattern matches part of one. The axis/exact-locators rule checks this.
npx --prefix web oxlint --deny-warnings --report-unused-disable-directives -c tests/e2e/.oxlintrc.json tests/e2e

# The SPA's raw HTML, prototype pollution and shared widget rules run in oxlint above. A disable comment never turns off a
# security rule or every rule at once: a security rule's exception is an overrides entry in web/.oxlintrc.json.
if git grep -nE --untracked '(eslint|oxlint)-disable(-next-line|-line)?([[:space:]]*(\*/|--|$)|[^-].*(react/no-danger|no-restricted-(properties|globals)|no-proto|no-extend-native|axis/no-(srcdoc|computed-key-write|prototype-write)))' -- web; then
  echo 'A disable comment names a security rule or no rule. Add an overrides entry in web/.oxlintrc.json with its reason.' >&2
  exit 1
fi

# Oxlint does not read HTML, so the entry pages are matched as text.
if git grep -niE 'dangerouslySetInnerHTML|innerHTML|outerHTML|insertAdjacentHTML|insertHTML|createContextualFragment|setHTMLUnsafe|parseHTMLUnsafe|DOMParser|srcdoc|document\.write' -- 'web/*.html'; then
  echo 'An HTML entry page writes raw HTML.' >&2
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

# The Helm charts: lint, then validate the rendered manifests against the Kubernetes and cert-manager
# schemas that scripts/setup.sh downloads. No schema is missing-ignored, so a new kind needs its
# schema file.
source scripts/lib/tools.sh
for chart in deploy/helm/axis deploy/helm/axis-controlplane; do
  "$helm" lint "$chart" --strict -f "$chart/ci/test-values.yaml"
  "$helm" template "$(basename "$chart")" "$chart" -f "$chart/ci/test-values.yaml" |
    "$kubeconform" -strict -summary -kubernetes-version "$KUBERNETES_VERSION" \
      -schema-location "$kubernetes_schemas/{{ .ResourceKind }}{{ .KindSuffix }}.json" \
      -schema-location "$crd_schemas/{{ .Group }}/{{ .ResourceKind }}_{{ .ResourceAPIVersion }}.json"
done
