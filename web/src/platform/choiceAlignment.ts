import { theme, type ConfigProviderProps, type ThemeConfig } from 'antd'

/**
 * Proposed: the box of every checkbox and the circle of every radio sit on the first line of their label, so a label
 * that wraps keeps its box at the top, not in its middle. Ant Design centres the box on the whole label. The offset is
 * half the first line's height less the box, from the theme's font size, line height and box size.
 */
export function choiceAlignment(config: ThemeConfig): {
  checkbox: ConfigProviderProps['checkbox']
  radio: ConfigProviderProps['radio']
} {
  const token = theme.getDesignToken(config)
  const icon = {
    alignSelf: 'flex-start',
    marginTop: (token.fontSize * token.lineHeight - token.controlInteractiveSize) / 2,
  } as const
  return { checkbox: { styles: { icon } }, radio: { styles: { icon } } }
}
