import { Alert, Result } from 'antd'
import { useCallback, useEffect, useState } from 'react'
import { useParams } from 'react-router'
import { consoleInstancesPath } from '../platform/console'
import { fetchInstance, forbidden, type InstanceDetail } from '../platform/inspection'
import { PageLayout } from '../platform/PageLayout'
import { InstanceDetailView } from '../platform/ProcessInstancePage'
import { useText } from '../platform/texts'
import { usePlatform } from './context'

/** The instance read, `null` when none has the id, or why it could not be shown. */
type InstanceState = InstanceDetail | null | typeof forbidden | 'failed'

/**
 * One instance of the operator console, opened from an entry of Failed work. It shows the same steps, history, tasks
 * and calls as the process inspector, from `GET /api/console/instances/{id}`. The page is outside every site, so it has
 * no release texts and shows the names of the steps. The detail offers no command yet. A user without the operator
 * role sees the forbidden page.
 */
export function ConsoleInstancePage() {
  const t = useText()
  const platform = usePlatform()
  const { id = '' } = useParams()
  const [instance, setInstance] = useState<InstanceState>()

  const load = useCallback(
    (signal?: AbortSignal) =>
      fetchInstance(consoleInstancesPath, id, signal)
        .then(setInstance)
        .catch((error: unknown) => {
          if (!signal?.aborted) {
            console.warn(`Loading the console instance '${id}' failed`, error)
            setInstance('failed')
          }
        }),
    [id],
  )

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

  return (
    <PageLayout
      title={t('shell.console.instance.title')}
      breadcrumb={[
        { label: t(platform.site.titleKey), to: '/' },
        { label: t('shell.console.title'), to: '/console' },
        { label: id },
      ]}
    >
      {instance === forbidden && (
        <div data-testid="console-forbidden">
          <Result status="403" title={t('shell.console.forbidden')} />
        </div>
      )}
      {instance === null && (
        <Alert data-testid="console-instance-not-found" type="warning" title={t('shell.console.instance.notFound')} />
      )}
      {instance === 'failed' && (
        <Alert data-testid="console-instance-error" type="error" title={t('shell.console.instance.loadFailed')} />
      )}
      {typeof instance === 'object' && instance !== null && (
        <InstanceDetailView
          instance={instance}
          instancesPath={consoleInstancesPath}
          // The console links to no inspector page, so the site path is never used.
          sitePath=""
          inspectors={undefined}
          locale={platform.current.locale}
          onChanged={() => void load()}
          stepNames
        />
      )}
    </PageLayout>
  )
}
