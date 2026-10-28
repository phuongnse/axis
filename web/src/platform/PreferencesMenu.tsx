import { GlobalOutlined } from '@ant-design/icons'
import { Button, Dropdown, type MenuProps } from 'antd'
import { useText } from './texts'
import { useThemeMode, type ThemeMode } from './themeMode'

const modes: readonly ThemeMode[] = ['light', 'dark']

/** A menu item that is one of a set and says whether it is the current one. */
function choice(key: string, label: string, current: boolean) {
  return { key, label, role: 'menuitemradio', 'aria-checked': current }
}

/**
 * The "Language and theme" button in the shell header. Its menu holds the site's languages and
 * Light and Dark, with the current ones marked. Picking the current one changes nothing.
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
  const { mode, toggleMode } = useThemeMode()
  const items = [
    {
      type: 'group',
      key: 'locale',
      label: t('shell.locale.label'),
      children: locales.map((tag) => choice(`locale:${tag}`, t(`shell.locale.${tag}`), tag === locale)),
    },
    {
      type: 'group',
      key: 'theme',
      label: t('shell.theme.label'),
      children: modes.map((candidate) =>
        choice(`theme:${candidate}`, t(`shell.theme.${candidate}`), candidate === mode),
      ),
    },
  ] as MenuProps['items']
  const label = t('shell.preferences.label')

  function pick({ key }: { key: string }) {
    const [kind, value] = key.split(':')
    if (kind === 'locale' && value !== locale) {
      onLocaleChange(value)
    } else if (kind === 'theme' && value !== mode) {
      toggleMode()
    }
  }

  return (
    <Dropdown trigger={['click']} menu={{ items, onClick: pick, 'aria-label': label }}>
      <Button type="text" aria-label={label} icon={<GlobalOutlined aria-hidden />} />
    </Dropdown>
  )
}
