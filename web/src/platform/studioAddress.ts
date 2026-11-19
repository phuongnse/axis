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

/** The address of an application in Studio, `/studio?app=<name>`, where `application` is its manifest name. */
export function studioApplicationAddress(application: string): string {
  return `/studio?${new URLSearchParams({ app: application })}`
}

/** The address of a release's page in Studio, `&view=release&release=<number>`. */
export function studioReleaseAddress(application: string, number: number): string {
  return `/studio?${new URLSearchParams({ app: application, view: 'release', release: String(number) })}`
}
