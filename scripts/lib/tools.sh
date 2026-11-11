# Pinned versions of the chart tools, and where they live. Source it from the repository root.
# scripts/setup.sh calls install_tools. scripts/lint.sh and scripts/test.sh use the paths. The image
# scanner's pin is in scripts/lib/trivy.sh, which the owner guards. The tools go into versioned
# folders under the home cache, so a version change downloads again and the lint step needs no network.
# Each download has a connect and a total time limit, and retry alone tries it again, as D147 says.

source scripts/lib/retry.sh
source scripts/lib/trivy.sh

HELM_VERSION=v3.22.1
KUBECONFORM_VERSION=v0.8.0
KUBERNETES_VERSION=1.33.0
# A commit of github.com/yannh/kubernetes-json-schema. A commit keeps the schemas fixed.
KUBERNETES_SCHEMAS_COMMIT=93153a1458e24e93f386c50f9980d584fcba574b
# The Kubernetes kinds the chart renders. A new kind needs its schema file here.
KUBERNETES_SCHEMA_FILES=(configmap-v1 cronjob-batch-v1 deployment-apps-v1 job-batch-v1 role-rbac-v1 rolebinding-rbac-v1 service-v1 serviceaccount-v1)
# A commit of github.com/datreeio/CRDs-catalog, which holds the schemas of cert-manager's kinds.
CRDS_CATALOG_COMMIT=27233a506de0085e71c7f9702597806a7f95eb24
# The cert-manager kinds the chart renders, as group/kind_version. A new kind needs its schema file here.
CRD_SCHEMA_FILES=(cert-manager.io/certificate_v1 cert-manager.io/issuer_v1)

# tools_dir and verify_sha256 come from scripts/lib/trivy.sh.
helm="$tools_dir/helm-$HELM_VERSION/helm"
kubeconform="$tools_dir/kubeconform-$KUBECONFORM_VERSION/kubeconform"
kubernetes_schemas="$tools_dir/kubernetes-schemas-$KUBERNETES_VERSION"
crd_schemas="$tools_dir/crd-schemas-$CRDS_CATALOG_COMMIT"

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
