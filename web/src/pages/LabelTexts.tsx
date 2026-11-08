import { Alert, Flex, Input, Space } from 'antd'
import { useContext, useState } from 'react'
import { getAt } from '../platform/jsonPatch'
import { labelKey } from '../platform/labelKeys'
import { StudioNamesContext, textsOf } from '../platform/studioNames'
import { useText } from '../platform/texts'
import type { ChangeHandler } from './SchemaForm'
import { LabelTextsContext } from './labelTextsContext'

/**
 * The editor of a label, an object whose only property is `textKey`. It shows the key and a text box per locale.
 * Typing is a change of the draft, so the draft names the key once the texts are typed, and Save writes the texts
 * before the patch, see docs/reference/studio.md#label-texts. A label with no key, or an empty one, gets a key made from
 * the resource and the path. Other labels that use the key are counted, because writing the texts changes them too. It
 * renders only inside the contexts of the names and of the resource.
 */
export function LabelTexts({ pointer, value, onChange }: { pointer: string; value: unknown; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)!
  const resource = useContext(LabelTextsContext)!
  const draftValue = getAt(value, '/textKey')
  const draftKey = typeof draftValue === 'string' ? draftValue : ''
  const [key, setKey] = useState(() =>
    draftKey !== ''
      ? draftKey
      : labelKey(resource.file, resource.name, `${pointer}/textKey`, new Set(names.texts.map((text) => text.key))),
  )
  const [seenKey, setSeenKey] = useState(draftKey)
  // A key that the draft takes from outside, such as an Undo, replaces the key in the box.
  if (draftKey !== seenKey) {
    setSeenKey(draftKey)
    if (draftKey !== '') {
      setKey(draftKey)
    }
  }

  const texts = resource.texts[key] ?? textsOf(names, key)
  const keyUsable = key !== '' && !key.includes('/')
  const known = names.texts.find((text) => text.key === key)
  const others = known ? known.usedBy - (getAt(resource.saved, `${pointer}/textKey`) === key ? 1 : 0) : 0

  function changeKey(next: string) {
    setKey(next)
    if (next === '' || next.includes('/')) {
      return
    }
    // A key with no texts yet takes the texts that are typed, as a renamed key keeps them.
    if (!names.texts.some((text) => text.key === next) && resource.texts[next] === undefined) {
      resource.onTextsChange(pointer, next, texts)
    }
    onChange(pointer, { ...(isRecord(value) ? value : {}), textKey: next })
  }

  function changeText(locale: string, next: string) {
    resource.onTextsChange(pointer, key, { ...texts, [locale]: next })
    if (keyUsable && draftKey !== key) {
      onChange(pointer, { ...(isRecord(value) ? value : {}), textKey: key })
    }
  }

  return (
    <Flex vertical gap="small" data-testid="studio-label">
      <Input aria-label={t('shell.studio.labelKey')} value={key} onChange={(event) => changeKey(event.target.value)} />
      {names.locales.map((locale) => (
        <Space.Compact key={locale} block>
          <Space.Addon>{locale}</Space.Addon>
          <Input
            aria-label={locale}
            value={texts[locale] ?? ''}
            onChange={(event) => changeText(locale, event.target.value)}
          />
        </Space.Compact>
      ))}
      {others > 0 && (
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
