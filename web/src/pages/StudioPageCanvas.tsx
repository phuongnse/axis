import { Card, Segmented, Typography } from 'antd'
import { useContext, useEffect, useLayoutEffect, useMemo, useState, type ReactNode, type SyntheticEvent } from 'react'
import { useLocation } from 'react-router'
import { setApiGuard } from '../platform/api'
import { getAt } from '../platform/jsonPatch'
import { widgetSummary } from '../platform/pageWidgets'
import { PageLayout } from '../platform/PageLayout'
import { PageLeaf } from '../platform/PageLeaf'
import { placeholderApi, placeholderPageValues } from '../platform/placeholderApi'
import type { PageWidgetMetadata, PlaceholderWidgetMetadata } from '../platform/site'
import {
  fetchStudioPageMetadata,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioPageMetadataAnswer,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { StudioNamesContext } from '../platform/studioNames'
import { useText, useTextCatalogs } from '../platform/texts'
import { TextProvider } from '../platform/TextProvider'
import { useDesignTokens } from '../platform/themeMode'
import { PhoneWidthOverride } from '../platform/usePhoneWidth'
import { WidgetTree } from '../platform/WidgetTree'
import { PlatformContext } from './context'
import { CanvasAlert } from './CanvasAlert'
import { canvasTexts, cancel, debounceMs, diagnosticsAt } from './studioCanvas'

/** The route's latest answer, or `'failed'`. `undefined` before the first. */
type Answer = StudioPageMetadataAnswer | 'failed' | undefined

type CanvasWidth = 'desktop' | 'phone'

/** The width of the page at phone width, in pixels. */
const phoneWidth = 375

const frameSelector = '[data-testid="studio-canvas-widget"]'

/**
 * Draws the unsaved draft of a page with the site's own widgets. It posts the draft to the draft page metadata
 * route 300 ms after the last edit and redraws from the answer. While it is mounted, the widgets' `/api/apps/`
 * calls get placeholder data in the browser and none goes to the server. A click or a submit on the canvas does
 * nothing. A widget that does not compile draws as a placeholder with its diagnostics. A widget that a diagnostic
 * names is marked. The width switch draws the page at phone width, 375 px, with the phone layout. A click selects the
 * innermost widget under the pointer. Each widget is a Tab stop, which Enter or Space selects, and its name is its
 * type and summary from the draft. The widget at `selected` is outlined and has `aria-current`.
 */
export function StudioPageCanvas({
  application,
  resourceId,
  draft,
  selected,
  onSelect,
}: {
  application: string
  resourceId: string
  draft: Record<string, unknown>
  /** The pointer of the selected widget. */
  selected: string
  onSelect: (path: string) => void
}) {
  const outer = useTextCatalogs()
  const names = useContext(StudioNamesContext)
  const platformLocale = useContext(PlatformContext)?.current.locale
  const { pathname } = useLocation()
  const { generation, lost } = useStudioConnection()
  const [answer, setAnswer] = useState<Answer>()
  const [width, setWidth] = useState<CanvasWidth>('desktop')
  const phone = width === 'phone'
  const drawn = answer === undefined || answer === 'failed' ? undefined : answer
  const page = drawn?.page ?? null
  const diagnostics = drawn?.diagnostics ?? []

  useEffect(() => {
    const controller = new AbortController()
    const timer = setTimeout(() => {
      fetchStudioPageMetadata(application, resourceId, draft, controller.signal).then(
        (result) => setAnswer(result),
        (error: unknown) => {
          if (controller.signal.aborted) {
            return
          }
          if (error instanceof ServerUnreachableError) {
            lost()
            return
          }
          console.warn(`Drawing the page '${resourceId}' failed`, error)
          setAnswer('failed')
        },
      )
    }, debounceMs)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [application, resourceId, draft, generation, lost])

  // A layout effect, because the widgets fetch in passive effects. A child's effect runs before its parent's, so a
  // passive effect here would let the widgets' first requests reach the server. Layout effects run before any of them.
  useLayoutEffect(() => {
    if (page) {
      setApiGuard(placeholderApi(page))
      return () => setApiGuard(null)
    }
  }, [page])

  const texts = useMemo(() => canvasTexts(names, platformLocale), [names, platformLocale])
  const catalogs = useMemo(() => (texts ? [texts.catalog, ...outer.catalogs] : outer.catalogs), [texts, outer.catalogs])
  const pageValues = useMemo(() => placeholderPageValues(page?.parameters ?? []), [page])
  const pages = useMemo(() => names?.pages.map((entry) => entry.name) ?? [], [names])

  const frameWidget = (widget: PageWidgetMetadata | PlaceholderWidgetMetadata, content: ReactNode) => {
    const path = widget.path
    const draftWidget = path === undefined ? undefined : getAt(draft, path)
    return (
      <CanvasWidgetFrame
        type={widget.type}
        path={path}
        summary={isContent(draftWidget) ? widgetSummary(draftWidget) : ''}
        marked={diagnosticsAt(diagnostics, path).length > 0}
        selected={path !== undefined && path === selected}
        onSelect={path === undefined ? undefined : () => onSelect(path)}
      >
        {content}
      </CanvasWidgetFrame>
    )
  }

  function click(event: SyntheticEvent) {
    cancel(event)
    const path = (event.target as HTMLElement).closest<HTMLElement>(frameSelector)?.dataset.path
    if (path !== undefined) {
      onSelect(path)
    }
  }

  return (
    <TextProvider catalogs={catalogs} development={outer.development}>
      <WidthSwitch value={width} onChange={setWidth} />
      <div data-testid="studio-page-canvas" onClickCapture={click} onSubmitCapture={cancel}>
        {drawn?.page === null && (
          <CanvasAlert testId="studio-canvas-cannot-draw" textKey="shell.studio.design.cannotDraw" />
        )}
        {answer === 'failed' && <CanvasAlert testId="studio-canvas-failed" textKey="shell.studio.design.loadFailed" />}
        {page && (
          <PhoneWidthOverride.Provider value={phone ? true : undefined}>
            <div data-testid="studio-canvas-frame" style={{ width: phone ? phoneWidth : '100%' }}>
              <CanvasPage title={page.titleKey}>
                <WidgetTree
                  widgets={page.widgets}
                  renderLeaf={(widget) => (
                    <PageLeaf
                      widget={widget}
                      sitePath={application}
                      pageName={page.name}
                      pages={pages}
                      locale={texts?.locale ?? platformLocale ?? 'en'}
                      fallbackLocale={texts?.fallbackLocale ?? 'en'}
                      pageValues={pageValues}
                      recordId={null}
                      returnTo={pathname}
                    />
                  )}
                  renderPlaceholder={(widget) => (
                    <PlaceholderBox widget={widget} diagnostics={diagnosticsAt(diagnostics, widget.path)} />
                  )}
                  frameWidget={frameWidget}
                />
              </CanvasPage>
            </div>
          </PhoneWidthOverride.Provider>
        )}
      </div>
    </TextProvider>
  )
}

function WidthSwitch({ value, onChange }: { value: CanvasWidth; onChange: (width: CanvasWidth) => void }) {
  const t = useText()
  return (
    <Segmented
      aria-label={t('shell.studio.design.width')}
      value={value}
      onChange={onChange}
      options={[
        { value: 'desktop', label: t('shell.studio.design.desktop') },
        { value: 'phone', label: t('shell.studio.design.phone') },
      ]}
    />
  )
}

function isContent(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * A widget of the canvas. It has a border in the error colour when a diagnostic names it, and `data-marked` then. A
 * widget with a path is a Tab stop that Enter or Space selects. The selected one has an outline in the primary colour
 * and `aria-current`. Its name is its summary, or its type, and "has diagnostics" when marked.
 */
function CanvasWidgetFrame({
  type,
  path,
  summary,
  marked,
  selected,
  onSelect,
  children,
}: {
  type: string | null
  path: string | undefined
  summary: string
  marked: boolean
  selected: boolean
  onSelect?: () => void
  children: ReactNode
}) {
  const t = useText()
  const { colour, radius } = useDesignTokens()
  const label = summary || (type ?? t('shell.studio.design.placeholder'))
  return (
    <div
      role="group"
      data-testid="studio-canvas-widget"
      data-path={path}
      data-marked={marked ? 'true' : undefined}
      aria-label={marked ? `${label} · ${t('shell.studio.design.hasDiagnostics')}` : label}
      aria-current={selected ? 'true' : undefined}
      tabIndex={path === undefined ? undefined : 0}
      onKeyDown={(event) => {
        if (onSelect && event.target === event.currentTarget && (event.key === 'Enter' || event.key === ' ')) {
          event.preventDefault()
          onSelect()
        }
      }}
      style={{
        border: `1px solid ${marked ? colour.danger : 'transparent'}`,
        borderRadius: radius.base,
        outline: selected ? `2px solid ${colour.primary}` : undefined,
        outlineOffset: 2,
      }}
    >
      {children}
    </div>
  )
}

// The title is a key of the application's texts, so it resolves inside the canvas's TextProvider.
function CanvasPage({ title, children }: { title: string; children: ReactNode }) {
  const t = useText()
  return <PageLayout title={t(title)}>{children}</PageLayout>
}

/** A widget that does not compile: a box with its type and a line for each of its diagnostics. */
function PlaceholderBox({
  widget,
  diagnostics,
}: {
  widget: PlaceholderWidgetMetadata
  diagnostics: StudioDiagnostic[]
}) {
  const t = useText()
  return (
    <Card size="small" data-testid="studio-canvas-placeholder" data-path={widget.path}>
      <Typography.Text type="secondary">
        {t('shell.studio.design.placeholder')}
        {widget.type === null ? '' : `: ${widget.type}`}
      </Typography.Text>
      <ul>
        {diagnostics.map((diagnostic, index) => (
          <li key={index} data-testid="studio-canvas-placeholder-diagnostic">
            {`${diagnostic.code}: ${diagnostic.message}`}
          </li>
        ))}
      </ul>
    </Card>
  )
}
