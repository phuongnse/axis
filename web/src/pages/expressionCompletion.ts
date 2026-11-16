import type { Completion, CompletionContext, CompletionResult } from '@codemirror/autocomplete'
import type { EditorView } from '@codemirror/view'
import type { ExpressionScope, StudioNames } from '../platform/studio'
import { labelText } from '../platform/studioNames'
import { expressionTokens, isOpenString, type ExpressionToken } from './expressionLanguage'
import { expressionOperators, type ExpressionOperator } from './expressionOperators'
import { signatureOf } from './expressionPanelEntries'
import type { ScopeOption } from './expressionScope'

/** Where the cursor stands, as far as the text before it says. */
export type CursorContext =
  | { kind: 'operators'; valueType: string | null; compares: boolean }
  | { kind: 'enum'; values: string[]; from: number }
  | { kind: 'names'; needed: string | null }

/** What completion draws its choices from. */
export interface CompletionSources {
  options: ScopeOption[]
  scope: ExpressionScope | null
  names: StudioNames | null | undefined
  locale: string
  t: (key: string) => string
}

/** The expression types that have words in `shell.studio.expression.type.<type>`. */
const typeWords = new Set([
  'text',
  'integer',
  'decimal',
  'boolean',
  'date',
  'date-time',
  'enum',
  'reference',
  'file',
  'null',
  'list',
])

const comparisonSymbols = new Set(['==', '!=', '<', '<=', '>', '>='])
const equalitySymbols = new Set(['==', '!='])
const arithmeticSymbols = new Set(['+', '-', '*', '/'])

const comparisonIds = ['equal', 'not-equal', 'less', 'less-or-equal', 'greater', 'greater-or-equal']
const nullIds = ['is-null', 'is-not-null']
const arithmeticIds = ['add', 'subtract', 'multiply', 'divide']
const valueIds = ['true', 'false', 'null', 'text', 'date']

/** The type an operator or value gives, for the places that need one. `null` when it gives no type to match. */
const valueIdTypes: Record<string, string | null> = {
  true: 'boolean',
  false: 'boolean',
  null: null,
  text: 'text',
  date: 'date',
  not: 'boolean',
}

function isWord(item: ExpressionToken | undefined, ...words: string[]): boolean {
  return item?.type === 'keyword' && words.includes(item.text.toLowerCase())
}

function isPunctuation(item: ExpressionToken | undefined, text: string): boolean {
  return item?.type === 'punctuation' && item.text === text
}

function isSymbol(item: ExpressionToken | undefined, symbols: Set<string>): boolean {
  return item?.type === 'operator' && symbols.has(item.text)
}

/** Whether the token ends a value: a name, a number, a closed string, `true`, `false`, `null` or the bracket that closes a call. */
function isValue(item: ExpressionToken | undefined): boolean {
  switch (item?.type) {
    case 'variableName':
    case 'number':
      return true
    case 'string':
      return !isOpenString(item)
    case 'keyword':
      return isWord(item, 'true', 'false', 'null')
    case 'punctuation':
      return item.text === ')'
    default:
      return false
  }
}

/** The index of the `(` that matches the `)` at `close`, or -1. */
function openOf(tokens: ExpressionToken[], close: number): number {
  let depth = 0
  for (let index = close; index >= 0; index--) {
    if (isPunctuation(tokens[index], ')')) {
      depth++
    } else if (isPunctuation(tokens[index], '(') && --depth === 0) {
      return index
    }
  }
  return -1
}

/** The index of the last `(` that nothing closes, or -1. */
function unmatchedOpen(tokens: ExpressionToken[]): number {
  let depth = 0
  for (let index = tokens.length - 1; index >= 0; index--) {
    if (isPunctuation(tokens[index], ')')) {
      depth++
    } else if (isPunctuation(tokens[index], '(')) {
      if (depth === 0) {
        return index
      }
      depth--
    }
  }
  return -1
}

/** A name that is not part of a path or a call: the option it names, by label, ignoring case. */
function nameAt(tokens: ExpressionToken[], index: number, options: ScopeOption[]): ScopeOption | undefined {
  const item = tokens[index]
  if (item?.type !== 'variableName' || isPunctuation(tokens[index - 1], '.') || isPunctuation(tokens[index - 1], ':')) {
    return undefined
  }
  const label = item.text.toLowerCase()
  return options.find((option) => option.type !== 'rule' && option.label.toLowerCase() === label)
}

