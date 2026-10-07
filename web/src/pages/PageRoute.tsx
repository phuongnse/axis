import { PageContainer } from '@ant-design/pro-components'
import { useEffect, useState } from 'react'
import { useLocation, useParams } from 'react-router'
import { FormWidget } from '../platform/FormWidget'
import { fetchPage, type PageMetadata } from '../platform/site'
import { TableWidget } from '../platform/TableWidget'
import { useText } from '../platform/texts'
import { useApplicationSite, useSiteLocale } from './context'
import { ShellError } from './LocaleLoadFailed'
import { NotFoundPage } from './NotFoundPage'

/** Which record a form page opens: a new one, or the one named by the `:id` segment. */
type FormMode = 'new' | 'edit'

// A record id in the hyphenated 8-4-4-4-12 hex form, as the record API accepts it.
const recordIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

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
  // A new page or record starts from a clean state rather than showing the previous one meanwhile.
  return (
    <SitePage
      key={`${site.path}/${page}/${form ?? ''}/${id}`}
      sitePath={site.path}
      name={page}
      locale={locale}
      form={form}
      recordId={form === 'edit' ? id : null}
      returnTo={returnTo}
    />
  )
}

interface SitePageProps {
  sitePath: string
  name: string
  locale: string
  form?: FormMode
  recordId: string | null
  returnTo: string
}

function SitePage({ sitePath, name, locale, form, recordId, returnTo }: SitePageProps) {
  const t = useText()
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

  // A table page has no record segment, and a form page always has one.
  const widget = page.widgets[0]
  const isForm = widget?.type === 'form'
  if (isForm !== (form !== undefined) || (recordId !== null && !recordIdPattern.test(recordId))) {
    return <NotFoundPage />
  }
  return (
    <PageContainer title={t(page.titleKey)}>
      {widget?.type === 'table' && <TableWidget sitePath={sitePath} widget={widget} locale={locale} />}
      {widget?.type === 'form' && <FormWidget widget={widget} recordId={recordId} returnTo={returnTo} />}
    </PageContainer>
  )
}
