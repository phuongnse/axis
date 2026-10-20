#!/usr/bin/env bash
# Installs the sample templates under samples/templates into artifacts/templates/<id>/<version>/, the
# layout that Templates:Path reads. A template has no code to compile, so the files are copied. The id
# and the version come from each template.json. Works with the bash 3.2 that ships with macOS.
set -euo pipefail
cd "$(dirname "$0")/../.."

mkdir -p artifacts/templates
for dir in samples/templates/*/; do
  dir="${dir%/}"
  [ -f "$dir/template.json" ] || continue
  id="$(node -p "require('./$dir/template.json').id")"
  version="$(node -p "require('./$dir/template.json').version")"
  target="artifacts/templates/$id/$version"
  rm -rf "$target"
  mkdir -p "$target"
  cp -R "$dir/." "$target/"
done
