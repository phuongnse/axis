import { Alert, Flex, Form, Input, Modal, Typography } from 'antd'
import { useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { requestCellTenantHost, type CellHostRequest } from '../platform/cell'
import { requiredMark } from '../platform/requiredMark'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
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
 * records that the customer must add, with the date the request expires. Add is on until a request runs. A click with
 * the host empty shows what is missing under the field, focuses it and sends nothing, see
 * docs/reference/actions-and-controls.md#fields. A refused request keeps the dialog open with the cell's reason.
 */
export function CellTenantHostDialog({ tenant, onRefused, onClose }: CellTenantHostDialogProps) {
  const t = useText()
  const { size } = useDesignTokens()
  const { locale } = usePlatform().current
  const [form] = Form.useForm<{ host: string }>()
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [added, setAdded] = useState<CellHostRequest | null>(null)

  async function add(host: string) {
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
      width={size.dialog.large}
      title={t('shell.cell.tenant.addHostTitle').replace('{tenant}', tenant)}
      okText={t('shell.cell.tenant.addHostOk')}
      cancelText={t('shell.users.roles.cancel')}
      confirmLoading={sending}
      onOk={() => form.submit()}
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
          <Form
            form={form}
            layout="vertical"
            requiredMark={requiredMark}
            initialValues={{ host: '' }}
            scrollToFirstError={{ focus: true }}
            onFinish={({ host }) => void add(host)}
          >
            <AxisField
              name="host"
              label={t('shell.cell.tenant.addHostField')}
              help={t('shell.cell.tenant.addHostField.help')}
              requiredMessage={t('shell.cell.tenant.addHostRequired')}
            >
              <Input
                // The browser also submits a form with one text box on Enter, so this stops it from adding twice.
                onPressEnter={(event) => {
                  event.preventDefault()
                  form.submit()
                }}
              />
            </AxisField>
          </Form>
        )}
        {/* oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert. */}
        {error && <Alert data-testid="cell-tenant-host-error" type="error" title={error} />}
      </Flex>
    </Modal>
  )
}
