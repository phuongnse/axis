import { useEffect, useState, type HTMLAttributes } from 'react'
import { Link } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { EventTime } from '../platform/EventTime'
import { PersonName } from '../platform/PersonName'
import { StatusTag } from '../platform/StatusTag'
import { studioRequestAddress } from '../platform/studioAddress'
import {
  fetchAuthoringRequests,
  ServerUnreachableError,
  type AuthoringRequest,
  type AuthoringRequestState,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { recordPageSize, recordQuery, tablePageSizes } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'
import { authoringRequestStates, requestFields, requestTitle } from './studioAuthoringRequest'

// The table has no parameters of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

/** The states a filter on State offers. Fixing is left out, because a filter reads a fix round as Working. */
const filterStates = (Object.keys(authoringRequestStates) as AuthoringRequestState[]).filter(
  (state) => state !== 'fixing',
)

/**
 * The AI requests of the open application: one row for each request, newest first unless the view sorts them, paged on the
 * server. A row shows the request's name, which opens its page, its state as a status tag, who sent it and when. The table
 * has Search, Filter, Columns, Views, Refresh, a sort on State, Sent by and Sent and a pager with a page size, all kept in the
 * address, see docs/reference/ai-authoring-in-studio.md#the-request-panel.
 */
export function StudioAuthoringRequests({ application }: { application: string }) {
  const t = useText()
  const { generation, lost } = useStudioConnection()
  const { view, setView, saved } = useTableView(
    requestFields,
    recordPageSize,
    noParameters,
    true,
    true,
    true,
    '',
    `studio.${application}.requests`,
  )
  const query = recordQuery(view)
  const [requests, setRequests] = useState<{ items: AuthoringRequest[]; totalCount: number } | 'failed'>()
  const [reload, setReload] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    fetchAuthoringRequests(application, query, controller.signal).then(setRequests, (error: unknown) => {
      if (controller.signal.aborted) {
        return
      }
      if (error instanceof ServerUnreachableError) {
        lost()
        return
      }
      console.warn(`Loading the AI requests of '${application}' failed`, error)
      setRequests('failed')
    })
    return () => controller.abort()
  }, [application, query, generation, reload, lost])

  const columns: AxisColumn<AuthoringRequest>[] = [
    {
      key: 'title',
      kind: 'freeText',
      title: t('shell.studio.aiRequests.request'),
      render: (_, row) => (
        <Link data-testid="studio-ai-requests-link" to={studioRequestAddress(application, row.id)}>
          {requestTitle(row)}
        </Link>
      ),
    },
    {
      key: 'state',
      kind: 'fixed',
      title: t('shell.studio.aiRequests.state'),
      filter: {
        kind: 'enum',
        options: filterStates.map((state) => ({ value: state, label: t(authoringRequestStates[state].word) })),
      },
      sorter: true,
      render: (_, row) => (
        <StatusTag tone={authoringRequestStates[row.state].tone} data-testid="studio-ai-requests-state">
          {t(authoringRequestStates[row.state].word)}
        </StatusTag>
      ),
    },
    {
      key: 'author',
      kind: 'freeText',
      title: t('shell.studio.aiRequests.sentBy'),
      filter: { kind: 'text' },
      sorter: true,
      render: (_, row) => <PersonName id={row.author} name={row.authorName ?? row.author} />,
    },
    {
      key: 'sentAt',
      kind: 'fixed',
      title: t('shell.studio.aiRequests.sent'),
      filter: { kind: 'date' },
      sorter: true,
      render: (_, row) => <EventTime value={row.sentAt} />,
    },
  ]
  const list = typeof requests === 'object' ? requests : undefined

  return (
    <section data-testid="studio-ai-requests">
      <AxisTable<AuthoringRequest>
        columns={columns}
        rowKey={(row) => row.id}
        dataSource={list?.items ?? []}
        loading={requests === undefined}
        emptyText={t('shell.studio.aiRequests.empty')}
        view={{
          query: view,
          onChange: setView,
          saved,
          pageSizes: tablePageSizes(),
          total: list?.totalCount ?? 0,
          search: true,
          columns: true,
        }}
        toolbar={{ onRefresh: () => setReload((value) => value + 1) }}
        failure={
          requests === 'failed' ? (
            <AxisAlert
              data-testid="studio-ai-requests-error"
              kind="error"
              message={t('shell.studio.aiRequests.loadFailed')}
            />
          ) : undefined
        }
        rowAttributes={(row) =>
          ({ 'data-testid': 'studio-ai-requests-row', 'data-id': row.id }) as HTMLAttributes<HTMLElement>
        }
      />
    </section>
  )
}
