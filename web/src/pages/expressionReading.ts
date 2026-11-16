import type { StudioNames } from '../platform/studio'
import { labelText } from '../platform/studioNames'
import { expressionTokens, type ExpressionToken } from './expressionLanguage'
import type { ScopeOption } from './expressionScope'

// Proposed: the line that reads an expression back in words, with the labels of the names it uses (#3265, step 1).
// The prototype parses in the browser; the build has the server's check answer the reading, so the client keeps no
// second grammar, see the rule comment on #3265.

/** One piece of a phrase: a name the author sees by its label, a value, or the words between them. */
export interface ReadingPart {
  kind: 'name' | 'value' | 'words'
  text: string
}

/**
 * What an expression reads as: one phrase, or a group of conditions that must all hold (`all`), of which one must
 * hold (`any`), that must not all hold (`notAll`, `not (a and b)`) or of which none may hold (`none`, `not (a or b)`).
 * A group of `all` or `any` whose conditions are all phrases reads on one line; any other group reads as a list.
 */
export type Reading =
  { kind: 'phrase'; parts: ReadingPart[] } | { kind: 'all' | 'any' | 'notAll' | 'none'; items: Reading[] }

type Node =
  | { t: 'or' | 'and'; items: Node[] }
  | { t: 'not'; item: Node }
  | { t: 'compare'; op: string; left: Node; right: Node }
  | { t: 'null'; item: Node; negated: boolean }
  | { t: 'in'; item: Node; values: Node[] }
  | { t: 'arith'; op: string; left: Node; right: Node }
  | { t: 'negate'; item: Node }
  | { t: 'path'; names: string[] }
  | { t: 'call'; name: string; args: Node[] }
  | { t: 'literal'; kind: 'number' | 'text' | 'true' | 'false' | 'null' | 'date'; text: string }
  | { t: 'group'; item: Node }

class Parser {
  private index = 0
  private readonly tokens: ExpressionToken[]
  constructor(tokens: ExpressionToken[]) {
    this.tokens = tokens
  }

  parse(): Node {
    const node = this.or()
    if (this.index < this.tokens.length) {
      throw new Error('unexpected token')
    }
    return node
  }

  private peek(): ExpressionToken | undefined {
    return this.tokens[this.index]
  }

  private word(): string | undefined {
    const token = this.peek()
    return token?.type === 'keyword' ? token.text.toLowerCase() : token?.text
  }

  private take(text?: string): ExpressionToken {
    const token = this.peek()
    if (token === undefined || (text !== undefined && this.word() !== text)) {
      throw new Error('unexpected end')
    }
    this.index++
    return token
  }

  private or(): Node {
    const items = [this.and()]
    while (this.word() === 'or') {
      this.take()
      items.push(this.and())
    }
    return items.length === 1 ? items[0] : { t: 'or', items }
  }

  private and(): Node {
    const items = [this.not()]
    while (this.word() === 'and') {
      this.take()
      items.push(this.not())
    }
    return items.length === 1 ? items[0] : { t: 'and', items }
  }

  private not(): Node {
    if (this.word() === 'not') {
      this.take()
      return { t: 'not', item: this.not() }
    }
    return this.comparison()
  }

  private comparison(): Node {
    const left = this.additive()
    const word = this.word()
    if (word !== undefined && ['==', '!=', '<', '<=', '>', '>='].includes(word)) {
      this.take()
      return { t: 'compare', op: word, left, right: this.additive() }
    }
    if (word === 'is') {
      this.take()
      const negated = this.word() === 'not'
      if (negated) {
        this.take()
      }
      this.take('null')
      return { t: 'null', item: left, negated }
    }
    if (word === 'in') {
      this.take()
      this.take('(')
      const values = [this.additive()]
      while (this.word() === ',') {
        this.take()
        values.push(this.additive())
      }
      this.take(')')
      return { t: 'in', item: left, values }
    }
    return left
  }

  private additive(): Node {
    let left = this.multiplicative()
    while (this.word() === '+' || this.word() === '-') {
      const op = this.take().text
      left = { t: 'arith', op, left, right: this.multiplicative() }
    }
    return left
  }

  private multiplicative(): Node {
    let left = this.unary()
    while (this.word() === '*' || this.word() === '/') {
      const op = this.take().text
      left = { t: 'arith', op, left, right: this.unary() }
    }
    return left
  }

