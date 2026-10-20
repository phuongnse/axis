import { Button, Form, Input, Modal, Select } from 'antd'
import { useState } from 'react'
import {
  createStudioResource,
  fetchStudioResource,
  ServerUnreachableError,
  type StudioDiagnostic,
  type StudioResourceItem,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { createStarter, starterKinds, textResourceName } from '../platform/studioStarters'
import { useText } from '../platform/texts'

/** The locale a new text resource copies when the application has no site. */
const fallbackLocale = 'en'

/** What the dialog says is wrong with the author's input, as the text key to show under the field. */
type Problem = 'exists' | 'invalid' | 'failed'

/**
 * The New resource button of the open application, and its dialog. The dialog asks for a kind and a name, or for a
 * locale when the kind is `text`, and creates the resource from the kind's starter with an id from
 * `crypto.randomUUID()`. A new text resource is named after its locale, such as `TextsVi`, and copies every key and
 * text of the default locale of the first site in file order, or of `en` when there is no site, so every label still
 * resolves. A `409` or a `400` shows under the field, and the dialog stays open. `onCreated` gets the new resource and
 * the folder's diagnostics after the write.
 */
export function StudioNewResource({
  application,
  resources,
  onCreated,
}: {
  application: string
  resources: StudioResourceItem[]
  onCreated: (item: StudioResourceItem, diagnostics: StudioDiagnostic[]) => void
}) {
  const t = useText()
  const { lost } = useStudioConnection()
  const [open, setOpen] = useState(false)
  const [kind, setKind] = useState<string>()
  const [value, setValue] = useState('')
  const [problem, setProblem] = useState<Problem>()
  const [creating, setCreating] = useState(false)
  const isText = kind === 'text'
  const label = t(isText ? 'shell.studio.newResource.locale' : 'shell.studio.newResource.name')

  function show() {
    setKind(undefined)
    setValue('')
    setProblem(undefined)
    setOpen(true)
  }

  async function create() {
    if (kind === undefined || value.trim() === '') {
      return
    }
    const entered = value.trim()
    setCreating(true)
    setProblem(undefined)
    try {
      const body =
        kind === 'text'
          ? createStarter(kind, {
              id: crypto.randomUUID(),
              name: textResourceName(entered),
              locale: entered,
              texts: await sourceTexts(application, resources),
            })
          : createStarter(kind, { id: crypto.randomUUID(), name: entered })
      const result = await createStudioResource(application, body)
      if (result.status === 'created') {
        setOpen(false)
        onCreated(result.item, result.diagnostics)
      } else {
        setProblem(result.status === 'conflict' ? 'exists' : 'invalid')
      }
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Creating a resource in '${application}' failed`, error)
        setProblem('failed')
      }
    } finally {
      setCreating(false)
    }
  }

  return (
    <>
      <Button onClick={show}>{t('shell.studio.newResource.button')}</Button>
      <Modal
        open={open}
        title={t('shell.studio.newResource.title')}
        okText={t('shell.studio.newResource.create')}
        cancelText={t('shell.studio.cancel')}
        okButtonProps={{ disabled: kind === undefined || value.trim() === '' }}
        confirmLoading={creating}
        onOk={create}
        onCancel={() => setOpen(false)}
      >
        <Form layout="vertical">
          <Form.Item label={t('shell.studio.newResource.kind')}>
            <Select
              aria-label={t('shell.studio.newResource.kind')}
              value={kind}
              // All kinds render, so each one can be reached without scrolling the list.
              virtual={false}
              options={starterKinds.map((starterKind) => ({ value: starterKind, label: starterKind }))}
              onChange={(next) => {
                setKind(next)
                setProblem(undefined)
              }}
            />
          </Form.Item>
          <Form.Item
            label={label}
            validateStatus={problem === undefined ? undefined : 'error'}
            help={problem === undefined ? undefined : t(`shell.studio.newResource.${problem}`)}
          >
            <Input
              aria-label={label}
              value={value}
              onChange={(event) => {
                setValue(event.target.value)
                setProblem(undefined)
              }}
              onPressEnter={create}
            />
          </Form.Item>
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
