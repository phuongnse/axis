import { StrictMode, type ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import { TextProvider } from '../platform/TextProvider'
import { useDesignTokens } from '../platform/themeMode'
import { PageTheme } from './PageTheme'
import { catalogs, type PreviewState } from './previews'

function Canvas({ children }: { children: ReactNode }) {
  const { colour, spacing } = useDesignTokens()
  return (
    <div style={{ padding: spacing.lg, background: colour.canvas, display: 'grid', gap: spacing.md }}>{children}</div>
  )
}

/**
 * One state under its caption. It has its own router, as the table keeps its paging in the URL query. It may be
 * narrower than its content, so a wide table scrolls inside its widget, as on a page, and the card never does.
 */
function Figure({ state }: { state: PreviewState }) {
  const { colour, spacing } = useDesignTokens()
  return (
    <figure style={{ margin: 0, minWidth: 0 }}>
      <figcaption style={{ marginBottom: spacing.xs, color: colour.textMuted }}>{state.caption}</figcaption>
      <MemoryRouter initialEntries={[state.at ?? '/ds/page']}>{state.draw()}</MemoryRouter>
    </figure>
  )
}

/** What the bundle mounts for a preview: each state of the widget under its caption on a canvas, in the mode of the page. */
export function Preview({ states }: { states: readonly PreviewState[] }) {
  return (
    <StrictMode>
      <PageTheme>
        <TextProvider catalogs={catalogs} development>
          <Canvas>
            {states.map((state) => (
              <Figure key={state.caption} state={state} />
            ))}
          </Canvas>
        </TextProvider>
      </PageTheme>
    </StrictMode>
  )
}
