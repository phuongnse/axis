import { Alert, Flex, Form, Input, Modal } from 'antd'
import { useRef, useState } from 'react'
import { CellJobTable } from '../platform/CellJobTable'
import { createCellTenant, type CellJob, type CellTenantRequest } from '../platform/cell'
import { newIdempotencyKey } from '../platform/processes'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useFollowCellJob } from '../platform/useFollowCellJob'
import { usePlatform } from './context'
import { useCellConsole } from './cellConsoleContext'

/** The ids that the provisioning API accepts, see `docs/reference/tenant-lifecycle.md#creating-a-tenant`. */
const idPattern = /^[a-z0-9-]{1,50}$/

/**
 * The dialog that creates a tenant: the tenant id, the application and the administrator's email. Create sends the
 * request with an `Idempotency-Key`, which stays for the same request body after a network error or a 5xx, so a retry
 * cannot queue a second job, and a changed field gets a new key. A 400, 409 or 422 shows the server's title and keeps the
 * form open. A 202 replaces the form with the job in the shared job table, which the dialog follows every 2 seconds
 * until the job ends. Close is there at any time and stops following the job, which keeps running on the publisher.
 * `onEnded` runs once when the job ends, or on Close when the job was queued and has not ended, so the page loads the
 * list again. The page mounts the dialog only while it is open.
 */
export function CreateCellTenantModal({ onClose, onEnded }: { onClose: () => void; onEnded: () => void }) {
  const t = useText()
  const { size } = useDesignTokens()
  const { locale } = usePlatform().current
  const { onRefused } = useCellConsole()
  const [form] = Form.useForm<CellTenantRequest>()
  const [job, setJob] = useState<CellJob>()
  const [ended, setEnded] = useState(false)
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  // The Idempotency-Key of each request body that got no final answer, so a retry cannot queue a second job.
  const keys = useRef(new Map<string, string>())

  useFollowCellJob(job && !ended ? job.id : null, {
    onJob: setJob,
    onEnd: () => {
      setEnded(true)
      onEnded()
    },
    onRefused,
  })

  async function create(values: CellTenantRequest) {
    const body = JSON.stringify({
      id: values.id.trim(),
      application: values.application.trim(),
      administrator: values.administrator.trim(),
    })
    setBusy(true)
    setError(undefined)
    try {
      // A key is kept only while its request has no final answer: a network error or a 5xx.
      const idempotencyKey = keys.current.get(body) ?? newIdempotencyKey()
      keys.current.set(body, idempotencyKey)
      let result
      try {
        result = await createCellTenant(body, idempotencyKey)
      } catch (failure: unknown) {
        console.warn('Creating the cell tenant failed', failure)
        setError(t('shell.cell.tenant.actionFailed'))
        return
      }
      keys.current.delete(body)
      if (result.kind === 'ok') {
        setJob(result.value)
      } else if (result.kind === 'problem') {
        setError(t('shell.cell.tenant.actionRefused').replace('{title}', result.title))
      } else {
        onRefused(result.kind)
      }
    } finally {
      setBusy(false)
    }
  }

  function close() {
    if (job && !ended) {
      onEnded()
    }
    onClose()
  }

  return (
    <Modal
      open
      width={job ? size.dialog.large : size.dialog.medium}
      data-testid="cell-create"
      title={t('shell.cell.create.title')}
      okText={t('shell.cell.create.ok')}
      cancelText={t('shell.cell.create.close')}
      confirmLoading={busy}
      footer={(_origin, { OkBtn, CancelBtn }) => (
        <>
          <CancelBtn />
          {!job && <OkBtn />}
        </>
      )}
      onOk={() =>
        void form.validateFields().then(
          (values) => create(values),
          () => {},
        )
      }
      onCancel={close}
    >
      {job ? (
        <CellJobTable jobs={[job]} locale={locale} />
      ) : (
        <Flex vertical gap="middle">
          {error && <Alert data-testid="cell-create-error" type="error" title={error} />}
          <Form form={form} layout="vertical" name="create-cell-tenant" requiredMark={false}>
            <Form.Item
              name="id"
              label={t('shell.cell.create.id')}
              rules={[{ required: true, whitespace: true }, { pattern: idPattern }]}
            >
              <Input maxLength={50} />
            </Form.Item>
            <Form.Item
              name="application"
              label={t('shell.cell.create.application')}
              extra={t('shell.cell.create.applicationHint')}
              rules={[{ required: true, whitespace: true }]}
            >
              <Input maxLength={200} />
            </Form.Item>
            <Form.Item
              name="administrator"
              label={t('shell.cell.create.administrator')}
              rules={[{ required: true, whitespace: true }, { type: 'email' }]}
            >
              <Input type="email" maxLength={200} />
            </Form.Item>
          </Form>
        </Flex>
      )}
    </Modal>
  )
}
