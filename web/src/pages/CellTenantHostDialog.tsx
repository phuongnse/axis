import { Alert, Flex, Input, Modal, Typography } from 'antd'
import { useId, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { requestCellTenantHost, type CellHostRequest } from '../platform/cell'
import { useText } from '../platform/texts'
import { usePlatform } from './context'

interface RecordRow {
  key: string
  type: string
  name: string
  value: string
}

interface CellTenantHostDialogProps {
  tenant: string
  onRefused: (kind: 'unauthorized' | 'forbidden') => void
  onClose: () => void
}

/**
 * The dialog that requests a host of the tenant's own. It asks for the host name and then shows the TXT and CNAME
 * records that the customer must add, with the date the request expires. A refused request keeps the dialog open with
 * the cell's reason.
 */
export function CellTenantHostDialog({ tenant, onRefused, onClose }: CellTenantHostDialogProps) {
  const t = useText()
  const { locale } = usePlatform().current
  const inputId = useId()
  const [host, setHost] = useState('')
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [added, setAdded] = useState<CellHostRequest | null>(null)

  async function add() {
    setSending(true)
    setError(null)
    try {
      const result = await requestCellTenantHost(tenant, host.trim())
      if (result.kind === 'ok') {
        setAdded(result.value)
      } else if (result.kind === 'problem') {
        setError(t('shell.cell.tenant.actionRefused').replace('{title}', result.title))
      } else {
        onRefused(result.kind)
      }
    } catch (failure: unknown) {
      console.warn(`Requesting a host of the cell tenant '${tenant}' failed`, failure)
      setError(t('shell.cell.tenant.actionFailed'))
    } finally {
      setSending(false)
    }
  }

  const records: RecordRow[] = added
    ? [
        { key: 'txt', type: 'TXT', name: added.txt.name, value: added.txt.value },
        { key: 'cname', type: 'CNAME', name: added.cname.name, value: added.cname.target },
      ]
    : []
  const columns: AxisColumn<RecordRow>[] = [
    { key: 'type', kind: 'fixed', title: t('shell.cell.tenant.hostRecord.type'), render: (_value, row) => row.type },
    { key: 'name', kind: 'freeText', title: t('shell.cell.tenant.hostRecord.name'), render: (_value, row) => row.name },
    {
      key: 'value',
      kind: 'freeText',
      title: t('shell.cell.tenant.hostRecord.value'),
      render: (_value, row) => row.value,
    },
  ]

  return (
    <Modal
      open
      data-testid="cell-tenant-host-dialog"
      // Wide enough for the record table, so it does not scroll sideways.
      width={880}
      title={t('shell.cell.tenant.addHostTitle').replace('{tenant}', tenant)}
      okText={t('shell.cell.tenant.addHostOk')}
      okButtonProps={{ disabled: host.trim() === '' }}
      cancelText={t('shell.users.roles.cancel')}
      confirmLoading={sending}
      onOk={() => void add()}
      onCancel={onClose}
      footer={
        added ? (
          <AxisButton role="secondary" onClick={onClose}>
            {t('shell.cell.tenant.close')}
          </AxisButton>
        ) : undefined
      }
    >
      <Flex vertical gap="middle">
        {added ? (
          <>
            <Typography.Text>{t('shell.cell.tenant.hostRecordsIntro')}</Typography.Text>
            <AxisTable<RecordRow>
              testId="cell-tenant-host-records"
              rowKey={(row) => row.key}
              dataSource={records}
              columns={columns}
              emptyText=""
            />
            <Typography.Text>
              {t('shell.cell.tenant.hostExpires').replace('{date}', new Date(added.expiresAt).toLocaleString(locale))}
            </Typography.Text>
          </>
        ) : (
          <Flex vertical gap="small">
            <label htmlFor={inputId}>{t('shell.cell.tenant.addHostField')}</label>
            <Input id={inputId} value={host} onChange={(event) => setHost(event.target.value)} />
          </Flex>
        )}
        {error && <Alert data-testid="cell-tenant-host-error" type="error" title={error} />}
      </Flex>
    </Modal>
  )
}
