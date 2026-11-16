import type { NavigationItem } from './site'

/**
 * The navigation entry the path belongs to: the one whose path equals it or is a parent of it, and
 * the longest such path when several are. Site paths are matched ignoring letter case, as the server
 * does. Home (`/`) belongs only to `/` itself.
 */
export function activeNavigationItem(
  navigation: readonly NavigationItem[],
  pathname: string,
): NavigationItem | undefined {
  const current = pathname.toLowerCase()
  let best: NavigationItem | undefined
  for (const item of navigation) {
    const path = item.path.toLowerCase()
    const matches = path === '/' ? current === '/' : current === path || current.startsWith(`${path}/`)
    if (matches && (best === undefined || path.length > best.path.length)) {
      best = item
    }
  }
  return best
}
