import { useEffect, useMemo, useState } from 'react'
import { useLocation, useParams, useSearchParams } from 'react-router'
import { FormWidget } from '../platform/FormWidget'
import { PageLayout } from '../platform/PageLayout'
import { readPageParameters } from '../platform/pageParameters'
import { ProcessInspectorWidget } from '../platform/ProcessInspectorWidget'
import { ProcessInstancePage } from '../platform/ProcessInstancePage'
import { fetchPage, type EntityWidgetMetadata, type PageMetadata, type WidgetMetadata } from '../platform/site'
import { TableWidget } from '../platform/TableWidget'
import { TaskInboxWidget } from '../platform/TaskInboxWidget'
import { TaskPage } from '../platform/TaskPage'
import { useText } from '../platform/texts'
import { useApplicationSite, useSiteLocale } from './context'
import { ShellError } from './LocaleLoadFailed'
import { NotFoundPage } from './NotFoundPage'

/** Which record a form page opens: a new one, or the one named by the `:id` segment. */
type FormMode = 'new' | 'edit'

// A record id in the hyphenated 8-4-4-4-12 hex form, as the record API accepts it.
const recordIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

// The compiler binds a form widget to an entity, never to a data source.
function hasEntity(widget: WidgetMetadata): widget is EntityWidgetMetadata {
  return widget.entity !== null
}

/** Shows the page named by the `:page` segment inside the site shell. */
export function PageRoute({ form }: { form?: FormMode }) {
  const site = useApplicationSite()
  const locale = useSiteLocale()
  const { page = '', id = '' } = useParams()
  const location = useLocation()
  // The table that opened the form passes its address, with paging and sorting, in history state.
  // Without it, such as in a new tab, save and cancel go to the site, which opens its first page.
  const from: unknown = (location.state as { from?: unknown } | null)?.from
  const returnTo = typeof from === 'string' && from.startsWith(`/${site.path}/`) ? from : `/${site.path}`
  const pages = useMemo(() => site.navigation.map((entry) => entry.page), [site.navigation])
  // A new page or record starts from a clean state rather than showing the previous one meanwhile.
  return (
    <SitePage
      key={`${site.path}/${page}/${form ?? ''}/${id}`}
      sitePath={site.path}
      pages={pages}
      name={page}
      locale={locale}
      fallbackLocale={site.locales.fallback}
      form={form}
      recordId={form === 'edit' ? id : null}
      returnTo={returnTo}
    />
  )
}

interface SitePageProps {
  sitePath: string
  /** The names of the site's navigation pages, in navigation order. */
  pages: readonly string[]
  name: string
  locale: string
  fallbackLocale: string
  form?: FormMode
  recordId: string | null
  returnTo: string
}

function SitePage({ sitePath, pages, name, locale, fallbackLocale, form, recordId, returnTo }: SitePageProps) {
  const t = useText()
  const [searchParams] = useSearchParams()
  const [page, setPage] = useState<PageMetadata | null | 'failed'>()

  useEffect(() => {
    const controller = new AbortController()
    fetchPage(sitePath, name, controller.signal)
      .then(setPage)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the page '${name}' failed`, error)
          setPage('failed')
        }
      })
    return () => controller.abort()
  }, [sitePath, name])

  if (page === undefined) {
    return null
  }
  if (page === null) {
    return <NotFoundPage />
  }
  if (page === 'failed') {
    return <ShellError />
  }

  // A page whose required parameter is missing or malformed in the address names no page. This runs
  // before any widget renders, so no rows are requested.
  const pageValues = readPageParameters(searchParams, page.parameters)
  if (pageValues === null) {
    return <NotFoundPage />
  }

  const widget = page.widgets[0]
  // A task's address is under its inbox page. `new` and an id in the wrong form are no task.
  if (widget?.type === 'taskInbox' && form !== undefined) {
    if (recordId === null || !recordIdPattern.test(recordId)) {
      return <NotFoundPage />
    }
    return (
      <PageLayout title={t(page.titleKey)}>
        <TaskPage
          sitePath={sitePath}
          pageName={page.name}
          widget={widget}
          taskId={recordId}
          locale={locale}
          fallbackLocale={fallbackLocale}
        />
      </PageLayout>
    )
  }
  // An instance's address is under its inspector page. `new` and an id in the wrong form are no instance.
  if (widget?.type === 'processInspector' && form !== undefined) {
    if (recordId === null || !recordIdPattern.test(recordId)) {
      return <NotFoundPage />
    }
    return (
      <PageLayout title={t(page.titleKey)}>
        <ProcessInstancePage
          sitePath={sitePath}
          widget={widget}
          instanceId={recordId}
          locale={locale}
          fallbackLocale={fallbackLocale}
        />
      </PageLayout>
    )
  }
  // A table page has no record segment, and a form page always has one.
  const isForm = widget?.type === 'form'
  if (isForm !== (form !== undefined) || (recordId !== null && !recordIdPattern.test(recordId))) {
    return <NotFoundPage />
  }
  return (
    <PageLayout title={t(page.titleKey)}>
      {widget?.type === 'table' && (
        <TableWidget sitePath={sitePath} widget={widget} locale={locale} pageValues={pageValues} />
      )}
      {widget?.type === 'form' && hasEntity(widget) && (
        <FormWidget
          widget={widget}
          sitePath={sitePath}
          pages={pages}
          recordId={recordId}
          returnTo={returnTo}
          locale={locale}
        />
      )}
      {widget?.type === 'taskInbox' && (
        <TaskInboxWidget
          sitePath={sitePath}
          pageName={page.name}
          widget={widget}
          locale={locale}
          fallbackLocale={fallbackLocale}
        />
      )}
      {widget?.type === 'processInspector' && (
        <ProcessInspectorWidget sitePath={sitePath} pageName={page.name} widget={widget} locale={locale} />
      )}
    </PageLayout>
  )
}
