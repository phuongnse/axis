import { ConfigProvider } from 'antd'
import { StrictMode, useState, type CSSProperties, type ReactNode } from 'react'
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
 * A frame of a fixed height, which the shell fills. Its popups open inside it, and the transform keeps a dialog's
 * mask inside it too, so several states fit one card.
 */
function Frame({ height, children }: { height: number; children: ReactNode }) {
  const { colour } = useDesignTokens()
  const [frame, setFrame] = useState<HTMLDivElement | null>(null)
  return (
    <div
      ref={setFrame}
      style={
        {
          height,
          overflow: 'auto',
          position: 'relative',
          transform: 'translateZ(0)',
          border: `1px solid ${colour.borderSubtle}`,
          '--shell-min-height': `${height}px`,
        } as CSSProperties
      }
    >
      {frame && <ConfigProvider getPopupContainer={() => frame}>{children}</ConfigProvider>}
    </div>
  )
}

/** One state under its caption. It has its own router, as the table keeps its paging in the URL query. */
function Figure({ state }: { state: PreviewState }) {
  const { colour, spacing } = useDesignTokens()
  const body = <MemoryRouter initialEntries={[state.entry ?? '/ds/page']}>{state.draw()}</MemoryRouter>
  return (
    <figure style={{ margin: 0 }}>
      <figcaption style={{ marginBottom: spacing.xs, color: colour.textMuted }}>{state.caption}</figcaption>
      {state.frameHeight === undefined ? body : <Frame height={state.frameHeight}>{body}</Frame>}
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
