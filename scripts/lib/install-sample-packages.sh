#!/usr/bin/env bash
# Installs each sample extension package under samples/packages that is written in Python into
# artifacts/extensions/<id>/<version>/, the layout that Extensions:Path reads: the folder is copied and the wheels
# that its requirements.txt locks are installed beside the adapter, offline from the cache that scripts/setup.sh fills.
# The install uses the pip flags of PipInstaller but skips the scan that Axis.PackageHost build runs, see
# docs/reference/extension-packages.md#sample-and-test-packages. The C# sample is installed by its project build.
# The id, the version and the language come from each axis-package.json. Works with the bash 3.2 that ships with macOS.
set -euo pipefail
cd "$(dirname "$0")/../.."
source scripts/lib/python-wheels.sh

fetch_python_wheels
mkdir -p artifacts/extensions
for dir in samples/packages/*/; do
  dir="${dir%/}"
  [ -f "$dir/axis-package.json" ] || continue
  [ "$(node -p "require('./$dir/axis-package.json').language || 'csharp'")" = python ] || continue
  id="$(node -p "require('./$dir/axis-package.json').id")"
  version="$(node -p "require('./$dir/axis-package.json').version")"
  target="artifacts/extensions/$id/$version"
  rm -rf "$target"
  mkdir -p "$target"
  cp -R "$dir/." "$target/"
  python3 -m pip install --require-hashes --no-deps --disable-pip-version-check --no-input --quiet \
    --no-index --find-links "$python_wheels" --target "$target" -r "$target/requirements.txt"
done
