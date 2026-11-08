import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { PlatformContext, type Platform } from './context'
import { LabelTexts } from './LabelTexts'
import type { ChangeHandler } from './SchemaForm'
import { LabelTextsContext } from './labelTextsContext'

const names = {
  locales: ['en', 'vi', 'fr', 'de'],
  texts: [
    { key: 'shop.title', texts: { en: 'Shop', vi: 'Cửa hàng', fr: 'Boutique', de: 'Laden' }, usedBy: 1 },
    { key: 'shop.partial', texts: { en: 'Partial', fr: 'Partiel', de: 'Teilweise' }, usedBy: 1 },
    { key: 'shop.two', texts: { en: 'Two', fr: 'Deux' }, usedBy: 1 },
    { key: 'shop.sparse', texts: { en: 'Sparse' }, usedBy: 1 },
    { key: 'shop.blankEnglish', texts: { vi: 'Chỉ tiếng Việt' }, usedBy: 1 },
    { key: 'shop.spaces', texts: { en: 'Spaces', vi: '   ', fr: 'Espaces', de: 'Leerzeichen' }, usedBy: 1 },
  ],
} as unknown as StudioNames

type TextsChange = (pointer: string, key: string, texts: Record<string, string>) => void

function renderLabel({
  textKey,
  withNames = true,
  withResource = true,
  locale,
  onChange = vi.fn<ChangeHandler>(),
  onTextsChange = vi.fn<TextsChange>(),
}: {
  textKey?: string
  withNames?: boolean
  withResource?: boolean
  locale?: string
  onChange?: ReturnType<typeof vi.fn<ChangeHandler>>
  onTextsChange?: ReturnType<typeof vi.fn<TextsChange>>
}) {
  const resource = {
    application: 'Shop',
    file: 'pages/home.json',
    name: 'Home',
    saved: {},
    texts: {},
    onTextsChange,
  }
  const label = (
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioNamesContext.Provider value={withNames ? names : undefined}>
          <LabelTextsContext.Provider value={withResource ? resource : null}>
            <LabelTexts pointer="/title" value={textKey === undefined ? undefined : { textKey }} onChange={onChange} />
          </LabelTextsContext.Provider>
        </StudioNamesContext.Provider>
      </TextProvider>
    </ThemeModeProvider>
  )
  render(
    locale === undefined ? (
      label
    ) : (
      <PlatformContext.Provider value={{ current: { locale, texts: {} } } as Platform}>
        {label}
      </PlatformContext.Provider>
    ),
  )
  return { onChange, onTextsChange }
}

afterEach(() => localStorage.clear())

async function openTranslations() {
  await userEvent.click(screen.getByRole('button', { name: 'Translations…' }))
  return screen.findByRole('dialog')
}

