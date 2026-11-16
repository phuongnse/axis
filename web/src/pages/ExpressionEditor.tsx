import { autocompletion, type CompletionContext } from '@codemirror/autocomplete'
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { setDiagnostics, type Diagnostic } from '@codemirror/lint'
import { Annotation, Compartment, EditorState } from '@codemirror/state'
import { EditorView, keymap } from '@codemirror/view'
import { ExpandAltOutlined } from '@ant-design/icons'
import { Flex, Modal, Typography } from 'antd'
import { useContext, useEffect, useRef, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import {
  checkStudioExpression,
  fetchStudioExpressionScope,
  type ExpressionCheck,
  type ExpressionScope,
  type StudioNames,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'
import { codeEditorTheme, sharedTheme } from './codeEditorTheme'
import { PlatformContext } from './context'
import { completeAt, insertAt } from './expressionCompletion'
import { expressionLanguage } from './expressionLanguage'
import { ExpressionPanel } from './ExpressionPanel'
import type { ScopeOption } from './expressionScope'

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
  /** The resource as the author has it. The scope of the place is loaded for it once. */
  draft: Record<string, unknown>
  /** The names an author can complete at this place, apart from the functions, which the expression scope lists. */
  options: ScopeOption[]
  /** The application's names, for the labels of the fields in the expanded panel. */
  names?: StudioNames | null
  /** Set for the editor inside the expanded dialog, which has no Expand button of its own. */
  inDialog?: boolean
  /** Called with the editor's view when it is made and with `null` when it is destroyed, so the panel of the dialog can insert at its cursor. */
  onView?: (view: EditorView | null) => void
  onChange: (path: string, next: string | undefined) => void
}

interface Result {
  type: string | null
  error: { code: string; message: string } | null
}

/** A code editor for one expression. It checks the text after the author stops typing and shows the newest answer. */
export default function ExpressionEditor({
  application,
  resourceId,
  path,
  name,
  value,
  required,
  draft,
  options,
  names,
  inDialog = false,
  onView,
  onChange,
}: ExpressionEditorProps) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { mode } = useThemeMode()
  const tokens = useDesignTokens()
  const hostRef = useRef<HTMLDivElement>(null)
  const viewRef = useRef<EditorView | null>(null)
  const dialogView = useRef<EditorView | null>(null)
  const [expanded, setExpanded] = useState(false)
  const [themeCompartment] = useState(() => new Compartment())
  const [result, setResult] = useState<Result>({ type: null, error: null })
  const [checking, setChecking] = useState(true)
  const placeKey = JSON.stringify([application, resourceId, path])
  const [loaded, setLoaded] = useState<{ placeKey: string; scope: ExpressionScope | null } | null>(null)
  // An answer for another place is not this place's scope.
  const scope = loaded?.placeKey === placeKey ? loaded.scope : null
  const latest = useRef({
    onChange,
    onView,
    required,
    options,
    scope,
    names,
    locale,
    t,
    draft,
    value,
    theme: expressionTheme(tokens, mode),
    themeCompartment,
  })

  useEffect(() => {
    latest.current = {
      onChange,
      onView,
      required,
      options,
      scope,
      names,
      locale,
      t,
      draft,
      value,
      theme: expressionTheme(tokens, mode),
      themeCompartment,
    }
  })

  // The scope is loaded once per place, with the draft as it stands then, so a keystroke in the draft loads no more.
  useEffect(() => {
    const controller = new AbortController()
    fetchStudioExpressionScope(application, resourceId, path, latest.current.draft, controller.signal).then(
      (answer) => {
        if (!controller.signal.aborted) {
          setLoaded({ placeKey, scope: answer })
        }
      },
      () => {},
    )
    return () => controller.abort()
  }, [application, resourceId, path, placeKey])

  useEffect(() => {
    const controller = new AbortController()
    let counter = 0
    let timer: ReturnType<typeof setTimeout> | undefined

    const check = (view: EditorView) => {
      const id = counter
      const text = view.state.doc.toString()
      if (text === '') {
        show(view, { type: null, diagnostics: [] })
        setChecking(false)
        return
      }
      checkStudioExpression(application, resourceId, path, text, controller.signal).then(
        (answer) => {
          if (id !== counter || controller.signal.aborted) {
            return
          }
          if (answer !== null) {
            show(view, answer)
          }
          setChecking(false)
        },
        () => {
          if (id === counter && !controller.signal.aborted) {
            setChecking(false)
          }
        },
      )
    }

    const initial = latest.current
    const complete = (context: CompletionContext) => completeAt(context, latest.current)
    const view = new EditorView({
      parent: hostRef.current!,
      state: EditorState.create({
        doc: initial.value,
        extensions: [
          expressionLanguage,
          history(),
          keymap.of([...defaultKeymap, ...historyKeymap]),
          autocompletion({ override: [complete], compareCompletions: () => 0 }),
          EditorView.lineWrapping,
          initial.themeCompartment.of(initial.theme),
          EditorView.contentAttributes.of({ 'aria-label': name }),
          EditorView.updateListener.of((update) => {
            if (!update.docChanged) {
              return
            }
            counter++
            setChecking(true)
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
    latest.current.onView?.(view)
    check(view)

    return () => {
      clearTimeout(timer)
      controller.abort()
      setChecking(true)
      view.destroy()
      viewRef.current = null
      latest.current.onView?.(null)
    }
  }, [application, resourceId, path, name])

  useEffect(() => {
    viewRef.current?.dispatch({ effects: themeCompartment.reconfigure(expressionTheme(tokens, mode)) })
  }, [themeCompartment, tokens, mode])

  useEffect(() => {
    const view = viewRef.current
    if (view && view.state.doc.toString() !== value) {
      view.dispatch({
        changes: { from: 0, to: view.state.doc.length, insert: value },
        annotations: external.of(true),
      })
    }
  }, [value])

  const words = (type: string) => t(`shell.studio.expression.type.${type}`)
  // What the check gave, or until it answers, what the place needs. A place that takes any type shows no line.
  const line =
    result.type !== null
      ? t('shell.studio.expression.gives').replace('{type}', words(result.type))
      : scope?.type
        ? t('shell.studio.expression.needs').replace('{type}', words(scope.type))
        : null

  return (
    <div aria-busy={checking}>
      <Flex gap={tokens.spacing.xs} align="flex-start">
        <div ref={hostRef} style={{ flex: 1, minWidth: 0 }} />
        {!inDialog && (
          <AxisButton
            role="icon"
            nameKey="shell.studio.expression.expand"
            icon={<ExpandAltOutlined />}
            onClick={() => setExpanded(true)}
          />
        )}
      </Flex>
      {result.error !== null ? (
        <div data-testid="studio-expression-error">
          <Typography.Text type="danger">{result.error.message}</Typography.Text>{' '}
          <Typography.Text type="secondary">
            <small>{result.error.code}</small>
          </Typography.Text>
        </div>
      ) : (
        line !== null && (
          <div>
            <Typography.Text data-testid="studio-expression-type">{line}</Typography.Text>
          </div>
        )
      )}
      {expanded && (
        <Modal
          open
          width={tokens.size.dialog.large}
          title={name}
          footer={null}
          onCancel={() => setExpanded(false)}
          destroyOnHidden
        >
          <Flex vertical gap={tokens.spacing.md}>
            <ExpressionEditor
              application={application}
              resourceId={resourceId}
              path={path}
              name={name}
              value={value}
              required={required}
              draft={draft}
              options={options}
              names={names}
              inDialog
              onView={(view) => (dialogView.current = view)}
              onChange={onChange}
            />
            <ExpressionPanel
              scope={scope}
              rules={options.filter((option) => option.type === 'rule')}
              names={names}
              onPick={(text, cursorInside) => {
                const view = dialogView.current
                if (view) {
                  const { from, to } = view.state.selection.main
                  insertAt(view, from, to, text, cursorInside)
                  view.focus()
                }
              }}
            />
          </Flex>
        </Modal>
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
      return { from, to, severity: 'error', message: item.message }
    })
    view.dispatch(setDiagnostics(view.state, diagnostics))
    const first = answer.diagnostics[0]
    setResult({ type: null, error: { code: first.code, message: first.message } })
  }
}

const expressionTheme = sharedTheme((tokens, mode) => [
  codeEditorTheme(tokens, mode),
  EditorView.theme({
    '.cm-content': { padding: '4px 0' },
    '.cm-completionInfo code': { fontFamily: tokens.type.codeFontFamily },
  }),
])
