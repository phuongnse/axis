import { Flex } from 'antd'
import { useEffect, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import type { CellHostRequestState } from '../platform/cell'
import { HostRequestDialog } from '../platform/HostRequestDialog'
import { HostRequestTable } from '../platform/HostRequestTable'
import { SectionPart } from '../platform/SectionParts'
import { fetchTenantHosts, requestTenantHost } from '../platform/tenantHosts'
import { useText } from '../platform/texts'
import { usePlatform } from './context'

/**
 * The "Hosts" section of the users page for an administrator in a cell tenant, see
 * docs/reference/platform-pages.md#export-data-and-usage. It renders nothing unless `GET /api/tenant/hosts` answers
 * `200`, so it stays hidden outside a cell, in a preview tenant and for a non-administrator. Add a host opens the
 * shared dialog, and once a host is requested the section loads its list again, so the new host shows as pending.
 */
export function TenantHostsSection() {
  const t = useText()
  const { locale } = usePlatform().current
  const [hosts, setHosts] = useState<CellHostRequestState[] | null>(null)
  const [reload, setReload] = useState(0)
  const [adding, setAdding] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchTenantHosts(controller.signal).then(
      (answer) => {
        if (!controller.signal.aborted) {
          setHosts(answer)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the hosts failed', error)
        }
      },
    )
    return () => controller.abort()
  }, [reload])

  if (!hosts) {
    return null
  }

  function refusal({ status }: { status: number }) {
    const key = { 400: 'invalid', 409: 'taken' }[status as 400 | 409] ?? 'refused'
    return t(`shell.users.hosts.${key}`)
  }

  return (
    <SectionPart testName="tenant-hosts" heading={{ title: t('shell.users.hosts.title') }}>
      <Flex vertical gap="middle" align="flex-start">
        <AxisButton role="main" onClick={() => setAdding(true)}>
          {t('shell.users.hosts.addHost')}
        </AxisButton>
        <HostRequestTable requests={hosts} texts="shell.users.hosts" locale={locale} testId="tenant-hosts" />
      </Flex>
      {adding && (
        <HostRequestDialog
          texts="shell.users.hosts"
          title={t('shell.users.hosts.addHostTitle')}
          locale={locale}
          testId="tenant-hosts"
          request={requestTenantHost}
          refusal={refusal}
          onAdded={() => setReload((count) => count + 1)}
          onClose={() => setAdding(false)}
        />
      )}
    </SectionPart>
  )
}
