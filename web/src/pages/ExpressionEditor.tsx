import { ExpandAltOutlined } from '@ant-design/icons'
import {
  autocompletion,
  type Completion,
  type CompletionContext,
  type CompletionResult,
} from '@codemirror/autocomplete'
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { defaultHighlightStyle, syntaxHighlighting } from '@codemirror/language'
import { setDiagnostics, type Diagnostic } from '@codemirror/lint'
import { Annotation, Compartment, EditorState } from '@codemirror/state'
import { EditorView, keymap } from '@codemirror/view'
import { Flex, Input, Modal, Typography } from 'antd'
import { useEffect, useRef, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { dialogWidth } from '../platform/dialogButtons'
import { checkStudioExpression, type ExpressionCheck } from '../platform/studio'
import { useText } from '../platform/texts'
import type { designTokensFor } from '../platform/designTokens'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'
import { expressionFunctionGroups, signatureOf, type ExpressionFunction } from './expressionFunctions'
import { expressionLanguage } from './expressionLanguage'
import type { ScopeOption } from './expressionScope'
import { useTextOfKey } from './useTextOfKey'

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
  /** The names an author can complete at this place. */
  options: ScopeOption[]
  /** Proposed: the type the place needs the expression to give, such as `boolean`, when it says. */
  expects?: string
  /** Proposed: the editor of the Expand dialog, tall, with the Fields and functions panel beside it. */
  expanded?: boolean
  onChange: (path: string, next: string | undefined) => void
}

interface Result {
  type: string | null
  error: { message: string; code: string } | null
}

/** The call the cursor is in: its function and the parameter the cursor is at. */
interface CallAt {
  fn: ExpressionFunction
  argument: number
}

/**
 * A code editor for one expression, the one every place of Studio that takes an expression draws. It checks the text
 * after the author stops typing and shows the newest answer. Proposed, so an author has the guide and the context to
 * write it right:
 * - a line under the box says what the place needs, "Gives yes or no", until the expression gives it, then what it
 *   gives, or what is wrong, in words, with its code after it; an empty expression is not checked;
 * - completion names each choice: a field by its label and type, a function by its signature and what it does, with an
 *   example, and only the functions allowed at this place;
 * - inside a call, a line shows the function's signature with the parameter the cursor is at;
 * - Expand opens the expression in a large dialog, with every field and function in a panel beside it, searchable,
 *   where a click inserts the name at the cursor.
 */
