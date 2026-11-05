import { Alert, Card, Flex, Result, Select, Tag } from 'antd'
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { AxisTable } from '../platform/AxisTable'
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
              <Tag color={tenant.status === 'readOnly' ? 'warning' : 'success'}>
                {t(`shell.console.tenant.status.${tenant.status}`)}
              </Tag>
            </Flex>
            <AxisTable<ConsoleApplication>
              rowKey={(row) => row.id}
              dataSource={tenant.applications}
              emptyText={t('shell.console.tenant.empty')}
              columns={[
                {
                  key: 'application',
                  kind: 'freeText',
                  title: t('shell.console.tenant.application'),
                  dataIndex: 'name',
                },
                {
                  key: 'release',
                  kind: 'freeText',
                  title: t('shell.console.tenant.release'),
                  render: (_value, row) =>
                    row.releaseNumber === null && !row.releaseLabel
                      ? null
                      : [row.releaseNumber === null ? null : `#${row.releaseNumber}`, row.releaseLabel]
                          .filter(Boolean)
                          .join(' '),
                },
                {
                  key: 'activatedAt',
                  kind: 'fixed',
                  title: t('shell.console.tenant.activatedAt'),
                  render: (_value, row) => new Date(row.activatedAt).toLocaleString(locale),
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
            <AxisTable<ConsoleFailedWorkItem>
              rowKey={(row) => row.callId ?? row.instanceId}
              loading={!failedWork && !failedWorkFailed}
              dataSource={failedWork?.items ?? []}
              emptyText={t('shell.console.failedWork.empty')}
              pagination={{ current: page, pageSize, total: failedWork?.totalCount ?? 0 }}
              onChange={(pagination) => setPage(pagination.current ?? 1)}
              columns={[
                {
                  key: 'application',
                  kind: 'freeText',
                  title: t('shell.console.failedWork.application'),
                  render: (_value, row) => row.application,
                },
                {
                  key: 'process',
                  kind: 'freeText',
                  title: t('shell.console.failedWork.process'),
                  render: (_value, row) => row.process,
                },
                {
                  key: 'instance',
                  kind: 'fixed',
                  title: t('shell.console.failedWork.instance'),
                  render: (_value, row) => (
                    <Link
                      to={`/console/instances/${row.instanceId}`}
                      data-testid={`console-failed-work-open-${row.instanceId}`}
                    >
                      {row.instanceId}
                    </Link>
                  ),
                },
                {
                  key: 'stepOrCall',
                  kind: 'freeText',
                  title: t('shell.console.failedWork.stepOrCall'),
                  render: (_value, row) =>
                    row.kind === 'instance'
                      ? row.step
                      : `${row.step}: ${row.connector ?? row.package} ${row.operation}`,
                },
                {
                  key: 'failedAt',
                  kind: 'fixed',
                  title: t('shell.console.failedWork.failedAt'),
                  render: (_value, row) => new Date(row.failedAt).toLocaleString(locale),
                },
                {
                  key: 'error',
                  kind: 'freeText',
                  title: t('shell.console.failedWork.error'),
                  render: (_value, row) => row.error,
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
