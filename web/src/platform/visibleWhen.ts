import { navigateValue, numberPattern, type NavigateScope } from './navigateAction'

/** A value the evaluator holds. A name's value is a `text` unless it is a boolean or null. */
type Value =
  | { kind: 'null' }
  | { kind: 'boolean'; value: boolean }
  | { kind: 'text'; value: string }
  | { kind: 'number'; value: string }

type Token = { kind: 'text' | 'number' | 'name' | 'keyword' | 'symbol'; value: string } | { kind: 'end'; value: '' }

/** The expression uses something the SPA does not work out, or reads a name the row does not hold. */
class Unsupported extends Error {}

const keywords = new Set(['and', 'or', 'not', 'is', 'in', 'null', 'true', 'false'])
const nullValue: Value = { kind: 'null' }

function tokenize(source: string): Token[] {
  const tokens: Token[] = []
  let position = 0
  while (position < source.length) {
    const rest = source.slice(position)
    const space = /^\s+/.exec(rest)
    if (space) {
      position += space[0].length
      continue
    }
    const text = /^'(?:[^']|'')*'/.exec(rest)
    const number = /^-?\d+(?:\.\d+)?/.exec(rest)
    const name = /^[A-Za-z][A-Za-z0-9]*/.exec(rest)
    const symbol = /^(?:==|!=|\(|\)|,)/.exec(rest)
    const match = text ?? number ?? name ?? symbol
    if (!match) {
      throw new Unsupported()
    }
    const value = match[0]
    position += value.length
    if (text) {
      tokens.push({ kind: 'text', value: value.slice(1, -1).replaceAll("''", "'") })
    } else if (number) {
      tokens.push({ kind: 'number', value })
    } else if (name) {
      // A name followed by `(` is a call, which the SPA does not work out.
      if (!keywords.has(value.toLowerCase()) && /^\s*\(/.test(source.slice(position))) {
        throw new Unsupported()
      }
      tokens.push(
        keywords.has(value.toLowerCase()) ? { kind: 'keyword', value: value.toLowerCase() } : { kind: 'name', value },
      )
    } else {
      tokens.push({ kind: 'symbol', value })
    }
  }
  tokens.push({ kind: 'end', value: '' })
  return tokens
}

/** A number as its canonical text, so `5`, `+5`, `05` and `5.00` are the same number. */
function canonicalNumber(text: string): string {
  const negative = text.startsWith('-')
  const [whole, fraction = ''] = text.replace(/^[+-]/, '').split('.')
  const integer = whole.replace(/^0+(?=\d)/, '')
  const decimals = fraction.replace(/0+$/, '')
  const canonical = decimals ? `${integer}.${decimals}` : integer
  return negative && canonical !== '0' ? `-${canonical}` : canonical
}

/** Whether `==` holds. It is null safe: `null == null` is true. A number literal compares as a number, anything else by value. */
function same(left: Value, right: Value): boolean {
  if (left.kind === 'null' || right.kind === 'null') {
    return left.kind === right.kind
  }
  if (left.kind === 'number' || right.kind === 'number') {
    const isNumber = (value: Value) =>
      value.kind === 'number' || (value.kind === 'text' && numberPattern.test(value.value))
    return (
      isNumber(left) &&
      isNumber(right) &&
      canonicalNumber((left as { value: string }).value) === canonicalNumber((right as { value: string }).value)
    )
  }
  return left.kind === right.kind && left.value === right.value
}

/** A boolean operand of `and`, `or` or `not`: `true`, `false`, or `null` for unknown. */
function truth(value: Value): boolean | null {
  if (value.kind === 'null') {
    return null
  }
  if (value.kind !== 'boolean') {
    throw new Unsupported()
  }
  return value.value
}

function fromTruth(value: boolean | null): Value {
  return value === null ? nullValue : { kind: 'boolean', value }
}

/**
 * Works out the expression while reading it, over `or > and > not > comparison > primary`. Every
 * operand is worked out, so a name the row does not hold makes the whole expression unsupported
 * even where `and` or `or` could stop early.
 */
class Evaluator {
  private position = 0
  private readonly tokens: Token[]
  private readonly scope: NavigateScope

  constructor(tokens: Token[], scope: NavigateScope) {
    this.tokens = tokens
    this.scope = scope
  }

  run(): Value {
    const value = this.or()
    if (this.peek().kind !== 'end') {
      throw new Unsupported()
    }
    return value
  }

