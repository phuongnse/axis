import { Flex, Progress, Tag, Typography } from 'antd'
import { useEffect, useId, useState } from 'react'
import { TenantUsageDayTable } from '../platform/TenantUsageDayTable'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { fetchTenantUsage, formatBytes, type TenantQuotaUse, type TenantUsage } from '../platform/tenantUsage'
import { usePlatform } from './context'

/**
 * The "Usage" section of the users page for an administrator in a cell tenant. It renders nothing unless
 * `GET /api/tenant/usage` answers `200`, so it stays hidden outside a cell and for a non-administrator.
 */
export function TenantUsageSection() {
  const t = useText()
  const { locale } = usePlatform().current
  const { colour } = useDesignTokens()
  const headingId = useId()
  const [usage, setUsage] = useState<TenantUsage | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetchTenantUsage(controller.signal).then(
      (answer) => {
        if (!controller.signal.aborted) {
          setUsage(answer)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the usage failed', error)
        }
      },
    )
    return () => controller.abort()
  }, [])

  if (!usage) {
    return null
  }

  const amount = (key: string, value: number) =>
    key === 'storage' ? formatBytes(value, locale) : value.toLocaleString(locale)

  function quotaRow(key: string, { limit, used }: TenantQuotaUse) {
    const name = t(`shell.users.usage.quota.${key}`)
    const atLimit = limit !== null && used !== null && used >= limit
    let lines: string[]
    if (limit === null) {
      lines = used === null ? [t('shell.users.usage.noLimit')] : [t('shell.users.usage.noLimit'), amount(key, used)]
    } else if (used === null) {
      lines = [amount(key, limit)]
    } else {
      lines = [
        t('shell.users.usage.usedOfLimit').replace('{used}', amount(key, used)).replace('{limit}', amount(key, limit)),
      ]
    }
    return (
      <Flex key={key} vertical gap="small" data-testid={`tenant-usage-quota-${key}`}>
        <Typography.Text strong>{name}</Typography.Text>
        {limit !== null && used !== null && (
          <Progress percent={atLimit ? 100 : (used / limit) * 100} showInfo={false} aria-label={name} />
        )}
        <Flex gap="small" align="center" wrap>
          <Flex vertical>
            {lines.map((line, index) => (
              <Typography.Text key={index}>{line}</Typography.Text>
            ))}
          </Flex>
          {atLimit && (
            <Tag
              variant="outlined"
              style={{ color: colour.dangerText, borderColor: colour.danger, background: 'transparent' }}
            >
              {t('shell.users.usage.atLimit')}
            </Tag>
          )}
        </Flex>
      </Flex>
    )
  }

  return (
    <section aria-labelledby={headingId} data-testid="tenant-usage">
      <Flex vertical gap="middle">
        <Typography.Title level={3} id={headingId}>
          {t('shell.users.usage.title')}
        </Typography.Title>
        <Flex vertical gap="middle">
          {Object.entries(usage.quotas).map(([key, quota]) => quotaRow(key, quota))}
        </Flex>
        <TenantUsageDayTable days={usage.days} locale={locale} testId="tenant-usage-days" />
      </Flex>
    </section>
  )
}
