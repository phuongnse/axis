import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { defaultHighlightStyle, syntaxHighlighting } from '@codemirror/language'
import { setDiagnostics, type Diagnostic } from '@codemirror/lint'
import { Annotation, Compartment, EditorState } from '@codemirror/state'
import { EditorView, keymap } from '@codemirror/view'
import { theme, Typography } from 'antd'
import { useEffect, useRef, useState } from 'react'
import { checkStudioExpression, type ExpressionCheck } from '../platform/studio'
import { useText } from '../platform/texts'
import { useThemeMode } from '../platform/themeMode'
import { expressionLanguage } from './expressionLanguage'

/** How long the author must stop typing before the expression is checked. */
const checkDelay = 300

/** Marks a change that came from the value, not from the author, so it is not reported as an edit. */
const external = Annotation.define<boolean>()

export interface ExpressionEditorProps {
  application: string
  resourceId: string
  /** The JSON Pointer of the expression in the resource. */
  path: string
  name: string
  value: string
  required: boolean
  onChange: (path: string, next: string | undefined) => void
}

interface Result {
  type: string | null
  error: string | null
}

/** A code editor for one expression. It checks the text after the author stops typing and shows the newest answer. */
export default function ExpressionEditor({
  application,
  resourceId,
  path,
  name,
  value,
  required,
  onChange,
}: ExpressionEditorProps) {
  const t = useText()
  const { mode } = useThemeMode()
  const { token } = theme.useToken()
  const hostRef = useRef<HTMLDivElement>(null)
  const viewRef = useRef<EditorView | null>(null)
  const [themeCompartment] = useState(() => new Compartment())
  const [result, setResult] = useState<Result>({ type: null, error: null })
  const latest = useRef({ onChange, required, value, theme: editorTheme(token, mode), themeCompartment })

  useEffect(() => {
    latest.current = { onChange, required, value, theme: editorTheme(token, mode), themeCompartment }
  })

  useEffect(() => {
    const controller = new AbortController()
    let counter = 0
    let timer: ReturnType<typeof setTimeout> | undefined

    const check = (view: EditorView) => {
      const id = counter
      const text = view.state.doc.toString()
      if (text === '' && !latest.current.required) {
        show(view, { type: null, diagnostics: [] })
        return
      }
      checkStudioExpression(application, resourceId, path, text, controller.signal).then(
        (answer) => {
          if (answer !== null && id === counter && !controller.signal.aborted) {
            show(view, answer)
          }
        },
        () => {},
      )
    }

    const initial = latest.current
    const view = new EditorView({
      parent: hostRef.current!,
      state: EditorState.create({
        doc: initial.value,
        extensions: [
          expressionLanguage,
          syntaxHighlighting(defaultHighlightStyle),
          history(),
          keymap.of([...defaultKeymap, ...historyKeymap]),
          initial.themeCompartment.of(initial.theme),
          EditorView.contentAttributes.of({ 'aria-label': name }),
          EditorView.updateListener.of((update) => {
            if (!update.docChanged) {
              return
            }
            counter++
            clearTimeout(timer)
            if (!update.transactions.some((transaction) => transaction.annotation(external))) {
              const text = update.state.doc.toString()
              latest.current.onChange(path, text === '' && !latest.current.required ? undefined : text)
            }
            timer = setTimeout(() => check(update.view), checkDelay)
          }),
        ],
      }),
    })
    viewRef.current = view
    check(view)

    return () => {
      clearTimeout(timer)
      controller.abort()
      view.destroy()
      viewRef.current = null
    }
  }, [application, resourceId, path, name])

  useEffect(() => {
    viewRef.current?.dispatch({ effects: themeCompartment.reconfigure(editorTheme(token, mode)) })
  }, [themeCompartment, token, mode])

  useEffect(() => {
    const view = viewRef.current
    if (view && view.state.doc.toString() !== value) {
      view.dispatch({
        changes: { from: 0, to: view.state.doc.length, insert: value },
        annotations: external.of(true),
      })
    }
  }, [value])

  return (
    <div>
      <div ref={hostRef} />
      {result.error !== null && (
        <div>
          <Typography.Text type="danger" data-testid="studio-expression-error">
            {result.error}
          </Typography.Text>
        </div>
      )}
      {result.type !== null && (
        <div>
          <Typography.Text data-testid="studio-expression-type">
            {t('shell.studio.expressionType')} <code>{result.type}</code>
          </Typography.Text>
        </div>
      )}
    </div>
  )

  function show(view: EditorView, answer: ExpressionCheck) {
    const length = view.state.doc.length
    if (answer.diagnostics.length === 0) {
      view.dispatch(setDiagnostics(view.state, []))
      setResult({ type: answer.type, error: null })
      return
    }
    const diagnostics: Diagnostic[] = answer.diagnostics.map((item) => {
      let from = Math.min(Math.max(item.start, 0), length)
      let to = Math.min(Math.max(item.end, 0), length)
      if (from === to && length > 0) {
        if (to < length) {
          to++
        } else {
          from--
        }
      }
      return { from, to, severity: 'error', message: `${item.code}: ${item.message}` }
    })
    view.dispatch(setDiagnostics(view.state, diagnostics))
    setResult({ type: null, error: diagnostics[0].message })
  }
}

function editorTheme(token: ReturnType<typeof theme.useToken>['token'], mode: 'light' | 'dark') {
  return EditorView.theme(
    {
      '&': {
        color: token.colorText,
        backgroundColor: token.colorBgContainer,
        border: `1px solid ${token.colorBorder}`,
        borderRadius: `${token.borderRadius}px`,
        fontFamily: token.fontFamilyCode,
        fontSize: `${token.fontSize}px`,
      },
      '&.cm-focused': { outline: 'none', borderColor: token.colorPrimary },
      '.cm-content': { caretColor: token.colorText, padding: '4px 0' },
      '.cm-scroller': { fontFamily: 'inherit' },
    },
    { dark: mode === 'dark' },
  )
}