export default function ExpressionEditor(props: ExpressionEditorProps) {
  const { application, resourceId, path, name, value, required, options, expects, expanded = false, onChange } = props
  const t = useText()
  const textOfKey = useTextOfKey()
  const { mode } = useThemeMode()
  const tokens = useDesignTokens()
  const { colour, spacing } = tokens
  const hostRef = useRef<HTMLDivElement>(null)
  const viewRef = useRef<EditorView | null>(null)
  const [themeCompartment] = useState(() => new Compartment())
  const [result, setResult] = useState<Result>({ type: null, error: null })
  const [empty, setEmpty] = useState(value.trim() === '')
  const [call, setCall] = useState<CallAt | null>(null)
  const [expanding, setExpanding] = useState(false)
  const describe = (option: ScopeOption) => completionDetail(option, textOfKey, t)
  const latest = useRef({
    onChange,
    required,
    options,
    value,
    describe,
    t,
    theme: editorTheme(tokens, mode, expanded),
    themeCompartment,
  })

  useEffect(() => {
    latest.current = {
      onChange,
      required,
      options,
      value,
      describe,
      t,
      theme: editorTheme(tokens, mode, expanded),
      themeCompartment,
    }
  })

  useEffect(() => {
    const controller = new AbortController()
    let counter = 0
    let timer: ReturnType<typeof setTimeout> | undefined

    const check = (view: EditorView) => {
      const id = counter
      const text = view.state.doc.toString()
      setEmpty(text.trim() === '')
      // Proposed: an empty expression is not sent, as the server answers it 400; the line says what is needed.
      if (text.trim() === '') {
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
    const complete = (context: CompletionContext) =>
      completeAt(context, latest.current.options, latest.current.describe, latest.current.t)
    const view = new EditorView({
      parent: hostRef.current!,
      state: EditorState.create({
        doc: initial.value,
        extensions: [
          expressionLanguage,
          syntaxHighlighting(defaultHighlightStyle),
          history(),
          keymap.of([...defaultKeymap, ...historyKeymap]),
          autocompletion({ override: [complete] }),
          initial.themeCompartment.of(initial.theme),
          EditorView.lineWrapping,
          EditorView.contentAttributes.of({ 'aria-label': name }),
          EditorView.updateListener.of((update) => {
            if (update.docChanged || update.selectionSet) {
              setCall(callAt(update.state.doc.toString(), update.state.selection.main.head, latest.current.options))
            }
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
    viewRef.current?.dispatch({ effects: themeCompartment.reconfigure(editorTheme(tokens, mode, expanded)) })
  }, [themeCompartment, tokens, mode, expanded])

  useEffect(() => {
    const view = viewRef.current
    if (view && view.state.doc.toString() !== value) {
      view.dispatch({
        changes: { from: 0, to: view.state.doc.length, insert: value },
        annotations: external.of(true),
      })
    }
  }, [value])

  /** Puts a name at the cursor, in place of the selection, and gives the editor back its focus. */
  function insert(option: ScopeOption) {
    const view = viewRef.current
    if (!view) {
      return
    }
    const { from, to } = view.state.selection.main
    const cursor = from + option.label.length - (option.cursorInside ? 1 : 0)
    view.dispatch({ changes: { from, to, insert: option.label }, selection: { anchor: cursor } })
    view.focus()
  }

  const status = result.error ? (
    <Typography.Text type="danger" data-testid="studio-expression-error">
      {result.error.message} <span style={{ color: colour.textMuted, fontSize: 12 }}>{result.error.code}</span>
    </Typography.Text>
  ) : result.type !== null && !empty ? (
    <Typography.Text style={{ color: colour.textMuted, fontSize: 12 }} data-testid="studio-expression-type">
      {t('shell.studio.expression.gives').replace('{type}', typeWords(result.type, t))}
    </Typography.Text>
  ) : expects ? (
    <Typography.Text style={{ color: colour.textMuted, fontSize: 12 }} data-testid="studio-expression-expects">
      {t('shell.studio.expression.expects').replace('{type}', typeWords(expects, t))}
    </Typography.Text>
  ) : null

  const editor = (
    <div>
      <Flex gap={spacing.xs} align="flex-start">
        <div ref={hostRef} style={{ flex: 1, minWidth: 0 }} />
        {!expanded && (
          <AxisButton
            role="icon"
            nameKey="shell.studio.expression.expand"
            icon={<ExpandAltOutlined />}
            onClick={() => setExpanding(true)}
            data-testid="studio-expression-expand"
          />
        )}
      </Flex>
      {call && (
        <div style={{ fontSize: 12, lineHeight: '20px', marginTop: 4 }} data-testid="studio-expression-signature">
          <SignatureLine call={call} />
          <span style={{ color: colour.textMuted }}> · {t(`shell.studio.expression.fn.${call.fn.name}`)}</span>
        </div>
      )}
      {status && <div style={{ marginTop: 4 }}>{status}</div>}
    </div>
  )

  return (
    <>
      {expanded ? (
        <div
          style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 280px', gap: spacing.md, alignItems: 'start' }}
        >
          {editor}
          <ExpressionPanel options={options} describe={describe} onInsert={insert} />
        </div>
      ) : (
        editor
      )}
      {!expanded && (
        <Modal
          open={expanding}
          width={dialogWidth('large')}
          title={name}
          destroyOnHidden
          onCancel={() => setExpanding(false)}
          footer={
            <AxisButton role="finish" onClick={() => setExpanding(false)}>
              {t('shell.studio.list.done')}
            </AxisButton>
          }
        >
          <ExpressionEditor {...props} expanded />
        </Modal>
      )}
    </>
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
    // Proposed: the message reads first, in words; its code follows, small, for a search of the reference.
    setResult({ type: null, error: { message: answer.diagnostics[0].message, code: answer.diagnostics[0].code } })
  }
}

/** A type in the words the entity designer names it by, such as "yes or no" for `boolean`, else as written. */
function typeWords(type: string, t: (key: string) => string): string {
  const key = `shell.studio.entity.type.${type}`
  const words = t(key)
  return words === key ? type : words.toLowerCase()
}

/** A function's signature with the parameter the cursor is at in bold. */
function SignatureLine({ call }: { call: CallAt }) {
  const { fn, argument } = call
  const last = fn.parameters.length - 1
  // A last parameter that repeats takes every argument after it.
  const at = fn.parameters[last]?.endsWith('…') ? Math.min(argument, last) : argument
  return (
    <code>
      {fn.name}(
      {fn.parameters.map((parameter, index) => (
        <span key={index}>
          {index > 0 && ', '}
          {index === at ? <strong>{parameter}</strong> : parameter}
        </span>
      ))}
      ) → {fn.result}
    </code>
  )
}

/**
 * Proposed: every name an expression may use here, in groups: the fields, with their labels and types, the rules, and
 * the functions by what they work on, each with what it does and an example. A search narrows them by name, label or
 * words, and a click inserts the name at the cursor.
 */
function ExpressionPanel({
  options,
  describe,
  onInsert,
}: {
  options: ScopeOption[]
  describe: (option: ScopeOption) => { detail: string; info: string | null }
  onInsert: (option: ScopeOption) => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [search, setSearch] = useState('')
  const needle = search.trim().toLowerCase()
  const shown = options.filter((option) => {
    if (needle === '') {
      return true
    }
    const { detail, info } = describe(option)
    return `${option.label} ${detail} ${info ?? ''}`.toLowerCase().includes(needle)
  })
  const groups: { key: string; title: string; items: ScopeOption[] }[] = [
    {
      key: 'fields',
      title: t('shell.studio.expression.group.fields'),
      items: shown.filter((option) => option.type === 'field' || option.type === 'parameter'),
    },
    {
      key: 'rules',
      title: t('shell.studio.expression.group.rules'),
      items: shown.filter((option) => option.type === 'rule'),
    },
    ...expressionFunctionGroups.map((group) => ({
      key: group,
      title: t(`shell.studio.expression.group.${group}`),
      items: shown.filter((option) => option.fn?.group === group),
    })),
  ].filter((group) => group.items.length > 0)

  return (
    <Flex vertical gap={spacing.sm} data-testid="studio-expression-panel">
      <Input.Search
        allowClear
        aria-label={t('shell.studio.expression.search')}
        placeholder={t('shell.studio.expression.search')}
        value={search}
        onChange={(event) => setSearch(event.target.value)}
      />
      <div style={{ maxHeight: 420, overflowY: 'auto' }}>
        {groups.map((group) => (
          <section key={group.key} style={{ marginBottom: spacing.sm }}>
            <Typography.Text strong style={{ display: 'block', marginBottom: 4 }}>
              {group.title}
            </Typography.Text>
            {group.items.map((option) => {
              const { detail, info } = describe(option)
              return (
                <AxisButton
                  key={option.label}
                  role="plain"
                  onClick={() => onInsert(option)}
                  data-testid={`studio-expression-insert-${option.label}`}
                  style={{
                    display: 'block',
                    width: '100%',
                    height: 'auto',
                    textAlign: 'start',
                    whiteSpace: 'normal',
                    padding: `4px ${spacing.xs}px`,
                  }}
                >
                  <code>{option.label}</code>
                  <span style={{ display: 'block', fontSize: 12, color: colour.textMuted }}>{detail}</span>
                  {info && <span style={{ display: 'block', fontSize: 12, color: colour.textMuted }}>{info}</span>}
                  {option.fn && (
                    <code style={{ display: 'block', fontSize: 12, color: colour.textMuted }}>{option.fn.example}</code>
                  )}
                </AxisButton>
              )
            })}
          </section>
        ))}
      </div>
    </Flex>
  )
}

/**
 * What completion and the panel say of a choice: a field's label and type, a rule's signature and label, a function's
 * signature, and for a function what it does.
 */
function completionDetail(
  option: ScopeOption,
  textOfKey: (key: string | null | undefined) => string | null,
  t: (key: string) => string,
): { detail: string; info: string | null } {
  if (option.fn) {
    return { detail: signatureOf(option.fn), info: t(`shell.studio.expression.fn.${option.fn.name}`) }
  }
  const label = textOfKey(option.labelKey)
  const type = option.type === 'rule' ? (option.detail ?? null) : option.detail ? typeWords(option.detail, t) : null
  return {
    detail: [label, type].filter((part): part is string => part !== null && part !== '').join(' · '),
    info: null,
  }
}

/** Completes the word before the cursor, or opens the list at the cursor when Ctrl+Space asks for it. */
function completeAt(
  context: CompletionContext,
  options: ScopeOption[],
  describe: (option: ScopeOption) => { detail: string; info: string | null },
  t: (key: string) => string,
): CompletionResult | null {
  const word = context.matchBefore(/[A-Za-z][A-Za-z0-9-]*(:[A-Za-z0-9]*)?/)
  if (!word && !context.explicit) {
    return null
  }
  return { from: word?.from ?? context.pos, options: options.map((option) => toCompletion(option, describe, t)) }
}

function toCompletion(
  option: ScopeOption,
  describe: (option: ScopeOption) => { detail: string; info: string | null },
  t: (key: string) => string,
): Completion {
  const { detail, info } = describe(option)
  return {
    label: option.label,
    detail: option.fn ? undefined : detail,
    type: option.type === 'field' || option.type === 'parameter' ? 'variable' : 'function',
    // Proposed: a function's completion shows its signature, what it does and an example beside the list.
    info: option.fn
      ? () => {
          const box = document.createElement('div')
          const signature = document.createElement('code')
          signature.textContent = signatureOf(option.fn!)
          const words = document.createElement('div')
          words.textContent = info ?? ''
          const example = document.createElement('code')
          example.textContent = `${t('shell.studio.expression.example')} ${option.fn!.example}`
          box.append(signature, words, example)
          box.style.display = 'grid'
          box.style.gap = '4px'
          box.style.maxWidth = '320px'
          return box
        }
      : undefined,
    apply: (view, completion, from, to) => {
      const cursor = from + completion.label.length - (option.cursorInside ? 1 : 0)
      view.dispatch({ changes: { from, to, insert: completion.label }, selection: { anchor: cursor } })
    },
  }
}

/**
 * The innermost call of a known function around `position`, and the argument the cursor is at, read from the text
 * before it: brackets open and close calls, a comma moves to the next argument, and text in single quotes is skipped.
 */
function callAt(text: string, position: number, options: ScopeOption[]): CallAt | null {
  const stack: { name: string; argument: number }[] = []
  let quoted = false
  for (let index = 0; index < position && index < text.length; index++) {
    const char = text[index]
    if (quoted) {
      quoted = char !== "'"
      continue
    }
    if (char === "'") {
      quoted = true
    } else if (char === '(') {
      stack.push({ name: /([A-Za-z][A-Za-z0-9]*)\s*$/.exec(text.slice(0, index))?.[1] ?? '', argument: 0 })
    } else if (char === ')') {
      stack.pop()
    } else if (char === ',' && stack.length > 0) {
      stack[stack.length - 1].argument++
    }
  }
  for (let index = stack.length - 1; index >= 0; index--) {
    const fn = options.find((option) => option.fn?.name.toLowerCase() === stack[index].name.toLowerCase())?.fn
    if (fn) {
      return { fn, argument: stack[index].argument }
    }
  }
  return null
}

function editorTheme(
  { colour, radius, type }: ReturnType<typeof designTokensFor>,
  mode: 'light' | 'dark',
  tall: boolean,
) {
  return EditorView.theme(
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
      '.cm-content': { caretColor: colour.text, padding: '4px 0', minHeight: tall ? '240px' : 'auto' },
      '.cm-scroller': { fontFamily: 'inherit' },
    },
    { dark: mode === 'dark' },
  )
}
