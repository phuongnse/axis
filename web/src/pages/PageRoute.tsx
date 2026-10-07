import { PageContainer } from '@ant-design/pro-components'
import { Empty } from 'antd'
import { useEffect, useState } from 'react'
import { useParams } from 'react-router'
import { fetchPage, type PageMetadata } from '../platform/site'
import { TableWidget } from '../platform/TableWidget'
import { useText } from '../platform/texts'
import { useApplicationSite, useSiteLocale } from './context'
import { ShellError } from './LocaleLoadFailed'
import { NotFoundPage } from './NotFoundPage'

/** Shows the page named by the `:page` segment inside the site shell. */
export function PageRoute() {
  const site = useApplicationSite()
  const locale = useSiteLocale()
  const { page = '' } = useParams()
  // A new page starts from a clean state rather than showing the previous page meanwhile.
  return <SitePage key={`${site.path}/${page}`} sitePath={site.path} name={page} locale={locale} />
}

function SitePage({ sitePath, name, locale }: { sitePath: string; name: string; locale: string }) {
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

  // Forms come with #53.
  const widget = page.widgets[0]
  return (
    <PageContainer title={t(page.titleKey)}>
      {widget?.type === 'table' && <TableWidget sitePath={sitePath} widget={widget} locale={locale} />}
      {widget?.type === 'form' && (
        <Empty data-testid="form-placeholder" description={t('shell.widget.form.unavailable')} />
      )}
    </PageContainer>
  )
}