  private unary(): Node {
    if (this.word() === '-') {
      this.take()
      return { t: 'negate', item: this.unary() }
    }
    return this.primary()
  }

  private primary(): Node {
    const token = this.take()
    const word = token.type === 'keyword' ? token.text.toLowerCase() : token.text
    if (token.type === 'number') {
      return { t: 'literal', kind: 'number', text: token.text }
    }
    if (token.type === 'string') {
      if (!token.text.endsWith("'") || token.text.length < 2) {
        throw new Error('open text')
      }
      return { t: 'literal', kind: 'text', text: token.text.slice(1, -1).replaceAll("''", "'") }
    }
    if (word === 'true' || word === 'false' || word === 'null') {
      return { t: 'literal', kind: word, text: word }
    }
    if (word === '(') {
      const item = this.or()
      this.take(')')
      return { t: 'group', item }
    }
    if (token.type === 'namespace') {
      this.take(':')
      return this.call(`${token.text}:${this.take().text}`)
    }
    if (token.type === 'variableName') {
      if (this.word() === '(') {
        return this.call(token.text)
      }
      const names = [token.text]
      while (this.word() === '.') {
        this.take()
        names.push(this.take().text)
      }
      return { t: 'path', names }
    }
    throw new Error('unexpected token')
  }

  private call(name: string): Node {
    this.take('(')
    const args: Node[] = []
    if (this.word() !== ')') {
      args.push(this.or())
      while (this.word() === ',') {
        this.take()
        args.push(this.or())
      }
    }
    this.take(')')
    if ((name === 'date' || name === 'dateTime') && args.length === 1 && args[0].t === 'literal') {
      return { t: 'literal', kind: 'date', text: args[0].text }
    }
    return { t: 'call', name, args }
  }
}

/** What reading needs: the names in scope, the application's names and texts, the author's language and words. */
export interface ReadingSources {
  options: ScopeOption[]
  names: StudioNames | null | undefined
  locale: string
  t: (key: string) => string
}

/** The functions whose reading has words of their own, `shell.studio.expression.reads.fn.<name>`; any other reads as a call. */
const functionsWithWords = new Set([
  'today',
  'now',
  'currentUser',
  'length',
  'lower',
  'upper',
  'year',
  'month',
  'day',
  'daysBetween',
  'addDays',
  'contains',
  'startsWith',
  'endsWith',
  'concat',
  'round',
  'coalesce',
  'if',
])

/**
 * Reads an expression back in words, with the label of each field and the label of each enum value, or `null` when
 * the text is empty or does not parse yet. A name not in scope reads as written, as the check reports it.
 */
export function readExpression(text: string, sources: ReadingSources): Reading | null {
  if (text.trim() === '') {
    return null
  }
  let node: Node
  try {
    node = new Parser(expressionTokens(text)).parse()
  } catch {
    return null
  }
  return new Reader(sources).reading(node)
}

class Reader {
  private readonly sources: ReadingSources
  constructor(sources: ReadingSources) {
    this.sources = sources
  }

  reading(node: Node): Reading {
    const bare = node.t === 'group' ? node.item : node
    // `not` before a group reads as a heading of its own over the group's conditions.
    const negated = bare.t === 'not' ? (bare.item.t === 'group' ? bare.item.item : bare.item) : null
    if (negated !== null && (negated.t === 'and' || negated.t === 'or')) {
      return { kind: negated.t === 'and' ? 'notAll' : 'none', items: negated.items.map((item) => this.reading(item)) }
    }
    if (bare.t === 'and' || bare.t === 'or') {
      return { kind: bare.t === 'and' ? 'all' : 'any', items: bare.items.map((item) => this.reading(item)) }
    }
    return { kind: 'phrase', parts: this.condition(bare) }
  }

