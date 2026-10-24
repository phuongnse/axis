import { useDroppable } from '@dnd-kit/core'
import { Alert, Card, Segmented, Typography } from 'antd'
import { useContext, useEffect, useLayoutEffect, useMemo, useState, type ReactNode, type SyntheticEvent } from 'react'
import { useLocation } from 'react-router'
import { setApiGuard } from '../platform/api'
import { getAt } from '../platform/jsonPatch'
import { childLists, widgetSummary } from '../platform/pageWidgets'
import { PageLayout } from '../platform/PageLayout'
import { PageLeaf } from '../platform/PageLeaf'
import { placeholderApi, placeholderPageValues } from '../platform/placeholderApi'
import type { PageWidgetMetadata, PlaceholderWidgetMetadata } from '../platform/site'
import {
  fetchStudioPageMetadata,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioNames,
  type StudioPageMetadataAnswer,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { StudioNamesContext } from '../platform/studioNames'
import { useText, useTextCatalogs, type TextCatalog } from '../platform/texts'
import { TextProvider } from '../platform/TextProvider'
import { useDesignTokens } from '../platform/themeMode'
import { PhoneWidthOverride } from '../platform/usePhoneWidth'
import { WidgetTree } from '../platform/WidgetTree'
import { PlatformContext } from './context'

/** How long after the last edit the canvas asks for the draft's metadata. */
const debounceMs = 300

/** The route's latest answer, or `'failed'`. `undefined` before the first. */
type Answer = StudioPageMetadataAnswer | 'failed' | undefined

type CanvasWidth = 'desktop' | 'phone'

/** The width of the page at phone width, in pixels. */
const phoneWidth = 375

/** A click or a submit on the canvas runs nothing: not a link, not a button and not a form. */
function cancel(event: SyntheticEvent) {
  event.preventDefault()
  event.stopPropagation()
}

const frameSelector = '[data-testid="studio-canvas-widget"]'

/**
 * The application's own texts, from Studio's names: the platform locale when the application has it, otherwise its
 * first locale, which is also the fallback. The platform texts come after, through the outer catalogs.
 */
function canvasTexts(names: StudioNames | undefined, platformLocale: string | undefined) {
  const fallbackLocale = names?.locales[0]
  if (!names || fallbackLocale === undefined) {
    return null
  }
  const locale =
    platformLocale !== undefined && names.locales.includes(platformLocale) ? platformLocale : fallbackLocale
  const catalog: TextCatalog = { texts: {}, fallbackTexts: {} }
  for (const item of names.texts) {
    if (Object.hasOwn(item.texts, locale)) {
      catalog.texts[item.key] = item.texts[locale]
    }
    if (Object.hasOwn(item.texts, fallbackLocale)) {
      catalog.fallbackTexts[item.key] = item.texts[fallbackLocale]
    }
  }
  return { catalog, locale, fallbackLocale }
}

/**
 * Draws the unsaved draft of a page with the site's own widgets. It posts the draft to the draft page metadata
 * route 300 ms after the last edit and redraws from the answer. While it is mounted, the widgets' `/api/apps/`
 * calls get placeholder data in the browser and none goes to the server. A click or a submit on the canvas does
 * nothing. A widget that does not compile draws as a placeholder with its diagnostics. A widget that a diagnostic
 * names is marked. The width switch draws the page at phone width, 375 px, with the phone layout. A click selects the
 * innermost widget under the pointer. Each widget is a Tab stop, which Enter or Space selects, and its name is its
 * type and summary from the draft. The widget at `selected` is outlined and has `aria-current`. Inside a drag context
 * every widget with a path and every widget list is a drop target, and a placeholder that is a container has a drop
 * area for each list its draft has.
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

  const frameList = (pointer: string, content: ReactNode) => (
    <CanvasListFrame pointer={pointer} empty={isEmptyList(draft, pointer)}>
      {content}
    </CanvasListFrame>
  )

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
                    <PlaceholderBox
                      widget={widget}
                      draft={draft}
                      diagnostics={diagnosticsAt(diagnostics, widget.path)}
                    />
                  )}
                  frameWidget={frameWidget}
                  frameList={frameList}
                />
              </CanvasPage>
            </div>
          </PhoneWidthOverride.Provider>
        )}
      </div>
    </TextProvider>
  )
}

/** The diagnostics that name the widget at `path`, or a path inside it. None when the widget has no path. */
function diagnosticsAt(diagnostics: StudioDiagnostic[], path: string | undefined): StudioDiagnostic[] {
  if (path === undefined) {
    return []
  }
  return diagnostics.filter((diagnostic) => diagnostic.path === path || diagnostic.path.startsWith(`${path}/`))
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

/** Whether the draft has a widget list at `pointer` with no widget in it. */
function isEmptyList(draft: Record<string, unknown>, pointer: string): boolean {
  const list = getAt(draft, pointer)
  return Array.isArray(list) && list.length === 0
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
  const { setNodeRef } = useDroppable({
    id: `widget:${path}`,
    data: { kind: 'widget', pointer: path },
    disabled: path === undefined,
  })
  return (
    <div
      ref={setNodeRef}
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

/**
 * A widget list of the canvas, and a drop target for a palette type: a drop on it, outside any widget of the list,
 * appends to the list. It has a dashed border while a drag is over it or while the list is empty, and a minimum height
 * so that an empty list can be hit.
 */
function CanvasListFrame({ pointer, empty, children }: { pointer: string; empty: boolean; children?: ReactNode }) {
  const { colour, radius } = useDesignTokens()
  const { setNodeRef, isOver } = useDroppable({ id: `list:${pointer}`, data: { kind: 'list', pointer } })
  return (
    <div
      ref={setNodeRef}
      data-testid="studio-canvas-list"
      data-list={pointer}
      style={{
        minHeight: 32,
        border: `1px dashed ${isOver ? colour.primary : empty ? colour.border : 'transparent'}`,
        borderRadius: radius.base,
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

function CanvasAlert({ testId, textKey }: { testId: string; textKey: string }) {
  const t = useText()
  return <Alert data-testid={testId} type="warning" title={t(textKey)} />
}

/**
 * A widget that does not compile: a box with its type and a line for each of its diagnostics, and a drop area for
 * each widget list its draft has.
 */
function PlaceholderBox({
  widget,
  draft,
  diagnostics,
}: {
  widget: PlaceholderWidgetMetadata
  draft: Record<string, unknown>
  diagnostics: StudioDiagnostic[]
}) {
  const t = useText()
  const draftWidget = getAt(draft, widget.path)
  // The widgets inside are not drawn, but the author can still drop into the lists the draft has.
  const lists = isContent(draftWidget) ? childLists(draftWidget, widget.path) : []
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
      {lists.map((list) => (
        <CanvasListFrame key={list.pointer} pointer={list.pointer} empty={isEmptyList(draft, list.pointer)} />
      ))}
    </Card>
  )
}
