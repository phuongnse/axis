import { Alert, Flex, Form, InputNumber, Modal, Radio, Typography } from 'antd'
import { useState } from 'react'
import { AxisField } from '../platform/AxisField'
import { setCellTenantQuotas, type CellQuota } from '../platform/cell'
import { requiredMark } from '../platform/requiredMark'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { currentChoice, quotaChanges, type QuotaChoice } from './cellQuotaChoices'

interface CellTenantQuotasDialogProps {
  tenant: string
  quotas: Record<string, CellQuota>
  /** Called with the tenant's quotas after the change, when the cell accepted it. */
  onSaved: (quotas: Record<string, CellQuota>) => void
  onRefused: (kind: 'unauthorized' | 'forbidden') => void
  onClose: () => void
}

/**
 * The dialog that sets the quotas of a tenant. Each key takes a value, no limit or the cell's default. Saving sends only
 * the keys that changed, and stays off until a choice differs from the tenant's quotas, an emptied value included. A
 * click with a chosen value empty shows what is missing under it, focuses it and sends nothing, see
 * docs/reference/actions-and-controls.md#fields. A refused request keeps the dialog open with the cell's reason.
 */
export function CellTenantQuotasDialog({ tenant, quotas, onSaved, onRefused, onClose }: CellTenantQuotasDialogProps) {
  const t = useText()
  const { size } = useDesignTokens()
  const [start] = useState<Record<string, QuotaChoice>>(() =>
    Object.fromEntries(Object.entries(quotas).map(([key, quota]) => [key, currentChoice(quota)])),
  )
  const [form] = Form.useForm<Record<string, QuotaChoice>>()
  const [choices, setChoices] = useState(start)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const changes = quotaChanges(quotas, choices)
  const changed = Object.keys(quotas).some((key) => differs(choices[key]!, start[key]!))

  async function save() {
    setSaving(true)
    setError(null)
    try {
      const result = await setCellTenantQuotas(tenant, changes)
      if (result.kind === 'ok') {
        onSaved(result.value)
        onClose()
      } else if (result.kind === 'problem') {
        setError(t('shell.cell.tenant.actionRefused').replace('{title}', result.title))
      } else {
        onRefused(result.kind)
      }
    } catch (failure: unknown) {
      console.warn(`Setting the quotas of the cell tenant '${tenant}' failed`, failure)
      setError(t('shell.cell.tenant.actionFailed'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      width={size.dialog.medium}
      data-testid="cell-tenant-quotas-dialog"
      title={t('shell.cell.tenant.setQuotasTitle').replace('{tenant}', tenant)}
      okText={t('shell.cell.tenant.setQuotasOk')}
      okButtonProps={{ disabled: !changed }}
      cancelText={t('shell.users.roles.cancel')}
      confirmLoading={saving}
      onOk={() => form.submit()}
      onCancel={onClose}
    >
      <Form
        form={form}
        layout="vertical"
        requiredMark={requiredMark}
        initialValues={start}
        scrollToFirstError={{ focus: true }}
        onValuesChange={(_changed, all) => setChoices(all)}
        onFinish={() => void save()}
      >
        <Flex vertical gap="middle">
          {Object.keys(quotas).map((key) => {
            const label = t(`shell.cell.tenant.quota.${key}`)
            const choice = choices[key]!
            return (
              <Flex key={key} vertical gap="small">
                <Typography.Text strong>{label}</Typography.Text>
                <Form.Item name={[key, 'kind']} noStyle>
                  <Radio.Group aria-label={label}>
                    <Radio value="value">{t('shell.cell.tenant.quota.choice')}</Radio>
                    <Radio value="none">{t('shell.cell.tenant.quota.noLimit')}</Radio>
                    <Radio value="default">{t('shell.cell.tenant.quota.default')}</Radio>
                  </Radio.Group>
                </Form.Item>
                <AxisField
                  name={[key, 'value']}
                  label={t('shell.cell.tenant.quota.valueLabel').replace('{quota}', label)}
                  help={t('shell.cell.tenant.quota.valueHelp')}
                  requiredMessage={choice.kind === 'value' ? t('shell.cell.tenant.quota.valueRequired') : undefined}
                >
                  <InputNumber min={0} precision={0} disabled={choice.kind !== 'value'} />
                </AxisField>
              </Flex>
            )
          })}
          {error && <Alert data-testid="cell-tenant-quotas-error" type="error" title={error} />}
        </Flex>
      </Form>
    </Modal>
  )
}

/** Whether a choice differs from the starting one: its kind, or for a value its number. */
function differs(choice: QuotaChoice, start: QuotaChoice): boolean {
  return choice.kind !== start.kind || (choice.kind === 'value' && choice.value !== start.value)
}
