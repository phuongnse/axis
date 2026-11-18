/**
 * The address of a Studio resource, optionally at a path, see docs/reference/studio.md#route-and-address. Every link
 * to a resource is built here.
 */
export function studioResourceAddress(application: string, resource: string, path?: string | null): string {
  return `/studio?${new URLSearchParams({ app: application, resource, ...(path ? { path } : {}) })}`
}

/** The address of an AI request's page, `&view=requests&request=<id>`, see docs/reference/ai-authoring-in-studio.md. */
export function studioRequestAddress(application: string, id: string): string {
  return `/studio?${new URLSearchParams({ app: application, view: 'requests', request: id })}`
}
