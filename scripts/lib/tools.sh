# Pinned versions of the chart tools, and where they live. Source it from the repository root.
# scripts/setup.sh calls install_tools and install_gvisor. scripts/lint.sh and scripts/test.sh use the paths. The image
# scanner's pin is in scripts/lib/trivy.sh, which the owner guards. The tools go into versioned
# folders under the home cache, so a version change downloads again and the lint step needs no network.
# Each download has a connect and a total time limit, and retry alone tries it again, as D147 says.

source scripts/lib/retry.sh
source scripts/lib/trivy.sh

HELM_VERSION=v3.22.1
KUBECONFORM_VERSION=v0.8.0
KUBERNETES_VERSION=1.33.0
# gVisor, the sandbox runtime of the Compose example's application package host, as a release date.
GVISOR_VERSION=20260817
# A commit of github.com/yannh/kubernetes-json-schema. A commit keeps the schemas fixed.
KUBERNETES_SCHEMAS_COMMIT=93153a1458e24e93f386c50f9980d584fcba574b
# The Kubernetes kinds the chart renders. A new kind needs its schema file here.
KUBERNETES_SCHEMA_FILES=(configmap-v1 cronjob-batch-v1 deployment-apps-v1 job-batch-v1 networkpolicy-networking-v1 role-rbac-v1 rolebinding-rbac-v1 service-v1 serviceaccount-v1)
# A commit of github.com/datreeio/CRDs-catalog, which holds the schemas of cert-manager's kinds.
CRDS_CATALOG_COMMIT=27233a506de0085e71c7f9702597806a7f95eb24
# The cert-manager kinds the chart renders, as group/kind_version. A new kind needs its schema file here.
CRD_SCHEMA_FILES=(cert-manager.io/certificate_v1 cert-manager.io/issuer_v1)

# tools_dir and verify_sha256 come from scripts/lib/trivy.sh.
helm="$tools_dir/helm-$HELM_VERSION/helm"
kubeconform="$tools_dir/kubeconform-$KUBECONFORM_VERSION/kubeconform"
kubernetes_schemas="$tools_dir/kubernetes-schemas-$KUBERNETES_VERSION"
crd_schemas="$tools_dir/crd-schemas-$CRDS_CATALOG_COMMIT"
gvisor="$tools_dir/gvisor-$GVISOR_VERSION/runsc"

# Checks FILE against the SHA-512 checksum EXPECTED. gVisor publishes SHA-512 sums, and verify_sha256
# in scripts/lib/trivy.sh checks SHA-256 only.
verify_sha512() {
  local file="$1" expected="$2" actual
  if command -v sha512sum >/dev/null; then
    actual="$(sha512sum "$file" | cut -d' ' -f1)"
  else
    actual="$(shasum -a 512 "$file" | cut -d' ' -f1)"
  fi
  if [ "$actual" != "$expected" ]; then
    echo "Checksum mismatch for $file: expected $expected, got $actual" >&2
    return 1
  fi
}

# Downloads each missing tool and skips the ones present. The schema files are fetched one by one,
# so a cache that lacks a newly listed file gets it. Each download goes to a temporary folder and
# is moved into place, so a failed download leaves nothing behind.
install_tools() {
  local os arch work
  case "$(uname -s)" in
    Linux) os=linux ;;
    Darwin) os=darwin ;;
    *) echo "Unsupported operating system: $(uname -s)" >&2; return 1 ;;
  esac
  case "$(uname -m)" in
    x86_64 | amd64) arch=amd64 ;;
    arm64 | aarch64) arch=arm64 ;;
    *) echo "Unsupported architecture: $(uname -m)" >&2; return 1 ;;
  esac

  mkdir -p "$tools_dir"
  work="$(mktemp -d "$tools_dir/.install.XXXXXX")"

  if [ ! -x "$helm" ]; then
    local archive="helm-$HELM_VERSION-$os-$arch.tar.gz" sum
    mkdir "$work/helm"
    retry 4 curl -fsSL --connect-timeout 30 --max-time 300 -o "$work/$archive" "https://get.helm.sh/$archive"
    sum="$(retry 4 curl -fsSL --connect-timeout 30 --max-time 300 "https://get.helm.sh/$archive.sha256sum" | cut -d' ' -f1)"
    verify_sha256 "$work/$archive" "$sum"
    tar -xzf "$work/$archive" -C "$work/helm" --strip-components=1 "$os-$arch/helm"
    mv "$work/helm" "$tools_dir/helm-$HELM_VERSION"
  fi

  if [ ! -x "$kubeconform" ]; then
    local archive="kubeconform-$os-$arch.tar.gz" sum
    mkdir "$work/kubeconform"
    retry 4 curl -fsSL --connect-timeout 30 --max-time 300 -o "$work/$archive" \
      "https://github.com/yannh/kubeconform/releases/download/$KUBECONFORM_VERSION/$archive"
    sum="$(retry 4 curl -fsSL --connect-timeout 30 --max-time 300 "https://github.com/yannh/kubeconform/releases/download/$KUBECONFORM_VERSION/CHECKSUMS" |
      awk -v name="$archive" '$2 == name { print $1 }')"
    verify_sha256 "$work/$archive" "$sum"
    tar -xzf "$work/$archive" -C "$work/kubeconform" kubeconform
    mv "$work/kubeconform" "$tools_dir/kubeconform-$KUBECONFORM_VERSION"
  fi

  local file
  mkdir -p "$kubernetes_schemas"
  for file in "${KUBERNETES_SCHEMA_FILES[@]}"; do
    if [ ! -f "$kubernetes_schemas/$file.json" ]; then
      retry 4 curl -fsSL --connect-timeout 30 --max-time 300 -o "$work/$file.json" \
        "https://raw.githubusercontent.com/yannh/kubernetes-json-schema/$KUBERNETES_SCHEMAS_COMMIT/v$KUBERNETES_VERSION-standalone-strict/$file.json"
      mv "$work/$file.json" "$kubernetes_schemas/$file.json"
    fi
  done

  local crd
  mkdir -p "$crd_schemas"
  for crd in "${CRD_SCHEMA_FILES[@]}"; do
    if [ ! -f "$crd_schemas/$crd.json" ]; then
      retry 4 curl -fsSL --connect-timeout 30 --max-time 300 -o "$work/crd.json" \
        "https://raw.githubusercontent.com/datreeio/CRDs-catalog/$CRDS_CATALOG_COMMIT/$crd.json"
      mkdir -p "$crd_schemas/$(dirname "$crd")"
      mv "$work/crd.json" "$crd_schemas/$crd.json"
    fi
  done

  rm -rf "$work"
}

