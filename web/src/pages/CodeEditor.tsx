import {
  autocompletion,
  closeBracketsKeymap,
  closeBrackets,
  completeAnyWord,
  completionKeymap,
} from '@codemirror/autocomplete'
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { bracketMatching, codeFolding, foldGutter, foldKeymap, indentOnInput } from '@codemirror/language'
import { diagnosticCount, linter, lintGutter, nextDiagnostic, type Diagnostic } from '@codemirror/lint'
import { openSearchPanel, search, searchKeymap } from '@codemirror/search'
import { Annotation, Compartment, EditorState, Transaction } from '@codemirror/state'
import { EditorView, highlightActiveLine, highlightActiveLineGutter, keymap, lineNumbers } from '@codemirror/view'
import { useEffect, useImperativeHandle, useMemo, useRef, useState, type Ref, type RefObject } from 'react'
import { useSelectedItemTint } from '../platform/antTheme'
import { useText } from '../platform/texts'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'
import { codeEditorTheme, sharedTheme } from './codeEditorTheme'
import { indentFolding, languageSupport, type FileLanguage } from './fileLanguage'
import { findPanel, type FindTexts } from './jsonFindPanel'
import { useFillHeight } from './jsonViewHeight'

/** Marks a change that came from the value, not from the author, so it is not reported as an edit. */
const external = Annotation.define<boolean>()

/** Where the cursor is, counted from 1. */
export interface CodeCursor {
  line: number
  column: number
}

export interface CodeEditorProps {
  /** The accessible name of the editor. */
  label: string
  value: string
  /** Called with the whole text after an author's edit. Without it, the editor reports no edits. */
  onChange?: (text: string) => void
  /** The language that colours, folds and indents the text, such as `fileLanguage(path)` of a file. */
  language: FileLanguage
  /** The texts of the find bar. */
  findTexts: FindTexts
  /** Refuses the author's edits. The cursor, find and `goTo` still work. */
  readOnly?: boolean
  /** Fills the window down to the shell footer, and stops its scroll at its end. Without it, the editor is as tall as its text. */
  fill?: boolean
  /**
   * With `fill`, the element whose bottom edge ends the content that follows the editor, such as the resource editor's
   * root. When absent, nothing follows the editor.
   */
  boundary?: RefObject<HTMLElement | null>
  /**
   * As tall as its text, with no scroll or border of its own, so the pane around it scrolls once, see
   * docs/reference/actions-and-controls.md#scroll-areas. An editor an author types in keeps a minimum height. Not with
   * `fill`: when both are set, `grow` wins.
   */
  grow?: boolean
  /**
   * Checks the text and marks each problem at its place. It is read when the editor is built: an editor built without it
   * never checks.
   */
  lint?: (view: EditorView) => Promise<Diagnostic[]>
  /** Called with the number of problems that `lint` marks, each time it changes. */
  onProblems?: (count: number) => void
  /** Called with the cursor's line and column, each time they may have changed. */
  onCursor?: (cursor: CodeCursor) => void
  ref?: Ref<CodeEditorHandle>
}

/** What the owner of a code editor can ask it to do. */
export interface CodeEditorHandle {
  /** Puts the cursor at the start of a line, counted from 1 and held within the text, and focuses the editor. */
  goTo(line: number): void
  /** Selects the next problem, and the first one after the last. */
  nextProblem: () => void
  /** Opens the find bar, or focuses its field when it is open. */
  openFind(): void
  /** Replaces the whole text as an author's edit, so `onChange` reports it. */
  replace(text: string): void
}

/** How long the checks wait after the last keystroke. */
const checkDelay = 250

/**
 * The one code editor of Studio, for a package's files and the JSON view, see
 * docs/reference/shared-widgets.md#which-widget. It folds, matches and closes brackets, indents on input, completes the
 * words of the text and marks the current line.
 */
