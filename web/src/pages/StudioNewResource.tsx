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
import { studioKindGroups } from './studioViews'
import { dialogWidth } from '../platform/dialogButtons'

/** The locale a new text resource copies when the application has no site. */
const fallbackLocale = 'en'

/** What the dialog says is wrong with the author's input, as the text key to show under the field. */
type Problem = 'exists' | 'invalid' | 'failed'

/**
 * The New resource button of the open application, and its dialog. Proposed: there are two. The band's New resource…,
 * on every page, asks for a kind, by its name in the sidebar's groups, then for a name. A kind's list has its own
 * button, such as New entity…, whose dialog asks for the name alone, as the kind is the list's. The dialog asks for a
 * locale instead of a name when the kind is `text`, and creates the resource from the kind's starter with an id from
 * `crypto.randomUUID()`. A new text resource is named after its locale, such as `TextsVi`, and copies every key and
 * text of the default locale of the first site in file order, or of `en` when there is no site, so every label still
 * resolves. A `409` or a `400` shows under the field, and the dialog stays open. `onCreated` gets the new resource and
 * the folder's diagnostics after the write.
 */
export function StudioNewResource({
  application,
  resources,
  kind: listKind,
  inBand,
  onCreated,
}: {
  application: string
  resources: StudioResourceItem[]
  /**
   * Proposed: the kind of the list the button sits on. When it has a starter, the button and the dialog name it, such
   * as New entity…, and the dialog asks no kind.
   */
  kind?: string
  /** Proposed: the band's button is a text button beside the band's other actions. */
  inBand?: boolean
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
  const fixedKind = listKind !== undefined && starterKinds.includes(listKind) ? listKind : undefined
  const fixedName = fixedKind ? kindOneName(fixedKind, t).toLocaleLowerCase() : undefined

  function show() {
    setKind(fixedKind)
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
      <Button type={inBand ? 'text' : 'default'} onClick={show}>
        {fixedName
          ? t('shell.studio.newResource.buttonOf').replace('{kind}', fixedName)
          : t('shell.studio.newResource.button')}
      </Button>
      <Modal
        width={dialogWidth('medium')}
        open={open}
        title={
          fixedName
            ? t('shell.studio.newResource.titleOf').replace('{kind}', fixedName)
            : t('shell.studio.newResource.title')
        }
        okText={t('shell.studio.newResource.create')}
        cancelText={t('shell.studio.cancel')}
        okButtonProps={{ disabled: kind === undefined || value.trim() === '' }}
        confirmLoading={creating}
        onOk={create}
        onCancel={() => setOpen(false)}
      >
        <Form layout="vertical">
          {fixedKind === undefined && (
            <Form.Item label={t('shell.studio.newResource.kind')}>
              <Select
                aria-label={t('shell.studio.newResource.kind')}
                value={kind}
                // All kinds render, so each one can be reached without scrolling the list, and the author can type to narrow it.
                showSearch
                optionFilterProp="label"
                virtual={false}
                options={kindOptions(t)}
                onChange={(next) => {
                  setKind(next)
                  setProblem(undefined)
                }}
              />
            </Form.Item>
          )}
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

/** A kind's name for one resource, such as Entity, or the kind itself when it has no text. */
function kindOneName(kind: string, t: (key: string) => string): string {
  const key = `shell.studio.kindOne.${kind}`
  const name = t(key)
  return name === key ? kind : name
}

/**
 * Proposed: the kinds the dialog offers, in the sidebar's groups and order, each by its name for one resource, and the
 * kinds outside every group under Other.
 */
function kindOptions(t: (key: string) => string) {
  const grouped = new Set(studioKindGroups.flatMap((group) => group.kinds.map((entry) => entry.kind)))
  const groups = studioKindGroups.map((group) => ({
    key: group.key,
    kinds: group.kinds.map((entry) => entry.kind).filter((kind) => starterKinds.includes(kind)),
  }))
  groups.push({ key: 'other', kinds: starterKinds.filter((kind) => !grouped.has(kind)) })
  return groups
    .filter((group) => group.kinds.length > 0)
    .map((group) => ({
      label: t(`shell.studio.nav.${group.key}`),
      title: t(`shell.studio.nav.${group.key}`),
      options: group.kinds.map((kind) => ({ value: kind, label: kindOneName(kind, t) })),
    }))
}
