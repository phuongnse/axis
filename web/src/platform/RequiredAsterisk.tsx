import { useDesignTokens } from './themeMode'

export function RequiredAsterisk() {
  const { colour } = useDesignTokens()
  return (
    <span aria-hidden="true" style={{ color: colour.dangerText, marginInlineStart: 4 }}>
      *
    </span>
  )
}
