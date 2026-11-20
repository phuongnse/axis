import { useCallback, useEffect, useMemo, useState } from 'react'
import { useLocation, useParams, useSearchParams } from 'react-router'
import { PageLayout } from '../platform/PageLayout'
import { PageLeaf, type PageLeafProps } from '../platform/PageLeaf'
import { detailRecordId, readPageParameters } from '../platform/pageParameters'
import { ProcessInstancePage } from '../platform/ProcessInstancePage'
import { RecordList, type RecordListValue } from '../platform/recordList'
import { isRecordPage } from '../platform/recordPages'
import { fetchPage, fetchRecordPage, type ApplicationSite, type PageMetadata } from '../platform/site'
import { siteAddress } from '../platform/siteLinks'
import { TaskPage } from '../platform/TaskPage'
import { useText } from '../platform/texts'
import { WidgetTree } from '../platform/WidgetTree'
import { leafWidgets } from '../platform/widgetTree'
import { useApplicationSite, useSiteLocale } from './context'
import { ShellError } from './LocaleLoadFailed'
import { NotFoundPage } from './NotFoundPage'

/** Which record a form page opens: a new one, or the one named by the `:id` segment. */
type FormMode = 'new' | 'edit'

// A record id in the hyphenated 8-4-4-4-12 hex form, as the record API accepts it.
const recordIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * Shows the page named by the `:page` segment inside the site shell. A page of a module is named by two segments,
 * `/{site}/{module id}/{name}`, which the route `:page/:id` also matches. A record id never looks like a page name, so
 * a second segment that is no record id names a page of the module `:page`.
 */
export function PageRoute({ form }: { form?: FormMode }) {
  const site = useApplicationSite()
  const locale = useSiteLocale()
  const { module, page: segment = '', id = '' } = useParams()
  const isModulePage = form === 'edit' && module === undefined && !recordIdPattern.test(id)
  const page = module ? `${module}/${segment}` : isModulePage ? `${segment}/${id}` : segment
  const mode = isModulePage ? undefined : form
  const origin = useOrigin(site)
  const load = useCallback((signal: AbortSignal) => fetchPage(site.path, page, signal), [site.path, page])
  // A new page or record starts from a clean state rather than showing the previous one meanwhile.
  return (
    <SitePage
      key={`${site.path}/${page}/${mode ?? ''}/${isModulePage ? '' : id}`}
      sitePath={site.path}
      pages={origin.pages}
      name={page}
      load={load}
      locale={locale}
      fallbackLocale={site.locales.fallback}
      form={mode}
      recordId={mode === 'edit' ? id : null}
      returnTo={origin.returnTo}
      trail={origin.trail}
    />
  )
}

/**
 * Shows the default record page of an entity, `/{site}/records/{entity}/{id}`, or `/{site}/records/{module id}/{name}/{id}`
 * for a module entity: the entity's form over every field, loaded from the record page route. An entity that does not
 * exist or that the person may not read names no page. See docs/reference/frontend.md#routing.
 */
export function RecordPageRoute() {
  const site = useApplicationSite()
  const locale = useSiteLocale()
  const { module, entity = '', id = '' } = useParams()
  const entityName = module ? `${module}/${entity}` : entity
  const origin = useOrigin(site)
  const load = useCallback(
    (signal: AbortSignal) => fetchRecordPage(site.path, entityName, signal),
    [site.path, entityName],
  )
  return (
    <SitePage
      key={`${site.path}/records/${entityName}/${id}`}
      sitePath={site.path}
      pages={origin.pages}
      name={`records/${entityName}`}
      load={load}
      locale={locale}
      fallbackLocale={site.locales.fallback}
      form="edit"
      recordId={id}
      returnTo={origin.returnTo}
      trail={origin.trail}
    />
  )
}

/** What a site page takes from where the person came from: the address to return to, the record list's trail and the site's pages. */
function useOrigin(site: ApplicationSite) {
  const location = useLocation()
  // The table that opened the form passes its address, with paging and sorting, in history state.
  // Without it, such as in a new tab, save and cancel go to the site, which opens its first page.
  const from: unknown = (location.state as { from?: unknown } | null)?.from
  const returnTo =
    typeof from === 'string' && from.startsWith(siteAddress(site.path, '')) ? from : siteAddress(site.path)
  const pages = useMemo(() => site.navigation.map((entry) => entry.page), [site.navigation])
  return { returnTo, pages, trail: { site, from: typeof from === 'string' ? from : null } }
}

/** The site and the address of the navigation page that opened this page, which the record list's link is built from. */
interface Trail {
  site: ApplicationSite
  from: string | null
}

