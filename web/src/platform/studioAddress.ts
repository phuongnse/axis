/**
 * The address of a Studio resource, optionally at a path and with a field's drawer open, see
 * docs/reference/studio.md#route-and-address. Every link to a resource is built here.
 */
export function studioResourceAddress(
  application: string,
  resource: string,
  path?: string | null,
  field?: string | null,
): string {
  return `/studio?${new URLSearchParams({
    app: application,
    resource,
    ...(path ? { path } : {}),
    ...(field ? { field } : {}),
  })}`
}

/** The address of an AI request's page, `&view=requests&request=<id>`, see docs/reference/ai-authoring-in-studio.md. */
export function studioRequestAddress(application: string, id: string): string {
  return `/studio?${new URLSearchParams({ app: application, view: 'requests', request: id })}`
}

/**
 * Proposed: the address of an application's packages, `&view=packages`, of one package, `&package=<id>`, and of one of its
 * files, `&file=<path>`, see #3978 and docs/reference/studio-packages.md#a-packages-files.
 */
export function studioPackageAddress(application: string, packageId?: string, file?: string): string {
  return `/studio?${new URLSearchParams({
    app: application,
    view: 'packages',
    ...(packageId ? { package: packageId } : {}),
    ...(file ? { file } : {}),
  })}`
}
