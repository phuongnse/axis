import { Flex, Input, Modal, Select, type InputRef, type RefSelectProps } from 'antd'
import { useRef, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { dialogWidth } from '../platform/dialogButtons'
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

/** The locale a new text resource copies when the application has no site. */
const fallbackLocale = 'en'

/** What the dialog says is wrong with the author's input, as the text key to show under the field. */
type Problem = 'exists' | 'invalid' | 'failed'

/**
 * The New resource button of the open application's band, a text button, and its dialog. The dialog asks for a kind, listed by its singular
 * name under the sidebar's groups, and a name, or for a locale when the kind is `text`. With `kind` set, it is instead the
 * button of that kind's list, named for the kind, such as New entity…, an outlined main button, and its dialog asks for the name
 * or the locale alone. It creates the resource from
 * the kind's starter with an id from `crypto.randomUUID()`. A new text resource is named after its locale, such as `TextsVi`, and copies every key and
 * text of the default locale of the first site in file order, or of `en` when there is no site, so every label still
 * resolves. A `409` or a `400` shows under the field, and the dialog stays open. `onCreated` gets the new resource and
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
  const { lost } = useStudioConnection()
  const [open, setOpen] = useState(false)
  const [chosenKind, setKind] = useState<string | undefined>(fixedKind)
  const kind = fixedKind ?? chosenKind
  const [value, setValue] = useState('')
  const [problem, setProblem] = useState<Problem>()
  const [creating, setCreating] = useState(false)
  // Proposed: whether Create was clicked, so a field left empty says so from then on.
  const [checked, setChecked] = useState(false)
  const kindInput = useRef<RefSelectProps>(null)
  const nameInput = useRef<InputRef>(null)
  const isText = kind === 'text'
  const kindOptions = kindGroupNames.flatMap((group) => {
    const options = studioKinds
      .filter((entry) => entry.group === group && starterKinds.includes(entry.kind))
      .map((entry) => ({ value: entry.kind, label: kindSingular(entry.kind, t) }))
    const title = t(`shell.studio.nav.group.${group}`)
    return options.length === 0 ? [] : [{ label: title, title, options }]
  })
  const label = t(isText ? 'shell.studio.newResource.locale' : 'shell.studio.newResource.name')

  const kindError = checked && kind === undefined ? t('shell.studio.newResource.kindMissing') : null
  const nameError =
    problem !== undefined
      ? t(`shell.studio.newResource.${problem}`)
      : checked && value.trim() === ''
        ? t(isText ? 'shell.studio.newResource.localeMissing' : 'shell.studio.newResource.nameMissing')
        : null

  function show() {
    setKind(fixedKind)
    setValue('')
    setProblem(undefined)
    setChecked(false)
    setOpen(true)
  }

  async function create() {
    // Proposed: Create is never disabled. A click checks the fields, says under each what is missing and focuses the
    // first, as the form widget's Save does.
    setChecked(true)
    if (kind === undefined) {
      kindInput.current?.focus()
      return
    }
    if (value.trim() === '') {
      nameInput.current?.focus()
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
        onCreated({ ...result.item, titles: {}, locale: kind === 'text' ? entered : null }, result.diagnostics)
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
      <AxisButton role={fixedKind === undefined ? 'secondary' : 'main'} onClick={show}>
        {t(
          fixedKind === undefined
            ? 'shell.studio.newResource.button'
            : `shell.studio.newResource.kindButton.${fixedKind}`,
        )}
      </AxisButton>
      <Modal
        width={dialogWidth('medium')}
        open={open}
        title={t(
          fixedKind === undefined
            ? 'shell.studio.newResource.title'
            : `shell.studio.newResource.kindTitle.${fixedKind}`,
        )}
        okText={t('shell.studio.newResource.create')}
        cancelText={t('shell.studio.cancel')}
        confirmLoading={creating}
        onOk={create}
        onCancel={() => setOpen(false)}
      >
        <Flex vertical gap="middle">
          {fixedKind === undefined && (
            <AxisField
              label={t('shell.studio.newResource.kind')}
              help={t('shell.studio.newResource.kind.help')}
              required
              error={kindError}
            >
              <Select
                ref={kindInput}
                aria-label={t('shell.studio.newResource.kind')}
                aria-required
                value={kind}
                style={{ width: '100%' }}
                // All kinds render, so each one can be reached without scrolling the list, and the author can type to narrow it.
                showSearch
                optionFilterProp="label"
                virtual={false}
                options={kindOptions}
                onChange={(next) => {
                  setKind(next)
                  setProblem(undefined)
                }}
              />
            </AxisField>
          )}
          <AxisField
            label={label}
            help={t(
              isText
                ? 'shell.studio.newResource.locale.help'
                : kind === 'role'
                  ? 'shell.studio.newResource.roleName.help'
                  : 'shell.studio.newResource.name.help',
            )}
            required
            error={nameError}
          >
            <Input
              ref={nameInput}
              aria-label={label}
              aria-required
              value={value}
              onChange={(event) => {
                setValue(event.target.value)
                setProblem(undefined)
              }}
              onPressEnter={create}
            />
          </AxisField>
        </Flex>
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