  private peek(): Token {
    return this.tokens[this.position]
  }

  private accept(kind: Token['kind'], value: string): boolean {
    const token = this.peek()
    if (token.kind === kind && token.value === value) {
      this.position++
      return true
    }
    return false
  }

  private or(): Value {
    const first = this.and()
    if (this.peek().kind !== 'keyword' || this.peek().value !== 'or') {
      return first
    }
    let result = truth(first)
    while (this.accept('keyword', 'or')) {
      const right = truth(this.and())
      result = result === true || right === true ? true : result === null || right === null ? null : false
    }
    return fromTruth(result)
  }

  private and(): Value {
    const first = this.not()
    if (this.peek().kind !== 'keyword' || this.peek().value !== 'and') {
      return first
    }
    let result = truth(first)
    while (this.accept('keyword', 'and')) {
      const right = truth(this.not())
      result = result === false || right === false ? false : result === null || right === null ? null : true
    }
    return fromTruth(result)
  }

  private not(): Value {
    if (!this.accept('keyword', 'not')) {
      return this.comparison()
    }
    const operand = truth(this.not())
    return fromTruth(operand === null ? null : !operand)
  }

  private comparison(): Value {
    const left = this.primary()
    if (this.accept('symbol', '==')) {
      return { kind: 'boolean', value: same(left, this.primary()) }
    }
    if (this.accept('symbol', '!=')) {
      return { kind: 'boolean', value: !same(left, this.primary()) }
    }
    if (this.accept('keyword', 'is')) {
      const negated = this.accept('keyword', 'not')
      if (!this.accept('keyword', 'null')) {
        throw new Unsupported()
      }
      return { kind: 'boolean', value: (left.kind === 'null') !== negated }
    }
    if (this.accept('keyword', 'in')) {
      if (!this.accept('symbol', '(')) {
        throw new Unsupported()
      }
      let found = false
      do {
        // An item is a literal other than null, so a name or a nested expression is not supported.
        const next = this.peek()
        const literal =
          next.kind === 'text' ||
          next.kind === 'number' ||
          (next.kind === 'keyword' && (next.value === 'true' || next.value === 'false'))
        if (!literal) {
          throw new Unsupported()
        }
        found = same(left, this.primary()) || found
      } while (this.accept('symbol', ','))
      if (!this.accept('symbol', ')')) {
        throw new Unsupported()
      }
      return { kind: 'boolean', value: left.kind !== 'null' && found }
    }
    return left
  }

  private primary(): Value {
    const token = this.tokens[this.position++]
    switch (token.kind) {
      case 'text':
        return { kind: 'text', value: token.value }
      case 'number':
        return { kind: 'number', value: token.value }
      case 'keyword':
        if (token.value === 'true' || token.value === 'false') {
          return { kind: 'boolean', value: token.value === 'true' }
        }
        if (token.value === 'null') {
          return nullValue
        }
        throw new Unsupported()
      case 'name':
        return this.resolve(token.value)
      case 'symbol': {
        if (token.value !== '(') {
          throw new Unsupported()
        }
        const inner = this.or()
        if (!this.accept('symbol', ')')) {
          throw new Unsupported()
        }
        return inner
      }
      default:
        throw new Unsupported()
    }
  }

  /** A name reads the row as a navigate expression does. A name the row does not hold is unsupported. */
  private resolve(name: string): Value {
    const value = navigateValue(name, this.scope)
    if (value === undefined || Array.isArray(value)) {
      throw new Unsupported()
    }
    if (value === null) {
      return nullValue
    }
    return typeof value === 'boolean' ? { kind: 'boolean', value } : { kind: 'text', value }
  }
}

/**
 * Whether a row action shows. A `null` expression always shows. Otherwise the button shows only when
 * the SPA works the expression out to `true`: `false`, `null`, syntax it does not support and a
 * name the row does not hold all hide it. It works out names, literals, `==`, `!=`, `is [not] null`,
 * `in (…)`, `and`, `or`, `not` and parentheses.
 */
export function isVisible(expression: string | null, scope: NavigateScope): boolean {
  if (expression === null) {
    return true
  }
  try {
    const result = new Evaluator(tokenize(expression), scope).run()
    return result.kind === 'boolean' && result.value
  } catch (error) {
    if (error instanceof Unsupported) {
      return false
    }
    throw error
  }
}
