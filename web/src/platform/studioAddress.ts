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

/** The address of a package's workspace, `&view=packages&package=<id>`, with `&file=<path>&line=<n>` to open a file at a line. */
export function studioPackageAddress(application: string, packageId: string, file?: string, line?: number): string {
  return `/studio?${new URLSearchParams({
    app: application,
    view: 'packages',
    package: packageId,
    ...(file !== undefined ? { file } : {}),
    ...(line !== undefined ? { line: String(line) } : {}),
  })}`
}

/** The address of a folder file at its line when it is a package's file, `packages/<id>/<path>`, and undefined otherwise. */
export function studioFileAddress(application: string, file: string, line?: number): string | undefined {
  const match = /^packages\/([^/]+)\/(.+)$/.exec(file)
  return match ? studioPackageAddress(application, match[1], match[2], line) : undefined
}

/** The views of a Studio application that have an address of their own, besides a resource, a kind and a package. */
export type StudioView = 'releases' | 'history' | 'policies' | 'packages' | 'publish'

/** The address of a view of an application, `&view=<view>`, such as the Releases entry or the publish page. */
export function studioViewAddress(application: string, view: StudioView): string {
  return `/studio?${new URLSearchParams({ app: application, view })}`
}

/** The address of a kind's list in an application, `&kind=<kind>`, such as `kind=entity`. */
export function studioKindAddress(application: string, kind: string): string {
  return `/studio?${new URLSearchParams({ app: application, kind })}`
}

/** The address of an application's History view, or of one revision's page, `&revision=<id>`. */
export function studioHistoryAddress(application: string, revision?: string): string {
  return `/studio?${new URLSearchParams({ app: application, view: 'history', ...(revision ? { revision } : {}) })}`
}

/**
 * The address's parameters with the drawer of field `name` open, `&field=<name>`, or with it closed for `null`. Every
 * other parameter stays as it is, so a link and a click on the drawer's field go to the same address.
 */
export function withStudioField(params: URLSearchParams, name: string | null): URLSearchParams {
  const next = new URLSearchParams(params)
  if (name === null) {
    next.delete('field')
  } else {
    next.set('field', name)
  }
  return next
}
