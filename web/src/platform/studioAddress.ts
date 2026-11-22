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

/**
 * Proposed, #4902: the address of a package's file at a line, `&view=packages&package=<id>&file=<path>&line=<n>`, where
 * `file` is the path inside the package. The workspace opens the file and puts the cursor on the line.
 */
export function studioPackageFileAddress(application: string, packageId: string, file: string, line?: number): string {
  return `/studio?${new URLSearchParams({
    app: application,
    view: 'packages',
    package: packageId,
    file,
    ...(line === undefined ? {} : { line: String(line) }),
  })}`
}

/** Proposed, #4902: gives the address that opens a resource at a path, so a reference to it is a real link. */
export type ResourceAddressOf = (resourceId: string, path: string | null) => string

/** Proposed, #4902: gives the address that opens a file of the folder at a line, so a file's place is a real link. */
export type FileAddressOf = (file: string, line?: number) => string
