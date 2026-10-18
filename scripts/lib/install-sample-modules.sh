#!/usr/bin/env bash
# Installs the sample modules under samples/modules into artifacts/modules/<id>/<version>/, the
# layout that Modules:Path reads. A module has no code to compile, so the files are copied. The id
# and the version come from each module.json. Works with the bash 3.2 that ships with macOS.
set -euo pipefail
cd "$(dirname "$0")/../.."

mkdir -p artifacts/modules
for dir in samples/modules/*/; do
  dir="${dir%/}"
  [ -f "$dir/module.json" ] || continue
  id="$(node -p "require('./$dir/module.json').id")"
  version="$(node -p "require('./$dir/module.json').version")"
  target="artifacts/modules/$id/$version"
  rm -rf "$target"
  mkdir -p "$target"
  cp -R "$dir/." "$target/"
done
