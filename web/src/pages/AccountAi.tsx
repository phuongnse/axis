import { Flex, Form, Input, Modal, Select, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { AxisForm } from '../platform/AxisForm'
import { DialogFooter } from '../platform/DialogFooter'
import { maskedKey } from '../platform/maskedKey'
import {
  connectOwnAi,
  fetchOwnAi,
  removeOwnAi,
  type OwnAi,
  type OwnAiConnectFailure,
  type OwnAiConnection,
  type OwnAiProvider,
  type OwnAiState,
} from '../platform/ownAi'
import { SectionPart, SectionParts } from '../platform/SectionParts'
import { StatusTag } from '../platform/StatusTag'
import type { StatusTone } from '../platform/statusTones'
import { TermList } from '../platform/TermList'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

type Dialog = 'connect' | 'change' | 'remove'

/** Proposed (#3926): each state of the key, its tag's tone, and whether a line under the facts says what to do. */
const states: Record<OwnAiState, { tone: StatusTone; notice?: 'error' | 'warning' }> = {
  working: { tone: 'success' },
  refused: { tone: 'danger', notice: 'error' },
  billing: { tone: 'attention', notice: 'warning' },
}

/**
 * Proposed (#3926): the account page's AI tab, where a person connects their own AI, whose key runs every AI request
 * they send. With no AI connected it says so and offers Connect your AI…, which opens a dialog of the provider and the API
 * key. It names no model: the author chooses one for each request in Studio's request panel, which #5204 designs. Once
 * connected it shows the provider and the key's last four characters with the key's state as a tag beside them, with what to do when the provider
 * refused the key or could not bill for it, and offers Change your AI… and Remove your AI…. After each change the tab
 * reads the AI again, so it shows what the server stored. See docs/reference/model-bindings.md#a-persons-own-key.
 */
export function AccountAi() {
  const t = useText()
  const { size } = useDesignTokens()
  const [ai, setAi] = useState<OwnAi | 'failed'>()
  const [dialog, setDialog] = useState<Dialog>()
  const [removing, setRemoving] = useState(false)
  const [removeFailed, setRemoveFailed] = useState(false)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    fetchOwnAi(controller.signal).then(
      (answer) => {
        if (!controller.signal.aborted) {
          setAi(answer)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading your AI failed', error)
          setAi('failed')
        }
      },
    )
    return () => controller.abort()
  }, [reload])

  const loaded = ai && ai !== 'failed' ? ai : undefined
  const connection = loaded?.connection ?? null

  function open(next: Dialog) {
    setRemoveFailed(false)
    setDialog(next)
  }

  function changed() {
    setDialog(undefined)
    setReload((count) => count + 1)
  }

  async function remove() {
    setRemoving(true)
    try {
      if (await removeOwnAi()) {
        changed()
        return
      }
      setRemoveFailed(true)
    } catch (error: unknown) {
      console.warn('Removing your AI failed', error)
      setRemoveFailed(true)
    } finally {
      setRemoving(false)
    }
    setDialog(undefined)
  }

  return (
    <SectionParts>
      <SectionPart
        testName="account-ai"
        busy={ai === undefined}
        heading={{ title: t('shell.account.ai.title'), line: t('shell.account.ai.line') }}
      >
        <Flex vertical gap="small" align="flex-start">
          {ai === undefined && <Typography.Text>{t('shell.account.ai.loading')}</Typography.Text>}
          {ai === 'failed' && (
            <AxisAlert data-testid="account-ai-load-failed" kind="error" message={t('shell.account.ai.loadFailed')} />
          )}
          {loaded && !connection && (
            <>
              <Typography.Text>{t('shell.account.ai.none')}</Typography.Text>
              <AxisButton role="secondary" data-testid="account-ai-connect" onClick={() => open('connect')}>
                {t('shell.account.ai.connect')}
              </AxisButton>
            </>
          )}
          {loaded && connection && (
            <Connected
              connection={connection}
              providers={loaded.providers}
              removeFailed={removeFailed}
              onChange={() => open('change')}
              onRemove={() => open('remove')}
            />
          )}
        </Flex>
        {loaded && (dialog === 'connect' || dialog === 'change') && (
          <ConnectDialog
            title={t(dialog === 'change' ? 'shell.account.ai.changeTitle' : 'shell.account.ai.connectTitle')}
            providers={loaded.providers}
            provider={connection?.provider}
            onConnected={changed}
            onCancel={() => setDialog(undefined)}
          />
        )}
        <Modal
          open={dialog === 'remove'}
          width={size.dialog.small}
          title={t('shell.account.ai.removeTitle')}
          footer={
            <DialogFooter
              destructive
              finishText={t('shell.account.ai.removeOk')}
              cancelText={t('shell.account.ai.cancel')}
              finishing={removing}
              finishTestId="account-ai-remove-ok"
              onFinish={() => void remove()}
              onCancel={() => setDialog(undefined)}
            />
          }
          onCancel={() => setDialog(undefined)}
        >
          <Typography.Text>{t('shell.account.ai.removeEffect')}</Typography.Text>
        </Modal>
      </SectionPart>
    </SectionParts>
  )
}

