#!/usr/bin/env bash
# Installs the .NET SDK that global.json names, for CI and CodeQL. The workflows restore ~/.cache/axis-dotnet
# from a cache keyed on global.json, so a run downloads the SDK only when the cache misses. The download
# gets 4 tries. The SDK is built in a temporary folder and moved into place, so a failed download leaves
# nothing behind. Under GitHub Actions the SDK folder goes to GITHUB_PATH and DOTNET_ROOT to GITHUB_ENV.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/lib/retry.sh

version="$(sed -n 's/^ *"version": *"\([^"]*\)".*/\1/p' global.json | head -n1)"
if [ -z "$version" ]; then
  echo "No SDK version found in global.json" >&2
  exit 1
fi

cache_dir="$HOME/.cache/axis-dotnet"
dotnet_dir="$cache_dir/$version"

if [ ! -d "$dotnet_dir/sdk/$version" ]; then
  mkdir -p "$cache_dir"
  work="$(mktemp -d "$cache_dir/.install.XXXXXX")"
  trap 'rm -rf "$work"' EXIT
  retry 4 curl -fsSL --retry 3 -o "$work/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh
  retry 4 bash "$work/dotnet-install.sh" --version "$version" --install-dir "$work/dotnet" --no-path
  rm -rf "$dotnet_dir"
  mv "$work/dotnet" "$dotnet_dir"
  rm -rf "$work"
  trap - EXIT
fi

export PATH="$dotnet_dir:$PATH"
export DOTNET_ROOT="$dotnet_dir"
if [ -n "${GITHUB_PATH:-}" ]; then
  echo "$dotnet_dir" >> "$GITHUB_PATH"
  echo "DOTNET_ROOT=$dotnet_dir" >> "${GITHUB_ENV:?GITHUB_ENV must be set with GITHUB_PATH}"
else
  echo "Installed .NET SDK $version in $dotnet_dir"
fi
