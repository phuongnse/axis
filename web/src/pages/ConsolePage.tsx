import { Alert, Card, Flex, Result, Select, Table, Tag } from 'antd'
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import {
  fetchConsoleFailedWork,
  fetchConsoleTenant,
  type ConsoleApplication,
  type ConsoleFailedWorkItem,
  type ConsoleFailedWorkPage,
  type ConsoleTenant,
} from '../platform/console'
import { ConsolePublishing } from '../platform/ConsolePublishing'
import { ConsoleWebhookDeliveries } from '../platform/ConsoleWebhookDeliveries'
import { EmptyDash } from '../platform/EmptyValue'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { ConsoleDeadLetters } from './ConsoleDeadLetters'
import { ConsoleSchedules } from './ConsoleSchedules'
import { usePlatform } from './context'

const pageSize = 20

/**
 * The operator console for the platform operator. It shows the Tenant view: the status of the tenant of the host and a
 * table of its applications with each active release. Below it, the Failed work view lists the failed instances and the
 * calls with an unknown outcome, newest first, with paging and an application filter. Under it, the Webhook deliveries
 * view lists the recent webhook deliveries, then the Publishing view lists the release history and the publish requests of
 * one application, read only, and the Dead letters view lists the dead letters of every application and redelivers or
 * discards each. Under that, the Schedules view lists the schedule triggers of the active releases with their last and
 * next occurrence. A user without the operator role sees the forbidden page.
 */
export function ConsolePage() {
  const t = useText()
  const platform = usePlatform()
  const { locale } = platform.current
  const [tenant, setTenant] = useState<ConsoleTenant>()
  const [forbidden, setForbidden] = useState(false)
  const [loadFailed, setLoadFailed] = useState(false)
  const [failedWork, setFailedWork] = useState<ConsoleFailedWorkPage>()
  const [failedWorkFailed, setFailedWorkFailed] = useState(false)
  const [application, setApplication] = useState<string>()
  const [page, setPage] = useState(1)

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

  useEffect(() => {
    const controller = new AbortController()
    fetchConsoleFailedWork({ application, page, pageSize }, controller.signal).then(
      (result) => {
        if (result.kind === 'ok') {
          setFailedWork(result.page)
          setFailedWorkFailed(false)
        } else {
          setForbidden(true)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the console failed work failed', error)
          setFailedWorkFailed(true)
        }
      },
    )
    return () => controller.abort()
  }, [application, page])

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
      {loadFailed && <Alert data-testid="console-error" type="error" title={t('shell.console.loadFailed')} />}
      {tenant && !forbidden && (
        <Card data-testid="console-tenant" title={t('shell.console.tenant.title')}>
          <Flex vertical gap="middle">
            <Flex gap="small" align="center">
              <span>{t('shell.console.tenant.status')}</span>
              <Tag color="green">{t(`shell.console.tenant.status.${tenant.status}`)}</Tag>
            </Flex>
            <Table<ConsoleApplication>
              style={{ width: '100%' }}
              rowKey="id"
              dataSource={tenant.applications}
              pagination={false}
              columns={[
                { title: t('shell.console.tenant.application'), dataIndex: 'name' },
                {
                  title: t('shell.console.tenant.release'),
                  render: (_: unknown, row: ConsoleApplication) =>
                    row.releaseNumber === null && !row.releaseLabel ? (
                      <EmptyDash />
                    ) : (
                      [row.releaseNumber === null ? null : `#${row.releaseNumber}`, row.releaseLabel]
                        .filter(Boolean)
                        .join(' ')
                    ),
                },
                {
                  title: t('shell.console.tenant.activatedAt'),
                  dataIndex: 'activatedAt',
                  render: (value: string) => new Date(value).toLocaleString(locale),
                },
              ]}
            />
          </Flex>
        </Card>
      )}
      {tenant && !forbidden && (
        <Card data-testid="console-failed-work" title={t('shell.console.failedWork.title')}>
          <Flex vertical gap="middle">
            <Select
              data-testid="console-failed-work-application"
              allowClear
              showSearch={{ optionFilterProp: 'label' }}
              aria-label={t('shell.console.failedWork.applicationFilter')}
              placeholder={t('shell.console.failedWork.applicationFilter')}
              style={{ maxWidth: 400 }}
              value={application}
              options={tenant.applications.map((row) => ({ value: row.id, label: row.name }))}
              onChange={(value: string | undefined) => {
                setApplication(value)
                setPage(1)
              }}
            />
            {failedWorkFailed && (
              <Alert
                data-testid="console-failed-work-error"
                type="error"
                title={t('shell.console.failedWork.loadFailed')}
              />
            )}
            <Table<ConsoleFailedWorkItem>
              style={{ width: '100%' }}
              rowKey={(row) => row.callId ?? row.instanceId}
              loading={!failedWork && !failedWorkFailed}
              dataSource={failedWork?.items ?? []}
              pagination={{
                current: page,
                pageSize,
                total: failedWork?.totalCount ?? 0,
                showSizeChanger: false,
                onChange: setPage,
              }}
              columns={[
                {
                  title: t('shell.console.failedWork.application'),
                  dataIndex: 'application',
                  render: (value: string | null) => value ?? <EmptyDash />,
                },
                {
                  title: t('shell.console.failedWork.process'),
                  dataIndex: 'process',
                  render: (value: string | null) => value ?? <EmptyDash />,
                },
                {
                  title: t('shell.console.failedWork.instance'),
                  dataIndex: 'instanceId',
                  render: (value: string) => (
                    <Link to={`/console/instances/${value}`} data-testid={`console-failed-work-open-${value}`}>
                      {value}
                    </Link>
                  ),
                },
                {
                  title: t('shell.console.failedWork.stepOrCall'),
                  render: (_: unknown, row: ConsoleFailedWorkItem) =>
                    row.kind === 'instance'
                      ? row.step
                      : `${row.step}: ${row.connector ?? row.package} ${row.operation}`,
                },
                {
                  title: t('shell.console.failedWork.failedAt'),
                  dataIndex: 'failedAt',
                  render: (value: string) => new Date(value).toLocaleString(locale),
                },
                {
                  title: t('shell.console.failedWork.error'),
                  dataIndex: 'error',
                  render: (value: string | null) => value ?? <EmptyDash />,
                },
              ]}
            />
          </Flex>
        </Card>
      )}
      {tenant && !forbidden && <ConsoleWebhookDeliveries applications={tenant.applications} locale={locale} />}
      {tenant && !forbidden && <ConsolePublishing applications={tenant.applications} locale={locale} />}
      {tenant && !forbidden && <ConsoleDeadLetters applications={tenant.applications} locale={locale} />}
      {tenant && !forbidden && <ConsoleSchedules applications={tenant.applications} locale={locale} />}
    </PageLayout>
  )
}
