/**
 * The address of a Studio resource, optionally at a path, see docs/reference/studio.md#route-and-address. Every link
 * to a resource is built here.
 */
export function studioResourceAddress(application: string, resource: string, path?: string | null): string {
  return `/studio?${new URLSearchParams({ app: application, resource, ...(path ? { path } : {}) })}`
}