  private condition(node: Node): ReadingPart[] {
    switch (node.t) {
      case 'compare': {
        const field = this.fieldOf(node.left) ?? this.fieldOf(node.right)
        const ops: Record<string, string> = {
          '==': 'equal',
          '!=': 'not-equal',
          '<': 'less',
          '<=': 'less-or-equal',
          '>': 'greater',
          '>=': 'greater-or-equal',
        }
        // An order between dates reads as before and after.
        const dates =
          ['less', 'less-or-equal', 'greater', 'greater-or-equal'].includes(ops[node.op]) &&
          (this.isDate(node.left) || this.isDate(node.right))
        return this.fill(`shell.studio.expression.reads.${ops[node.op]}${dates ? '.date' : ''}`, {
          a: this.value(node.left, field),
          b: this.value(node.right, field),
        })
      }
      case 'null':
        return this.fill(`shell.studio.expression.reads.${node.negated ? 'is-not-null' : 'is-null'}`, {
          a: this.value(node.item),
        })
      case 'in': {
        const field = this.fieldOf(node.item)
        return this.fill('shell.studio.expression.reads.in', {
          a: this.value(node.item),
          list: this.list(node.values.map((value) => this.value(value, field))),
        })
      }
      case 'not':
        // `not` before a yes-or-no field reads as that field being no.
        if (this.fieldOf(node.item)?.valueType === 'boolean') {
          return this.fill('shell.studio.expression.reads.equal', {
            a: this.value(node.item),
            b: [{ kind: 'value', text: this.sources.t('shell.studio.expression.reads.no') }],
          })
        }
        return this.fill('shell.studio.expression.reads.not', { a: this.inner(node.item) })
      default: {
        // A yes-or-no value on its own, such as a boolean field, reads as that value being yes.
        const field = this.fieldOf(node)
        if (field?.valueType === 'boolean') {
          return this.fill('shell.studio.expression.reads.equal', {
            a: this.value(node),
            b: [{ kind: 'value', text: this.sources.t('shell.studio.expression.reads.yes') }],
          })
        }
        return this.value(node)
      }
    }
  }

  /** A condition inside `not`: a group keeps its brackets in words, as "all of" or "any of". */
  private inner(node: Node): ReadingPart[] {
    const bare = node.t === 'group' ? node.item : node
    if (bare.t === 'and' || bare.t === 'or') {
      const items = bare.items.map((item) => this.inner(item))
      return this.fill(`shell.studio.expression.reads.${bare.t === 'and' ? 'allOf' : 'anyOf'}`, {
        list: this.list(items, bare.t === 'and' ? 'conjunction' : 'disjunction'),
      })
    }
    return this.condition(bare)
  }

  private value(node: Node, field?: ScopeOption): ReadingPart[] {
    switch (node.t) {
      case 'literal':
        return [{ kind: 'value', text: this.literal(node, field) }]
      case 'path':
        return this.path(node.names)
      case 'group':
        return [{ kind: 'words', text: '(' }, ...this.value(node.item, field), { kind: 'words', text: ')' }]
      case 'negate':
        return [{ kind: 'words', text: '−' }, ...this.value(node.item, field)]
      case 'arith': {
        const symbol = { '+': '+', '-': '−', '*': '×', '/': '÷' }[node.op] ?? node.op
        return [
          ...this.value(node.left, field),
          { kind: 'words', text: ` ${symbol} ` },
          ...this.value(node.right, field),
        ]
      }
      case 'call':
        return this.call(node)
      default:
        return this.inner(node)
    }
  }

  private call(node: Extract<Node, { t: 'call' }>): ReadingPart[] {
    const args = node.args.map((arg) => this.value(arg))
    if (functionsWithWords.has(node.name)) {
      return this.fill(`shell.studio.expression.reads.fn.${node.name}`, {
        a: args[0] ?? [],
        b: args[1] ?? [],
        c: args[2] ?? [],
        list: this.list(args, 'conjunction'),
      })
    }
    const rule = this.option(node.name, 'rule')
    const label = rule ? labelOf(this.sources, rule) : undefined
    if (label !== undefined && args.length === 0) {
      return [{ kind: 'name', text: label }]
    }
    return [
      { kind: label !== undefined ? 'name' : 'words', text: `${label ?? node.name}(` },
      ...args.flatMap((arg, index) => (index === 0 ? arg : [{ kind: 'words' as const, text: ', ' }, ...arg])),
      { kind: 'words', text: ')' },
    ]
  }

