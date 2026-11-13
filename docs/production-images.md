# Production images

## Production images and Axis.Admin

The production images and the admin tool are the same in development as in an install. The
`Dockerfile` has eight production targets, `axis-server`, `axis-worker`, `axis-agent`, `axis-admin`,
`axis-controlplane` and the package host images `axis-package-host-dotnet`, `axis-package-host-node` and
`axis-package-host-python`, which hold no samples, development settings or source control metadata. Build one
with `docker build --target axis-server --build-arg AXIS_VERSION=<version> .`. The default target is
the development server. `scripts/smoke.sh` builds each image once, up to three attempts with
waits of 15 and then 30 seconds between attempts, because a download in the build can fail at times. The `axis-admin`
stage builds on the `admin-tools` stage, which installs the system packages and declares no `AXIS_VERSION`. Docker
caches that layer, so a build with a new version does not download the packages again, and the
[smoke tests](development.md#smoke-tests) save it for later runs. The package host images build the same way, on a
runtime stage each and on a `trivy` stage that downloads the Trivy that `scripts/lib/trivy.sh` pins, checked against its
pinned checksum.

`Axis.Admin` is the console tool that the platform operator runs. It creates, suspends, resumes,
lists, backs up and restores tenants, and it migrates and activates them. Run it with
`dotnet run --project src/Axis.Admin -- <command>`, or with the `axis-admin` image. See
[administration](reference/administration.md). The production install is in `deploy/compose/`
and `deploy/helm/axis/`, see [installation](reference/installation.md).
