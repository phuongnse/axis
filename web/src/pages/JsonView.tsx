import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { openSearchPanel, search, searchKeymap } from '@codemirror/search'
import { Annotation, Compartment, EditorState, Transaction } from '@codemirror/state'
import { EditorView, keymap, lineNumbers } from '@codemirror/view'
import { useEffect, useImperativeHandle, useLayoutEffect, useRef, useState, type Ref, type RefObject } from 'react'
import { selectedItemTint } from '../platform/antTheme'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'
import { codeEditorTheme, sharedTheme } from './codeEditorTheme'
import { findPanel, type FindTexts } from './jsonFindPanel'
import { jsonLanguage } from './jsonLanguage'
import { jsonViewHeight, minHeight } from './jsonViewHeight'

/** Marks a change that came from the value, not from the author, so it is not reported as an edit. */
const external = Annotation.define<boolean>()

export interface JsonViewProps {
  /** The accessible name of the editor. */
  label: string
  value: string
  onChange: (text: string) => void
  /**
   * The element whose bottom edge ends the content that follows the view, such as the resource editor's root. When
   * absent, nothing follows the view.
   */
  boundary?: RefObject<HTMLElement | null>
  /** The texts of the find bar. */
  findTexts: FindTexts
  ref?: Ref<JsonViewHandle>
}

/** What the owner of a JSON view can ask it to do. */
export interface JsonViewHandle {
  /** Opens the find bar, or focuses its field when it is open. */
  openFind(): void
  /** Replaces the whole text as an author's edit, so `onChange` reports it. */
  replace(text: string): void
}

/** A code editor for a resource's JSON, with line numbers, that fills the window down to the shell footer. */
export default function JsonView({ label, value, onChange, boundary, findTexts, ref }: JsonViewProps) {
  const { mode } = useThemeMode()
  const tokens = useDesignTokens()
  const hostRef = useRef<HTMLDivElement>(null)
  const viewRef = useRef<EditorView | null>(null)
  const [themeCompartment] = useState(() => new Compartment())
  const [height, setHeight] = useState(minHeight)
  const latest = useRef({ onChange, value, findTexts, theme: jsonTheme(tokens, mode), themeCompartment })

  useEffect(() => {
    latest.current = { onChange, value, findTexts, theme: jsonTheme(tokens, mode), themeCompartment }
  })

  useEffect(() => {
    const initial = latest.current
    const view = new EditorView({
      parent: hostRef.current!,
      state: EditorState.create({
        doc: initial.value,
        extensions: [
          lineNumbers(),
          jsonLanguage,
          history(),
          search({ top: true, createPanel: findPanel(() => latest.current.findTexts) }),
          // Go to line opens a dialog with CodeMirror's own English text, so the view leaves it out.
          keymap.of([
            ...defaultKeymap,
            ...historyKeymap,
            ...searchKeymap.filter((binding) => binding.key !== 'Mod-Alt-g'),
          ]),
          initial.themeCompartment.of(initial.theme),
          EditorView.contentAttributes.of({ 'aria-label': label }),
          EditorView.updateListener.of((update) => {
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
    }
    // The view is built once for its label. The value reaches it through the effect below.
  }, [label])

  useEffect(() => {
    viewRef.current?.dispatch({ effects: themeCompartment.reconfigure(jsonTheme(tokens, mode)) })
  }, [themeCompartment, tokens, mode])

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

const jsonTheme = sharedTheme((tokens, mode) => {
  const { colour, spacing, radius, type } = tokens
  return [
    codeEditorTheme(tokens, mode),
    EditorView.theme({
      '&': { height: '100%' },
      '.cm-scroller': { overflow: 'auto' },
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
