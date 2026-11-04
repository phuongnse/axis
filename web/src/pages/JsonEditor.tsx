import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { json, jsonParseLinter } from '@codemirror/lang-json'
import { HighlightStyle, syntaxHighlighting, syntaxTree } from '@codemirror/language'
import { linter, lintGutter, type Diagnostic } from '@codemirror/lint'
import { Annotation, Compartment, EditorState } from '@codemirror/state'
import { EditorView, keymap, lineNumbers } from '@codemirror/view'
import type { SyntaxNode } from '@lezer/common'
import { tags } from '@lezer/highlight'
import Ajv2020, { type ErrorObject, type ValidateFunction } from 'ajv/dist/2020'
import { useContext, useEffect, useLayoutEffect, useMemo, useRef } from 'react'
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
 */
export function JsonEditor({
  value,
  schema,
  label,
  onChange,
}: {
  value: string
  schema: JsonSchema | null
  label: string
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
          keymap.of([...defaultKeymap, ...historyKeymap]),
          linter(schemaLinter, { delay: 300 }),
          lintGutter(),
          themeCompartment.of(editorTheme(tokens, mode)),
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
    // The editor is made once; its value, theme and schema follow below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    viewRef.current?.dispatch({ effects: themeCompartment.reconfigure(editorTheme(tokens, mode)) })
  }, [themeCompartment, tokens, mode])

  // Proposed: the view reaches down to spacing-md above the footer, whatever the window's height, as a fixed share of the
  // window left a band under it that grew with the screen. It never goes under its minimum, and the page scrolls then.
  useLayoutEffect(() => {
    const host = hostRef.current!
    function fit() {
      if (host.offsetParent === null) {
        return
      }
      const top = host.getBoundingClientRect().top + window.scrollY
      const footer = document.querySelector('footer')?.offsetHeight ?? 0
      host.style.setProperty(
        '--json-view-height',
        `${Math.max(minimumHeight, window.innerHeight - top - footer - bottomGap)}px`,
      )
    }
    fit()
    const observer = new ResizeObserver(fit)
    observer.observe(host)
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

  return <div ref={hostRef} data-testid="studio-json-editor" />
}

function editorTheme({ colour, radius, type }: ReturnType<typeof designTokensFor>, mode: 'light' | 'dark') {
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
        '.cm-content': { caretColor: colour.text },
        '.cm-scroller': { fontFamily: 'inherit', overflow: 'auto' },
        '.cm-gutters': {
          backgroundColor: colour.surface,
          color: colour.textMuted,
          borderRight: `1px solid ${colour.borderSubtle}`,
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
