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
  ])
}

/** The look of a code editor, as a field's border and the token colours, shared by every code editor in Studio. */
export function codeEditorTheme({ colour, radius, type }: DesignTokens, mode: ThemeMode): Extension {
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
