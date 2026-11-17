import { Flex, Form, Input, Modal, Typography } from 'antd'
import { useEffect, useState, type ReactNode } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { requiredMark } from '../platform/requiredMark'
import { SectionPart } from '../platform/SectionParts'
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
 * The "Model key" section of the users page for an administrator where the installation allows tenant keys. A `403` or
 * `404` from `GET /api/tenant/model-key` leaves it out, so it stays hidden from a non-administrator, where
 * `Ai:AllowTenantKeys` is off and for a tenant with no directory row. While the key loads the section says so, and
 * when the read fails any other way it says that the key could not be loaded. Once loaded it shows `••••` and the key's last four
 * characters, or says that no key is set. The Set and Change buttons open a dialog with a password field, and only its
 * Save button sends the key. A click on Save with the field blank shows what to enter under it, focuses it and sends
 * nothing, see docs/reference/actions-and-controls.md#fields. The Remove button asks for a confirmation first. After each change the section reads the
 * key again, so it shows what the server stored. See docs/reference/platform-pages.md#model-key.
 */
export function TenantModelKeySection() {
  const t = useText()
  const { size } = useDesignTokens()
  const [modelKey, setModelKey] = useState<TenantModelKey | 'failed' | null>()
  const [dialog, setDialog] = useState<Dialog>()
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
          setModelKey('failed')
        }
      },
    )
    return () => controller.abort()
  }, [])

  if (modelKey === null) {
    return null
  }
  if (modelKey === undefined || modelKey === 'failed') {
    return (
      <ModelKeyShell>
        {modelKey === undefined ? (
          <Typography.Text>{t('shell.users.modelKey.loading')}</Typography.Text>
        ) : (
          <AxisAlert
            data-testid="tenant-model-key-load-failed"
            kind="error"
            message={t('shell.users.modelKey.loadFailed')}
          />
        )}
      </ModelKeyShell>
    )
  }

  function close() {
    setDialog(undefined)
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
    <ModelKeyShell
      dialogs={
        <>
          {dialog === 'set' && (
            <SetKeyDialog
              title={t(modelKey.set ? 'shell.users.modelKey.changeTitle' : 'shell.users.modelKey.setTitle')}
              busy={busy}
              onSave={(key) => void change(() => setTenantModelKey(key), 'saveFailed')}
              onCancel={close}
            />
          )}
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
        </>
      }
    >
      {modelKey.set ? (
        <Typography.Text data-testid="tenant-model-key-masked">{`••••${modelKey.ending ?? ''}`}</Typography.Text>
      ) : (
        <Typography.Text>{t('shell.users.modelKey.none')}</Typography.Text>
      )}
      {failure && (
        <AxisAlert data-testid="tenant-model-key-notice" kind="error" message={t(`shell.users.modelKey.${failure}`)} />
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
    </ModelKeyShell>
  )
}

/** The part and its heading, which the loading, failed and loaded states share. */
function ModelKeyShell({ children, dialogs }: { children: ReactNode; dialogs?: ReactNode }) {
  const t = useText()
  return (
    <SectionPart testName="tenant-model-key" heading={{ title: t('shell.users.modelKey.title') }}>
      <Flex vertical gap="small">
        {children}
      </Flex>
      {dialogs}
    </SectionPart>
  )
}

/** The dialog that takes the key. It renders only while open, so closing it clears the field. */
function SetKeyDialog({
  title,
  busy,
  onSave,
  onCancel,
}: {
  title: string
  busy: boolean
  onSave: (key: string) => void
  onCancel: () => void
}) {
  const t = useText()
  const { size } = useDesignTokens()
  const [form] = Form.useForm<{ key: string }>()

  return (
    <Modal
      open
      width={size.dialog.small}
      title={title}
      okText={t('shell.users.modelKey.save')}
      cancelText={t('shell.users.roles.cancel')}
      confirmLoading={busy}
      onOk={() => form.submit()}
      onCancel={onCancel}
    >
      <Form
        form={form}
        layout="vertical"
        requiredMark={requiredMark}
        scrollToFirstError={{ focus: true }}
        onFinish={({ key }) => onSave(key)}
      >
        <AxisField
          name="key"
          label={t('shell.users.modelKey.field')}
          help={t('shell.users.modelKey.help')}
          requiredMessage={t('shell.users.modelKey.keyRequired')}
        >
          <Input.Password autoComplete="off" />
        </AxisField>
      </Form>
    </Modal>
  )
}
