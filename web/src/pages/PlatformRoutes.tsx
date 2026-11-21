import { lazy, Suspense } from 'react'
import { Route, Routes } from 'react-router'
import { AccountPage } from './AccountPage'
import { CellConsoleRoute } from './CellConsoleRoute'
import { CellJobsPage } from './CellJobsPage'
import { CellPage } from './CellPage'
import { CellTenantPage } from './CellTenantPage'
import { CellTenantsPage } from './CellTenantsPage'
import { ConsoleInstancePage } from './ConsoleInstancePage'
import { ConsolePage } from './ConsolePage'
import { usePlatform } from './context'
import { HomePage } from './HomePage'
import { LicencesPage } from './LicencesPage'
import { NotFoundPage } from './NotFoundPage'
import { PageRoute, RecordPageRoute } from './PageRoute'
import { PlatformShellRoute } from './PlatformShellRoute'
import { SiteIndex, SiteRoute } from './SiteRoute'
import { UserPage } from './UserPage'
import { UsersPage } from './UsersPage'

/**
 * Studio is for authors only, so it loads in a chunk of its own, and the main bundle stays within the budget of
 * docs/development.md#bundle-budget.
 */
const LazyStudioPage = lazy(() => import('./StudioPage').then((module) => ({ default: module.StudioPage })))

/**
 * The routes of the platform. On the host of a site the SPA serves that site at `/` and its pages at `/{page}`, and
 * nothing else, as the server serves nothing else there. Elsewhere it serves the platform's own routes and every site
 * under `/{site}`. On the cell console's host it serves the console's pages: Cell at `/`, Jobs at `/jobs`, Tenants at
 * `/tenants` and one tenant at `/tenants/{id}`.
 */
export function PlatformRoutes() {
  const { hostSite, cellConsole } = usePlatform().site
  if (cellConsole) {
    return (
      <Routes>
        <Route element={<CellConsoleRoute />}>
          <Route index element={<CellPage />} />
          <Route path="jobs" element={<CellJobsPage />} />
          <Route path="tenants" element={<CellTenantsPage />} />
          <Route path="tenants/:id" element={<CellTenantPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Routes>
    )
  }
  if (hostSite) {
    return (
      <Routes>
        <Route element={<SiteRoute path={hostSite} />}>
          <Route index element={<SiteIndex />} />
          <Route path="records/:entity/:id" element={<RecordPageRoute />} />
          <Route path="records/:module/:entity/:id" element={<RecordPageRoute />} />
          <Route path=":page" element={<PageRoute />} />
          <Route path=":page/new" element={<PageRoute form="new" />} />
          <Route path=":page/:id" element={<PageRoute form="edit" />} />
          <Route path=":module/:page/new" element={<PageRoute form="new" />} />
          <Route path=":module/:page/:id" element={<PageRoute form="edit" />} />
        </Route>
        <Route
          path="*"
          element={
            <PlatformShellRoute>
              <NotFoundPage />
            </PlatformShellRoute>
          }
        />
      </Routes>
    )
  }
  return (
    <Routes>
      <Route
        index
        element={
          <PlatformShellRoute>
            <HomePage />
          </PlatformShellRoute>
        }
      />
      <Route
        path="studio"
        element={
          <PlatformShellRoute>
            <Suspense fallback={null}>
              <LazyStudioPage />
            </Suspense>
          </PlatformShellRoute>
        }
      />
      <Route
        path="account"
        element={
          <PlatformShellRoute>
            <AccountPage />
          </PlatformShellRoute>
        }
      />
      <Route
        path="users"
        element={
          <PlatformShellRoute>
            <UsersPage />
          </PlatformShellRoute>
        }
      />
      <Route
        path="users/:id"
        element={
          <PlatformShellRoute>
            <UserPage />
          </PlatformShellRoute>
        }
      />
      <Route
        path="licences"
        element={
          <PlatformShellRoute>
            <LicencesPage />
          </PlatformShellRoute>
        }
      />
      <Route
        path="console"
        element={
          <PlatformShellRoute>
            <ConsolePage />
          </PlatformShellRoute>
        }
      />
      <Route
        path="console/instances/:id"
        element={
          <PlatformShellRoute>
            <ConsoleInstancePage />
          </PlatformShellRoute>
        }
      />
      <Route path=":site" element={<SiteRoute />}>
        <Route index element={<SiteIndex />} />
        <Route path="records/:entity/:id" element={<RecordPageRoute />} />
        <Route path="records/:module/:entity/:id" element={<RecordPageRoute />} />
        <Route path=":page" element={<PageRoute />} />
        <Route path=":page/new" element={<PageRoute form="new" />} />
        <Route path=":page/:id" element={<PageRoute form="edit" />} />
        <Route path=":module/:page/new" element={<PageRoute form="new" />} />
        <Route path=":module/:page/:id" element={<PageRoute form="edit" />} />
      </Route>
      <Route
        path="*"
        element={
          <PlatformShellRoute>
            <NotFoundPage />
          </PlatformShellRoute>
        }
      />
    </Routes>
  )
}
