import { defaultKeymap, history, historyKeymap, redo, redoDepth, undo, undoDepth } from '@codemirror/commands'
import { diagnosticCount, nextDiagnostic } from '@codemirror/lint'
import { openSearchPanel, search, searchKeymap } from '@codemirror/search'
import { Annotation, Compartment, EditorState, Transaction, type Extension } from '@codemirror/state'
import { EditorView, keymap, lineNumbers } from '@codemirror/view'
import { useEffect, useImperativeHandle, useLayoutEffect, useRef, useState, type Ref, type RefObject } from 'react'
import { selectedItemTint } from '../platform/antTheme'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'
import { codeEditorTheme, sharedTheme } from './codeEditorTheme'
import { findPanel, type FindTexts } from './jsonFindPanel'
import { jsonViewHeight, minHeight } from './jsonViewHeight'

/** Marks a change that came from the value, not from the author, so it is not reported as an edit. */
const external = Annotation.define<boolean>()

export interface CodeEditorProps {
  /** The accessible name of the editor. */
  label: string
  value: string
  onChange: (text: string) => void
  /** The highlighting of the text's language, from `codeLanguage`. */
  language: Extension
  /** The checks that mark problems in the text, such as a linter. Keep the same value while they do not change. */
  checks?: Extension
  /** The text can be read, selected and searched, but not changed. */
  readOnly?: boolean
  /**
   * The element whose bottom edge ends the content that follows the editor, such as the resource editor's root. When
   * absent, nothing follows the editor.
   */
  boundary?: RefObject<HTMLElement | null>
  /** The texts of the find bar. */
  findTexts: FindTexts
  /** Called with the number of problems that the checks mark, each time it changes. */
  onProblems?: (count: number) => void
  /** Called with the number of steps that can be undone and redone, each time either changes. */
  onHistory?: (steps: { undo: number; redo: number }) => void
  ref?: Ref<CodeEditorHandle>
}

/** What the owner of a code editor can ask it to do. */
export interface CodeEditorHandle {
  /** Selects the next problem, and the first one after the last. */
  nextProblem: () => void
  /** Opens the find bar, or focuses its field when it is open. */
  openFind(): void
  /** Replaces the whole text as an author's edit, so `onChange` reports it. */
  replace(text: string): void
  /** Undoes the editor's last step. */
  undo(): void
  /** Redoes the step that was last undone. */
  redo(): void
}

/**
 * The code editor of Studio, with line numbers and the find bar, that fills the window down to the shell footer. The
 * JSON view and a package's files draw through it, see docs/reference/studio-packages.md#a-packages-files.
 */