/** The name of the call whose `(` is at `open`, with its module as `<module>:<name>`. */
function calleeAt(tokens: ExpressionToken[], open: number): string | null {
  const name = tokens[open - 1]
  if (name?.type !== 'variableName') {
    return null
  }
  const module = tokens[open - 3]
  return isPunctuation(tokens[open - 2], ':') && module?.type === 'namespace'
    ? `${module.text}:${name.text}`
    : name.text
}

/** The type of the value that ends at the token, or `null` when it is not known. */
function valueTypeAt(
  tokens: ExpressionToken[],
  index: number,
  options: ScopeOption[],
  scope: ExpressionScope | null,
): string | null {
  const item = tokens[index]
  switch (item?.type) {
    case 'number':
      return item.text.includes('.') ? 'decimal' : 'integer'
    case 'string':
      return 'text'
    case 'keyword':
      return isWord(item, 'true', 'false') ? 'boolean' : isWord(item, 'null') ? 'null' : null
    case 'variableName':
      return nameAt(tokens, index, options)?.valueType ?? null
    case 'punctuation': {
      const open = item.text === ')' ? openOf(tokens, index) : -1
      const callee = open > 0 ? calleeAt(tokens, open) : null
      if (callee === null) {
        return null
      }
      const rule = options.find(
        (option) => option.type === 'rule' && option.label.toLowerCase() === `${callee}()`.toLowerCase(),
      )
      if (rule) {
        return rule.valueType ?? null
      }
      const call = scope?.functions.find((item) => item.name.toLowerCase() === callee.toLowerCase())
      if (call) {
        return typeWords.has(call.result) ? call.result : null
      }
      return callee.toLowerCase() === 'date' ? 'date' : null
    }
    default:
      return null
  }
}

/** Whether the clause that ends the tokens already compares, see [operators and precedence](docs/reference/expressions.md#operators-and-precedence). */
function compares(tokens: ExpressionToken[]): boolean {
  let depth = 0
  for (let index = tokens.length - 1; index >= 0; index--) {
    const item = tokens[index]
    if (isPunctuation(item, ')')) {
      depth++
    } else if (isPunctuation(item, '(')) {
      if (depth === 0) {
        return false
      }
      depth--
    } else if (depth === 0) {
      if (
        isWord(item, 'and', 'or') ||
        isPunctuation(item, ',') ||
        (isWord(item, 'not') && !isWord(tokens[index - 1], 'is'))
      ) {
        return false
      }
      if (isSymbol(item, comparisonSymbols) || isWord(item, 'is', 'in')) {
        return true
      }
    }
  }
  return false
}

/** The enum field whose values the open string at the end of the tokens takes, as `<field> ==`, `<field> !=` or `<field> in (`. */
function enumContext(tokens: ExpressionToken[], options: ScopeOption[]): CursorContext | null {
  const last = tokens[tokens.length - 1]
  let index = tokens.length - 2
  let field: number
  if (isSymbol(tokens[index], equalitySymbols)) {
    field = index - 1
  } else {
    while (
      isPunctuation(tokens[index], ',') &&
      tokens[index - 1]?.type === 'string' &&
      !isOpenString(tokens[index - 1])
    ) {
      index -= 2
    }
    if (!isPunctuation(tokens[index], '(') || !isWord(tokens[index - 1], 'in')) {
      return null
    }
    field = index - 2
  }
  const values = nameAt(tokens, field, options)?.values
  return values && values.length > 0 ? { kind: 'enum', values, from: last.from + 1 } : null
}

/** The type the cursor's place needs, when the tokens before it say. */
function neededType(tokens: ExpressionToken[], options: ScopeOption[], scope: ExpressionScope | null): string | null {
  const last = tokens[tokens.length - 1]
  if (last === undefined) {
    return scope?.type ?? null
  }
  if (isWord(last, 'and', 'or', 'not')) {
    return 'boolean'
  }
  if (isSymbol(last, comparisonSymbols) || isSymbol(last, arithmeticSymbols)) {
    return valueTypeAt(tokens, tokens.length - 2, options, scope)
  }
  if (isPunctuation(last, '(') || isPunctuation(last, ',')) {
    const open = unmatchedOpen(tokens)
    if (open >= 2 && isWord(tokens[open - 1], 'in')) {
      return valueTypeAt(tokens, open - 2, options, scope)
    }
  }
  return null
}

