import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { Annotation, Compartment, EditorState, Transaction } from '@codemirror/state'
import { EditorView, keymap, lineNumbers } from '@codemirror/view'
import { useEffect, useLayoutEffect, useRef, useState, type RefObject } from 'react'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'
import { codeEditorTheme, sharedTheme } from './codeEditorTheme'
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
}

/** A code editor for a resource's JSON, with line numbers, that fills the window down to the shell footer. */
export default function JsonView({ label, value, onChange, boundary }: JsonViewProps) {
  const { mode } = useThemeMode()
  const tokens = useDesignTokens()
  const hostRef = useRef<HTMLDivElement>(null)
  const viewRef = useRef<EditorView | null>(null)
  const [themeCompartment] = useState(() => new Compartment())
  const [height, setHeight] = useState(minHeight)
  const latest = useRef({ onChange, value, theme: jsonTheme(tokens, mode), themeCompartment })

  useEffect(() => {
    latest.current = { onChange, value, theme: jsonTheme(tokens, mode), themeCompartment }
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
          keymap.of([...defaultKeymap, ...historyKeymap]),
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
  const { colour, spacing } = tokens
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
    }),
  ]
})