export default function CodeEditor({
  label,
  value,
  onChange,
  language,
  checks,
  readOnly = false,
  boundary,
  findTexts,
  onProblems,
  onHistory,
  ref,
}: CodeEditorProps) {
  const { mode } = useThemeMode()
  const tokens = useDesignTokens()
  const hostRef = useRef<HTMLDivElement>(null)
  const viewRef = useRef<EditorView | null>(null)
  const [themeCompartment] = useState(() => new Compartment())
  const [languageCompartment] = useState(() => new Compartment())
  const [readOnlyCompartment] = useState(() => new Compartment())
  const [checksCompartment] = useState(() => new Compartment())
  const [height, setHeight] = useState(minHeight)
  const latest = useRef({
    onChange,
    value,
    findTexts,
    theme: codeViewTheme(tokens, mode),
    language,
    checks,
    readOnly,
    onProblems,
    onHistory,
  })

  useEffect(() => {
    latest.current = {
      onChange,
      value,
      findTexts,
      theme: codeViewTheme(tokens, mode),
      language,
      checks,
      readOnly,
      onProblems,
      onHistory,
    }
  })

  useEffect(() => {
    const initial = latest.current
    let reported = 0
    let steps = { undo: 0, redo: 0 }
    const view = new EditorView({
      parent: hostRef.current!,
      state: EditorState.create({
        doc: initial.value,
        extensions: [
          lineNumbers(),
          languageCompartment.of(initial.language),
          history(),
          search({ top: true, createPanel: findPanel(() => latest.current.findTexts) }),
          // Go to line opens a dialog with CodeMirror's own English text, so the editor leaves it out.
          keymap.of([
            ...defaultKeymap,
            ...historyKeymap,
            ...searchKeymap.filter((binding) => binding.key !== 'Mod-Alt-g'),
          ]),
          themeCompartment.of(initial.theme),
          readOnlyCompartment.of(EditorState.readOnly.of(initial.readOnly)),
          EditorView.contentAttributes.of({ 'aria-label': label }),
          checksCompartment.of(initial.checks ?? []),
          EditorView.updateListener.of((update) => {
            const count = diagnosticCount(update.state)
            if (count !== reported) {
              reported = count
              latest.current.onProblems?.(count)
            }
            const next = { undo: undoDepth(update.state), redo: redoDepth(update.state) }
            if (next.undo !== steps.undo || next.redo !== steps.redo) {
              steps = next
              latest.current.onHistory?.(next)
            }
            if (update.docChanged && !update.transactions.some((transaction) => transaction.annotation(external))) {
              latest.current.onChange(update.state.doc.toString())
            }
          }),
        ],
      }),
    })
    viewRef.current = view
    return () => {
      view.destroy()
      viewRef.current = null
      if (reported !== 0) {
        latest.current.onProblems?.(0)
      }
    }
    // The editor is built once for its label. The value, the language, the checks and read-only reach it through the effects below.
  }, [label, languageCompartment, readOnlyCompartment, checksCompartment, themeCompartment])

  useEffect(() => {
    viewRef.current?.dispatch({ effects: themeCompartment.reconfigure(codeViewTheme(tokens, mode)) })
  }, [themeCompartment, tokens, mode])

  useEffect(() => {
    viewRef.current?.dispatch({ effects: languageCompartment.reconfigure(language) })
  }, [languageCompartment, language])

  useEffect(() => {
    viewRef.current?.dispatch({ effects: checksCompartment.reconfigure(checks ?? []) })
  }, [checksCompartment, checks])

  useEffect(() => {
    viewRef.current?.dispatch({ effects: readOnlyCompartment.reconfigure(EditorState.readOnly.of(readOnly)) })
  }, [readOnlyCompartment, readOnly])

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
    undo() {
      if (viewRef.current) {
        undo(viewRef.current)
      }
    },
    redo() {
      if (viewRef.current) {
        redo(viewRef.current)
      }
    },
  }))

  const gap = tokens.spacing.md
  const fit = () => {
    const host = hostRef.current
    if (!host || host.offsetParent === null) {
      return
    }
    const rect = host.getBoundingClientRect()
    const content = host.closest('main')
    const next = jsonViewHeight({
      windowHeight: window.innerHeight,
      top: rect.top + window.scrollY,
      footerHeight: document.querySelector('[role=contentinfo]')?.getBoundingClientRect().height ?? 0,
      below:
        (boundary?.current ? Math.max(boundary.current.getBoundingClientRect().bottom - rect.bottom, 0) : 0) +
        (content ? parseFloat(getComputedStyle(content).paddingBottom) || 0 : 0),
      gap,
    })
    setHeight((current) => (current === next ? current : next))
  }
  const fitRef = useRef(fit)

  useLayoutEffect(() => {
    fitRef.current = fit
    fit()
  })

  useEffect(() => {
    const onResize = () => fitRef.current()
    window.addEventListener('resize', onResize)
    return () => window.removeEventListener('resize', onResize)
  }, [])

  return <div ref={hostRef} style={{ height }} />
}

const codeViewTheme = sharedTheme((tokens, mode) => {
  const { colour, spacing, radius, type } = tokens
  return [
    codeEditorTheme(tokens, mode),
    EditorView.theme({
      '&': { height: '100%' },
      // A scroll area stops at its end, so the page does not scroll,
      // see docs/reference/actions-and-controls.md#scroll-areas.
      '.cm-scroller': { overflow: 'auto', overscrollBehavior: 'contain' },
      '.cm-content': { padding: `${spacing.sm}px 0` },
      '.cm-line': { paddingInline: `${spacing.sm}px` },
      '.cm-gutters': {
        backgroundColor: colour.surface,
        color: colour.textMuted,
        border: 'none',
        borderInlineEnd: `1px solid ${colour.borderSubtle}`,
      },
      '.cm-lineNumbers .cm-gutterElement': { paddingInline: `${spacing.sm}px` },
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
      '.cm-searchMatch.cm-searchMatch-selected': { backgroundColor: selectedItemTint(mode) },
    }),
  ]
})
