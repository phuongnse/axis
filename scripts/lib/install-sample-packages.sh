#!/usr/bin/env bash
# Installs each sample extension package under samples/packages that is written in Python or Node.js into
# artifacts/extensions/<id>/<version>/, the layout that Extensions:Path reads: the folder is copied and the libraries
# that its lock file locks are installed beside the adapter, offline. A Python package gets the wheels that its
# requirements.txt locks, from the cache that scripts/setup.sh fills. A Node.js package gets the dependencies of its
# package-lock.json, from the tarballs under its vendor folder. The installs use the flags of PipInstaller and
# NodeInstall but skip the scan that Axis.PackageHost build runs, see
# docs/reference/extension-packages.md#sample-and-test-packages. The C# sample is installed by its project build.
# The id, the version and the language come from each axis-package.json.
# Arguments: the languages to install, python and node. With none, it installs both. Python needs python3 and pip,
# and the Docker stage and scripts/dev.sh ask for node alone.
# Works with the bash 3.2 that ships with macOS.
set -euo pipefail
cd "$(dirname "$0")/../.."
source scripts/lib/python-wheels.sh

languages=("$@")
if [ "${#languages[@]}" = 0 ]; then
  languages=(python node)
fi
wanted() {
  local language
  for language in "${languages[@]}"; do
    [ "$language" = "$1" ] && return 0
  done
  return 1
}
for language in "${languages[@]}"; do
  case "$language" in
    python | node) ;;
    *)
      echo "The language must be python or node, not '$language'." >&2
      exit 2
      ;;
  esac
done

if wanted python; then
  fetch_python_wheels
fi
mkdir -p artifacts/extensions
for dir in samples/packages/*/; do
  dir="${dir%/}"
  [ -f "$dir/axis-package.json" ] || continue
  language="$(node -p "require('./$dir/axis-package.json').language || 'csharp'")"
  wanted "$language" || continue
  id="$(node -p "require('./$dir/axis-package.json').id")"
  version="$(node -p "require('./$dir/axis-package.json').version")"
  target="artifacts/extensions/$id/$version"
  rm -rf "$target"
  mkdir -p "$target"
  cp -R "$dir/." "$target/"
  if [ "$language" = python ]; then
    python3 -m pip install --require-hashes --no-deps --disable-pip-version-check --no-input --quiet \
      --no-index --find-links "$python_wheels" --target "$target" -r "$target/requirements.txt"
  else
    (cd "$target" && rm -rf node_modules && npm ci --ignore-scripts --offline --no-audit --no-fund --no-update-notifier)
  fi
done