/**
 * What fits at the cursor, from the tokens of the text before it, see
 * [the expression editor](docs/reference/studio.md#expression-editor). After a value it is the operators of the
 * value's type. Inside the quotes after an enum field it is the field's values. Elsewhere it is the names, with the
 * type the place needs. `null` inside a string that takes no values. The tokens, not a parse, give it, so half-written
 * text gets the nearest guess.
 */
export function cursorContext(
  tokens: ExpressionToken[],
  options: ScopeOption[],
  scope: ExpressionScope | null,
): CursorContext | null {
  const last = tokens[tokens.length - 1]
  if (last !== undefined && isOpenString(last)) {
    return enumContext(tokens, options)
  }
  if (isValue(last)) {
    return {
      kind: 'operators',
      valueType: valueTypeAt(tokens, tokens.length - 1, options, scope),
      compares: compares(tokens),
    }
  }
  return { kind: 'names', needed: neededType(tokens, options, scope) }
}

/**
 * The ids of the operators a value of the type takes, comparisons first, see
 * [operators and precedence](docs/reference/expressions.md#operators-and-precedence). A clause that already
 * compares takes only `and` and `or`. A value of unknown type takes every operator.
 */
export function operatorIds(valueType: string | null, alreadyCompares: boolean): string[] {
  if (alreadyCompares) {
    return ['and', 'or']
  }
  switch (valueType) {
    case 'integer':
    case 'decimal':
      return [...comparisonIds, ...nullIds, 'in', ...arithmeticIds]
    case 'date':
    case 'date-time':
      return [...comparisonIds, ...nullIds, 'in']
    case 'text':
    case 'enum':
    case 'reference':
      return ['equal', 'not-equal', ...nullIds, 'in']
    case 'boolean':
      return ['equal', 'not-equal', ...nullIds, 'in', 'and', 'or']
    case 'file':
      return nullIds
    case 'list':
      return []
    default:
      return [...comparisonIds, ...nullIds, 'and', 'or', 'in', ...arithmeticIds]
  }
}

/** Whether a name that gives `type` fits a place that needs `needed`. A whole number fits a decimal. */
function meets(type: string | null | undefined, needed: string | null): boolean {
  return needed !== null && type != null && (type === needed || (needed === 'decimal' && type === 'integer'))
}

/** A DOM node of lines, with `code` ones in the code font. It sets text only, because a label is the author's text. */
function infoOf(lines: { text: string | undefined; code?: boolean }[]): Node | null {
  const shown = lines.filter((line) => line.text !== undefined && line.text !== '')
  if (shown.length === 0) {
    return null
  }
  const root = document.createElement('div')
  for (const line of shown) {
    const row = document.createElement('div')
    if (line.code) {
      const code = document.createElement('code')
      code.textContent = line.text!
      row.append(code)
    } else {
      row.textContent = line.text!
    }
    root.append(row)
  }
  return root
}

/** Replaces the range with the text and puts the cursor after it, or one character before the end where a value goes inside brackets or quotes. */
export function insertAt(view: EditorView, from: number, to: number, text: string, cursorInside: boolean) {
  const cursor = from + text.length - (cursorInside ? 1 : 0)
  view.dispatch({ changes: { from, to, insert: text }, selection: { anchor: cursor } })
}

function wordOf(type: string | null | undefined, sources: CompletionSources): string | undefined {
  if (type === null || type === undefined) {
    return undefined
  }
  return typeWords.has(type) ? sources.t(`shell.studio.expression.type.${type}`) : type
}

function givesOf(type: string | null | undefined, sources: CompletionSources): string | undefined {
  const words = wordOf(type, sources)
  return words === undefined ? undefined : sources.t('shell.studio.expression.gives').replace('{type}', words)
}

function nameCompletion(option: ScopeOption, sources: CompletionSources): Completion {
  const words = wordOf(option.valueType, sources)
  const label = sources.names ? labelText(sources.names, option.labelKey, sources.locale) : undefined
  return {
    label: option.label,
    type: 'variable',
    detail: words,
    info: () => infoOf([{ text: label }, { text: words }]),
    apply: (view, completion, from, to) => insertAt(view, from, to, completion.label, option.cursorInside),
  }
}

function ruleCompletion(option: ScopeOption, sources: CompletionSources): Completion {
  const signature = signatureOf(
    option.label.replace(/\(\)$/, ''),
    (option.parameters ?? []).map((parameter) => parameter.name),
  )
  return {
    label: option.label,
    type: 'function',
    detail: signature,
    info: () => infoOf([{ text: signature, code: true }, { text: givesOf(option.valueType, sources) }]),
    apply: (view, completion, from, to) => insertAt(view, from, to, completion.label, option.cursorInside),
  }
}