/** Proposed (#3926): what to do when the key does not work, first, then the connected AI's facts and its two actions. */
function Connected({
  connection,
  providers,
  removeFailed,
  onChange,
  onRemove,
}: {
  connection: OwnAiConnection
  providers: OwnAiProvider[]
  removeFailed: boolean
  onChange: () => void
  onRemove: () => void
}) {
  const t = useText()
  const { tone, notice } = states[connection.state]
  const provider = providers.find((candidate) => candidate.id === connection.provider)
  return (
    <>
      {notice && (
        <AxisAlert
          data-testid="account-ai-state-notice"
          kind={notice}
          message={t(`shell.account.ai.state.${connection.state}.notice`)}
        />
      )}
      {removeFailed && (
        <AxisAlert data-testid="account-ai-remove-failed" kind="error" message={t('shell.account.ai.removeFailed')} />
      )}
      <TermList
        terms={[
          { key: 'provider', term: t('shell.account.ai.provider'), value: provider?.name ?? connection.provider },
          {
            key: 'key',
            term: t('shell.account.ai.key'),
            // The key's state is a tag beside the key, as a session's This session sits beside its time.
            value: (
              <Flex gap="small" align="center">
                {maskedKey(connection.ending)}
                <StatusTag tone={tone} data-testid="account-ai-state">
                  {t(`shell.account.ai.state.${connection.state}`)}
                </StatusTag>
              </Flex>
            ),
          },
        ]}
      />
      <Flex gap="small">
        <AxisButton role="secondary" data-testid="account-ai-change" onClick={onChange}>
          {t('shell.account.ai.change')}
        </AxisButton>
        <AxisButton role="secondary" data-testid="account-ai-remove" onClick={onRemove}>
          {t('shell.account.ai.remove')}
        </AxisButton>
      </Flex>
    </>
  )
}

interface ConnectValues {
  provider: string
  key: string
}

/**
 * Proposed (#3926): the dialog that connects an AI, or changes the connected one. The provider comes first, then how the
 * person signs in to it, which is an API key for every provider today. The author chooses the model for each request in
 * Studio, so the dialog asks for none. It renders only while open, so closing it clears the key. Connect sends the key,
 * which the agent host tests with the provider before the server keeps it. A failed connect says why above the fields,
 * as the provider answered the test, and keeps the dialog open.
 */
function ConnectDialog({
  title,
  providers,
  provider,
  onConnected,
  onCancel,
}: {
  title: string
  providers: OwnAiProvider[]
  /** The connected AI's provider, which a change starts on. */
  provider?: string
  onConnected: () => void
  onCancel: () => void
}) {
  const t = useText()
  const { size } = useDesignTokens()
  const [form] = Form.useForm<ConnectValues>()
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<OwnAiConnectFailure>()

  async function connect(values: ConnectValues) {
    setBusy(true)
    setFailure(undefined)
    try {
      const failed = await connectOwnAi(values.provider, values.key)
      if (failed === null) {
        onConnected()
        return
      }
      setFailure(failed)
    } catch (error: unknown) {
      console.warn('Connecting your AI failed', error)
      setFailure('failed')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      width={size.dialog.small}
      title={title}
      footer={
        <DialogFooter
          finishText={t('shell.account.ai.connectOk')}
          cancelText={t('shell.account.ai.cancel')}
          finishing={busy}
          finishTestId="account-ai-connect-ok"
          onFinish={() => form.submit()}
          onCancel={onCancel}
        />
      }
      onCancel={onCancel}
    >
      <AxisForm
        form={form}
        initialValues={{ provider: provider ?? providers[0]?.id }}
        onFinish={(values) => void connect(values)}
      >
        {failure && (
          <AxisAlert
            data-testid="account-ai-connect-failed"
            kind="error"
            message={t(`shell.account.ai.connectFailed.${failure}`)}
          />
        )}
        <AxisField
          name="provider"
          label={t('shell.account.ai.provider')}
          help={t('shell.account.ai.providerHelp')}
          requiredMessage={t('shell.account.ai.providerRequired')}
        >
          <Select options={providers.map((choice) => ({ value: choice.id, label: choice.name }))} />
        </AxisField>
        <AxisField
          name="key"
          label={t('shell.account.ai.key')}
          help={t('shell.account.ai.keyHelp')}
          requiredMessage={t('shell.account.ai.keyRequired')}
        >
          <Input.Password autoComplete="off" />
        </AxisField>
      </AxisForm>
    </Modal>
  )
}
