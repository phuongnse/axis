import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { Link, createMemoryRouter, RouterProvider } from 'react-router'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { LeaveWithoutSaving, UnsavedChangesGuard } from '../platform/UnsavedChangesGuard'

/**
 * Renders `ui` under the guard of docs/reference/studio.md#editor, in a data router that starts at `/studio`, with a
 * link to `/elsewhere` that stands for a link in the shell. Returns the router, so a test can read the address and move.
 */
export function renderGuarded(ui: ReactNode) {
  const router = createMemoryRouter(
    [
      {
        path: '*',
        element: (
          <UnsavedChangesGuard>
            {ui}
            <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
              <Link to="/elsewhere">Elsewhere</Link>
              <LeaveWithoutSaving />
            </TextProvider>
          </UnsavedChangesGuard>
        ),
      },
    ],
    { initialEntries: ['/studio'] },
  )
  render(
    <ThemeModeProvider>
      <RouterProvider router={router} />
    </ThemeModeProvider>,
  )
  return router
}

/** Follows the link to `/elsewhere`. */
export async function followLink() {
  await userEvent.click(screen.getByRole('link', { name: 'Elsewhere' }))
}
