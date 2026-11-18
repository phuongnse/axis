import { describe, expect, it } from 'vitest'
import { readingText } from './readingText'
import type { ExpressionReading } from './studio'

const texts: Record<string, string> = {
  'shell.studio.expression.reads.greater': '{0} is greater than {1}',
  'shell.studio.expression.reads.is-true': '{0} is yes',
  'shell.studio.expression.reads.in': '{0} is one of {1}',
  'shell.studio.expression.reads.join.and': 'and',
  'shell.studio.expression.reads.join.or': 'or',
  'shell.studio.expression.reads.group.none': 'None of these:',
  'request.field.totalAmount': 'Total amount',
}
const t = (key: string) => texts[key] ?? key
const labels = { label: (key: string | null) => (key ? texts[key] : undefined), locale: 'en' }
const amount: ExpressionReading = { kind: 'name', name: 'totalAmount', labelKey: 'request.field.totalAmount' }

describe('readingText', () => {
  it("reads a condition on one line, naming a field by its label and a number in the reader's format", () => {
    const reading: ExpressionReading = {
      kind: 'phrase',
      words: 'greater',
      slots: [[amount], [{ kind: 'value', type: 'decimal', value: 50000, labelKey: null }]],
    }
    expect(readingText(reading, labels, t)).toBe('Total amount is greater than 50,000')
  })

  it('joins a run of and on one line, and names a field with no label by its name', () => {
    const reading: ExpressionReading = {
      kind: 'all',
      items: [
        { kind: 'phrase', words: 'is-true', slots: [[{ kind: 'name', name: 'catalogItem', labelKey: null }]] },
        { kind: 'phrase', words: 'is-true', slots: [[amount]] },
      ],
    }
    expect(readingText(reading, labels, t)).toBe('catalogItem is yes and Total amount is yes')
  })

  it('reads a not group as its heading and its items, and the values of in joined with or', () => {
    const reading: ExpressionReading = {
      kind: 'none',
      items: [
        {
          kind: 'phrase',
          words: 'in',
          slots: [
            [amount],
            [
              { kind: 'value', type: 'integer', value: 1, labelKey: null },
              { kind: 'value', type: 'integer', value: 2, labelKey: null },
            ],
          ],
        },
      ],
    }
    expect(readingText(reading, labels, t)).toBe('None of these: Total amount is one of 1 or 2')
  })
})