  /** A path reads from its last name back: "Name of Supplier". */
  private path(names: string[]): ReadingPart[] {
    const labels: string[] = []
    const option = this.option(names[0], 'field') ?? this.option(names[0], 'parameter')
    labels.push(option ? (labelOf(this.sources, option) ?? names[0]) : names[0])
    let entity = option?.target ?? null
    for (const name of names.slice(1)) {
      const field = this.sources.names?.entities
        .find((item) => item.name.toLowerCase() === entity?.toLowerCase())
        ?.fields.find((item) => item.name.toLowerCase() === name.toLowerCase())
      const label =
        field && this.sources.names ? labelText(this.sources.names, field.labelKey, this.sources.locale) : undefined
      labels.push(label ?? name)
      entity = field?.target ?? null
    }
    let parts: ReadingPart[] = [{ kind: 'name', text: labels[0] }]
    for (const label of labels.slice(1)) {
      parts = this.fill('shell.studio.expression.reads.of', { a: [{ kind: 'name', text: label }], b: parts })
    }
    return parts
  }

  private literal(node: Extract<Node, { t: 'literal' }>, field?: ScopeOption): string {
    const { locale, t } = this.sources
    switch (node.kind) {
      case 'number':
        return formatter(`number:${locale}`, () => new Intl.NumberFormat(locale, { maximumFractionDigits: 20 })).format(
          Number(node.text),
        )
      case 'true':
        return t('shell.studio.expression.reads.yes')
      case 'false':
        return t('shell.studio.expression.reads.no')
      case 'null':
        return t('shell.studio.expression.reads.null')
      case 'date': {
        const date = new Date(`${node.text}${node.text.length === 10 ? 'T00:00:00' : ''}`)
        return Number.isNaN(date.getTime())
          ? node.text
          : new Intl.DateTimeFormat(locale, {
              dateStyle: 'long',
              ...(node.text.length > 10 ? { timeStyle: 'short' } : {}),
            }).format(date)
      }
      case 'text': {
        const key = field?.valueLabels?.[node.text]
        const label = key && this.sources.names ? labelText(this.sources.names, key, locale) : undefined
        return label ?? `“${node.text}”`
      }
    }
  }

  private isDate(node: Node): boolean {
    const type = this.fieldOf(node)?.valueType
    return (
      type === 'date' ||
      type === 'date-time' ||
      (node.t === 'literal' && node.kind === 'date') ||
      (node.t === 'call' && ['today', 'now', 'addDays', 'toDate'].includes(node.name))
    )
  }

  /** The field a value is, when it is a name in scope, so a value compared with an enum field reads by its label. */
  private fieldOf(node: Node): ScopeOption | undefined {
    return node.t === 'path' && node.names.length === 1
      ? (this.option(node.names[0], 'field') ?? this.option(node.names[0], 'parameter'))
      : undefined
  }

  private option(name: string, type: ScopeOption['type']): ScopeOption | undefined {
    const wanted = type === 'rule' ? `${name}()` : name
    return this.sources.options.find(
      (option) => option.type === type && option.label.toLowerCase() === wanted.toLowerCase(),
    )
  }

  private list(items: ReadingPart[][], type: 'conjunction' | 'disjunction' = 'disjunction'): ReadingPart[] {
    const marks = items.map((_, index) => `\u0000${index}\u0000`)
    const locale = this.sources.locale
    const joined = formatter(`list:${type}:${locale}`, () => new Intl.ListFormat(locale, { type })).format(marks)
    return splitMarks(joined, (index) => items[Number(index)])
  }

  /** The text of `key` with each `{name}` filled by its parts, so the words keep each language's order. */
  private fill(key: string, slots: Record<string, ReadingPart[]>): ReadingPart[] {
    const text = this.sources.t(key).replace(/\{(\w+)\}/g, (_, name: string) => `\u0000${name}\u0000`)
    return splitMarks(text, (name) => slots[name] ?? [])
  }
}

function splitMarks(text: string, part: (mark: string) => ReadingPart[]): ReadingPart[] {
  return text.split('\u0000').flatMap((piece, index) => {
    if (index % 2 === 1) {
      return part(piece)
    }
    return piece === '' ? [] : [{ kind: 'words' as const, text: piece }]
  })
}

function labelOf(sources: ReadingSources, option: ScopeOption): string | undefined {
  return sources.names ? labelText(sources.names, option.labelKey, sources.locale) : undefined
}

const formatters = new Map<string, unknown>()

/** One formatter for each kind and language, made once, as making one costs far more than using it. */
export function formatter<T>(key: string, make: () => T): T {
  let made = formatters.get(key) as T | undefined
  if (made === undefined) {
    made = make()
    formatters.set(key, made)
  }
  return made
}
