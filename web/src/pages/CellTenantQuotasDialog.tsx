import { Form, InputNumber, Modal } from 'antd'
import { AxisForm } from '../platform/AxisForm'
import { AxisAlert } from '../platform/AxisAlert'
import { Fragment, useState } from 'react'
import { AxisChoiceField } from '../platform/AxisChoiceField'
import { AxisField } from '../platform/AxisField'
import { setCellTenantQuotas, type CellQuota } from '../platform/cell'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { currentChoice, quotaChanges, type QuotaChoice } from './cellQuotaChoices'
import { DialogFooter } from '../platform/DialogFooter'

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
      footer={
        <DialogFooter
          finishText={t('shell.cell.tenant.setQuotasOk')}
          cancelText={t('shell.users.roles.cancel')}
          finishing={saving}
          finishDisabled={!changed}
          onFinish={() => form.submit()}
          onCancel={onClose}
        />
      }
      onCancel={onClose}
    >
      <AxisForm
        form={form}
        initialValues={start}
        onValuesChange={(_changed, all) => setChoices(all)}
        onFinish={() => void save()}
      >
        {Object.keys(quotas).map((key) => {
          const label = t(`shell.cell.tenant.quota.${key}`)
          const choice = choices[key]!
          return (
            <Fragment key={key}>
              <AxisChoiceField
                name={[key, 'kind']}
                label={label}
                help={t('shell.cell.tenant.quota.kindHelp')}
                requiredMessage={t('shell.cell.tenant.quota.kindRequired')}
                options={[
                  {
                    value: 'value',
                    label: t('shell.cell.tenant.quota.choice'),
                    help: t('shell.cell.tenant.quota.choiceHelp'),
                  },
                  {
                    value: 'none',
                    label: t('shell.cell.tenant.quota.noLimit'),
                    help: t('shell.cell.tenant.quota.noLimitHelp'),
                  },
                  {
                    value: 'default',
                    label: t('shell.cell.tenant.quota.default'),
                    help: t('shell.cell.tenant.quota.defaultHelp'),
                  },
                ]}
              />
              <AxisField
                name={[key, 'value']}
                label={t('shell.cell.tenant.quota.valueLabel').replace('{quota}', label)}
                help={t('shell.cell.tenant.quota.valueHelp')}
                requiredMessage={choice.kind === 'value' ? t('shell.cell.tenant.quota.valueRequired') : undefined}
              >
                <InputNumber min={0} precision={0} disabled={choice.kind !== 'value'} />
              </AxisField>
            </Fragment>
          )
        })}
        {error && <AxisAlert data-testid="cell-tenant-quotas-error" kind="error" message={error} />}
      </AxisForm>
    </Modal>
  )
}

/** Whether a choice differs from the starting one: its kind, or for a value its number. */
function differs(choice: QuotaChoice, start: QuotaChoice): boolean {
  return choice.kind !== start.kind || (choice.kind === 'value' && choice.value !== start.value)
}
