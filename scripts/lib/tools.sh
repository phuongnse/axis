# Pinned versions of the chart tools and the image scanner, and where they live. Source it from the
# repository root. scripts/setup.sh calls install_tools. scripts/lint.sh and scripts/test.sh use the
# paths. scripts/scan-images.sh calls install_trivy, so setup and NexKit's checks never download Trivy.
# The tools go into versioned folders under the home cache, so a version change
# downloads again and the lint step needs no network.

source scripts/lib/retry.sh

HELM_VERSION=v3.22.1
KUBECONFORM_VERSION=v0.8.0
KUBERNETES_VERSION=1.33.0
# A commit of github.com/yannh/kubernetes-json-schema. A commit keeps the schemas fixed.
KUBERNETES_SCHEMAS_COMMIT=93153a1458e24e93f386c50f9980d584fcba574b
# The kinds the chart renders. A new kind needs its schema file here.
KUBERNETES_SCHEMA_FILES=(deployment-apps-v1 job-batch-v1 service-v1)
# Trivy scans the images. Aqua withdrew the releases of its March 2026 supply chain incident, so the
# pin is a later release, and its archives' checksums are pinned here rather than read from the
# release itself.
TRIVY_VERSION=0.76.1
TRIVY_SHA256_LINUX_64BIT=c7cbe3137fb8a48a17574bede3dd6beb16742d1e9772929940fc90082c39d9c0
TRIVY_SHA256_LINUX_ARM64=e174dff2a6dba5e3ae41f8d0537a6324405bd08132497340304c6b20e0f16a1f

tools_dir="$HOME/.cache/axis-tools"
helm="$tools_dir/helm-$HELM_VERSION/helm"
kubeconform="$tools_dir/kubeconform-$KUBECONFORM_VERSION/kubeconform"
kubernetes_schemas="$tools_dir/kubernetes-schemas-$KUBERNETES_VERSION"
trivy="$tools_dir/trivy-$TRIVY_VERSION/trivy"

# Checks FILE against the SHA-256 checksum EXPECTED.
verify_sha256() {
  local file="$1" expected="$2" actual
  if command -v sha256sum >/dev/null; then
    actual="$(sha256sum "$file" | cut -d' ' -f1)"
  else
    actual="$(shasum -a 256 "$file" | cut -d' ' -f1)"
  fi
  if [ "$actual" != "$expected" ]; then
    echo "Checksum mismatch for $file: expected $expected, got $actual" >&2
    return 1
  fi
}

# Downloads each missing tool and skips the ones present. Each one is built in a temporary
# folder and moved into place, so a failed download leaves nothing behind.
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
    retry 4 curl -fsSL --retry 3 -o "$work/$archive" "https://get.helm.sh/$archive"
    sum="$(retry 4 curl -fsSL --retry 3 "https://get.helm.sh/$archive.sha256sum" | cut -d' ' -f1)"
    verify_sha256 "$work/$archive" "$sum"
    tar -xzf "$work/$archive" -C "$work/helm" --strip-components=1 "$os-$arch/helm"
    mv "$work/helm" "$tools_dir/helm-$HELM_VERSION"
  fi

  if [ ! -x "$kubeconform" ]; then
    local archive="kubeconform-$os-$arch.tar.gz" sum
    mkdir "$work/kubeconform"
    retry 4 curl -fsSL --retry 3 -o "$work/$archive" \
      "https://github.com/yannh/kubeconform/releases/download/$KUBECONFORM_VERSION/$archive"
    sum="$(retry 4 curl -fsSL --retry 3 "https://github.com/yannh/kubeconform/releases/download/$KUBECONFORM_VERSION/CHECKSUMS" |
      awk -v name="$archive" '$2 == name { print $1 }')"
    verify_sha256 "$work/$archive" "$sum"
    tar -xzf "$work/$archive" -C "$work/kubeconform" kubeconform
    mv "$work/kubeconform" "$tools_dir/kubeconform-$KUBECONFORM_VERSION"
  fi

  if [ ! -d "$kubernetes_schemas" ]; then
    local file
    mkdir "$work/schemas"
    for file in "${KUBERNETES_SCHEMA_FILES[@]}"; do
      retry 4 curl -fsSL --retry 3 -o "$work/schemas/$file.json" \
        "https://raw.githubusercontent.com/yannh/kubernetes-json-schema/$KUBERNETES_SCHEMAS_COMMIT/v$KUBERNETES_VERSION-standalone-strict/$file.json"
    done
    mv "$work/schemas" "$kubernetes_schemas"
  fi

  rm -rf "$work"
}

# Downloads Trivy when it is missing, checked against its pinned checksum. The image scan runs on
# Linux, in CI and in the development container.
install_trivy() {
  local platform sum work archive
  if [ -x "$trivy" ]; then return 0; fi
  if [ "$(uname -s)" != Linux ]; then
    echo "The image scan runs on Linux, not $(uname -s)" >&2
    return 1
  fi
  case "$(uname -m)" in
    x86_64 | amd64) platform=Linux-64bit sum="$TRIVY_SHA256_LINUX_64BIT" ;;
    arm64 | aarch64) platform=Linux-ARM64 sum="$TRIVY_SHA256_LINUX_ARM64" ;;
    *) echo "Unsupported architecture: $(uname -m)" >&2; return 1 ;;
  esac
  archive="trivy_${TRIVY_VERSION}_$platform.tar.gz"
  mkdir -p "$tools_dir"
  work="$(mktemp -d "$tools_dir/.install.XXXXXX")"
  mkdir "$work/trivy"
  retry 4 curl -fsSL --retry 3 -o "$work/$archive" \
    "https://github.com/aquasecurity/trivy/releases/download/v$TRIVY_VERSION/$archive"
  verify_sha256 "$work/$archive" "$sum"
  tar -xzf "$work/$archive" -C "$work/trivy" trivy
  mv "$work/trivy" "$tools_dir/trivy-$TRIVY_VERSION"
  rm -rf "$work"
}
