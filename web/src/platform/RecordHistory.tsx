import { Alert, Card, Empty, Flex, Pagination, Space, Spin, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { historySummary } from './historySummary'
import { inspectTarget } from './inspection'
import { markCurrentPage } from './pagination'
import { fetchHistory, historyPageSize, type HistoryItem, type HistoryPage } from './records'
import { fetchPage, type FieldMetadata } from './site'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

interface RecordHistoryProps {
  /** The path of the application site the form is on, for the links to its inspector pages. */
  sitePath: string
  /** The names of the site's navigation pages, in navigation order. */
  pages: readonly string[]
  recordsPath: string
  recordId: string
  /** The entity's fields, to show the labels of the fields an entry names. */
  fields: readonly FieldMetadata[]
  /** The UI locale, for showing each entry's time. */
  locale: string
}

/** The outcome of the last finished request. */
interface LoadState {
  /** The page requested, so a result for an older request shows as loading. */
  page?: number
  /** The last page loaded. It stays while the next page loads. */
  result?: HistoryPage
  failed: boolean
}

/** The text of `key`, or `fallback` when no catalog has the key. */
function textOr(t: (key: string) => string, key: string, fallback: string): string {
  const text = t(key)
  return text === key ? fallback : text
}

/** Who did what an entry records: the user's name, System, Anonymous, or the raw actor id. */
function actorOf({ actor, actorName }: HistoryItem, t: (key: string) => string): string {
  if (actorName !== null) {
    return actorName
  }
  if (actor === 'system') {
    return t('shell.history.system')
  }
  return actor === 'anonymous' ? t('shell.history.anonymous') : actor
}

/**
 * The history panel of a saved record: its audit records, newest first, 20 to a page. Each entry
 * shows its action, who did it, when, and a short summary. An entry with an `inspectPath` also links
 * to the instance on the first navigation page whose process inspector names its process. The panel
 * loads the pages' metadata only when an entry on screen has an `inspectPath`. The page is the panel's own and stays
 * out of the URL. A failed load shows an error inside the panel and leaves the form working.
 */
export function RecordHistory({ sitePath, pages, recordsPath, recordId, fields, locale }: RecordHistoryProps) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [page, setPage] = useState(1)
  const [state, setState] = useState<LoadState>({ failed: false })
  // The inspector page of each process, once loaded.
  const [inspectors, setInspectors] = useState<ReadonlyMap<string, string>>()
  const loading = state.page !== page
  const failed = state.failed && !loading

  useEffect(() => {
    const controller = new AbortController()
    fetchHistory(recordsPath, recordId, page, controller.signal)
      .then((result) => setState({ page, result, failed: false }))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading the history of '${recordId}' failed`, error)
          setState({ page, failed: true })
        }
      })
    return () => controller.abort()
  }, [recordsPath, recordId, page])

  const wantsLinks = state.result?.items.some((item) => typeof item.inspectPath === 'string') ?? false
  const inspectorsLoaded = inspectors !== undefined

  useEffect(() => {
    if (!wantsLinks || inspectorsLoaded) {
      return
    }
    const controller = new AbortController()
    void Promise.allSettled(pages.map((name) => fetchPage(sitePath, name, controller.signal))).then((results) => {
      if (controller.signal.aborted) {
        return
      }
      const found = new Map<string, string>()
      results.forEach((result, index) => {
        if (result.status === 'rejected') {
          console.warn(`Loading the page '${pages[index]}' for the history links failed`, result.reason)
          return
        }
        const widget = result.value?.widgets[0]
        if (widget?.type === 'processInspector' && widget.process !== null && !found.has(widget.process)) {
          found.set(widget.process, pages[index])
        }
      })
      setInspectors(found)
    })
    return () => controller.abort()
  }, [wantsLinks, inspectorsLoaded, sitePath, pages])

  const time = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'medium' })
  const items = state.result?.items ?? []
  const totalCount = state.result?.totalCount ?? 0

  return (
    <Card data-testid="record-history" aria-busy={loading} title={t('shell.history.title')} size="small">
      {failed ? (
        <Alert data-testid="record-history-error" type="error" title={t('shell.history.loadFailed')} />
      ) : (
        <Spin spinning={loading}>
          {!loading && items.length === 0 ? (
            <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('shell.history.empty')} />
          ) : (
            <Flex vertical>
              {items.map((item) => {
                const summary = historySummary(item, fields, t)
                const target = typeof item.inspectPath === 'string' ? inspectTarget(item.inspectPath) : null
                const inspectorPage = target === null ? undefined : inspectors?.get(target.process)
                return (
                  <Flex
                    key={item.id}
                    vertical
                    gap={2}
                    data-testid="history-entry"
                    style={{ paddingBlock: spacing.sm, borderBlockEnd: `1px solid ${colour.divider}` }}
                  >
                    <Space wrap size="small">
                      <Typography.Text strong>
                        {textOr(t, `shell.history.action.${item.action}`, item.action)}
                      </Typography.Text>
                      <Typography.Text>{actorOf(item, t)}</Typography.Text>
                      <Typography.Text type="secondary">
                        <time dateTime={item.occurredAt}>{time.format(new Date(item.occurredAt))}</time>
                      </Typography.Text>
                      {target !== null && inspectorPage !== undefined && (
                        <Link
                          to={`/${sitePath}/${inspectorPage.toLowerCase()}/${target.instanceId}`}
                          data-testid="history-inspect-link"
                        >
                          {t('shell.history.openInstance')}
                        </Link>
                      )}
                    </Space>
                    {summary !== null && <Typography.Text type="secondary">{summary}</Typography.Text>}
                  </Flex>
                )
              })}
            </Flex>
          )}
          {totalCount > historyPageSize && (
            <Flex justify="end" style={{ marginTop: spacing.lg }}>
              <Pagination
                current={page}
                pageSize={historyPageSize}
                total={totalCount}
                showSizeChanger={false}
                itemRender={markCurrentPage(page)}
                onChange={setPage}
              />
            </Flex>
          )}
        </Spin>
      )}
    </Card>
  )
}
