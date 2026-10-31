import { Alert, Card, Flex, Result, Table, Tag } from 'antd'
import { useEffect, useState } from 'react'
import { fetchConsoleTenant, type ConsoleApplication, type ConsoleTenant } from '../platform/console'
import { ConsoleWebhookDeliveries } from '../platform/ConsoleWebhookDeliveries'
import { EmptyDash } from '../platform/EmptyValue'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { usePlatform } from './context'

/**
 * The operator console for the platform operator. It shows the Tenant view: the status of the tenant of the host and a
 * table of its applications with each active release. Under it is the Webhook deliveries view. A user without the operator role sees the forbidden page.
 */
export function ConsolePage() {
  const t = useText()
  const platform = usePlatform()
  const { locale } = platform.current
  const [tenant, setTenant] = useState<ConsoleTenant>()
  const [forbidden, setForbidden] = useState(false)
  const [loadFailed, setLoadFailed] = useState(false)

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
      {tenant && !forbidden && <ConsoleWebhookDeliveries applications={tenant.applications} locale={locale} />}
    </PageLayout>
  )
}
