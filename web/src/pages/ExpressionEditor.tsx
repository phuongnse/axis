import { ExpandAltOutlined } from '@ant-design/icons'
import {
  autocompletion,
  startCompletion,
  type Completion,
  type CompletionContext,
  type CompletionResult,
  type CompletionSection,
} from '@codemirror/autocomplete'
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { defaultHighlightStyle, syntaxHighlighting } from '@codemirror/language'
import { setDiagnostics, type Diagnostic } from '@codemirror/lint'
import { Annotation, Compartment, EditorState } from '@codemirror/state'
import { EditorView, keymap } from '@codemirror/view'
import { Flex, Input, Modal, Segmented, Tag, Typography } from 'antd'
import { useEffect, useRef, useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { dialogWidth } from '../platform/dialogButtons'
import { checkStudioExpression, type ExpressionCheck } from '../platform/studio'
import { useText, withWord } from '../platform/texts'
import type { designTokensFor } from '../platform/designTokens'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'
import {
  expressionFunctionGroups,
  expressionOperatorGroups,
  expressionOperators,
  givesType,
  operatorSymbol,
  signatureOf,
  type ExpressionFunction,
  type ExpressionOperator,
} from './expressionFunctions'
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
    expects,
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
      expects,
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
    const complete = (context: CompletionContext) => completeAt(context, latest.current)
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
              // Proposed: a space after a value opens the operators it takes, and a quote after an enum field its
              // values, as an author needs them there and does not know their names yet.
              const head = update.state.selection.main.head
              const before = update.state.sliceDoc(0, head)
              if (
                update.transactions.some((transaction) => transaction.isUserEvent('input.type')) &&
                ((before.endsWith(' ') && operandTypeBefore(before, latest.current.options) !== undefined) ||
                  (before.endsWith("'") && openQuoteAt(before) === head - 1))
              ) {
                queueMicrotask(() => startCompletion(update.view))
              }
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

  /** Puts text at the cursor, in place of the selection, the cursor `back` before its end, and gives back the focus. */
  function insert(text: string, back = 0) {
    const view = viewRef.current
    if (!view) {
      return
    }
    const { from, to } = view.state.selection.main
    // A word goes after what is there with a space between them, as an author would type it.
    const previous = view.state.sliceDoc(Math.max(from - 1, 0), from)
    const spaced = previous !== '' && /[^\s(]/.test(previous) && /^[A-Za-z=!<>+*/-]/.test(text) ? ` ${text}` : text
    view.dispatch({ changes: { from, to, insert: spaced }, selection: { anchor: from + spaced.length - back } })
    view.focus()
  }

  const status = result.error ? (
    <Typography.Text type="danger" data-testid="studio-expression-error">
      {result.error.message} <span style={{ color: colour.textMuted, fontSize: 12 }}>{result.error.code}</span>
    </Typography.Text>
  ) : result.type !== null && !empty ? (
    <Typography.Text style={{ color: colour.textMuted, fontSize: 12 }} data-testid="studio-expression-type">
      {withWord(t('shell.studio.expression.gives'), '{type}', typeWords(result.type, t))}
    </Typography.Text>
  ) : expects ? (
    <Typography.Text style={{ color: colour.textMuted, fontSize: 12 }} data-testid="studio-expression-expects">
      {withWord(t('shell.studio.expression.expects'), '{type}', typeWords(expects, t))}
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

/** A type in the words the entity designer names it by, such as "Yes or no" for `boolean`, else as written. */
function typeWords(type: string, t: (key: string) => string): string {
  const key = `shell.studio.entity.type.${type}`
  const words = t(key)
  return words === key ? type : words
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
 * Proposed: everything an expression may use here, in three tabs: Fields, with their labels and types, and the rules;
 * Operators, the comparisons, empty or not, And, Or and Not, the list test, arithmetic and the values; and Functions, by
 * what they work on. Each one says what it does, with an example. A search looks in every tab, and a click inserts it
 * at the cursor.
 */
function ExpressionPanel({
  options,
  describe,
  onInsert,
}: {
  options: ScopeOption[]
  describe: (option: ScopeOption) => { detail: string; info: string | null }
  onInsert: (text: string, back?: number) => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [tab, setTab] = useState<'fields' | 'operators' | 'functions'>('fields')
  const [search, setSearch] = useState('')
  const needle = search.trim().toLowerCase()
  const item = (option: ScopeOption): PanelItem => {
    const { detail, info } = describe(option)
    return {
      key: option.label,
      code: option.label,
      detail,
      info,
      example: option.fn?.example ?? null,
      insert: () => onInsert(option.label, option.cursorInside ? 1 : 0),
    }
  }
  const operatorItem = (operator: ExpressionOperator): PanelItem => ({
    key: operator.id,
    code: operatorLabel(operator),
    detail: t(`shell.studio.expression.op.${operator.id}`),
    info: null,
    example: operator.example,
    insert: () => onInsert(operator.insert, operator.cursorBack ?? 0),
  })
  const groups: { tab: typeof tab; title: string; items: PanelItem[] }[] = [
    {
      tab: 'fields' as const,
      title: t('shell.studio.expression.group.fields'),
      items: options.filter((option) => option.type === 'field' || option.type === 'parameter').map(item),
    },
    {
      tab: 'fields' as const,
      title: t('shell.studio.expression.group.rules'),
      items: options.filter((option) => option.type === 'rule').map(item),
    },
    ...expressionOperatorGroups.map((group) => ({
      tab: 'operators' as const,
      title: t(`shell.studio.expression.opGroup.${group}`),
      items: expressionOperators.filter((operator) => operator.group === group).map(operatorItem),
    })),
    ...expressionFunctionGroups.map((group) => ({
      tab: 'functions' as const,
      title: t(`shell.studio.expression.group.${group}`),
      items: options.filter((option) => option.fn?.group === group).map(item),
    })),
  ]
  // Proposed: a search looks in every tab and lists what it finds in one list, best first: a name or symbol that starts
  // with the words, then one that holds them, then one whose words hold them. Without a search, the chosen tab shows.
  const shown =
    needle === ''
      ? groups.filter((group) => group.tab === tab && group.items.length > 0)
      : [
          {
            tab,
            title: t('shell.studio.expression.results'),
            items: groups
              .flatMap((group) =>
                group.items.map((entry) => ({
                  entry: { ...entry, group: group.title },
                  score: matchScore(entry, needle),
                })),
              )
              .filter((found) => found.score < 3)
              .sort((a, b) => a.score - b.score)
              .map((found) => found.entry),
          },
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
      {needle === '' && (
        <Segmented
          block
          value={tab}
          onChange={(next) => setTab(next as typeof tab)}
          options={(['fields', 'operators', 'functions'] as const).map((value) => ({
            value,
            label: t(`shell.studio.expression.tab.${value}`),
          }))}
        />
      )}
      <div style={{ maxHeight: 420, overflowY: 'auto' }}>
        {shown.length === 0 && (
          <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.expression.none')}</Typography.Text>
        )}
        {shown.map((group) => (
          <section key={`${group.tab}-${group.title}`} style={{ marginBottom: spacing.sm }}>
            <Typography.Text strong style={{ display: 'block', marginBottom: 4 }}>
              {group.title}
            </Typography.Text>
            {group.items.map((entry) => (
              <AxisButton
                key={entry.key}
                role="plain"
                onClick={entry.insert}
                data-testid={`studio-expression-insert-${entry.key}`}
                style={{
                  display: 'block',
                  width: '100%',
                  height: 'auto',
                  textAlign: 'start',
                  whiteSpace: 'normal',
                  padding: `4px ${spacing.xs}px`,
                }}
              >
                <Flex gap={spacing.xs} align="baseline" wrap>
                  <code>{entry.code}</code>
                  <span style={{ fontSize: 12, color: colour.textMuted }}>{entry.detail}</span>
                  {entry.group && <Tag style={{ marginInlineStart: 'auto' }}>{entry.group}</Tag>}
                </Flex>
                {entry.info && (
                  <span style={{ display: 'block', fontSize: 12, color: colour.textMuted }}>{entry.info}</span>
                )}
                {entry.example && (
                  <code style={{ display: 'block', fontSize: 12, color: colour.textMuted }}>{entry.example}</code>
                )}
              </AxisButton>
            ))}
          </section>
        ))}
      </div>
    </Flex>
  )
}

/** One thing the panel offers: what it reads as, what it is, and what a click inserts. */
interface PanelItem {
  key: string
  /** The group a search result comes from. */
  group?: string
  code: string
  detail: string
  info: string | null
  example: string | null
  insert: () => void
}

/** How well an entry matches a search: -1 its name is it, 0 its name starts with it, 1 its name holds it, 2 its words do, 3 not at all. */
function matchScore(entry: PanelItem, needle: string): number {
  const code = entry.code.toLowerCase()
  if (code === needle || code.replace(/\(\)$/, '') === needle) {
    return -1
  }
  if (code.startsWith(needle)) {
    return 0
  }
  if (code.includes(needle)) {
    return 1
  }
  return `${entry.detail} ${entry.info ?? ''} ${entry.example ?? ''}`.toLowerCase().includes(needle) ? 2 : 3
}

/** How an operator reads in a list: its symbol or words, a text as `'text'` and a date as `date('…')`. */
function operatorLabel(operator: ExpressionOperator): string {
  if (operator.id === 'text') {
    return "'text'"
  }
  if (operator.id === 'dateValue') {
    return "date('yyyy-mm-dd')"
  }
  if (operator.id === 'in') {
    return 'in (…)'
  }
  return operatorSymbol(operator)
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
  if (option.type === 'rule' || !option.detail) {
    return { detail: [label, option.detail ?? null].filter((part): part is string => !!part).join(' · '), info: null }
  }
  // A type after a label follows the line's capitals, as a word Axis defines does in a sentence.
  return { detail: withWord(label ? `${label} · {type}` : '{type}', '{type}', typeWords(option.detail, t)), info: null }
}

/** What completion reads from the editor: the scope of the place, how to describe a choice, the texts and the type. */
interface CompletionScope {
  options: ScopeOption[]
  describe: (option: ScopeOption) => { detail: string; info: string | null }
  t: (key: string) => string
  expects?: string
}

const sections = (t: (key: string) => string) => {
  const section = (name: string, rank: number): CompletionSection => ({ name, rank })
  return {
    fields: section(t('shell.studio.expression.group.fields'), 1),
    values: section(t('shell.studio.expression.opGroup.value'), 2),
    logic: section(t('shell.studio.expression.opGroup.logic'), 3),
    rules: section(t('shell.studio.expression.group.rules'), 4),
    functions: section(t('shell.studio.expression.tab.functions'), 5),
    operators: section(t('shell.studio.expression.tab.operators'), 1),
  }
}

/**
 * Proposed: completion that knows where the cursor stands. After a value it offers the operators that value's type
 * takes, then And and Or; inside quotes after an enum field, that field's values; elsewhere the fields, the values, Not,
 * the rules and the functions, those that give the type the place needs first. It opens on a word, on Ctrl+Space, and by
 * itself after a space that follows a value or a quote that follows an enum field.
 */
function completeAt(context: CompletionContext, scope: CompletionScope): CompletionResult | null {
  const before = context.state.sliceDoc(0, context.pos)
  const quote = openQuoteAt(before)
  if (quote !== -1) {
    const field = enumFieldBefore(before.slice(0, quote), scope.options)
    if (!field?.values?.length) {
      return null
    }
    return {
      from: quote + 1,
      validFor: /^[^']*$/,
      options: field.values.map((value, index) => ({
        label: value,
        boost: -index,
        type: 'enum',
        detail: scope.describe(field).detail,
        apply: (view, _completion, from, to) => {
          const closed = view.state.sliceDoc(to, to + 1) === "'"
          const insert = closed ? value : `${value}'`
          view.dispatch({
            changes: { from, to, insert },
            selection: { anchor: from + insert.length + (closed ? 1 : 0) },
          })
        },
      })),
    }
  }
  const word = context.matchBefore(/[A-Za-z][A-Za-z0-9-]*(:[A-Za-z0-9]*)?/)
  if (!word && !context.explicit) {
    return null
  }
  const from = word?.from ?? context.pos
  const named = sections(scope.t)
  const prefix = context.state.sliceDoc(0, from)
  const operand = operandTypeBefore(prefix, scope.options)
  if (operand !== undefined) {
    // A clause that already compares, such as `total is null`, goes on with And or Or alone.
    const complete = clauseCompares(prefix)
    return {
      from,
      options: expressionOperators
        .filter((operator) => operator.position === 'after')
        .filter((operator) => !complete || operator.group === 'logic')
        .filter((operator) => operand === 'any' || !operator.types || operator.types.includes(operand))
        .map((operator, index) => operatorCompletion(operator, scope.t, named.operators, index)),
    }
  }
  return {
    from,
    options: [
      ...scope.options.map((option) => toCompletion(option, scope, named)),
      ...expressionOperators
        .filter((operator) => operator.position === 'operand')
        .map((operator, index) =>
          operatorCompletion(operator, scope.t, operator.group === 'logic' ? named.logic : named.values, index),
        ),
    ],
  }
}

function operatorCompletion(
  operator: ExpressionOperator,
  t: (key: string) => string,
  section: CompletionSection,
  index: number,
): Completion {
  return {
    label: operatorLabel(operator),
    // Proposed: the list keeps the order of the language's words, the comparisons first, not the alphabet's.
    boost: -index,
    detail: t(`shell.studio.expression.op.${operator.id}`),
    info: `${t('shell.studio.expression.example')} ${operator.example}`,
    type: 'keyword',
    section,
    apply: (view, _completion, from, to) => {
      view.dispatch({
        changes: { from, to, insert: operator.insert },
        selection: { anchor: from + operator.insert.length - (operator.cursorBack ?? 0) },
      })
    },
  }
}

function toCompletion(option: ScopeOption, scope: CompletionScope, named: ReturnType<typeof sections>): Completion {
  const { detail, info } = scope.describe(option)
  const fits =
    scope.expects !== undefined &&
    (option.fn ? givesType(option.fn, scope.expects) : option.valueType === scope.expects)
  return {
    label: option.label,
    detail: option.fn ? undefined : detail,
    type: option.type === 'field' || option.type === 'parameter' ? 'variable' : 'function',
    section: option.fn ? named.functions : option.type === 'rule' ? named.rules : named.fields,
    // Proposed: what gives the type the place needs comes first.
    boost: fits ? 10 : 0,
    // Proposed: a function's completion shows its signature, what it does and an example beside the list.
    info: option.fn
      ? () => {
          const box = document.createElement('div')
          const signature = document.createElement('code')
          signature.textContent = signatureOf(option.fn!)
          const words = document.createElement('div')
          words.textContent = info ?? ''
          const example = document.createElement('code')
          example.textContent = `${scope.t('shell.studio.expression.example')} ${option.fn!.example}`
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
 * The type of the value the text ends with, when it ends with one, so an operator may follow: a field's type, `text`
 * after a closing quote, `decimal` after a number, `boolean` after true or false, and `any` after a bracket or a name it
 * does not know. Undefined when a value is to start: at the start, after an operator, a bracket or a comma, or after
 * And, Or, Not, Is or In.
 */
function operandTypeBefore(text: string, options: ScopeOption[]): string | undefined {
  const trimmed = text.replace(/\s+$/, '')
  if (trimmed === '') {
    return undefined
  }
  if (trimmed.endsWith(')')) {
    return 'any'
  }
  if (trimmed.endsWith("'") && openQuoteAt(trimmed) === -1) {
    return 'text'
  }
  if (/\d$/.test(trimmed) && !/[A-Za-z][A-Za-z0-9]*$/.test(trimmed)) {
    return 'decimal'
  }
  const name = /([A-Za-z][A-Za-z0-9]*)$/.exec(trimmed)?.[1]
  if (name === undefined) {
    return undefined
  }
  const lower = name.toLowerCase()
  if (['and', 'or', 'not', 'is', 'in'].includes(lower)) {
    return undefined
  }
  if (lower === 'true' || lower === 'false') {
    return 'boolean'
  }
  if (lower === 'null') {
    return 'any'
  }
  return options.find((option) => option.label.toLowerCase() === lower)?.valueType ?? 'any'
}

/**
 * Whether the clause the text ends in already compares: after its last And, Or, Not or opening bracket it holds a
 * comparison, `is null` or `in`. Text in quotes does not count.
 */
function clauseCompares(text: string): boolean {
  const plain = text.replace(/'[^']*'/g, "''")
  const clause = plain.split(/\b(?:and|or|not)\b|\(/i).pop() ?? ''
  return /(==|!=|<=|>=|<|>|\bis\b|\bin\b)/i.test(clause)
}

/** Where the quote the text is inside opens, or -1 when the text is not inside quotes. Two quotes are one quote. */
function openQuoteAt(text: string): number {
  let open = -1
  for (let index = 0; index < text.length; index++) {
    if (text[index] === "'") {
      open = open === -1 ? index : -1
    }
  }
  return open
}

/** The enum field a text value is compared with: `field ==`, `field !=` or `field in (` with values before it. */
function enumFieldBefore(text: string, options: ScopeOption[]): ScopeOption | undefined {
  const name =
    /([A-Za-z][A-Za-z0-9]*)\s*(==|!=)\s*$/.exec(text)?.[1] ??
    /([A-Za-z][A-Za-z0-9]*)\s+in\s*\((\s*'[^']*'\s*,)*\s*$/i.exec(text)?.[1]
  return name === undefined ? undefined : options.find((option) => option.label.toLowerCase() === name.toLowerCase())
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
