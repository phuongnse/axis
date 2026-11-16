import { CompletionContext } from '@codemirror/autocomplete'
import { EditorState } from '@codemirror/state'
import { describe, expect, it } from 'vitest'
import type { ExpressionScope } from '../platform/studio'
import { completeAt, cursorContext, operatorIds, type CompletionSources } from './expressionCompletion'
import { expressionTokens } from './expressionLanguage'
import type { ScopeOption } from './expressionScope'

function field(label: string, valueType: string, values: string[] | null = null): ScopeOption {
  return { label, type: 'field', cursorInside: false, valueType, values }
}

const options: ScopeOption[] = [
  field('title', 'text'),
  field('amount', 'decimal'),
  field('count', 'integer'),
  field('contract', 'file'),
  field('urgent', 'boolean'),
  field('status', 'enum', ['Draft', 'Submitted', 'Approved']),
  field('dueDate', 'date'),
  field('lines', 'list'),
  {
    label: 'IsManager()',
    type: 'rule',
    cursorInside: true,
    valueType: 'boolean',
    parameters: [{ name: 'user', type: 'reference' }],
  },
]

const scope: ExpressionScope = {
  type: 'boolean',
  fields: [],
  functions: [
    { name: 'length', parameters: ['text'], result: 'integer', group: 'Text', example: "length('a')", description: '' },
    {
      name: 'sum',
      parameters: ['list'],
      result: 'list<Line>',
      group: 'Aggregate',
      example: 'sum(lines)',
      description: '',
    },
  ],
}

function at(text: string) {
  return cursorContext(expressionTokens(text), options, scope)
}

function operatorsAfter(text: string) {
  const found = at(text)
  if (found?.kind !== 'operators') {
    throw new Error(`Expected operators after '${text}', got ${JSON.stringify(found)}.`)
  }
  return operatorIds(found.valueType, found.compares)
}

/** The labels of the list that `text` opens with the cursor at its end, in the order shown. */
function labelsAt(text: string, explicit = true) {
  const sources: CompletionSources = { options, scope, names: null, locale: 'en', t: (key) => key }
  const state = EditorState.create({ doc: text })
  const result = completeAt(new CompletionContext(state, text.length, explicit), sources)
  return result?.options.map((option) => option.label) ?? null
}

describe('expressionTokens', () => {
  it('gives the tokens with their positions, and no white space', () => {
    expect(expressionTokens("a == 'b'\nc")).toEqual([
      { type: 'variableName', text: 'a', from: 0 },
      { type: 'operator', text: '==', from: 2 },
      { type: 'string', text: "'b'", from: 5 },
      { type: 'variableName', text: 'c', from: 9 },
    ])
  })
})

describe('operators after a value', () => {
  it('offer a decimal the comparisons first, then the null tests, in and the arithmetic', () => {
    expect(operatorsAfter('amount ')).toEqual([
      'equal',
      'not-equal',
      'less',
      'less-or-equal',
      'greater',
      'greater-or-equal',
      'is-null',
      'is-not-null',
      'in',
      'add',
      'subtract',
      'multiply',
      'divide',
    ])
    expect(operatorsAfter('1 ')).toEqual(operatorsAfter('amount '))
    expect(operatorsAfter('1.5 ')).toEqual(operatorsAfter('amount '))
  })

  it('offer a file only the null tests', () => {
    expect(operatorsAfter('contract ')).toEqual(['is-null', 'is-not-null'])
  })

  it('offer text, an enum and a reference no ordering operator and no arithmetic', () => {
    for (const text of ['title ', "'abc' ", 'status ']) {
      expect(operatorsAfter(text)).toEqual(['equal', 'not-equal', 'is-null', 'is-not-null', 'in'])
    }
  })

  it('offer a date the comparisons but no arithmetic, and a boolean and and or after its comparisons', () => {
    expect(operatorsAfter('dueDate ')).not.toContain('add')
    expect(operatorsAfter('dueDate ')).toContain('less')
    expect(operatorsAfter('urgent ').slice(-2)).toEqual(['and', 'or'])
    expect(operatorsAfter('true ')).toContain('equal')
  })

  it('offer every operator but the values and not after a value of unknown type', () => {
    const everything = operatorIds(null, false)
    expect(everything).toContain('and')
    expect(everything).toContain('add')
    expect(everything).not.toContain('not')
    expect(everything).not.toContain('true')
    expect(operatorsAfter('order.total ')).toEqual(everything)
    expect(operatorsAfter('coalesce(a, b) ')).toEqual(everything)
  })

  it('offer a list nothing', () => {
    expect(operatorIds('list', false)).toEqual([])
  })

  it('take the type of a call from the rule or the function it names', () => {
    expect(operatorsAfter('IsManager(id) ')).toContain('and')
    expect(operatorsAfter('length(title) ')).toContain('add')
    expect(operatorsAfter("date('2026-01-31') ")).not.toContain('add')
  })

  it('offer only and and or after a clause that already compares', () => {
    for (const text of [
      'amount > 0 ',
      'contract is null ',
      'contract is not null ',
      "status in ('Draft') ",
      'urgent == true ',
    ]) {
      expect(operatorsAfter(text)).toEqual(['and', 'or'])
    }
    expect(operatorsAfter('amount > 0 and amount ')).toContain('less')
  })
})

