import { Alert } from 'antd'
import { useEffect, useState } from 'react'
import { fetchCellJobs, type CellJob } from '../platform/cell'
import { CellJobTable } from '../platform/CellJobTable'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { usePlatform } from './context'
import { useCellConsole } from './cellConsoleContext'

/** The Jobs view of the cell console: the newest jobs of the cell, newest first, with their steps and errors. It is read only. */
export function CellJobsPage() {
  const t = useText()
  const { locale } = usePlatform().current
  const { onRefused } = useCellConsole()
  const [jobs, setJobs] = useState<CellJob[]>()
  const [failed, setFailed] = useState(false)

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
  }, [onRefused])

  return (
    <PageLayout title={t('shell.cell.jobs.title')}>
      {failed && <Alert data-testid="cell-jobs-error" type="error" title={t('shell.cell.jobs.loadFailed')} />}
      {jobs && <CellJobTable jobs={jobs} locale={locale} />}
    </PageLayout>
  )
}
