import { ConfigProvider, Segmented } from 'antd'
import { StrictMode, useState, type CSSProperties, type ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import { TextProvider } from '../platform/TextProvider'
import { useDesignTokens } from '../platform/themeMode'
import { PageTheme } from './PageTheme'
import { catalogs, chooseFixtures, stateSlug, type PreviewState } from './previews'

function Canvas({ children }: { children: ReactNode }) {
  const { colour, spacing } = useDesignTokens()
  return (
    <div
      style={{
        padding: spacing.lg,
        background: colour.canvas,
        display: 'grid',
        gap: spacing.md,
        gridTemplateColumns: 'minmax(0, 1fr)',
      }}
    >
      {children}
    </div>
  )
}

/**
 * A screen exactly as tall as its height. The page scrolls inside it, as it scrolls in a window, so a dialog opens in
 * the middle of what shows. The outer box does not scroll. It holds the popups and dialogs, and the transform keeps
 * them inside it, so several states fit one card.
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
          boxSizing: 'content-box',
          position: 'relative',
          transform: 'translateZ(0)',
          border: `1px solid ${colour.borderSubtle}`,
          '--shell-min-height': `${height}px`,
        } as CSSProperties
      }
    >
      <div style={{ height: '100%', overflow: 'auto' }}>
        {frame && <ConfigProvider getPopupContainer={() => frame}>{children}</ConfigProvider>}
      </div>
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

/**
 * The states of a preview that answers the same paths in each of them, or whose states are screens in frames, one at a
 * time under a State control. The `state` query of the page, a state's caption in kebab case, picks the first one
 * shown, else the first state does.
 */
function Chooser({ states }: { states: readonly PreviewState[] }) {
  const { colour, spacing } = useDesignTokens()
  const [state, setState] = useState(() => {
    const slug = new URLSearchParams(window.location.search).get('state')
    const first = states.find((candidate) => stateSlug(candidate.caption) === slug) ?? states[0]
    chooseFixtures(first)
    return first
  })
  return (
    <>
      <style>{'[data-fixtures] .ant-segmented-group { flex-wrap: wrap; }'}</style>
      <div data-fixtures style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: spacing.sm }}>
        <span style={{ color: colour.textMuted }}>State</span>
        <Segmented
          aria-label="State"
          style={{ maxWidth: '100%' }}
          options={states.map(({ caption }) => caption)}
          value={state.caption}
          onChange={(caption) => {
            const next = states.find((candidate) => candidate.caption === caption)!
            chooseFixtures(next)
            setState(next)
          }}
        />
      </div>
      <Figure key={state.caption} state={state} />
    </>
  )
}

/** What the bundle mounts for a preview: each state of the widget under its caption on a canvas, in the mode of the page. */
export function Preview({ states }: { states: readonly PreviewState[] }) {
  return (
    <StrictMode>
      <PageTheme>
        <TextProvider catalogs={catalogs} development>
          <Canvas>
            {/* Proposed: a preview whose states are screens, each in a frame, shows one state at a time, as one that
                answers the same paths in each state does, so each screen shows at its full size and a link opens it,
                see the review on #3650. */}
            {states.some((state) => state.fixtures) ||
            (states.length > 1 && states.every((state) => state.frameHeight !== undefined)) ? (
              <Chooser states={states} />
            ) : (
              states.map((state) => <Figure key={state.caption} state={state} />)
            )}
          </Canvas>
        </TextProvider>
      </PageTheme>
    </StrictMode>
  )
}