export default function CodeEditor({
  label,
  value,
  onChange,
  language,
  findTexts,
  readOnly = false,
  fill = false,
  boundary,
  grow = false,
  lint,
  onProblems,
  onCursor,
  ref,
}: CodeEditorProps) {
  const t = useText()
  const { mode } = useThemeMode()
  const tokens = useDesignTokens()
  const hostRef = useRef<HTMLDivElement>(null)
  const viewRef = useRef<EditorView | null>(null)
  const [compartments] = useState(() => ({
    theme: new Compartment(),
    language: new Compartment(),
    readOnly: new Compartment(),
    phrases: new Compartment(),
  }))
  const selectedTint = useSelectedItemTint()
  const theme = editorTheme(tokens, mode, selectedTint, !grow ? 'scroll' : readOnly ? 'grow' : 'growEditable')
  const phrases = useMemo(() => codePhrases(t), [t])
  const latest = useRef({ onChange, value, findTexts, theme, language, readOnly, phrases, lint, onProblems, onCursor })

  useEffect(() => {
    latest.current = { onChange, value, findTexts, theme, language, readOnly, phrases, lint, onProblems, onCursor }
  })

  useEffect(() => {
    const initial = latest.current
    let reported = 0
    const cursorOf = (state: EditorState): CodeCursor => {
      const head = state.selection.main.head
      const line = state.doc.lineAt(head)
      return { line: line.number, column: head - line.from + 1 }
    }
    const view = new EditorView({
      parent: hostRef.current!,
      state: EditorState.create({
        doc: initial.value,
        extensions: [
          lineNumbers(),
          foldGutter(),
          codeFolding(),
          bracketMatching(),
          closeBrackets(),
          indentOnInput(),
          // The words of the text only: the language packages' own suggestions and snippets bring CodeMirror's English.
          autocompletion({ override: [completeAnyWord] }),
          highlightActiveLine(),
          highlightActiveLineGutter(),
          compartments.language.of(languageExtension(initial.language)),
          compartments.readOnly.of(readOnlyExtension(initial.readOnly)),
          compartments.phrases.of(initial.phrases),
          history(),
          search({ top: true, createPanel: findPanel(() => latest.current.findTexts) }),
          // Go to line opens a dialog with CodeMirror's own English text, so the editor leaves it out.
          keymap.of([
            ...closeBracketsKeymap,
            ...defaultKeymap,
            ...historyKeymap,
            ...foldKeymap,
            ...completionKeymap,
            ...searchKeymap.filter((binding) => binding.key !== 'Mod-Alt-g'),
          ]),
          compartments.theme.of(initial.theme),
          EditorView.contentAttributes.of({ 'aria-label': label }),
          initial.lint
            ? [
                lintGutter(),
                linter((editor) => latest.current.lint?.(editor) ?? Promise.resolve([]), { delay: checkDelay }),
              ]
            : [],
          EditorView.updateListener.of((update) => {
            const count = diagnosticCount(update.state)
            if (count !== reported) {
              reported = count
              latest.current.onProblems?.(count)
            }
            if (update.docChanged && !update.transactions.some((transaction) => transaction.annotation(external))) {
              latest.current.onChange?.(update.state.doc.toString())
            }
            if (update.selectionSet || update.docChanged) {
              latest.current.onCursor?.(cursorOf(update.state))
            }
          }),
        ],
      }),
    })
    viewRef.current = view
    initial.onCursor?.(cursorOf(view.state))
    return () => {
      view.destroy()
      viewRef.current = null
      if (reported !== 0) {
        latest.current.onProblems?.(0)
      }
    }
    // The view is built once for its label. The value, language, read-only state and texts reach it through the effects below.
  }, [label, compartments])

  useEffect(() => {
    viewRef.current?.dispatch({ effects: compartments.theme.reconfigure(theme) })
  }, [compartments, theme])

  useEffect(() => {
    viewRef.current?.dispatch({ effects: compartments.language.reconfigure(languageExtension(language)) })
  }, [compartments, language])

  useEffect(() => {
    viewRef.current?.dispatch({ effects: compartments.readOnly.reconfigure(readOnlyExtension(readOnly)) })
  }, [compartments, readOnly])

  useEffect(() => {
    viewRef.current?.dispatch({ effects: compartments.phrases.reconfigure(phrases) })
  }, [compartments, phrases])

  useEffect(() => {
    const view = viewRef.current
    if (view && view.state.doc.toString() !== value) {
      view.dispatch({
        changes: { from: 0, to: view.state.doc.length, insert: value },
        annotations: [external.of(true), Transaction.addToHistory.of(false)],
      })
    }
  }, [value])

  useImperativeHandle(ref, () => ({
    goTo(line) {
      const view = viewRef.current
      if (view) {
        const target = view.state.doc.line(Math.min(Math.max(Math.trunc(line), 1), view.state.doc.lines))
        view.dispatch({ selection: { anchor: target.from }, scrollIntoView: true })
        view.focus()
      }
    },
    nextProblem() {
      const view = viewRef.current
      if (view) {
        nextDiagnostic(view)
        view.focus()
      }
    },
    openFind() {
      if (viewRef.current) {
        openSearchPanel(viewRef.current)
      }
    },
    replace(text) {
      const view = viewRef.current
      if (view && view.state.doc.toString() !== text) {
        view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text }, userEvent: 'input.format' })
      }
    },
  }))

  const height = useFillHeight(hostRef, fill && !grow, boundary)

  return (
    <div
      ref={hostRef}
      style={grow ? { display: 'flex', flexDirection: 'column', flex: '1 0 auto' } : fill ? { height } : undefined}
    />
  )
}

