import { Alert, Button, Flex, Input, Space } from 'antd'
import { useContext, useState } from 'react'
import { getAt } from '../platform/jsonPatch'
import { labelKey } from '../platform/labelKeys'
import { putStudioTexts, ServerUnreachableError, type StudioNames } from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import type { ChangeHandler } from './SchemaForm'
import { LabelTextsContext } from './labelTextsContext'

/** The texts of a key, one entry per locale, with an empty text for a locale the key does not have. */
function textsOf(names: StudioNames, key: string): Record<string, string> {
  const known = names.texts.find((text) => text.key === key)
  return Object.fromEntries(names.locales.map((locale) => [locale, known?.texts[locale] ?? '']))
}

/**
 * The editor of a label, an object whose only property is `textKey`. It shows the key and a text box per locale.
 * Write texts sends every locale in one call, then puts the key in the draft, so the next Save saves it. A label with
 * no key, or an empty one, gets a key made from the resource and the path. Other labels that use the key are counted,
 * because writing the texts changes them too. It renders only inside the contexts of the names and of the resource.
 */
export function LabelTexts({ pointer, value, onChange }: { pointer: string; value: unknown; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)!
  const resource = useContext(LabelTextsContext)!
  const { reconnecting, lost } = useStudioConnection()
  const [key, setKey] = useState(() => {
    const current = getAt(value, '/textKey')
    return typeof current === 'string' && current !== ''
      ? current
      : labelKey(resource.file, resource.name, `${pointer}/textKey`, new Set(names.texts.map((text) => text.key)))
  })
  const [texts, setTexts] = useState(() => textsOf(names, key))
  const [writing, setWriting] = useState(false)
  const [written, setWritten] = useState(false)
  const [failed, setFailed] = useState(false)

  const known = names.texts.find((text) => text.key === key)
  const others = known ? known.usedBy - (getAt(resource.saved, `${pointer}/textKey`) === key ? 1 : 0) : 0

  function changeKey(next: string) {
    setKey(next)
    setWritten(false)
    setFailed(false)
    if (names.texts.some((text) => text.key === next)) {
      setTexts(textsOf(names, next))
    }
  }

  function changeText(locale: string, next: string) {
    setTexts((current) => ({ ...current, [locale]: next }))
    setWritten(false)
    setFailed(false)
  }

  async function write() {
    setWriting(true)
    setWritten(false)
    setFailed(false)
    try {
      const result = await putStudioTexts(
        resource.application,
        key,
        Object.fromEntries(names.locales.map((locale) => [locale, texts[locale] ?? ''])),
      )
      onChange(pointer, { ...(isRecord(value) ? value : {}), textKey: key })
      setWritten(true)
      resource.onTextsWritten(result.diagnostics)
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else {
        console.warn(`Writing the texts of '${key}' failed`, error)
        setFailed(true)
      }
    } finally {
      setWriting(false)
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
      {failed && (
        <Alert data-testid="studio-label-write-error" type="error" title={t('shell.studio.writeTextsFailed')} />
      )}
      <Flex gap="small" align="center">
        <Button disabled={writing || reconnecting || key === '' || key.includes('/')} onClick={write}>
          {t('shell.studio.writeTexts')}
        </Button>
        {written && <span data-testid="studio-label-written">{t('shell.studio.textsWritten')}</span>}
      </Flex>
    </Flex>
  )
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
