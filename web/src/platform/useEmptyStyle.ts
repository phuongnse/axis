import type { CSSProperties } from 'react'
import { useDesignTokens } from './themeMode'

/** The style of an empty value: muted italics. What tells an empty value from a typed one is the styling. */
export function useEmptyStyle(): CSSProperties {
  return { fontStyle: 'italic', color: useDesignTokens().colour.textMuted }
}
