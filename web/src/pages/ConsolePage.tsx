import { Flex, Result } from 'antd'
import dayjs from 'dayjs'
import { AxisAlert } from '../platform/AxisAlert'
import { useEffect, useState } from 'react'
import { AxisTable } from '../platform/AxisTable'
import { fetchConsoleTenant, type ConsoleApplication, type ConsoleTenant } from '../platform/console'
import { ConsolePublishing } from '../platform/ConsolePublishing'
import { ConsoleWebhookDeliveries } from '../platform/ConsoleWebhookDeliveries'
import { PageLayout } from '../platform/PageLayout'
import { SectionPart, SectionParts } from '../platform/SectionParts'
import { StatusTag } from '../platform/StatusTag'
import { recordPageSize } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'
import { ConsoleDeadLetters } from './ConsoleDeadLetters'
import { ConsoleFailedWork } from './ConsoleFailedWork'
import { ConsoleSchedules } from './ConsoleSchedules'
import { usePlatform } from './context'

const noParameters: string[] = []

const applicationFields = [
  { name: 'application', type: 'text' },
  { name: 'release', type: 'text' },
  { name: 'activatedAt', type: 'date' },
] as const

/** The release an application shows, as "#3 Spring", or null when it has no release. */
function releaseText(row: ConsoleApplication): string | null {
  return row.releaseNumber === null && !row.releaseLabel
    ? null
    : [row.releaseNumber === null ? null : `#${row.releaseNumber}`, row.releaseLabel].filter(Boolean).join(' ')
}

/**
 * The operator console for the platform operator. It shows the Tenant view: the status of the tenant of the host and a
 * table of its applications with each active release. The table filters its own rows, kept in the address under
 * `applications.`, see docs/reference/actions-and-controls.md#tables. Below it, the Failed work view lists the failed
 * instances and the calls with an unknown outcome, newest first, paged on the server with a filter on each column, whose
 * state the address keeps under the `failedWork.` names. Under it, the Webhook deliveries
 * view lists the recent webhook deliveries, paged on the server with the table toolbar, whose state the address keeps under
 * the `webhookDeliveries.` names. Then the Publishing view lists the release history and the publish requests of
 * one application, read only, and the Dead letters view lists the dead letters of every application and redelivers or
 * discards each, paged on the server with the table toolbar, whose state the address keeps under the `deadLetters.` names.
 * Under that, the Schedules view lists the schedule triggers of the active releases with their last and
 * next occurrence, paged on the server with the table toolbar, whose state the address keeps under the plain names. A user without the operator role sees the forbidden page.
 */
export function ConsolePage() {
  const t = useText()
  const platform = usePlatform()
  const { locale } = platform.current
  const [tenant, setTenant] = useState<ConsoleTenant>()
  const [forbidden, setForbidden] = useState(false)
  const [loadFailed, setLoadFailed] = useState(false)
  const applicationView = useTableView(
    applicationFields,
    recordPageSize,
    noParameters,
    false,
    true,
    false,
    'applications.',
  )

  useEffect(() => {
    const controller = new AbortController()
    fetchConsoleTenant(controller.signal).then(
      (result) => {
        if (result.kind === 'ok') {
          setTenant(result.tenant)
        } else {
          setForbidden(true)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the console tenant failed', error)
          setLoadFailed(true)
        }
      },
    )
    return () => controller.abort()
  }, [])

  return (
    <PageLayout
      title={t('shell.console.title')}
      breadcrumb={[{ label: t(platform.site.titleKey), to: '/' }, { label: t('shell.console.title') }]}
    >
      {forbidden && (
        <div data-testid="console-forbidden">
          <Result status="403" title={t('shell.console.forbidden')} />
        </div>
      )}
      {loadFailed && <AxisAlert data-testid="console-error" kind="error" message={t('shell.console.loadFailed')} />}
      {tenant && !forbidden && (
        <SectionParts>
          <SectionPart testId="console-tenant" heading={{ title: t('shell.console.tenant.title') }}>
            <Flex vertical gap="middle">
              <Flex gap="small" align="center">
                <span>{t('shell.console.tenant.status')}</span>
                <StatusTag tone={tenant.status === 'readOnly' ? 'attention' : 'neutral'}>
                  {t(`shell.console.tenant.status.${tenant.status}`)}
                </StatusTag>
              </Flex>
              <AxisTable<ConsoleApplication>
                rowKey={(row) => row.id}
                view={{ query: applicationView.view, onChange: applicationView.setView, paging: false }}
                dataSource={tenant.applications}
                emptyText={t('shell.console.tenant.empty')}
                columns={[
                  {
                    key: 'application',
                    kind: 'freeText',
                    title: t('shell.console.tenant.application'),
                    dataIndex: 'name',
                    filter: { kind: 'text', value: (row) => row.name },
                  },
                  {
                    key: 'release',
                    kind: 'freeText',
                    title: t('shell.console.tenant.release'),
                    render: (_value, row) => releaseText(row),
                    filter: { kind: 'text', value: releaseText },
                  },
                  {
                    key: 'activatedAt',
                    kind: 'fixed',
                    title: t('shell.console.tenant.activatedAt'),
                    render: (_value, row) => new Date(row.activatedAt).toLocaleString(locale),
                    filter: { kind: 'date', value: (row) => dayjs(row.activatedAt).format('YYYY-MM-DD') },
                  },
                ]}
              />
            </Flex>
          </SectionPart>
          <ConsoleFailedWork applications={tenant.applications} locale={locale} />
          <ConsoleWebhookDeliveries applications={tenant.applications} locale={locale} />
          <ConsolePublishing applications={tenant.applications} locale={locale} />
          <ConsoleDeadLetters applications={tenant.applications} locale={locale} />
          <ConsoleSchedules locale={locale} />
        </SectionParts>
      )}
    </PageLayout>
  )
}
