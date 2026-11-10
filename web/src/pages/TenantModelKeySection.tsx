import { Flex, Input, Modal, Typography } from 'antd'
import { useEffect, useId, useState } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import {
  fetchTenantModelKey,
  removeTenantModelKey,
  setTenantModelKey,
  type TenantModelKey,
} from '../platform/tenantModelKey'

type Dialog = 'set' | 'remove'

/**
 * The "Model key" section of the users page for an administrator where the installation allows tenant keys. It renders
 * nothing unless `GET /api/tenant/model-key` answers `200`, so it stays hidden from a non-administrator, where
 * `Ai:AllowTenantKeys` is off and for a tenant with no directory row. It shows `••••` and the key's last four
 * characters, or says that no key is set. The Set and Change buttons open a dialog with a password field, and only its
 * Save button sends the key. The Remove button asks for a confirmation first. After each change the section reads the
 * key again, so it shows what the server stored. See docs/reference/platform-pages.md#model-key.
 */
export function TenantModelKeySection() {
  const t = useText()
  const { size } = useDesignTokens()
  const headingId = useId()
  const [modelKey, setModelKey] = useState<TenantModelKey | null>(null)
  const [dialog, setDialog] = useState<Dialog>()
  const [draft, setDraft] = useState('')
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<'saveFailed' | 'removeFailed'>()

  useEffect(() => {
    const controller = new AbortController()
    fetchTenantModelKey(controller.signal).then(
      (answer) => {
        if (!controller.signal.aborted) {
          setModelKey(answer)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the model key failed', error)
        }
      },
    )
    return () => controller.abort()
  }, [])

  if (!modelKey) {
    return null
  }

  function close() {
    setDialog(undefined)
    setDraft('')
  }

  async function change(run: () => Promise<boolean>, notice: 'saveFailed' | 'removeFailed') {
    setBusy(true)
    setFailure(undefined)
    try {
      if (await run()) {
        setModelKey(await fetchTenantModelKey())
      } else {
        setFailure(notice)
      }
    } catch (error: unknown) {
      console.warn('Changing the model key failed', error)
      setFailure(notice)
    } finally {
      setBusy(false)
      close()
    }
  }

  function open(next: Dialog) {
    setFailure(undefined)
    setDialog(next)
  }

  return (
    <section aria-labelledby={headingId} data-testid="tenant-model-key">
      <Flex vertical gap="small">
        <Typography.Title level={3} id={headingId}>
          {t('shell.users.modelKey.title')}
        </Typography.Title>
        {modelKey.set ? (
          <Typography.Text data-testid="tenant-model-key-masked">{`••••${modelKey.ending ?? ''}`}</Typography.Text>
        ) : (
          <Typography.Text>{t('shell.users.modelKey.none')}</Typography.Text>
        )}
        {failure && (
          <AxisAlert
            data-testid="tenant-model-key-notice"
            kind="error"
            message={t(`shell.users.modelKey.${failure}`)}
          />
        )}
        <Flex gap="small">
          <AxisButton role="secondary" onClick={() => open('set')}>
            {t(modelKey.set ? 'shell.users.modelKey.change' : 'shell.users.modelKey.set')}
          </AxisButton>
          {modelKey.set && (
            <AxisButton role="secondary" onClick={() => open('remove')}>
              {t('shell.users.modelKey.remove')}
            </AxisButton>
          )}
        </Flex>
      </Flex>
      <Modal
        open={dialog === 'set'}
        width={size.dialog.small}
        title={t(modelKey.set ? 'shell.users.modelKey.changeTitle' : 'shell.users.modelKey.setTitle')}
        okText={t('shell.users.modelKey.save')}
        cancelText={t('shell.users.roles.cancel')}
        okButtonProps={{ disabled: draft.trim() === '' }}
        confirmLoading={busy}
        onOk={() => void change(() => setTenantModelKey(draft), 'saveFailed')}
        onCancel={close}
      >
        <AxisField label={t('shell.users.modelKey.field')} help={t('shell.users.modelKey.help')}>
          <Input.Password autoComplete="off" value={draft} onChange={(event) => setDraft(event.target.value)} />
        </AxisField>
      </Modal>
      <Modal
        open={dialog === 'remove'}
        width={size.dialog.small}
        title={t('shell.users.modelKey.removeConfirm')}
        okText={t('shell.users.modelKey.remove.ok')}
        cancelText={t('shell.users.roles.cancel')}
        confirmLoading={busy}
        onOk={() => void change(removeTenantModelKey, 'removeFailed')}
        onCancel={close}
      >
        <Typography.Text>{t('shell.users.modelKey.removeEffect')}</Typography.Text>
      </Modal>
    </section>
  )
}