/** The navigation entry of the page the person came from, when that is another page of the site. */
function navigationOrigin(trail: Trail, pageName: string) {
  const { site, from } = trail
  if (!from?.startsWith(siteAddress(site.path, ''))) {
    return undefined
  }
  const pathname = from.split(/[?#]/, 1)[0].toLowerCase()
  return site.navigation.find(
    (entry) =>
      entry.page.toLowerCase() !== pageName.toLowerCase() &&
      pathname === siteAddress(site.path, entry.page).toLowerCase(),
  )
}

/**
 * The list a record's page links back to: the navigation page the person came from, with its table's search, sort and
 * page, or else the site, which opens its first page and so is named after the first navigation entry.
 * See docs/reference/frontend.md#routing.
 */
function recordListFor(trail: Trail, pageName: string): RecordListValue {
  const { site, from } = trail
  const origin = navigationOrigin(trail, pageName)
  if (origin && from) {
    return { labelKey: origin.labelKey, to: from }
  }
  return { labelKey: site.navigation[0]?.labelKey ?? site.titleKey, to: siteAddress(site.path) }
}

interface SitePageProps {
  sitePath: string
  /** The names of the site's navigation pages, in navigation order. */
  pages: readonly string[]
  /** The page's key, for the log. */
  name: string
  /** Loads the page's metadata, or answers `null` when it names no page. */
  load: (signal: AbortSignal) => Promise<PageMetadata | null>
  locale: string
  fallbackLocale: string
  form?: FormMode
  recordId: string | null
  returnTo: string
  trail: Trail
}

function SitePage({
  sitePath,
  pages,
  name,
  load,
  locale,
  fallbackLocale,
  form,
  recordId,
  returnTo,
  trail,
}: SitePageProps) {
  const t = useText()
  const [searchParams] = useSearchParams()
  const [page, setPage] = useState<PageMetadata | null | 'failed'>()

  useEffect(() => {
    const controller = new AbortController()
    load(controller.signal)
      .then(setPage)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the page '${name}' failed`, error)
          setPage('failed')
        }
      })
    return () => controller.abort()
  }, [load, name])

  if (page === undefined) {
    return null
  }
  if (page === null) {
    return <NotFoundPage />
  }
  if (page === 'failed') {
    return <ShellError />
  }

  // A route check finds its widget anywhere in the tree, not only as the first widget.
  const leaves = leafWidgets(page.widgets)
  // A detail that the site names as its entity's record page takes its record from the id segment, so that segment
  // fills the detail's `record` parameter. A page with a form keeps its id for the form.
  const isForm = leaves.some((widget) => widget.type === 'form')
  const recordPageDetail =
    form === 'edit' && !isForm
      ? leaves.find(
          (widget) =>
            widget.type === 'detail' &&
            widget.entity !== null &&
            isRecordPage(sitePath, trail.site.recordPages, widget.entity.name, page.name),
        )
      : undefined
  const params = new URLSearchParams(searchParams)
  if (recordPageDetail?.detail && recordId !== null) {
    params.set(recordPageDetail.detail.record, recordId)
  }

  // A page whose required parameter is missing or malformed in the address names no page. This runs
  // before any widget renders, so no rows are requested.
  const pageValues = readPageParameters(params, page.parameters)
  if (pageValues === null) {
    return <NotFoundPage />
  }

  const taskInbox = leaves.find((widget) => widget.type === 'taskInbox')
  const processInspector = leaves.find((widget) => widget.type === 'processInspector')
  const details = leaves.filter((widget) => widget.type === 'detail')
  // A task's address is under its inbox page. `new` and an id in the wrong form are no task.
  if (taskInbox && form !== undefined) {
    if (recordId === null || !recordIdPattern.test(recordId)) {
      return <NotFoundPage />
    }
    return (
      <PageLayout title={t(page.titleKey)}>
        <TaskPage
          sitePath={sitePath}
          pageName={page.name}
          widget={taskInbox}
          taskId={recordId}
          locale={locale}
          fallbackLocale={fallbackLocale}
        />
      </PageLayout>
    )
  }
  // An instance's address is under its inspector page. `new` and an id in the wrong form are no instance.
  if (processInspector && form !== undefined) {
    if (recordId === null || !recordIdPattern.test(recordId)) {
      return <NotFoundPage />
    }
    // The instance page draws its own heading. It takes the page's title, and the list its not-found line links back to.
    return (
      <RecordList value={recordListFor(trail, page.name)}>
        <ProcessInstancePage
          sitePath={sitePath}
          pages={pages}
          widget={processInspector}
          instanceId={recordId}
          title={t(page.titleKey)}
          locale={locale}
          fallbackLocale={fallbackLocale}
        />
      </RecordList>
    )
  }
  // A detail shows the record its page parameter names. A value that is no record id names no record, so none is requested.
  if (details.some((widget) => !recordIdPattern.test(detailRecordId(widget, pageValues) ?? ''))) {
    return <NotFoundPage />
  }
  // A table page has no record segment. A form page and a detail record page always have one, and `new` only a form.
  const takesId = isForm || recordPageDetail !== undefined
  if (
    takesId !== (form !== undefined) ||
    (form === 'new' && !isForm) ||
    (recordId !== null && !recordIdPattern.test(recordId))
  ) {
    return <NotFoundPage />
  }

  const leafProps: PageLeafProps = {
    sitePath,
    pageName: page.name,
    pages,
    locale,
    fallbackLocale,
    pageValues,
    recordId,
    returnTo,
  }

  return (
    <PageLayout title={t(page.titleKey)}>
      <RecordList value={recordListFor(trail, page.name)}>
        <WidgetTree widgets={page.widgets} renderLeaf={(widget) => <PageLeaf widget={widget} {...leafProps} />} />
      </RecordList>
    </PageLayout>
  )
}
