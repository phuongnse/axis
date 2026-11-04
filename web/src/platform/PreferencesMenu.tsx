import { SettingOutlined } from '@ant-design/icons'
import { Dropdown, type MenuProps } from 'antd'
import { useMemo, type CSSProperties, type ReactElement } from 'react'
import { selectedItemTint } from './antTheme'
import { AxisButton } from './AxisButton'
import { useText } from './texts'
import { useDesignTokens, useThemeMode, type ThemeChoice } from './themeMode'

const choices: readonly ThemeChoice[] = ['system', 'light', 'dark']

/** The texts of the language and theme menu: the trigger's name, the two group titles and the three theme choices. */
export interface PreferencesTexts {
  label: string
  language: string
  theme: string
  system: string
  light: string
  dark: string
}

/** A language the menu offers: its tag and the name it shows. */
export interface PreferencesLanguage {
  tag: string
  label: string
}

/**
 * Proposed: the language and theme menu that `children` opens, shared by the shell header and the identity provider's
 * pages. It holds every language offered and System, Light and Dark, with the current ones marked, so more languages
 * only add rows. Picking the current one changes nothing. The theme choice goes to the theme provider.
 */
export function PreferencesDropdown({
  languages,
  language,
  texts,
  onLanguageChange,
  children,
}: {
  languages: readonly PreferencesLanguage[]
  language: string
  texts: PreferencesTexts
  onLanguageChange: (tag: string) => void
  children: ReactElement
}) {
  const { mode, choice, choose } = useThemeMode()
  const { colour } = useDesignTokens()
  const selected = useMemo<CSSProperties>(
    () => ({ color: colour.primaryText, background: selectedItemTint(mode) }),
    [colour, mode],
  )

  function option(key: string, label: string, current: boolean) {
    return { key, label, role: 'menuitemradio', 'aria-checked': current, style: current ? selected : undefined }
  }

  const items = [
    {
      type: 'group',
      key: 'locale',
      label: texts.language,
      children: languages.map((entry) => option(`locale:${entry.tag}`, entry.label, entry.tag === language)),
    },
    { type: 'divider' },
    {
      type: 'group',
      key: 'theme',
      label: texts.theme,
      children: choices.map((candidate) => option(`theme:${candidate}`, texts[candidate], candidate === choice)),
    },
  ] as MenuProps['items']

  function pick({ key }: { key: string }) {
    const [kind, value] = key.split(':')
    if (kind === 'locale' && value !== language) {
      onLanguageChange(value)
    } else if (kind === 'theme' && value !== choice) {
      choose(value as ThemeChoice)
    }
  }

  return (
    <Dropdown trigger={['click']} menu={{ items, onClick: pick, 'aria-label': texts.label }}>
      {children}
    </Dropdown>
  )
}

/**
 * The "Language and theme" button in the shell header. Its menu holds the site's languages and
 * System, Light and Dark, with the current ones marked. Picking the current one changes nothing.
 */
export function PreferencesMenu({
  locales,
  locale,
  onLocaleChange,
}: {
  locales: readonly string[]
  locale: string
  onLocaleChange: (locale: string) => void
}) {
  const t = useText()
  const texts: PreferencesTexts = {
    label: t('shell.preferences.label'),
    language: t('shell.locale.label'),
    theme: t('shell.theme.label'),
    system: t('shell.theme.system'),
    light: t('shell.theme.light'),
    dark: t('shell.theme.dark'),
  }
  return (
    <PreferencesDropdown
      languages={locales.map((tag) => ({ tag, label: t(`shell.locale.${tag}`) }))}
      language={locale}
      texts={texts}
      onLanguageChange={onLocaleChange}
    >
      <AxisButton role="icon" nameKey="shell.preferences.label" icon={<SettingOutlined />} />
    </PreferencesDropdown>
  )
}
