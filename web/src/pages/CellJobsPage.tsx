import { Flex, Modal, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { useEffect, useRef, useState } from 'react'
import { fetchCellJobs, runCellJobAgain, type CellJob } from '../platform/cell'
import { CellJobTable } from '../platform/CellJobTable'
import { PageLayout } from '../platform/PageLayout'
import { newIdempotencyKey } from '../platform/processes'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useFollowCellJob } from '../platform/useFollowCellJob'
import { usePlatform } from './context'
import { useCellConsole } from './cellConsoleContext'

/**
 * The Jobs view of the cell console: the newest jobs of the cell, newest first, with their steps and errors. A failed job
 * other than a `purge` job offers Run again, behind a confirmation that names the job's kind and tenant. Confirming queues a
 * new job with an `Idempotency-Key`, which stays for a retry after a network error or a 5xx and goes with a final answer.
 * The page puts the new job first and follows it every 2 seconds until it ends, then loads the list again.
 */
export function CellJobsPage() {
  const t = useText()
  const { size } = useDesignTokens()
  const { locale } = usePlatform().current
  const { onRefused } = useCellConsole()
  const [jobs, setJobs] = useState<CellJob[]>()
  const [failed, setFailed] = useState(false)
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
    fetchCellJobs(controller.signal).then(
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
          setFailed(true)
        }
      },
    )
    return () => controller.abort()
  }, [onRefused, reload])

  useFollowCellJob(following, {
    onJob: (job) => setJobs((current) => current?.map((item) => (item.id === job.id ? job : item))),
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
        setJobs((current) => [result.value, ...(current ?? []).filter((item) => item.id !== result.value.id)])
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

  return (
    <PageLayout title={t('shell.cell.jobs.title')}>
      {failed && <AxisAlert data-testid="cell-jobs-error" kind="error" message={t('shell.cell.jobs.loadFailed')} />}
      {jobs && (
        <CellJobTable
          jobs={jobs}
          locale={locale}
          onRunAgain={(job) => {
            setActionError(null)
            setConfirming(job)
          }}
        />
      )}
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