describe('LabelTexts', () => {
  it('shows one box in the author language and no text key box', () => {
    renderLabel({ textKey: 'shop.title' })

    expect(screen.getByRole('textbox', { name: 'English' })).toHaveValue('Shop')
    expect(screen.queryByRole('textbox', { name: 'Vietnamese' })).toBeNull()
    expect(screen.queryByRole('textbox', { name: 'Text key' })).toBeNull()
  })

  it('shows the box in the language of the author when the application has it', () => {
    renderLabel({ textKey: 'shop.title', locale: 'vi' })

    expect(screen.getByRole('textbox', { name: 'Tiếng Việt' })).toHaveValue('Cửa hàng')
    expect(screen.queryByRole('textbox', { name: 'Tiếng Anh' })).toBeNull()
  })

  it('shows the first language of the application when it lacks the language of the author', () => {
    renderLabel({ textKey: 'shop.title', locale: 'ja' })

    const english = new Intl.DisplayNames(['ja'], { type: 'language' }).of('en')
    expect(screen.getByRole('textbox', { name: english })).toHaveValue('Shop')
  })

  it('edits every language in Translations… and keeps them as one change on Done', async () => {
    const { onChange, onTextsChange } = renderLabel({ textKey: 'shop.title' })

    const dialog = await openTranslations()
    const english = within(dialog).getByRole('textbox', { name: 'English' })
    const vietnamese = within(dialog).getByRole('textbox', { name: 'Vietnamese' })
    await userEvent.clear(english)
    await userEvent.type(english, 'Store')
    await userEvent.clear(vietnamese)
    await userEvent.type(vietnamese, 'Gian hàng')
    expect(onTextsChange).not.toHaveBeenCalled()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Done' }))

    expect(onTextsChange).toHaveBeenCalledTimes(1)
    expect(onTextsChange).toHaveBeenCalledWith('/title', 'shop.title', {
      en: 'Store',
      vi: 'Gian hàng',
      fr: 'Boutique',
      de: 'Laden',
    })
    expect(onChange).not.toHaveBeenCalled()
  })

  it('puts the texts back on Cancel', async () => {
    const { onChange, onTextsChange } = renderLabel({ textKey: 'shop.title' })

    const dialog = await openTranslations()
    const vietnamese = within(dialog).getByRole('textbox', { name: 'Vietnamese' })
    await userEvent.clear(vietnamese)
    await userEvent.type(vietnamese, 'Gian hàng')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(onTextsChange).not.toHaveBeenCalled()
    expect(onChange).not.toHaveBeenCalled()
    expect(within(screen.getByTestId('studio-label')).getByRole('textbox', { name: 'English' })).toHaveValue('Shop')
    await userEvent.click(screen.getByRole('button', { name: 'Translations…' }))
    for (const box of await screen.findAllByRole('textbox', { name: 'Vietnamese' })) {
      expect(box).toHaveValue('Cửa hàng')
    }
  })

  it('names the one language that has no text', () => {
    renderLabel({ textKey: 'shop.partial' })

    expect(screen.getByTestId('studio-label-not-translated')).toHaveTextContent('Not translated into Vietnamese')
  })

  it('names two languages that have no text', () => {
    renderLabel({ textKey: 'shop.two' })

    expect(screen.getByTestId('studio-label-not-translated')).toHaveTextContent(
      'Not translated into Vietnamese and German',
    )
  })

  it('counts a text of only spaces as missing', () => {
    renderLabel({ textKey: 'shop.spaces' })

    expect(screen.getByTestId('studio-label-not-translated')).toHaveTextContent('Not translated into Vietnamese')
  })

  it('counts three languages that have no text', () => {
    renderLabel({ textKey: 'shop.sparse' })

    expect(screen.getByTestId('studio-label-not-translated')).toHaveTextContent('Not translated into 3 languages')
  })

  it('shows no line when every language has a text', () => {
    renderLabel({ textKey: 'shop.title' })

    expect(screen.queryByTestId('studio-label-not-translated')).toBeNull()
  })

  it('shows no line while the shown box is empty', () => {
    renderLabel({ textKey: 'shop.blankEnglish' })

    expect(screen.getByRole('textbox', { name: 'English' })).toHaveValue('')
    expect(screen.queryByTestId('studio-label-not-translated')).toBeNull()
  })

  it('shows a disabled empty box before the names load, with no key box', () => {
    renderLabel({ textKey: 'shop.title', withNames: false })

    const box = screen.getByRole('textbox')
    expect(box).toBeDisabled()
    expect(box).toHaveValue('')
    expect(screen.queryByRole('textbox', { name: 'Text key' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Translations…' })).toBeNull()
  })

  it('makes a key from the resource and the path when the label has none', async () => {
    const { onChange, onTextsChange } = renderLabel({})

    await userEvent.type(screen.getByRole('textbox', { name: 'English' }), 'H')

    expect(onTextsChange).toHaveBeenCalledWith('/title', 'pages.home.title', expect.objectContaining({ en: 'H' }))
    expect(onChange).toHaveBeenCalledWith('/title', { textKey: 'pages.home.title' })
  })

  it('draws the plain text key box where there is no resource to write texts to', async () => {
    const { onChange } = renderLabel({ textKey: 'shop.title', withResource: false })

    const key = screen.getByRole('textbox', { name: 'Text key' })
    expect(key).toHaveValue('shop.title')
    await userEvent.type(key, 'x')

    expect(onChange).toHaveBeenLastCalledWith('/title/textKey', 'shop.titlex')
    expect(screen.queryByRole('button', { name: 'Translations…' })).toBeNull()
  })
})
