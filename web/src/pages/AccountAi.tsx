import { Flex, Form, Input, Modal, Select, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { DialogFooter } from '../platform/DialogFooter'
import { connectOwnAi, fetchOwnAi, type OwnAi, type OwnAiProvider } from '../platform/ownAi'
import { requiredMark } from '../platform/requiredMark'
import { SectionPart, SectionParts } from '../platform/SectionParts'
import { TermList } from '../platform/TermList'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

/**
 * Proposed (#3926): the account page's AI tab, where a person connects their own AI, whose key runs every AI request
 * they send. With no AI connected it says so and offers Connect your AI…, which opens a dialog of the provider and the API
 * key. The person chooses no model: the installation's binding names it. Once connected it shows the provider, the
 * binding's model and the key's last four characters. See docs/reference/model-bindings.md#a-persons-own-key.
 */
export function AccountAi() {
  const t = useText()
  const [ai, setAi] = useState<OwnAi | 'failed'>()
  const [connecting, setConnecting] = useState(false)
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

  const connection = ai && ai !== 'failed' ? ai.connection : null
  const provider =
    ai && ai !== 'failed' ? ai.providers.find((candidate) => candidate.id === connection?.provider) : undefined

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
          {ai && ai !== 'failed' && !connection && (
            <>
              <Typography.Text>{t('shell.account.ai.none')}</Typography.Text>
              <AxisButton role="secondary" data-testid="account-ai-connect" onClick={() => setConnecting(true)}>
                {t('shell.account.ai.connect')}
              </AxisButton>
            </>
          )}
          {connection && (
            <TermList
              terms={[
                {
                  key: 'provider',
                  term: t('shell.account.ai.provider'),
                  value: provider?.name ?? connection.provider,
                },
                {
                  key: 'model',
                  term: t('shell.account.ai.model'),
                  value: provider?.model,
                },
                { key: 'key', term: t('shell.account.ai.key'), value: `••••${connection.ending}` },
              ]}
            />
          )}
        </Flex>
        {connecting && ai && ai !== 'failed' && (
          <ConnectDialog
            providers={ai.providers}
            onConnected={() => {
              setConnecting(false)
              setReload((count) => count + 1)
            }}
            onCancel={() => setConnecting(false)}
          />
        )}
      </SectionPart>
    </SectionParts>
  )
}

interface ConnectValues {
  provider: string
  key: string
}

/**
 * Proposed (#3926): the dialog that connects an AI. The provider comes first, then how the person signs in to it, which
 * is an API key for every provider today. The provider's binding names the model, so the dialog asks for none. It renders only while open, so closing it clears
 * the key. Connect sends the key, which the agent host tests with the provider before the server keeps it.
 */
function ConnectDialog({
  providers,
  onConnected,
  onCancel,
}: {
  providers: OwnAiProvider[]
  onConnected: () => void
  onCancel: () => void
}) {
  const t = useText()
  const { size, spacing } = useDesignTokens()
  const [form] = Form.useForm<ConnectValues>()
  const [busy, setBusy] = useState(false)
  const [failed, setFailed] = useState(false)
  async function connect({ provider, key }: ConnectValues) {
    setBusy(true)
    setFailed(false)
    try {
      if (await connectOwnAi(provider, key)) {
        onConnected()
        return
      }
      setFailed(true)
    } catch (error: unknown) {
      console.warn('Connecting your AI failed', error)
      setFailed(true)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      width={size.dialog.small}
      title={t('shell.account.ai.connectTitle')}
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
      <Form
        form={form}
        layout="vertical"
        requiredMark={requiredMark}
        scrollToFirstError={{ focus: true }}
        initialValues={{ provider: providers[0]?.id }}
        onFinish={(values) => void connect(values)}
      >
        <Flex vertical gap={spacing.lg}>
          <AxisField
            name="provider"
            label={t('shell.account.ai.provider')}
            help={t('shell.account.ai.providerHelp')}
            requiredMessage={t('shell.account.ai.providerRequired')}
          >
            <Select options={providers.map((provider) => ({ value: provider.id, label: provider.name }))} />
          </AxisField>
          <AxisField
            name="key"
            label={t('shell.account.ai.key')}
            help={t('shell.account.ai.keyHelp')}
            requiredMessage={t('shell.account.ai.keyRequired')}
          >
            <Input.Password autoComplete="off" />
          </AxisField>
          {failed && (
            <AxisAlert
              data-testid="account-ai-connect-failed"
              kind="error"
              message={t('shell.account.ai.connectFailed')}
            />
          )}
        </Flex>
      </Form>
    </Modal>
  )
}
