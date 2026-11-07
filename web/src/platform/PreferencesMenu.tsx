import { SettingOutlined } from '@ant-design/icons'
import { Dropdown, type MenuProps } from 'antd'
import { AxisButton } from './AxisButton'
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

  function option(key: string, label: string, current: boolean) {
    return { key, label, role: 'menuitemradio', 'aria-checked': current }
  }

  const items = [
    {
      type: 'group',
      key: 'locale',
      label: t('shell.locale.label'),
      children: locales.map((tag) => option(`locale:${tag}`, t(`shell.locale.${tag}`), tag === locale)),
    },
    { type: 'divider' },
    {
      type: 'group',
      key: 'theme',
      label: t('shell.theme.label'),
      children: choices.map((candidate) =>
        option(`theme:${candidate}`, t(`shell.theme.${candidate}`), candidate === choice),
      ),
    },
  ] as MenuProps['items']
  const selectedKeys = [`locale:${locale}`, `theme:${choice}`]
  const label = t('shell.preferences.label')

  function pick({ key }: { key: string }) {
    const [kind, value] = key.split(':')
    if (kind === 'locale' && value !== locale) {
      onLocaleChange(value)
    } else if (kind === 'theme' && value !== choice) {
      choose(value as ThemeChoice)
    }
  }

  return (
    <Dropdown trigger={['click']} menu={{ items, selectable: true, selectedKeys, onClick: pick, 'aria-label': label }}>
      <AxisButton role="icon" nameKey="shell.preferences.label" icon={<SettingOutlined />} />
    </Dropdown>
  )
}
