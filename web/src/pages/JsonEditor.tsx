import {
  AlignLeftOutlined,
  CloseOutlined,
  CopyOutlined,
  DownOutlined,
  SearchOutlined,
  UpOutlined,
} from '@ant-design/icons'
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { json, jsonParseLinter } from '@codemirror/lang-json'
import { HighlightStyle, syntaxHighlighting, syntaxTree } from '@codemirror/language'
import { diagnosticCount, linter, lintGutter, nextDiagnostic, type Diagnostic } from '@codemirror/lint'
import {
  closeSearchPanel,
  findNext,
  findPrevious,
  openSearchPanel,
  search,
  searchKeymap,
  SearchQuery,
  setSearchQuery,
} from '@codemirror/search'
import { Annotation, Compartment, EditorState } from '@codemirror/state'
import { EditorView, keymap, lineNumbers } from '@codemirror/view'
import type { SyntaxNode } from '@lezer/common'
import { tags } from '@lezer/highlight'
import Ajv2020, { type ErrorObject, type ValidateFunction } from 'ajv/dist/2020'
import { Button, Flex, Input, message as antMessage, Typography, type InputRef } from 'antd'
import { useContext, useEffect, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { selectedItemTint } from '../platform/antTheme'
import { AxisButton } from '../platform/AxisButton'
import type { designTokensFor } from '../platform/designTokens'
import { ShellSidebarContext } from '../platform/shellSidebar'
import type { JsonSchema } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import { useDesignTokens, useThemeMode } from '../platform/themeMode'

const external = Annotation.define<boolean>()

/** The least height of the view, in pixels. */
const minimumHeight = 240

/** The schema's validator, made once a schema. Axis's own `x-axis-*` keywords are annotations, so strict mode is off. */
const validators = new WeakMap<object, ValidateFunction>()
function validatorFor(schema: JsonSchema): ValidateFunction | null {
  const known = validators.get(schema)
  if (known) {
    return known
  }
  try {
    const validate = new Ajv2020({ strict: false, allErrors: true }).compile(schema as object)
    validators.set(schema, validate)
    return validate
  } catch (error) {
    console.warn('Compiling the schema for the JSON view failed', error)
    return null
  }
}

/** The value node of a JSON document, or of a property, as Lezer's JSON grammar has it. */
function valueOf(node: SyntaxNode | null): SyntaxNode | null {
  if (!node) {
    return null
  }
  if (node.name === 'JsonText') {
    return node.firstChild
  }
  if (node.name === 'Property') {
    return node.lastChild
  }
  return node
}

/** The span of the value at a JSON Pointer, or the nearest one above it that the text holds. */
function spanAt(state: EditorState, pointer: string): { from: number; to: number; key?: SyntaxNode } {
  let node = valueOf(syntaxTree(state).topNode)
  let key: SyntaxNode | undefined
  for (const raw of pointer.split('/').slice(1)) {
    const segment = raw.replace(/~1/g, '/').replace(/~0/g, '~')
    if (!node) {
      break
    }
    let next: SyntaxNode | null = null
    if (node.name === 'Object') {
      for (let child = node.firstChild; child; child = child.nextSibling) {
        const name = child.name === 'Property' ? child.firstChild : null
        if (name && state.sliceDoc(name.from + 1, name.to - 1) === segment) {
          next = valueOf(child)
          key = name
          break
        }
      }
    } else if (node.name === 'Array') {
      let index = 0
      for (let child = node.firstChild; child; child = child.nextSibling) {
        if (['[', ']', ','].includes(child.name)) {
          continue
        }
        if (index++ === Number(segment)) {
          next = child
          break
        }
      }
    }
    if (!next) {
      break
    }
    node = next
  }
  return node ? { from: node.from, to: node.to, key } : { from: 0, to: 0 }
}

/** Whether the text parses as JSON. */
function isJson(text: string): boolean {
  try {
    JSON.parse(text)
    return true
  } catch {
    return false
  }
}

/** How many matches the text has, and which of them is selected, counted from 1, or 0 when none is. */
function matchesOf(state: EditorState, text: string): { index: number; count: number } {
  if (text === '') {
    return { index: 0, count: 0 }
  }
  const { from, to } = state.selection.main
  const cursor = new SearchQuery({ search: text }).getCursor(state)
  let index = 0
  let count = 0
  for (let match = cursor.next(); !match.done; match = cursor.next()) {
    count += 1
    if (match.value.from === from && match.value.to === to) {
      index = count
    }
  }
  return { index, count }
}

/**
 * Proposed: the find bar at the top of the JSON view. The browser's own find cannot see the lines a code editor has not
 * drawn, so a long resource needs this one. It finds as one types: Enter moves to the next match, Shift+Enter to the one
 * before, and Escape closes it and goes back to the text.
 */
function JsonFindBar({ view }: { view: EditorView }) {
  const t = useText()
  const { colour, spacing, type } = useDesignTokens()
  const input = useRef<InputRef>(null)
  // Each letter searches again from where the bar opened, so typing never walks past the first match.
  const start = useRef(view.state.selection.main.from)
  const [text, setText] = useState('')
  const { index, count } = matchesOf(view.state, text)

  useEffect(() => {
    input.current?.focus({ cursor: 'all' })
  }, [])

  function change(next: string) {
    setText(next)
    view.dispatch({
      effects: setSearchQuery.of(new SearchQuery({ search: next })),
      selection: { anchor: start.current },
    })
    if (next !== '') {
      findNext(view)
    }
  }

  function close() {
    closeSearchPanel(view)
    view.focus()
  }

  function press(event: KeyboardEvent) {
    if (event.key === 'Enter') {
      event.preventDefault()
      if (event.shiftKey) {
        findPrevious(view)
      } else {
        findNext(view)
      }
    } else if (event.key === 'Escape') {
      event.preventDefault()
      close()
    }
  }

  const label = t('shell.studio.json.find')
  const none = text !== '' && count === 0
  return (
    <Flex
      data-testid="studio-json-find"
      align="center"
      gap={spacing.xs}
      style={{ padding: `${spacing.xs}px ${spacing.sm}px`, fontFamily: type.fontFamily }}
    >
      <Input
        ref={input}
        size="small"
        aria-label={label}
        placeholder={label}
        value={text}
        status={none ? 'error' : undefined}
        onChange={(event) => change(event.target.value)}
        onKeyDown={press}
        style={{ width: 240 }}
        {...{ 'main-field': 'true' }}
      />
      <Typography.Text role="status" style={{ color: colour.textMuted, minWidth: 72 }}>
        {none
          ? t('shell.studio.json.findNone')
          : text === ''
            ? ''
            : t('shell.studio.json.findCount').replace('{index}', String(index)).replace('{count}', String(count))}
      </Typography.Text>
      <AxisButton
        role="icon"
        nameKey="shell.studio.json.findPrevious"
        icon={<UpOutlined />}
        disabled={count === 0}
        onClick={() => findPrevious(view)}
      />
      <AxisButton
        role="icon"
        nameKey="shell.studio.json.findNext"
        icon={<DownOutlined />}
        disabled={count === 0}
        onClick={() => findNext(view)}
      />
      <AxisButton role="icon" nameKey="shell.studio.json.findClose" icon={<CloseOutlined />} onClick={close} />
    </Flex>
  )
}

/** A schema error in words a person reads, never Ajv's own. */
function message(error: ErrorObject, t: (key: string) => string): string {
  const params = error.params as Record<string, unknown>
  switch (error.keyword) {
    case 'required':
      return t('shell.studio.json.required').replace('{name}', String(params.missingProperty))
    case 'additionalProperties':
      return t('shell.studio.json.unknown').replace('{name}', String(params.additionalProperty))
    case 'type':
      return t('shell.studio.json.type').replace('{type}', String(params.type))
    case 'enum':
      return t('shell.studio.json.enum').replace('{values}', (params.allowedValues as unknown[]).join(', '))
    case 'const':
      return t('shell.studio.json.const').replace('{value}', JSON.stringify(params.allowedValue))
    default:
      return t('shell.studio.json.invalid')
  }
}

/**
 * Proposed: the JSON view of a resource, a code editor rather than a text box: JSON highlighting, line numbers, and two
 * checks as one types, each marked at its line in the gutter and underlined where it is: a syntax error where the text
 * stops being JSON, and every place the content breaks the kind's schema, said in words ("Add version.", "colour is
 * not a field of this resource."). The text goes to `onChange` on every change; the editor keeps the draft whole.
 * `actionsSlot` is the element at the end of the tab bar that its actions draw in: how many problems there are, which
 * moves to the next one, Find, Format, which is off while the text is not JSON, and Copy.
 */
export function JsonEditor({
  value,
  schema,
  label,
  actionsSlot,
  onChange,
}: {
  value: string
  schema: JsonSchema | null
  label: string
  actionsSlot: HTMLElement | null
  onChange: (text: string) => void
}) {
  const t = useText()
  const tokens = useDesignTokens()
  const { mode } = useThemeMode()
  const bottomGap = useContext(ShellSidebarContext)?.contentPadding ?? tokens.spacing.lg
  const hostRef = useRef<HTMLDivElement>(null)
  const viewRef = useRef<EditorView | null>(null)
  const latest = useRef({ onChange, schema, t })
  const themeCompartment = useMemo(() => new Compartment(), [])
  const [messageApi, messageHolder] = antMessage.useMessage()
  const [problems, setProblems] = useState(0)
  const [broken, setBroken] = useState(() => !isJson(value))
  const [findHost, setFindHost] = useState<HTMLElement | null>(null)
  // The find bar's count reads the editor's state, which React does not hold.
  const [, setRevision] = useState(0)
  useEffect(() => {
    latest.current = { onChange, schema, t }
  })

  useEffect(() => {
    const parse = jsonParseLinter()
    const schemaLinter = (view: EditorView): Diagnostic[] => {
      const syntax = parse(view)
      if (syntax.length > 0) {
        return syntax.map((diagnostic) => ({ ...diagnostic, message: latest.current.t('shell.studio.json.syntax') }))
      }
      const { schema: current, t: text } = latest.current
      const validate = current ? validatorFor(current) : null
      if (!validate) {
        return []
      }
      validate(JSON.parse(view.state.doc.toString()))
      return (validate.errors ?? [])
        .filter((error) => !['if', 'anyOf', 'oneOf', 'allOf'].includes(error.keyword))
        .map((error) => {
          const span = spanAt(view.state, error.instancePath)
          // An unknown property is marked at its own name, which the parent's path does not reach.
          if (error.keyword === 'additionalProperties') {
            const extra = spanAt(
              view.state,
              `${error.instancePath}/${String((error.params as Record<string, unknown>).additionalProperty)}`,
            )
            return {
              from: extra.key?.from ?? span.from,
              to: extra.key?.to ?? span.to,
              severity: 'error' as const,
              message: message(error, text),
            }
          }
          return { from: span.from, to: span.to, severity: 'error' as const, message: message(error, text) }
        })
    }
    const view = new EditorView({
      parent: hostRef.current!,
      state: EditorState.create({
        doc: value,
        extensions: [
          json(),
          lineNumbers(),
          history(),
          search({
            top: true,
            createPanel: () => {
              const dom = document.createElement('div')
              return { dom, top: true, mount: () => setFindHost(dom), destroy: () => setFindHost(null) }
            },
          }),
          keymap.of([...defaultKeymap, ...historyKeymap, ...searchKeymap]),
          linter(schemaLinter, { delay: 300 }),
          lintGutter(),
          themeCompartment.of(editorTheme(tokens, mode)),
          EditorView.contentAttributes.of({ 'aria-label': label }),
          EditorView.updateListener.of((update) => {
            setProblems(diagnosticCount(update.state))
            if (update.docChanged) {
              setBroken(!isJson(update.state.doc.toString()))
            }
            if (update.docChanged || update.selectionSet) {
              setRevision((revision) => revision + 1)
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
    }
    // The editor is made once; its value, theme and schema follow below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    viewRef.current?.dispatch({ effects: themeCompartment.reconfigure(editorTheme(tokens, mode)) })
  }, [themeCompartment, tokens, mode])

  // Proposed: the view reaches down to spacing-md above the footer or the save bar, whatever the window's height, as a
  // fixed share of the window left a band under it that grew with the screen. It never goes under its minimum, and the page scrolls then.
  useLayoutEffect(() => {
    const host = hostRef.current!
    function fit() {
      if (host.offsetParent === null) {
        return
      }
      const top = host.getBoundingClientRect().top + window.scrollY
      // The save bar sits over the footer while it shows, so it is what the view stops above.
      const below = document.querySelector<HTMLElement>('[data-save-bar]') ?? document.querySelector('footer')
      host.style.setProperty(
        '--json-view-height',
        `${Math.max(minimumHeight, window.innerHeight - top - (below?.offsetHeight ?? 0) - bottomGap)}px`,
      )
    }
    fit()
    const observer = new ResizeObserver(fit)
    observer.observe(host)
    // The page area changes height when the save bar comes and goes.
    const page = host.closest('main')
    if (page) {
      observer.observe(page)
    }
    window.addEventListener('resize', fit)
    return () => {
      observer.disconnect()
      window.removeEventListener('resize', fit)
    }
  }, [bottomGap])

  useEffect(() => {
    const view = viewRef.current
    if (view && view.state.doc.toString() !== value) {
      view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value }, annotations: external.of(true) })
    }
  }, [value])

  async function copy() {
    try {
      await navigator.clipboard.writeText(viewRef.current!.state.doc.toString())
      void messageApi.success(t('shell.studio.json.copied'))
    } catch (error) {
      console.warn('Copying the JSON failed', error)
      void messageApi.error(t('shell.studio.json.copyFailed'))
    }
  }

  function format() {
    const view = viewRef.current!
    const text = view.state.doc.toString()
    const formatted = JSON.stringify(JSON.parse(text), null, 2)
    if (formatted !== text) {
      view.dispatch({ changes: { from: 0, to: text.length, insert: formatted } })
    }
  }

  function nextProblem() {
    const view = viewRef.current!
    view.focus()
    nextDiagnostic(view)
  }

  const actions = (
    <Flex align="center" gap={tokens.spacing.xs}>
      {problems > 0 && (
        <Button
          type="text"
          data-testid="studio-json-problems"
          style={{ color: tokens.colour.dangerText }}
          onClick={nextProblem}
        >
          {problems === 1
            ? t('shell.studio.json.problem')
            : t('shell.studio.json.problems').replace('{count}', String(problems))}
        </Button>
      )}
      <AxisButton
        role="icon"
        nameKey="shell.studio.json.find"
        icon={<SearchOutlined />}
        aria-keyshortcuts="Control+F Meta+F"
        onClick={() => openSearchPanel(viewRef.current!)}
      />
      <AxisButton
        role="icon"
        nameKey="shell.studio.json.format"
        icon={<AlignLeftOutlined />}
        disabled={broken}
        onClick={format}
      />
      <AxisButton role="icon" nameKey="shell.studio.json.copy" icon={<CopyOutlined />} onClick={() => void copy()} />
    </Flex>
  )

  return (
    <>
      {messageHolder}
      <div ref={hostRef} data-testid="studio-json-editor" />
      {actionsSlot && createPortal(actions, actionsSlot)}
      {findHost && viewRef.current && createPortal(<JsonFindBar view={viewRef.current} />, findHost)}
    </>
  )
}

function editorTheme({ colour, radius, spacing, type }: ReturnType<typeof designTokensFor>, mode: 'light' | 'dark') {
  return [
    EditorView.theme(
      {
        '&': {
          height: `var(--json-view-height, ${minimumHeight}px)`,
          color: colour.text,
          backgroundColor: colour.surface,
          border: `1px solid ${colour.border}`,
          borderRadius: `${radius.base}px`,
          // Proposed: the gutter's square background stays inside the rounded border, which it cut at the corners.
          overflow: 'hidden',
          fontFamily: type.codeFontFamily,
          fontSize: `${type.fontSize}px`,
        },
        '&.cm-focused': { outline: `2px solid ${colour.focusRing}` },
        // Proposed: the text keeps spacing-sm from the border on every side. CodeMirror moves the gutter down with the content.
        '.cm-content': { caretColor: colour.text, paddingBlock: `${spacing.sm}px` },
        '.cm-line': { paddingInline: `${spacing.sm}px` },
        '.cm-lineNumbers .cm-gutterElement': { paddingInline: `${spacing.sm}px ${spacing.xs}px` },
        '.cm-scroller': { fontFamily: 'inherit', overflow: 'auto' },
        '.cm-gutters': {
          backgroundColor: colour.surface,
          color: colour.textMuted,
          borderRight: `1px solid ${colour.borderSubtle}`,
        },
        '.cm-panels': { backgroundColor: colour.surface, color: colour.text },
        '.cm-panels.cm-panels-top': { borderBottom: `1px solid ${colour.borderSubtle}` },
        // Proposed: every match has a thin outline and the one in focus the selected tint, from the tokens, not CodeMirror's yellow.
        '.cm-searchMatch': { backgroundColor: 'transparent', outline: `1px solid ${colour.border}` },
        '.cm-searchMatch.cm-searchMatch-selected, .cm-searchMatch-selected span': {
          backgroundColor: selectedItemTint(mode),
          outline: `1px solid ${colour.primaryText}`,
        },
      },
      { dark: mode === 'dark' },
    ),
    // Proposed: JSON draws in the Axis tokens, not CodeMirror's own red. A value's text stays plain, because it is most of
    // what a person reads; the names carry the brand colour, and numbers and literals their own.
    syntaxHighlighting(
      HighlightStyle.define([
        { tag: tags.propertyName, color: colour.primaryText },
        { tag: tags.string, color: colour.text },
        { tag: tags.number, color: colour.infoText },
        { tag: [tags.bool, tags.null], color: colour.warningText },
        { tag: [tags.separator, tags.brace, tags.squareBracket, tags.punctuation], color: colour.textMuted },
      ]),
    ),
  ]
}
