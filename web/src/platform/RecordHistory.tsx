import { Alert, Card, Flex, List, Space, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { historySummary } from './historySummary'
import { fetchHistory, historyPageSize, type HistoryItem, type HistoryPage } from './records'
import type { FieldMetadata } from './site'
import { useText } from './texts'

interface RecordHistoryProps {
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

/** Who did what an entry records: the test user's name, System, Anonymous, or the raw actor id. */
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
 * shows its action, who did it, when, and a short summary. The page is the panel's own and stays
 * out of the URL. A failed load shows an error inside the panel and leaves the form working.
 */
export function RecordHistory({ recordsPath, recordId, fields, locale }: RecordHistoryProps) {
  const t = useText()
  const [page, setPage] = useState(1)
  const [state, setState] = useState<LoadState>({ failed: false })
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

  const time = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'medium' })
  const totalCount = state.result?.totalCount ?? 0

  return (
    <Card data-testid="record-history" title={t('shell.history.title')} size="small">
      {failed ? (
        <Alert data-testid="record-history-error" type="error" message={t('shell.history.loadFailed')} />
      ) : (
        <List<HistoryItem>
          loading={loading}
          dataSource={state.result?.items ?? []}
          locale={{ emptyText: t('shell.history.empty') }}
          pagination={
            totalCount > historyPageSize && {
              current: page,
              pageSize: historyPageSize,
              total: totalCount,
              showSizeChanger: false,
              onChange: setPage,
            }
          }
          renderItem={(item) => {
            const summary = historySummary(item, fields, t)
            return (
              <List.Item key={item.id} data-testid="history-entry">
                <Flex vertical gap={2}>
                  <Space wrap size="small">
                    <Typography.Text strong>
                      {textOr(t, `shell.history.action.${item.action}`, item.action)}
                    </Typography.Text>
                    <Typography.Text>{actorOf(item, t)}</Typography.Text>
                    <Typography.Text type="secondary">
                      <time dateTime={item.occurredAt}>{time.format(new Date(item.occurredAt))}</time>
                    </Typography.Text>
                  </Space>
                  {summary !== null && <Typography.Text type="secondary">{summary}</Typography.Text>}
                </Flex>
              </List.Item>
            )
          }}
        />
      )}
    </Card>
  )
}
