import { TranslationOutlined } from '@ant-design/icons'
import { Alert, Flex, Input, Modal } from 'antd'
import { useContext, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { getAt } from '../platform/jsonPatch'
import { labelKey } from '../platform/labelKeys'
import { languageName, type StudioNames } from '../platform/studio'
import { StudioNamesContext, textsOf } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import type { ChangeHandler } from './SchemaForm'
import { LabelTextsContext } from './labelTextsContext'

type Resource = NonNullable<React.ContextType<typeof LabelTextsContext>>

/**
 * The editor of a label, an object whose only property is `textKey`, see
 * docs/reference/studio-resource-form.md#labels. It shows one box in the author's language, a Translations… dialog
 * for every language and a line that names the languages that lack a text. Typing is a change of the draft, so the
 * draft names the key once a text is typed, and Save writes the texts before the patch, see
 * docs/reference/studio.md#label-texts. A label with no key, or an empty one, gets a key made from the resource and the
 * path, which is never shown. Other labels that use the key are counted, because writing the texts changes them too.
 * Outside the resource's context, as in the module editor, it is the plain key box, because a module layer's label is
 * a key and a save there writes no text.
 */
export function LabelTexts({ pointer, value, onChange }: { pointer: string; value: unknown; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const resource = useContext(LabelTextsContext)
  const textKey = getAt(value, '/textKey')

  if (resource === null) {
    return (
      <Input
        aria-label={t('shell.studio.labelKey')}
        value={typeof textKey === 'string' ? textKey : ''}
        onChange={(event) => onChange(`${pointer}/textKey`, event.target.value)}
      />
    )
  }
  if (names === undefined) {
    return (
      <div data-testid="studio-label">
        <Input disabled aria-busy="true" />
      </div>
    )
  }
  return <LabelEditor pointer={pointer} value={value} onChange={onChange} names={names} resource={resource} />
}

function LabelEditor({
  pointer,
  value,
  onChange,
  names,
  resource,
}: {
  pointer: string
  value: unknown
  onChange: ChangeHandler
  names: StudioNames
  resource: Resource
}) {
  const t = useText()
  const { colour, size, spacing } = useDesignTokens()
  const author = useContext(PlatformContext)?.current.locale ?? 'en'
  const draftValue = getAt(value, '/textKey')
  const draftKey = typeof draftValue === 'string' ? draftValue : ''
  const [key, setKey] = useState(() =>
    draftKey !== ''
      ? draftKey
      : labelKey(resource.file, resource.name, `${pointer}/textKey`, new Set(names.texts.map((text) => text.key))),
  )
  const [seenKey, setSeenKey] = useState(draftKey)
  // A key that the draft takes from outside, such as an Undo, replaces the key.
  if (draftKey !== seenKey) {
    setSeenKey(draftKey)
    if (draftKey !== '') {
      setKey(draftKey)
    }
  }
  // The dialog edits a copy of the texts that Done keeps and Cancel throws away.
  const [copy, setCopy] = useState<Record<string, string> | null>(null)
  const [open, setOpen] = useState(false)

  const texts = resource.texts[key] ?? textsOf(names, key)
  const keyUsable = key !== '' && !key.includes('/')
  const known = names.texts.find((text) => text.key === key)
  const others = known ? known.usedBy - (getAt(resource.saved, `${pointer}/textKey`) === key ? 1 : 0) : 0
  const shown = names.locales.includes(author) ? author : names.locales[0]
  const blank = (locale: string) => (texts[locale] ?? '').trim() === ''
  const missing = shown === undefined || blank(shown) ? [] : names.locales.filter((locale) => blank(locale))

  function changeTexts(next: Record<string, string>) {
    resource.onTextsChange(pointer, key, next)
    if (keyUsable && draftKey !== key) {
      onChange(pointer, { ...(isRecord(value) ? value : {}), textKey: key })
    }
  }

  function notTranslated() {
    if (missing.length > 2) {
      return t('shell.studio.label.notTranslatedCount').replace('{count}', String(missing.length))
    }
    const languages = new Intl.ListFormat(author, { type: 'conjunction' }).format(
      missing.map((locale) => languageName(locale, author)),
    )
    return t('shell.studio.label.notTranslated').replace('{languages}', languages)
  }

  return (
    <Flex vertical gap="small" data-testid="studio-label">
      <div>
        <Input
          aria-label={shown === undefined ? undefined : languageName(shown, author)}
          value={shown === undefined ? '' : (texts[shown] ?? '')}
          onChange={(event) => shown !== undefined && changeTexts({ ...texts, [shown]: event.target.value })}
          suffix={
            <AxisButton
              role="icon"
              nameKey="shell.studio.label.translations"
              icon={<TranslationOutlined />}
              onClick={() => {
                setCopy({ ...texts })
                setOpen(true)
              }}
            />
          }
        />
        {missing.length > 0 && (
          <div
            style={{ fontSize: 12, lineHeight: '20px', color: colour.textMuted, marginTop: spacing.xs }}
            data-testid="studio-label-not-translated"
          >
            {notTranslated()}
          </div>
        )}
      </div>
      <Modal
        open={open}
        width={size.dialog.medium}
        title={t('shell.studio.label.translationsTitle')}
        okText={t('shell.studio.label.done')}
        cancelText={t('shell.studio.cancel')}
        onOk={() => {
          if (copy !== null) {
            changeTexts(copy)
          }
          setOpen(false)
        }}
        onCancel={() => setOpen(false)}
        destroyOnHidden
      >
        <Flex vertical gap="small">
          {names.locales.map((locale) => (
            <Input
              key={locale}
              aria-label={languageName(locale, author)}
              value={copy?.[locale] ?? ''}
              onChange={(event) => setCopy((current) => ({ ...current, [locale]: event.target.value }))}
            />
          ))}
        </Flex>
      </Modal>
      {others > 0 && (
        // oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert.
        <Alert
          data-testid="studio-label-used-by"
          type="warning"
          title={
            <>
              {t('shell.studio.labelUsedBy')} <strong>{others}</strong>
            </>
          }
        />
      )}
    </Flex>
  )
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