function languageExtension(language: FileLanguage) {
  return languageSupport(language) ?? indentFolding
}

/** Refuses every change that does not come from the value, but keeps the editor focusable so the cursor and find work. */
function readOnlyExtension(readOnly: boolean) {
  return readOnly
    ? [
        EditorState.readOnly.of(true),
        EditorState.changeFilter.of((transaction) => transaction.annotation(external) === true),
        EditorView.contentAttributes.of({ 'aria-readonly': 'true' }),
      ]
    : []
}

/** CodeMirror's own English phrases that can reach the screen, in the texts of Axis. */
function codePhrases(t: (key: string) => string) {
  return EditorState.phrases.of({
    'Fold line': t('shell.studio.code.fold'),
    'Unfold line': t('shell.studio.code.unfold'),
    'folded code': t('shell.studio.code.folded'),
    unfold: t('shell.studio.code.unfold'),
    'Folded lines': t('shell.studio.code.folded'),
    'Unfolded lines': t('shell.studio.code.unfolded'),
    to: t('shell.studio.code.to'),
    Completions: t('shell.studio.code.completions'),
  })
}

/** How the editor's height follows its text: it scrolls in itself, or grows with the text, with a minimum height when editable. */
type EditorHeight = 'scroll' | 'grow' | 'growEditable'

/** The least height of an editable editor that grows, in lines, which leaves room to type a small input. */
const growMinimumLines = 5

const editorTheme = sharedTheme((tokens, mode, selectedTint: string, height: EditorHeight) => {
  const { colour, spacing, radius, type } = tokens
  const grows = height !== 'scroll'
  return [
    // CodeMirror gives the theme listed first precedence, so the growing editor's own rules go before the shared look.
    grows ? EditorView.theme({ '&': { borderWidth: 0, borderRadius: 'unset' } }) : [],
    codeEditorTheme(tokens, mode),
    EditorView.theme({
      '&': grows ? { height: 'auto', flex: '1 0 auto' } : { height: '100%' },
      // A scroll area stops at its end, so the page does not scroll,
      // see docs/reference/actions-and-controls.md#scroll-areas. A growing editor has none, its pane scrolls.
      '.cm-scroller': grows
        ? { overflowX: 'auto', overflowY: 'hidden', overscrollBehavior: 'auto' }
        : { overflow: 'auto', overscrollBehavior: 'contain' },
      ...(height === 'growEditable' ? { '.cm-content, .cm-gutter': { minHeight: `${growMinimumLines}lh` } } : {}),
      '.cm-content': { padding: `${spacing.sm}px 0` },
      '.cm-line': { paddingInline: `${spacing.sm}px` },
      '.cm-gutters': {
        backgroundColor: colour.surface,
        color: colour.textMuted,
        border: 'none',
        borderInlineEnd: `1px solid ${colour.borderSubtle}`,
      },
      '.cm-lineNumbers .cm-gutterElement': { paddingInline: `${spacing.sm}px` },
      '.cm-foldGutter .cm-gutterElement': { color: colour.textMuted },
      // The current line and a matching bracket use existing tokens,
      // see docs/reference/studio-packages.md#a-packages-files.
      '.cm-activeLine, .cm-activeLineGutter': { backgroundColor: colour.fillSubtle },
      '.cm-matchingBracket, &.cm-focused .cm-matchingBracket': {
        backgroundColor: 'transparent',
        outline: `1px solid ${colour.border}`,
      },
      '.cm-panels': { backgroundColor: colour.surface, color: colour.text },
      '.cm-panels-top': { borderBottom: `1px solid ${colour.borderSubtle}` },
      '.cm-findBar': {
        display: 'flex',
        alignItems: 'center',
        gap: `${spacing.xs}px`,
        padding: `${spacing.xs}px ${spacing.sm}px`,
      },
      '.cm-findBar input': {
        backgroundColor: colour.surface,
        color: colour.text,
        border: `1px solid ${colour.border}`,
        borderRadius: `${radius.base}px`,
        fontFamily: type.codeFontFamily,
        fontSize: 'inherit',
        padding: `2px ${spacing.xs}px`,
      },
      '.cm-findBar input:focus': { outline: `2px solid ${colour.focusRing}` },
      '.cm-findCount': { color: colour.textMuted },
      '.cm-searchMatch': { backgroundColor: 'transparent', outline: `1px solid ${colour.border}` },
      '.cm-searchMatch.cm-searchMatch-selected': { backgroundColor: selectedTint },
    }),
  ]
})
