import { Form, Input, Modal, Select } from 'antd'
import { useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { requiredMark } from '../platform/requiredMark'
import {
  createStudioResource,
  fetchStudioResource,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioResourceItem,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { kindGroupNames, kindSingular, studioKinds } from '../platform/studioKinds'
import { createStarter, starterKinds, textResourceName } from '../platform/studioStarters'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'

/** The locale a new text resource copies when the application has no site. */
const fallbackLocale = 'en'

/** What the server says is wrong with the author's input, as the text key to show under the field. */
type Problem = 'exists' | 'invalid' | 'failed'

type Values = { kind?: string; value: string }

/**
 * The New resource button of the open application's band, a text button, and its dialog. The dialog asks for a kind, listed by its singular
 * name under the sidebar's groups, and a name, or for a locale when the kind is `text`. With `kind` set, it is instead the
 * button of that kind's list, named for the kind, such as New entity…, an outlined main button, and its dialog asks for the name
 * or the locale alone. It creates the resource from
 * the kind's starter with an id from `crypto.randomUUID()`. A new text resource is named after its locale, such as `TextsVi`, and copies every key and
 * text of the default locale of the first site in file order, or of `en` when there is no site, so every label still
 * resolves. The fields are marked required and have help, see [New resource](docs/reference/studio.md#new-resource).
 * Create is on until a create runs. A click with a field empty shows what is missing under it and focuses it, and sends nothing.
 * A `409` or a `400` shows under the field, and the dialog stays open. `onCreated` gets the new resource and
 * the folder's diagnostics after the write.
 */
export function StudioNewResource({
  kind: fixedKind,
  application,
  resources,
  onCreated,
}: {
  kind?: string
  application: string
  resources: StudioResourceItem[]
  onCreated: (item: StudioResourceItem, diagnostics: StudioDiagnostic[]) => void
}) {
  const t = useText()
  const { size } = useDesignTokens()
  const { lost } = useStudioConnection()
  const [open, setOpen] = useState(false)
  const [form] = Form.useForm<Values>()
  const kind = Form.useWatch('kind', form) ?? fixedKind
  const [creating, setCreating] = useState(false)
  const isText = kind === 'text'
  const kindOptions = kindGroupNames.flatMap((group) => {
    const options = studioKinds
      .filter((entry) => entry.group === group && starterKinds.includes(entry.kind))
      .map((entry) => ({ value: entry.kind, label: kindSingular(entry.kind, t) }))
    const title = t(`shell.studio.nav.group.${group}`)
    return options.length === 0 ? [] : [{ label: title, title, options }]
  })
  const label = t(isText ? 'shell.studio.newResource.locale' : 'shell.studio.newResource.name')

  function problemOf(problem: Problem) {
    form.setFields([{ name: 'value', errors: [t(`shell.studio.newResource.${problem}`)] }])
  }

  async function create(values: Values) {
    const chosen = values.kind ?? fixedKind!
    const entered = values.value.trim()
    setCreating(true)
    try {
      const body =
        chosen === 'text'
          ? createStarter(chosen, {
              id: crypto.randomUUID(),
              name: textResourceName(entered),
              locale: entered,
              texts: await sourceTexts(application, resources),
            })
          : createStarter(chosen, { id: crypto.randomUUID(), name: entered })
      const result = await createStudioResource(application, body)
      if (result.status === 'created') {
        setOpen(false)
        onCreated({ ...result.item, titles: {}, locale: chosen === 'text' ? entered : null }, result.diagnostics)
      } else {
        problemOf(result.status === 'conflict' ? 'exists' : 'invalid')
      }
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Creating a resource in '${application}' failed`, error)
        problemOf('failed')
      }
    } finally {
      setCreating(false)
    }
  }

  return (
    <>
      <AxisButton role={fixedKind === undefined ? 'secondary' : 'main'} onClick={() => setOpen(true)}>
        {t(
          fixedKind === undefined
            ? 'shell.studio.newResource.button'
            : `shell.studio.newResource.kindButton.${fixedKind}`,
        )}
      </AxisButton>
      <Modal
        open={open}
        width={size.dialog.medium}
        title={t(
          fixedKind === undefined
            ? 'shell.studio.newResource.title'
            : `shell.studio.newResource.kindTitle.${fixedKind}`,
        )}
        okText={t('shell.studio.newResource.create')}
        cancelText={t('shell.studio.cancel')}
        confirmLoading={creating}
        onOk={() => form.submit()}
        onCancel={() => setOpen(false)}
        // Each opening starts with empty fields.
        afterClose={() => form.resetFields()}
      >
        <Form
          form={form}
          layout="vertical"
          requiredMark={requiredMark}
          initialValues={{ kind: fixedKind, value: '' }}
          scrollToFirstError={{ focus: true }}
          // A server problem under the value belongs to the kind it was shown for, and goes when the kind changes.
          onValuesChange={(changed) => 'kind' in changed && form.setFields([{ name: 'value', errors: [] }])}
          onFinish={create}
        >
          {fixedKind === undefined && (
            <AxisField
              name="kind"
              label={t('shell.studio.newResource.kind')}
              help={t('shell.studio.newResource.kind.help')}
              requiredMessage={t('shell.studio.newResource.kindRequired')}
            >
              <Select
                aria-label={t('shell.studio.newResource.kind')}
                // All kinds render, so each one can be reached without scrolling the list, and the author can type to narrow it.
                showSearch
                optionFilterProp="label"
                virtual={false}
                options={kindOptions}
              />
            </AxisField>
          )}
          <AxisField
            name="value"
            label={label}
            help={t(isText ? 'shell.studio.newResource.locale.help' : 'shell.studio.newResource.name.help')}
            requiredMessage={t(
              isText ? 'shell.studio.newResource.localeRequired' : 'shell.studio.newResource.nameRequired',
            )}
          >
            <Input aria-label={label} onPressEnter={() => form.submit()} />
          </AxisField>
        </Form>
      </Modal>
    </>
  )
}

/**
 * The texts a new locale starts from: those of the default locale of the first site, or of `en` without a site. It is
 * an empty map when no text resource has that locale.
 */
async function sourceTexts(application: string, resources: StudioResourceItem[]): Promise<Record<string, string>> {
  const site = resources.filter((item) => item.kind === 'site').toSorted(byFile)[0]
  let locale = fallbackLocale
  if (site !== undefined) {
    const content = (await fetchStudioResource(application, site.id)).content
    const configured = (content.locales as { default?: unknown } | undefined)?.default
    if (typeof configured === 'string') {
      locale = configured
    }
  }
  for (const item of resources.filter((resource) => resource.kind === 'text')) {
    const content = (await fetchStudioResource(application, item.id)).content
    if (typeof content.locale === 'string' && content.locale.toLowerCase() === locale.toLowerCase()) {
      return (content.texts as Record<string, string> | undefined) ?? {}
    }
  }
  return {}
}

function byFile(left: StudioResourceItem, right: StudioResourceItem): number {
  return left.file < right.file ? -1 : left.file > right.file ? 1 : 0
}
