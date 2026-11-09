import type { RouteObject } from 'react-router'
import { PlatformRoot } from './App'

/**
 * The routes of the SPA, for the data router of docs/reference/frontend.md#routing. `main.tsx` passes them to
 * `createBrowserRouter`, and a test passes them to `createMemoryRouter` to start at any address.
 */
export function appRoutes(development: boolean): RouteObject[] {
  return [{ path: '*', element: <PlatformRoot development={development} /> }]
}