describe('enum values', () => {
  it('open after an enum field, a comparison and a quote, from after the quote', () => {
    expect(at("status == '")).toEqual({ kind: 'enum', values: ['Draft', 'Submitted', 'Approved'], from: 11 })
    expect(at("status != 'Sub")).toMatchObject({ kind: 'enum', from: 11 })
    expect(at("STATUS == '")).toMatchObject({ kind: 'enum' })
  })

  it('open inside the list of in, after earlier values', () => {
    expect(at("status in ('")).toMatchObject({ kind: 'enum', from: 12 })
    expect(at("status in ('Draft', '")).toMatchObject({ kind: 'enum', from: 21 })
  })

  it('stay closed inside a string that is not an enum field’s', () => {
    expect(at("title == '")).toBeNull()
    expect(at("amount > 'x")).toBeNull()
    expect(at("'")).toBeNull()
  })
})

describe('the names', () => {
  it('need the place’s type at the start, boolean after and, or and not', () => {
    expect(at('')).toEqual({ kind: 'names', needed: 'boolean' })
    expect(at('amount > 0 and ')).toEqual({ kind: 'names', needed: 'boolean' })
    expect(at('urgent or ')).toEqual({ kind: 'names', needed: 'boolean' })
    expect(at('not ')).toEqual({ kind: 'names', needed: 'boolean' })
  })

  it('need the left operand’s type after a comparison, arithmetic, in and a comma of in', () => {
    expect(at('amount > ')).toEqual({ kind: 'names', needed: 'decimal' })
    expect(at('status == ')).toEqual({ kind: 'names', needed: 'enum' })
    expect(at('amount + ')).toEqual({ kind: 'names', needed: 'decimal' })
    expect(at('title in (')).toEqual({ kind: 'names', needed: 'text' })
    expect(at("title in ('a', ")).toEqual({ kind: 'names', needed: 'text' })
    expect(at('length(')).toEqual({ kind: 'names', needed: null })
  })

  it('put those that give the needed type first, then the fields, values, not, rules and functions in order', () => {
    expect(labelsAt('')).toEqual([
      'urgent',
      'true',
      'false',
      'not',
      'IsManager()',
      'title',
      'amount',
      'count',
      'contract',
      'status',
      'dueDate',
      'lines',
      'null',
      "'text'",
      "date('…')",
      'length()',
      'sum()',
    ])
  })

  it('put a whole number among the decimals, and keep the source order inside each group', () => {
    expect(labelsAt('dueDate > ')?.slice(0, 2)).toEqual(['dueDate', "date('…')"])
    expect(labelsAt('amount > ')?.slice(0, 3)).toEqual(['amount', 'count', 'length()'])
    expect(labelsAt('amount > ')?.slice(3, 6)).toEqual(['title', 'contract', 'urgent'])
  })
})

describe('completion', () => {
  it('opens by itself only after a space that follows a value', () => {
    expect(labelsAt('amount ', false)?.[0]).toBe('==')
    expect(labelsAt('amount > ', false)).toBeNull()
    expect(labelsAt('amount>', false)).toBeNull()
    expect(labelsAt('amount)', false)).toBeNull()
  })

  it('opens by itself after a quote that follows an enum field, and offers the values', () => {
    expect(labelsAt("status == '", false)).toEqual(['Draft', 'Submitted', 'Approved'])
  })

  it('answers a typed word for the text before the word', () => {
    expect(labelsAt('amount i', false)).toContain('is null')
    expect(labelsAt('amount > 0 and u', false)).toContain('urgent')
  })

  it('offers nothing after a list', () => {
    expect(labelsAt('lines ')).toBeNull()
  })
})