function functionCompletion(item: ExpressionScope['functions'][number]): Completion {
  const signature = signatureOf(item.name, item.parameters)
  const label = `${item.name}()`
  return {
    label,
    type: 'function',
    detail: signature,
    info: () =>
      infoOf([{ text: signature, code: true }, { text: item.description }, { text: item.example, code: true }]),
    apply: (view, _completion, from, to) => insertAt(view, from, to, label, item.parameters.length > 0),
  }
}

/**
 * An operator or value. An operator goes in without the spaces that the panel puts around it, because the author types
 * the space, and it gets a leading space only when the text before it has none.
 */
function languageCompletion(item: ExpressionOperator, sources: CompletionSources): Completion {
  const words = sources.t(`shell.studio.expression.op.${item.id}`)
  return {
    label: item.name,
    type: valueIds.includes(item.id) ? 'constant' : 'keyword',
    detail: words,
    info: () => infoOf([{ text: words }, { text: item.example, code: true }]),
    apply: (view, _completion, from, to) => {
      const spaced = item.insert.startsWith(' ') && from > 0 && !/\s/.test(view.state.sliceDoc(from - 1, from))
      const text = item.insert.trim()
      insertAt(view, from, to, spaced ? ` ${text}` : text, item.cursorInside)
    },
  }
}

/** One value of an enum, which closes the quote that the author opened. */
function enumCompletion(value: string): Completion {
  return {
    label: value,
    type: 'enum',
    apply: (view, _completion, from, to) => insertAt(view, from, to, `${value.replaceAll("'", "''")}'`, false),
  }
}

function operatorCompletions(context: CursorContext & { kind: 'operators' }, sources: CompletionSources): Completion[] {
  return operatorIds(context.valueType, context.compares).map((id) =>
    languageCompletion(
      expressionOperators.find((item) => item.id === id)!,
      sources,
    ),
  )
}

/**
 * The fields and parameters, the values, `not`, the rules and the functions, each in the order its source gives, with
 * those that give the type the place needs first.
 */
function nameCompletions(needed: string | null, sources: CompletionSources): Completion[] {
  const language = (id: string) => ({
    type: valueIdTypes[id],
    completion: languageCompletion(
      expressionOperators.find((item) => item.id === id)!,
      sources,
    ),
  })
  const candidates = [
    ...sources.options
      .filter((option) => option.type === 'field' || option.type === 'parameter')
      .map((option) => ({ type: option.valueType, completion: nameCompletion(option, sources) })),
    ...valueIds.map(language),
    language('not'),
    ...sources.options
      .filter((option) => option.type === 'rule')
      .map((option) => ({ type: option.valueType, completion: ruleCompletion(option, sources) })),
    ...(sources.scope?.functions ?? []).map((item) => ({
      type: typeWords.has(item.result) ? item.result : null,
      completion: functionCompletion(item),
    })),
  ]
  const fits = candidates.filter((candidate) => meets(candidate.type, needed))
  return [...fits, ...candidates.filter((candidate) => !fits.includes(candidate))].map(
    (candidate) => candidate.completion,
  )
}

/**
 * Completes at the cursor, see [the expression editor](docs/reference/studio.md#expression-editor). With a word
 * typed, the list answers for the text before the word. With none, it opens only when Ctrl+Space asks, after a
 * space that follows a value, or after a quote that follows an enum field.
 */
export function completeAt(context: CompletionContext, sources: CompletionSources): CompletionResult | null {
  const word = context.matchBefore(/[A-Za-z][A-Za-z0-9-]*(:[A-Za-z0-9]*)?/)
  const start = word?.from ?? context.pos
  const before = context.state.sliceDoc(0, start)
  const found = cursorContext(expressionTokens(before), sources.options, sources.scope)
  if (found === null) {
    return null
  }
  if (found.kind === 'enum') {
    return { from: found.from, options: found.values.map(enumCompletion) }
  }
  if (!word && !context.explicit && !(found.kind === 'operators' && /\s$/.test(before))) {
    return null
  }
  const options =
    found.kind === 'operators' ? operatorCompletions(found, sources) : nameCompletions(found.needed, sources)
  return options.length === 0 ? null : { from: start, options }
}
