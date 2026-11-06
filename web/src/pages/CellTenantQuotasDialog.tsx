import { Alert, Flex, InputNumber, Modal, Radio, Typography } from 'antd'
import { useState } from 'react'
import { setCellTenantQuotas, type CellQuota } from '../platform/cell'
import { useText } from '../platform/texts'
import { currentChoice, quotaChanges, type QuotaChoice, type QuotaChoiceKind } from './cellQuotaChoices'

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
 * the keys that changed, and stays off until one has. A refused request keeps the dialog open with the cell's reason.
 */
export function CellTenantQuotasDialog({ tenant, quotas, onSaved, onRefused, onClose }: CellTenantQuotasDialogProps) {
  const t = useText()
  const [choices, setChoices] = useState<Record<string, QuotaChoice>>(() =>
    Object.fromEntries(Object.entries(quotas).map(([key, quota]) => [key, currentChoice(quota)])),
  )
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const changes = quotaChanges(quotas, choices)
  const changed = Object.keys(changes.set).length + changes.remove.length > 0
  const incomplete = Object.values(choices).some((choice) => choice.kind === 'value' && choice.value === null)

  function choose(key: string, choice: Partial<QuotaChoice>) {
    setChoices((current) => ({ ...current, [key]: { ...current[key]!, ...choice } }))
  }

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
      data-testid="cell-tenant-quotas-dialog"
      title={t('shell.cell.tenant.setQuotasTitle').replace('{tenant}', tenant)}
      okText={t('shell.cell.tenant.setQuotasOk')}
      okButtonProps={{ disabled: !changed || incomplete }}
      cancelText={t('shell.users.roles.cancel')}
      confirmLoading={saving}
      onOk={() => void save()}
      onCancel={onClose}
    >
      <Flex vertical gap="middle">
        {Object.keys(quotas).map((key) => {
          const label = t(`shell.cell.tenant.quota.${key}`)
          const choice = choices[key]!
          return (
            <Flex key={key} vertical gap="small">
              <Typography.Text strong>{label}</Typography.Text>
              <Radio.Group
                aria-label={label}
                value={choice.kind}
                onChange={(event) => choose(key, { kind: event.target.value as QuotaChoiceKind })}
              >
                <Radio value="value">{t('shell.cell.tenant.quota.choice')}</Radio>
                <Radio value="none">{t('shell.cell.tenant.quota.noLimit')}</Radio>
                <Radio value="default">{t('shell.cell.tenant.quota.default')}</Radio>
              </Radio.Group>
              <InputNumber
                aria-label={t('shell.cell.tenant.quota.valueLabel').replace('{quota}', label)}
                min={0}
                precision={0}
                value={choice.value}
                disabled={choice.kind !== 'value'}
                onChange={(value) => choose(key, { value })}
              />
            </Flex>
          )
        })}
        {error && <Alert data-testid="cell-tenant-quotas-error" type="error" title={error} />}
      </Flex>
    </Modal>
  )
}
