import { SettingOutlined } from '@ant-design/icons'
import { Dropdown } from 'antd'
import { AxisButton } from './AxisButton'
import { menuOf } from './menuActions'
import { useText } from './texts'
import { useThemeMode, type ThemeChoice } from './themeMode'

const choices: readonly ThemeChoice[] = ['system', 'light', 'dark']

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
  const { choice, choose } = useThemeMode()

  // Each language and each theme is a choice in its own group. Picking the current one changes nothing.
  const menu = menuOf(
    [
      ...locales.map((tag) => ({
        key: `locale:${tag}`,
        labelKey: `shell.locale.${tag}`,
        groupKey: 'shell.locale.label',
        selected: tag === locale,
        onClick: () => {
          if (tag !== locale) onLocaleChange(tag)
        },
      })),
      ...choices.map((candidate) => ({
        key: `theme:${candidate}`,
        labelKey: `shell.theme.${candidate}`,
        groupKey: 'shell.theme.label',
        selected: candidate === choice,
        onClick: () => {
          if (candidate !== choice) choose(candidate)
        },
      })),
    ],
    t,
  )
  const label = t('shell.preferences.label')

  return (
    <Dropdown trigger={['click']} menu={{ ...menu, 'aria-label': label }}>
      <AxisButton role="subtle" nameKey="shell.preferences.label" icon={<SettingOutlined />} />
    </Dropdown>
  )
}
