let hostSite: string | null = null

/**
 * Sets the site whose own host serves the SPA, or `null` when the host belongs to no site. The platform sets it once,
 * when it loads the site metadata, so no component has to pass it on.
 */
export function setHostSite(path: string | null): void {
  hostSite = path?.toLowerCase() ?? null
}

/**
 * The address of a site's page in the SPA: `/{sitePath}/{rest}`, or `/{rest}` when `sitePath` is the site of the host,
 * which serves the site at its root. Without `rest` it is the site's own address, `/{sitePath}` or `/`.
 */
export function siteAddress(sitePath: string, rest?: string): string {
  const onHost = hostSite !== null && sitePath.toLowerCase() === hostSite
  if (rest === undefined) {
    return onHost ? '/' : `/${sitePath}`
  }
  return onHost ? `/${rest}` : `/${sitePath}/${rest}`
}
