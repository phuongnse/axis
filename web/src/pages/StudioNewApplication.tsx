import { Button, Form, Input, Modal } from 'antd'
import { useContext, useEffect, useState } from 'react'
import { AxisField } from '../platform/AxisField'
import { requiredMark } from '../platform/requiredMark'
import { StartFromField } from '../platform/StartFromField'
import { createStudioApp, fetchStudioTemplates, ServerUnreachableError, type StudioTemplate } from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { isApplicationName } from '../platform/studioStarters'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'

/** What the server refused, as the text key to show under the name. */
type Problem = 'exists' | 'invalid' | 'failed'

type Values = { name: string; template: string }

/**
 * The New application… button and its dialog. The button is the solid main button with `primary`. The dialog asks for a name and for Blank or an installed template, and
 * shows each template's title and description in the UI locale, with `en` as the fallback. A built-in template keeps the
 * server's order and carries a "Built in" tag after its title. The name is marked required and both fields have help, see
 * [New application](docs/reference/studio.md#new-application). Create is on until a create runs. A click with the name
 * empty or breaking the API's rule shows what is wrong under it and focuses it, and sends nothing. A `409` shows that
 * the name is taken, and a `400` that it is refused, and the dialog stays open in both cases. When the template list does
 * not load, the dialog offers only Blank. `onCreated` gets the new application's name once the create succeeds.
 * Given `open` and `onOpenChange`, the dialog is controlled by its parent and draws no button, so another control can open it.
 */
export function StudioNewApplication({
  onCreated,
  primary,
  open: controlledOpen,
  onOpenChange,
}: {
  onCreated: (name: string) => void
  primary?: boolean
  open?: boolean
  onOpenChange?: (open: boolean) => void
}) {
  const t = useText()
  const { size } = useDesignTokens()
  const { lost } = useStudioConnection()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const [ownOpen, setOwnOpen] = useState(false)
  const controlled = controlledOpen !== undefined
  const open = controlled ? controlledOpen : ownOpen
  const [form] = Form.useForm<Values>()
  const [templates, setTemplates] = useState<StudioTemplate[] | 'failed'>()
  const [creating, setCreating] = useState(false)

  function problemOf(problem: Problem) {
    form.setFields([{ name: 'name', errors: [t(`shell.studio.newApplication.${problem}`)] }])
  }

  function setOpen(next: boolean) {
    if (controlled) {
      onOpenChange?.(next)
    } else {
      setOwnOpen(next)
    }
  }

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

  async function create(values: Values) {
    const entered = values.name.trim()
    const template = values.template
    setCreating(true)
    try {
      const result = await createStudioApp(entered, template === '' ? undefined : template)
      if (result.status === 'created') {
        setOpen(false)
        onCreated(result.name)
      } else {
        problemOf(result.status === 'conflict' ? 'exists' : 'invalid')
      }
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Creating the application '${entered}' failed`, error)
        problemOf('failed')
      }
    } finally {
      setCreating(false)
    }
  }

  return (
    <>
      {!controlled && (
        // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
        <Button type={primary ? 'primary' : 'default'} onClick={() => setOpen(true)}>
          {t('shell.studio.newApplication.button')}
        </Button>
      )}
      <Modal
        open={open}
        width={size.dialog.medium}
        title={t('shell.studio.newApplication.title')}
        okText={t('shell.studio.newApplication.create')}
        cancelText={t('shell.studio.cancel')}
        confirmLoading={creating}
        onOk={() => form.submit()}
        onCancel={() => setOpen(false)}
        // Each opening starts with empty fields and loads the templates again.
        afterClose={() => {
          form.resetFields()
          setTemplates(undefined)
        }}
      >
        <Form
          form={form}
          layout="vertical"
          requiredMark={requiredMark}
          initialValues={{ name: '', template: '' }}
          scrollToFirstError={{ focus: true }}
          onFinish={create}
        >
          <AxisField
            name="name"
            label={t('shell.studio.newApplication.name')}
            help={t('shell.studio.newApplication.name.help')}
            requiredMessage={t('shell.studio.newApplication.nameRequired')}
            rules={[
              {
                // An empty name has the required message, so this skips it.
                validator: (_, value: string) =>
                  value.trim() === '' || isApplicationName(value.trim())
                    ? Promise.resolve()
                    : Promise.reject(new Error(t('shell.studio.newApplication.invalid'))),
              },
            ]}
          >
            <Input
              aria-label={t('shell.studio.newApplication.name')}
              // The browser also submits a form with one text box on Enter, so this stops it from creating twice.
              onPressEnter={(event) => {
                event.preventDefault()
                form.submit()
              }}
            />
          </AxisField>
          <StartFromField
            name="template"
            texts={{
              label: t('shell.studio.newApplication.template'),
              help: t('shell.studio.newApplication.template.help'),
              blank: t('shell.studio.newApplication.blank'),
              builtIn: t('shell.studio.newApplication.builtIn'),
              failed: t('shell.studio.newApplication.templatesFailed'),
            }}
            applications={
              Array.isArray(templates)
                ? templates.map((item) => ({
                    id: item.id,
                    title: localized(item.title, locale),
                    description: localized(item.description, locale),
                    builtIn: item.builtIn,
                  }))
                : templates
            }
          />
        </Form>
      </Modal>
    </>
  )
}

/** The text in the locale, or the `en` text when the template has none in it. */
function localized(texts: Record<string, string>, locale: string): string {
  return texts[locale] ?? texts.en
}
