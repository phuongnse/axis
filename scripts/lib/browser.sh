# Installs the Chromium browser for Playwright. Source it from the repository root.
# scripts/setup.sh calls install_browser locally and install_browser_ci on CI.
# On CI the system packages Chromium needs come from the Ubuntu archive only. Playwright's
# --with-deps runs apt-get update on every APT source, so one broken source that Axis does not use
# (such as packages.microsoft.com) would stop Setup.

# Installs the Chromium browser only. No system packages, no sudo.
install_browser() {
  npx --prefix tests/e2e playwright install chromium
}

# Installs the Chromium browser and its system packages. apt only sees the Ubuntu archive sources,
# and only runs when a package is missing.
install_browser_ci() {
  npx --prefix tests/e2e playwright install chromium

  if ! command -v apt-get >/dev/null; then
    echo "apt-get not found: skipping Chromium's system dependencies."
    return 0
  fi

  local out status=0
  out="$(npx --prefix tests/e2e playwright install-deps --dry-run chromium)" || status=$?
  if ((status != 0)); then
    echo "Could not get Chromium's system dependencies from Playwright's dry run (exit $status):" >&2
    echo "$out" >&2
    return 1
  fi
  local packages=()
  read -ra packages <<<"$(sed -nE 's/.*apt-get install -y --no-install-recommends ([^"]+).*/\1/p' <<<"$out" | head -n 1)"
  if ((${#packages[@]} == 0)); then
    echo "Could not read Chromium's system dependencies from Playwright's dry run:" >&2
    echo "$out" >&2
    return 1
  fi

  local package missing=0
  for package in "${packages[@]}"; do
    if [[ "$(dpkg-query -W -f='${db:Status-Status}' "$package" 2>/dev/null)" != "installed" ]]; then
      missing=1
      break
    fi
  done
  if ((missing == 0)); then
    echo "Chromium's system dependencies are already installed."
    return 0
  fi

  # Copy only the source files that name an Ubuntu archive host. apt reads this folder instead of
  # /etc/apt, so the runner's own files stay as they are.
  local apt_etc="${AXIS_APT_ETC:-/etc/apt}" parts file copied=0
  parts="$(mktemp -d)"
  for file in "$apt_etc/sources.list" "$apt_etc"/sources.list.d/*.list "$apt_etc"/sources.list.d/*.sources; do
    if [[ -f "$file" ]] && grep -qE '(archive|security|ports)\.ubuntu\.com' "$file"; then
      cp "$file" "$parts/"
      copied=1
    fi
  done
  if ((copied == 0)); then
    rm -rf "$parts"
    echo "Found no Ubuntu APT source in $apt_etc for Chromium's system dependencies." >&2
    return 1
  fi

  local sudo=()
  if [[ "$(id -u)" != "0" ]]; then
    sudo=(sudo)
  fi
  local opts=(-o Dir::Etc::sourcelist=/dev/null -o "Dir::Etc::sourceparts=$parts" -o APT::Get::List-Cleanup=0 -o Acquire::Retries=3)

  status=0
  "${sudo[@]}" apt-get "${opts[@]}" update || status=$?
  if ((status != 0)); then
    rm -rf "$parts"
    echo "Could not update the Ubuntu APT sources that Chromium's system dependencies come from (apt-get exited with $status)." >&2
    return 1
  fi
  "${sudo[@]}" apt-get "${opts[@]}" install -y --no-install-recommends "${packages[@]}" || status=$?
  rm -rf "$parts"
  if ((status != 0)); then
    echo "Could not install Chromium's system dependencies (apt-get exited with $status): ${packages[*]}" >&2
    return 1
  fi
}
