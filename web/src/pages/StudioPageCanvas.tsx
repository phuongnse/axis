import { Alert, Card, Typography } from 'antd'
import { useContext, useEffect, useLayoutEffect, useMemo, useState, type ReactNode, type SyntheticEvent } from 'react'
import { useLocation } from 'react-router'
import { setApiGuard } from '../platform/api'
import { PageLayout } from '../platform/PageLayout'
import { PageLeaf } from '../platform/PageLeaf'
import { placeholderApi, placeholderPageValues } from '../platform/placeholderApi'
import type { PageMetadata, PlaceholderWidgetMetadata } from '../platform/site'
import { fetchStudioPageMetadata, ServerUnreachableError, type StudioNames } from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { StudioNamesContext } from '../platform/studioNames'
import { useText, useTextCatalogs, type TextCatalog } from '../platform/texts'
import { TextProvider } from '../platform/TextProvider'
import { WidgetTree } from '../platform/WidgetTree'
import { PlatformContext } from './context'

/** How long after the last edit the canvas asks for the draft's metadata. */
const debounceMs = 300

/** The route's latest answer: the page, `null` when it cannot be drawn, or `'failed'`. `undefined` before the first. */
type Answer = PageMetadata | null | 'failed' | undefined

/** A click or a submit on the canvas runs nothing: not a link, not a button and not a form. */
function cancel(event: SyntheticEvent) {
  event.preventDefault()
  event.stopPropagation()
}

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
 * nothing. A widget that does not compile draws as a plain box with its type.
 */
export function StudioPageCanvas({
  application,
  resourceId,
  draft,
}: {
  application: string
  resourceId: string
  draft: Record<string, unknown>
}) {
  const outer = useTextCatalogs()
  const names = useContext(StudioNamesContext)
  const platformLocale = useContext(PlatformContext)?.current.locale
  const { pathname } = useLocation()
  const { generation, lost } = useStudioConnection()
  const [answer, setAnswer] = useState<Answer>()
  const page = typeof answer === 'object' && answer !== null ? answer : null

  useEffect(() => {
    const controller = new AbortController()
    const timer = setTimeout(() => {
      fetchStudioPageMetadata(application, resourceId, draft, controller.signal).then(
        (result) => setAnswer(result.page),
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

  return (
    <TextProvider catalogs={catalogs} development={outer.development}>
      <div data-testid="studio-page-canvas" onClickCapture={cancel} onSubmitCapture={cancel}>
        {answer === null && <CanvasAlert testId="studio-canvas-cannot-draw" textKey="shell.studio.design.cannotDraw" />}
        {answer === 'failed' && <CanvasAlert testId="studio-canvas-failed" textKey="shell.studio.design.loadFailed" />}
        {page && (
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
              renderPlaceholder={(widget) => <PlaceholderBox widget={widget} />}
            />
          </CanvasPage>
        )}
      </div>
    </TextProvider>
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

/** A widget that does not compile, drawn as a plain box with its type. */
function PlaceholderBox({ widget }: { widget: PlaceholderWidgetMetadata }) {
  const t = useText()
  return (
    <Card size="small" data-testid="studio-canvas-placeholder" data-path={widget.path}>
      <Typography.Text type="secondary">
        {t('shell.studio.design.placeholder')}
        {widget.type === null ? '' : `: ${widget.type}`}
      </Typography.Text>
    </Card>
  )
}
