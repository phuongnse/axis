import { Flex, Modal, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { useEffect, useRef, useState } from 'react'
import { fetchCellJobs, runCellJobAgain, type CellJob, type CellJobPage } from '../platform/cell'
import { CellJobTable } from '../platform/CellJobTable'
import { PageLayout } from '../platform/PageLayout'
import { newIdempotencyKey } from '../platform/processes'
import { recordPageSize, recordQuery, tablePageSizes } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useFollowCellJob } from '../platform/useFollowCellJob'
import { useTableView } from '../platform/useTableView'
import { usePlatform } from './context'
import { useCellConsole } from './cellConsoleContext'

/** The columns the view's filter and sort can name, in table order, see `docs/reference/tenant-lifecycle.md#routes`. */
const jobFields = [
  { name: 'createdAt', type: 'date' },
  { name: 'kind', type: 'enum' },
  { name: 'tenant', type: 'text' },
  { name: 'state', type: 'enum' },
  { name: 'steps', type: 'text' },
  { name: 'error', type: 'text' },
] as const
// The job table has no parameters of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

/**
 * The Jobs view of the cell console: the jobs of the cell, newest first unless sorted, with their steps and errors. The server
 * searches, filters, sorts and pages the list. The table has Search, which looks in the tenant and the error, Filter, Columns,
 * Refresh, a sort on Created, Kind, Tenant and State, and a pager with a page size, all kept in the address, see
 * `docs/reference/cell-console.md#views` and `docs/reference/actions-and-controls.md#tables`. A failed job
 * other than a `purge` job offers Run again, behind a confirmation that names the job's kind and tenant. Confirming queues a
 * new job with an `Idempotency-Key`, which stays for a retry after a network error or a 5xx and goes with a final answer.
 * The page puts the new job first on the shown page and follows it every 2 seconds until it ends, then loads the page again.
 */
export function CellJobsPage() {
  const t = useText()
  const { size } = useDesignTokens()
  const { locale } = usePlatform().current
  const { onRefused } = useCellConsole()
  const { view, setView } = useTableView(jobFields, recordPageSize, noParameters, true, true, true)
  const query = recordQuery(view)
  const [jobs, setJobs] = useState<CellJobPage | 'failed'>()
  const [confirming, setConfirming] = useState<CellJob | null>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  // The job to follow. Setting it again to the same id does not restart the check, so a queued job sets it once.
  const [following, setFollowing] = useState<string | null>(null)
  // Counts the jobs that ended, so the list loads again.
  const [reload, setReload] = useState(0)
  // The Idempotency-Key of each job's request that got no final answer, by the id of the failed job, so a retry cannot
  // queue a second job.
  const actionKeys = useRef(new Map<string, string>())

  useEffect(() => {
    const controller = new AbortController()
    fetchCellJobs(query, controller.signal).then(
      (result) => {
        if (result.kind === 'ok') {
          setJobs(result.value)
        } else {
          onRefused(result.kind)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the cell jobs failed', error)
          setJobs('failed')
        }
      },
    )
    return () => controller.abort()
  }, [onRefused, query, reload])

  useFollowCellJob(following, {
    onJob: (job) =>
      setJobs((current) => withItems(current, (items) => items.map((item) => (item.id === job.id ? job : item)))),
    onEnd: () => {
      setFollowing(null)
      setReload((count) => count + 1)
    },
    onRefused,
  })

  async function runAgain(job: CellJob) {
    setBusy(true)
    setActionError(null)
    try {
      // A key is kept only while its request has no final answer: a network error or a 5xx.
      const idempotencyKey = actionKeys.current.get(job.id) ?? newIdempotencyKey()
      actionKeys.current.set(job.id, idempotencyKey)
      let result
      try {
        result = await runCellJobAgain(job.id, idempotencyKey)
      } catch (error: unknown) {
        console.warn(`Running the cell job '${job.id}' again failed`, error)
        setActionError(t('shell.cell.tenant.actionFailed'))
        return
      }
      actionKeys.current.delete(job.id)
      if (result.kind === 'ok') {
        setConfirming(null)
        setJobs((current) =>
          withItems(current, (items) => [result.value, ...items.filter((item) => item.id !== result.value.id)]),
        )
        setFollowing(result.value.id)
      } else if (result.kind === 'problem') {
        setActionError(t('shell.cell.tenant.actionRefused').replace('{title}', result.title))
      } else {
        onRefused(result.kind)
      }
    } finally {
      setBusy(false)
    }
  }

  const page = typeof jobs === 'object' ? jobs : undefined

  return (
    <PageLayout title={t('shell.cell.jobs.title')}>
      <CellJobTable
        jobs={page?.items ?? []}
        locale={locale}
        linkTenants
        loading={jobs === undefined}
        view={{
          query: view,
          onChange: setView,
          pageSizes: tablePageSizes(),
          total: page?.totalCount ?? 0,
          search: true,
          columns: true,
        }}
        onRefresh={() => setReload((count) => count + 1)}
        failure={
          jobs === 'failed' ? (
            <AxisAlert data-testid="cell-jobs-error" kind="error" message={t('shell.cell.jobs.loadFailed')} />
          ) : undefined
        }
        onRunAgain={(job) => {
          setActionError(null)
          setConfirming(job)
        }}
      />
      {confirming && (
        <Modal
          open
          width={size.dialog.small}
          data-testid="cell-jobs-confirm"
          title={t('shell.cell.jobs.runAgainConfirm')
            .replace('{kind}', confirming.kind)
            .replace('{tenant}', confirming.tenantId)}
          okText={t('shell.cell.jobs.runAgainOk')}
          cancelText={t('shell.users.roles.cancel')}
          confirmLoading={busy}
          onOk={() => void runAgain(confirming)}
          onCancel={() => setConfirming(null)}
        >
          <Flex vertical gap="small">
            <Typography.Text>{t('shell.cell.jobs.runAgainBody')}</Typography.Text>
            {actionError && <AxisAlert data-testid="cell-jobs-action-error" kind="error" message={actionError} />}
          </Flex>
        </Modal>
      )}
    </PageLayout>
  )
}

// Changes the items of the loaded page and leaves a page that has not loaded, or failed, as it is.
function withItems(current: CellJobPage | 'failed' | undefined, change: (items: CellJob[]) => CellJob[]) {
  return typeof current === 'object' ? { ...current, items: change(current.items) } : current
}