# Downloads the runsc at URL to FILE and checks it against the SHA-512 that gVisor publishes beside it.
# The caller tests the status, so every step stops the function itself.
fetch_gvisor() {
  local file="$1" url="$2" sum
  retry 4 curl -fsSL --connect-timeout 30 --max-time 300 -o "$file" "$url" || return 1
  sum="$(retry 4 curl -fsSL --connect-timeout 30 --max-time 300 "$url.sha512" | cut -d' ' -f1)" || return 1
  verify_sha512 "$file" "$sum" || return 1
  chmod 0755 "$file"
}

# Registers gVisor's runsc runtime with Docker, which the application-packages profile of the Compose
# example needs, see docs/reference/installation.md#on-premises. It skips the step on systems other than
# Linux, when Docker is not reachable and when Docker already lists runsc. Otherwise it downloads the
# pinned release into the tools cache, checks its SHA-512, copies it to /usr/local/bin, runs
# runsc install and reloads Docker. Only runsc is installed: Docker calls it directly, and the
# containerd shim serves containerd without Docker. A runtime that is still missing after the reload
# is a warning, because docker compose up without the profile needs no gVisor.
install_gvisor() {
  local runtimes arch work url
  if [ "$(uname -s)" != Linux ]; then
    echo "Skipping gVisor: it installs on Linux only, and this is $(uname -s)" >&2
    return 0
  fi
  if ! runtimes="$(docker info --format '{{json .Runtimes}}' 2>/dev/null)"; then
    echo "Skipping gVisor: Docker is not reachable" >&2
    return 0
  fi
  if [[ "$runtimes" == *'"runsc"'* ]]; then return 0; fi
  case "$(uname -m)" in
    x86_64 | amd64) arch=x86_64 ;;
    arm64 | aarch64) arch=aarch64 ;;
    *) echo "Unsupported architecture: $(uname -m)" >&2; return 1 ;;
  esac

  if [ ! -x "$gvisor" ]; then
    mkdir -p "$tools_dir"
    work="$(mktemp -d "$tools_dir/.install.XXXXXX")"
    url="https://storage.googleapis.com/gvisor/releases/release/$GVISOR_VERSION/$arch/runsc"
    mkdir "$work/gvisor"
    if ! fetch_gvisor "$work/gvisor/runsc" "$url"; then
      rm -rf "$work"
      return 1
    fi
    mv "$work/gvisor" "$tools_dir/gvisor-$GVISOR_VERSION"
    rm -rf "$work"
  fi

  as_root() { if [ "$(id -u)" = 0 ]; then "$@"; else sudo "$@"; fi; }
  as_root install -m 0755 "$gvisor" /usr/local/bin/runsc
  as_root /usr/local/bin/runsc install
  # Rootless Docker, Docker Desktop and snap Docker have no system unit or dockerd to reload, so a failed
  # reload is not fatal: the check below decides whether to warn.
  if command -v systemctl >/dev/null; then
    as_root systemctl reload docker || echo "Warning: Docker did not reload" >&2
  else
    as_root kill -HUP "$(pidof dockerd || true)" || echo "Warning: Docker did not reload" >&2
  fi
  runtimes="$(docker info --format '{{json .Runtimes}}' 2>/dev/null || true)"
  if [[ "$runtimes" != *'"runsc"'* ]]; then
    echo "Warning: Docker does not list the runsc runtime after the reload. The application-packages profile needs it." >&2
  fi
}
