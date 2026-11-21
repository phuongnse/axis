import { HighlightStyle, syntaxHighlighting } from '@codemirror/language'
import type { Extension } from '@codemirror/state'
import { EditorView } from '@codemirror/view'
import { tags } from '@lezer/highlight'
import type { DesignTokens } from '../platform/designTokens'
import type { ThemeMode } from '../platform/themeMode'

/** The colour of each kind of token in a code editor, from the design tokens. */
export function codeHighlightStyle(colour: DesignTokens['colour']): HighlightStyle {
  return HighlightStyle.define([
    { tag: [tags.propertyName, tags.variableName, tags.namespace], color: colour.primaryText },
    { tag: tags.string, color: colour.text },
    { tag: tags.number, color: colour.infoText },
    { tag: [tags.bool, tags.null, tags.keyword], color: colour.warningText },
    { tag: [tags.punctuation, tags.operator], color: colour.textMuted },
    // Italics keep a comment apart from punctuation, which is also `textMuted`.
    { tag: tags.comment, color: colour.textMuted, fontStyle: 'italic' },
    { tag: tags.typeName, color: colour.successText },
  ])
}

/**
 * Builds a theme once for a set of tokens, a mode and any extra keys, so every editor shares it. An extra key, such as
 * the selected tint of the theme in use, builds one more theme. Each theme adds its style rules to the page, and the
 * more rules there are, the slower every style lookup is.
 */
export function sharedTheme<Keys extends string[] = []>(
  build: (tokens: DesignTokens, mode: ThemeMode, ...keys: Keys) => Extension,
) {
  const built = new WeakMap<DesignTokens, Map<string, Extension>>()
  return (tokens: DesignTokens, mode: ThemeMode, ...keys: Keys): Extension => {
    const themes = built.get(tokens) ?? new Map<string, Extension>()
    built.set(tokens, themes)
    const key = [mode, ...keys].join(' ')
    let theme = themes.get(key)
    if (!theme) {
      theme = build(tokens, mode, ...keys)
      themes.set(key, theme)
    }
    return theme
  }
}

/** The look of a code editor, as a field's border and the token colours, shared by every code editor in Studio. */
export const codeEditorTheme = sharedTheme(buildCodeEditorTheme)

function buildCodeEditorTheme({ colour, radius, type }: DesignTokens, mode: ThemeMode): Extension {
  return [
    EditorView.theme(
      {
        '&': {
          color: colour.text,
          backgroundColor: colour.surface,
          border: `1px solid ${colour.border}`,
          borderRadius: `${radius.base}px`,
          fontFamily: type.codeFontFamily,
          fontSize: `${type.fontSize}px`,
        },
        '&.cm-focused': { outline: `2px solid ${colour.focusRing}` },
        '.cm-content': { caretColor: colour.text },
        '.cm-scroller': { fontFamily: 'inherit' },
      },
      { dark: mode === 'dark' },
    ),
    syntaxHighlighting(codeHighlightStyle(colour)),
  ]
}
