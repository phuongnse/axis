import { StrictMode, type ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import { TextProvider } from '../platform/TextProvider'
import { useDesignTokens } from '../platform/themeMode'
import { PageTheme } from './PageTheme'
import { catalogs } from './previews'

function Canvas({ children }: { children: ReactNode }) {
  const { colour, spacing } = useDesignTokens()
  return <div style={{ padding: spacing.lg, background: colour.canvas }}>{children}</div>
}

/** What the bundle mounts for a preview: the widget on a canvas, in the mode of the page. */
export function Preview({ draw }: { draw: () => ReactNode }) {
  return (
    <StrictMode>
      <PageTheme>
        <MemoryRouter initialEntries={['/ds/page']}>
          <TextProvider catalogs={catalogs} development>
            <Canvas>{draw()}</Canvas>
          </TextProvider>
        </MemoryRouter>
      </PageTheme>
    </StrictMode>
  )
}
