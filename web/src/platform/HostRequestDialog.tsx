import { Flex, Form, Input, Modal, Typography } from 'antd'
import { AxisForm } from './AxisForm'
import { useState } from 'react'
import { AxisAlert } from './AxisAlert'
import { AxisButton } from './AxisButton'
import { AxisField } from './AxisField'
import { DialogFooter } from './DialogFooter'
import { AxisTable, type AxisColumn } from './AxisTable'
import type { CellHostRequest, CellWriteResult } from './cell'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/** The key prefix of a screen's texts for a host request, as in `shell.users.hosts.addHostOk`. */
export type HostTexts = 'shell.cell.tenant' | 'shell.users.hosts'

interface RecordRow {
  key: string
  type: string
  name: string
  value: string
}

interface HostRequestDialogProps {
  /** The prefix of the texts: `addHostOk`, `addHostField`, `addHostField.help`, `addHostRequired`, `hostRecordsIntro`, `hostRecord.*`, `hostExpires`, `close` and `actionFailed`. */
  texts: HostTexts
  title: string
  locale: string
  /** The prefix of the test ids: `-dialog`, `-records` and `-error`. */
  testId: string
  /** Sends the request. A 5xx or a network error throws. */
  request: (host: string) => Promise<CellWriteResult<CellHostRequest>>
  /** The message for a refused request, in the words of the screen that draws the dialog. */
  refusal: (problem: { status: number; title: string }) => string
  /** Called for an `unauthorized` or `forbidden` answer, when the screen handles those itself. */
  onRefused?: (kind: 'unauthorized' | 'forbidden') => void
  onAdded: () => void
  onClose: () => void
}

/**
 * The dialog that requests a host of the tenant's own. The cell console's Tenant view and the users page's Hosts
 * section both draw it, see docs/reference/shared-widgets.md#which-widget. It asks for the host name and then shows the
 * TXT and CNAME records that must be added at the DNS provider, with the date the request expires. Add is on until a
 * request runs. A click with the host empty shows what is missing under the field, focuses it and sends nothing, see
 * docs/reference/actions-and-controls.md#fields. A refused request keeps the dialog open with the message that
 * `refusal` gives. An answered request calls `onAdded`, so the screen can show the new request.
 */
export function HostRequestDialog({
  texts,
  title,
  locale,
  testId,
  request,
  refusal,
  onRefused,
  onAdded,
  onClose,
}: HostRequestDialogProps) {
  const t = useText()
  const { size } = useDesignTokens()
  const [form] = Form.useForm<{ host: string }>()
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [added, setAdded] = useState<CellHostRequest | null>(null)

  async function add(host: string) {
    setSending(true)
    setError(null)
    try {
      const result = await request(host.trim())
      if (result.kind === 'ok') {
        setAdded(result.value)
        onAdded()
      } else if (result.kind === 'problem') {
        setError(refusal(result))
      } else if (onRefused) {
        onRefused(result.kind)
      } else {
        setError(refusal({ status: result.kind === 'unauthorized' ? 401 : 403, title: '' }))
      }
    } catch (failure: unknown) {
      console.warn('Requesting a host failed', failure)
      setError(t(`${texts}.actionFailed`))
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
    { key: 'type', kind: 'fixed', title: t(`${texts}.hostRecord.type`), render: (_value, row) => row.type },
    { key: 'name', kind: 'freeText', title: t(`${texts}.hostRecord.name`), render: (_value, row) => row.name },
    { key: 'value', kind: 'freeText', title: t(`${texts}.hostRecord.value`), render: (_value, row) => row.value },
  ]

  return (
    <Modal
      open
      data-testid={`${testId}-dialog`}
      width={size.dialog.large}
      title={title}
      onCancel={onClose}
      footer={
        added ? (
          <AxisButton role="secondary" onClick={onClose}>
            {t(`${texts}.close`)}
          </AxisButton>
        ) : (
          <DialogFooter
            finishText={t(`${texts}.addHostOk`)}
            cancelText={t('shell.users.roles.cancel')}
            finishing={sending}
            onFinish={() => form.submit()}
            onCancel={onClose}
          />
        )
      }
    >
      <Flex vertical gap="middle">
        {added ? (
          <>
            <Typography.Text>{t(`${texts}.hostRecordsIntro`)}</Typography.Text>
            <AxisTable<RecordRow>
              testId={`${testId}-records`}
              rowKey={(row) => row.key}
              dataSource={records}
              columns={columns}
              emptyText=""
            />
            <Typography.Text>
              {t(`${texts}.hostExpires`).replace('{date}', new Date(added.expiresAt).toLocaleString(locale))}
            </Typography.Text>
          </>
        ) : (
          <AxisForm
            form={form}
            initialValues={{ host: '' }}
            onFinish={({ host }) => void add(host)}
            message={error && <AxisAlert data-testid={`${testId}-error`} kind="error" message={error} />}
          >
            <AxisField
              name="host"
              label={t(`${texts}.addHostField`)}
              help={t(`${texts}.addHostField.help`)}
              requiredMessage={t(`${texts}.addHostRequired`)}
            >
              <Input
                // The browser also submits a form with one text box on Enter, so this stops it from adding twice.
                onPressEnter={(event) => {
                  event.preventDefault()
                  form.submit()
                }}
              />
            </AxisField>
          </AxisForm>
        )}
      </Flex>
    </Modal>
  )
}
