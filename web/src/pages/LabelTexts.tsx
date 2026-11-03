import { TranslationOutlined } from '@ant-design/icons'
import { Alert, Button, Flex, Input, Modal, Space, Typography } from 'antd'
import { useContext, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { dialogWidth } from '../platform/dialogButtons'
import { getAt } from '../platform/jsonPatch'
import { labelKey } from '../platform/labelKeys'
import { putStudioTexts, ServerUnreachableError, type StudioNames } from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
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
  // Proposed: the texts as they were when Translations opened, which Cancel puts back, or null while it is closed.
  const [translating, setTranslating] = useState<Record<string, string> | null>(null)
  const uiLocale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour } = useDesignTokens()

  const known = names.texts.find((text) => text.key === key)
  const others = known ? known.usedBy - (getAt(resource.saved, `${pointer}/textKey`) === key ? 1 : 0) : 0
  // Proposed: inside an editor with one Save, a label is a text box per language, named by the language, and its key
  // is Studio's own: it is made from the resource and the path and never shown.
  // Proposed: inside an editor with one Save, a label is one text box in the author's language, or the application's
  // first when it lacks that one, as content tools edit one language at a time. Translations…, an icon beside it, opens
  // every language of the label in a medium dialog, and a muted line under it names the languages it still lacks.
  // Many labels at once are translated in the text resources' table. Its key is Studio's own and never shown.
  if (resource.setPending) {
    const setPending = resource.setPending
    const shown = resource.pending?.[key] ?? textsOf(names, key)
    const language = new Intl.DisplayNames([uiLocale], { type: 'language' })
    const nameOf = (locale: string) => language.of(locale) ?? locale
    const primary = names.locales.includes(uiLocale) ? uiLocale : names.locales[0]
    const missing = names.locales.filter((locale) => locale !== primary && (shown[locale] ?? '').trim() === '')
    const write = (locale: string, text: string) => {
      setPending(key, { ...shown, [locale]: text })
      if (getAt(value, '/textKey') !== key) {
        onChange(pointer, { ...(isRecord(value) ? value : {}), textKey: key })
      }
    }
    return (
      <Flex vertical gap={4} data-testid="studio-label">
        {/* Proposed: Translations sits inside the box, at its end, as a password box holds its eye, so the box is as wide
            as every other field's. It stays a real button, reached with Tab and named by its tooltip. */}
        <Input
          aria-label={nameOf(primary)}
          value={shown[primary] ?? ''}
          onChange={(event) => write(primary, event.target.value)}
          suffix={
            names.locales.length > 1 ? (
              <AxisButton
                role="icon"
                size="small"
                nameKey="shell.studio.translations.open"
                icon={<TranslationOutlined />}
                onClick={() => setTranslating(shown)}
                data-testid="studio-label-translations"
                style={{ marginInlineEnd: -4, marginBlock: -1 }}
              />
            ) : undefined
          }
        />
        {missing.length > 0 && (
          <Typography.Text style={{ color: colour.textMuted, fontSize: 12 }} data-testid="studio-label-missing">
            {t('shell.studio.translations.missing').replace(
              '{languages}',
              new Intl.ListFormat(uiLocale, { type: 'conjunction' }).format(missing.map(nameOf)),
            )}
          </Typography.Text>
        )}
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
        <Modal
          open={translating !== null}
          width={dialogWidth('medium')}
          title={t('shell.studio.translations.title')}
          okText={t('shell.studio.list.done')}
          cancelText={t('shell.studio.cancel')}
          onOk={() => setTranslating(null)}
          onCancel={() => {
            if (translating) {
              setPending(key, translating)
            }
            setTranslating(null)
          }}
          destroyOnHidden
        >
          <div
            data-testid="studio-label-translations-dialog"
            style={{
              display: 'grid',
              gridTemplateColumns: 'max-content minmax(0, 1fr)',
              columnGap: 16,
              rowGap: 8,
              alignItems: 'center',
            }}
          >
            {names.locales.map((locale) => [
              <Typography.Text key={`${locale}/name`}>{nameOf(locale)}</Typography.Text>,
              <Input
                key={`${locale}/text`}
                aria-label={nameOf(locale)}
                value={shown[locale] ?? ''}
                onChange={(event) => write(locale, event.target.value)}
              />,
            ])}
          </div>
        </Modal>
      </Flex>
    )
  }

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
