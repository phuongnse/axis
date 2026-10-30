import { Alert, Button, Flex, Form, Input, Modal, Radio, Typography } from 'antd'
import { useContext, useEffect, useState, type ReactNode } from 'react'
import { createStudioApp, fetchStudioTemplates, ServerUnreachableError, type StudioTemplate } from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { isApplicationName } from '../platform/studioStarters'
import { useText } from '../platform/texts'
import { PlatformContext } from './context'

/** What the server refused, as the text key to show under the name. */
type Problem = 'exists' | 'invalid' | 'failed'

/**
 * The New application button and its dialog. The dialog asks for a name and for Blank or an installed template, and
 * shows each template's title and description in the UI locale, with `en` as the fallback. Create stays off while the
 * name breaks the API's rule. A `409` shows that the name is taken, and a `400` that it is refused, and the dialog stays
 * open in both cases. When the template list does not load, the dialog offers only Blank. `onCreated` gets the new
 * application's name once the create succeeds.
 */
export function StudioNewApplication({
  onCreated,
  trigger,
  primary = false,
}: {
  onCreated: (name: string) => void
  /** Proposed: what opens the dialog, given the function that opens it, such as the sidebar's application menu. */
  trigger?: (show: () => void) => ReactNode
  /** Proposed: the button is the page's main action, as on Studio's list of applications. */
  primary?: boolean
}) {
  const t = useText()
  const { lost } = useStudioConnection()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const [open, setOpen] = useState(false)
  const [value, setValue] = useState('')
  const [template, setTemplate] = useState('')
  const [templates, setTemplates] = useState<StudioTemplate[] | 'failed'>()
  const [problem, setProblem] = useState<Problem>()
  const [creating, setCreating] = useState(false)
  const entered = value.trim()
  const nameInvalid = entered !== '' && !isApplicationName(entered)

  useEffect(() => {
    if (!open) {
      return
    }
    const controller = new AbortController()
    fetchStudioTemplates(controller.signal).then(setTemplates, (error: unknown) => {
      if (controller.signal.aborted) {
        return
      }
      if (error instanceof ServerUnreachableError) {
        lost()
        return
      }
      console.warn('Loading the Studio templates failed', error)
      setTemplates('failed')
    })
    return () => controller.abort()
  }, [open, lost])

  function show() {
    setValue('')
    setTemplate('')
    setTemplates(undefined)
    setProblem(undefined)
    setOpen(true)
  }

  async function create() {
    if (!isApplicationName(entered)) {
      return
    }
    setCreating(true)
    setProblem(undefined)
    try {
      const result = await createStudioApp(entered, template === '' ? undefined : template)
      if (result.status === 'created') {
        setOpen(false)
        onCreated(result.name)
      } else {
        setProblem(result.status === 'conflict' ? 'exists' : 'invalid')
      }
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Creating the application '${entered}' failed`, error)
        setProblem('failed')
      }
    } finally {
      setCreating(false)
    }
  }

  const message = nameInvalid
    ? t('shell.studio.newApplication.invalid')
    : problem === undefined
      ? undefined
      : t(`shell.studio.newApplication.${problem}`)

  return (
    <>
      {trigger ? (
        trigger(show)
      ) : (
        <Button type={primary ? 'primary' : 'default'} onClick={show}>
          {t('shell.studio.newApplication.button')}
        </Button>
      )}
      <Modal
        open={open}
        title={t('shell.studio.newApplication.title')}
        okText={t('shell.studio.newApplication.create')}
        cancelText={t('shell.studio.cancel')}
        okButtonProps={{ disabled: !isApplicationName(entered) }}
        confirmLoading={creating}
        onOk={create}
        onCancel={() => setOpen(false)}
      >
        <Form layout="vertical">
          <Form.Item
            label={t('shell.studio.newApplication.name')}
            validateStatus={message === undefined ? undefined : 'error'}
            help={message}
          >
            <Input
              aria-label={t('shell.studio.newApplication.name')}
              value={value}
              onChange={(event) => {
                setValue(event.target.value)
                setProblem(undefined)
              }}
              onPressEnter={create}
            />
          </Form.Item>
          <Form.Item label={t('shell.studio.newApplication.template')}>
            {templates === 'failed' && (
              <Alert type="warning" showIcon title={t('shell.studio.newApplication.templatesFailed')} />
            )}
            <Radio.Group
              aria-label={t('shell.studio.newApplication.template')}
              value={template}
              onChange={(event) => setTemplate(event.target.value)}
            >
              <Flex vertical>
                <Radio value="">{t('shell.studio.newApplication.blank')}</Radio>
                {Array.isArray(templates) &&
                  templates.map((item) => (
                    <Radio key={item.id} value={item.id}>
                      <span>{localized(item.title, locale)}</span>
                      <Typography.Text type="secondary" style={{ display: 'block' }}>
                        {localized(item.description, locale)}
                      </Typography.Text>
                    </Radio>
                  ))}
              </Flex>
            </Radio.Group>
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}

/** The text in the locale, or the `en` text when the template has none in it. */
function localized(texts: Record<string, string>, locale: string): string {
  return texts[locale] ?? texts.en
}
