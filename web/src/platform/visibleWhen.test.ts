import { describe, expect, it } from 'vitest'
import { isVisible } from './visibleWhen'

const scope = {
  id: 'row-1',
  values: {
    status: 'open',
    Title: 'Hello',
    count: '5.00',
    other: '5',
    done: false,
    urgent: true,
    note: null,
    empty: null,
    items: [{ a: '1' }],
  },
}

describe('isVisible', () => {
  it('shows a button with no condition', () => {
    expect(isVisible(null, scope)).toBe(true)
  })

  it.each([
    ["status == 'open'", true],
    ["status == 'closed'", false],
    ["status != 'closed'", true],
    ["TITLE == 'Hello'", true],
    ["status == 'OPEN'", false],
    ["title == 'it''s'", false],
    ['id == ' + "'row-1'", true],
    ['done == false', true],
    ['urgent == true', true],
    ['urgent', true],
    ['done', false],
    ['true', true],
    ['false', false],
  ])('works out %s to %s', (expression, expected) => {
    expect(isVisible(expression, scope)).toBe(expected)
  })

  it.each([
    ['note is null', true],
    ['note is not null', false],
    ['status is null', false],
    ['status is not null', true],
    ['NOTE IS NULL', true],
    ['null == null', true],
    ['note == null', true],
    ['note != null', false],
    ['status == null', false],
    ['status != null', true],
    ['note == empty', true],
    ["note == 'open'", false],
    ["note != 'open'", true],
  ])('treats null as a value in %s', (expression, expected) => {
    expect(isVisible(expression, scope)).toBe(expected)
  })

  it.each([
    ["status in ('open', 'approved')", true],
    ["status in ('closed')", false],
    ["note in ('open')", false],
    ['null in (1, 2)', false],
    ['count in (4, 5)', true],
    ['count in (true)', false],
  ])('works out %s to %s', (expression, expected) => {
    expect(isVisible(expression, scope)).toBe(expected)
  })

  it.each([
    ['count == 5', true],
    ['count == 5.0', true],
    ['count == 05', true],
    ['other == 5.00', true],
    ['count == 6', false],
    ['count == -5', false],
    ["count == '5'", false],
    ['count == other', false],
    ['other == 5', true],
  ])('compares a number literal as a number in %s', (expression, expected) => {
    expect(isVisible(expression, scope)).toBe(expected)
  })

  it.each([
    ['not done', true],
    ['not urgent', false],
    ['not not urgent', true],
    ['urgent and done', false],
    ['urgent and not done', true],
    ['done or urgent', true],
    ['done or done', false],
    ["status == 'open' and (done or urgent)", true],
    ["status == 'closed' or title == 'Hello' and done", false],
    ["(status == 'closed' or title == 'Hello') and urgent", true],
    ['URGENT AND NOT DONE', true],
  ])('works out %s to %s', (expression, expected) => {
    expect(isVisible(expression, scope)).toBe(expected)
  })

  it.each([
    ['not null'],
    ['true and null'],
    ['null and true'],
    ['null or false'],
    ['false or null'],
    ['null'],
    ['not (null)'],
    ['(note) == true'],
  ])('hides the button when %s is null', (expression) => {
    expect(isVisible(expression, scope)).toBe(false)
  })

  it('lets false and true decide when the other side is null', () => {
    expect(isVisible('not (false and null)', scope)).toBe(true)
    expect(isVisible('true or null', scope)).toBe(true)
    expect(isVisible('null or true', scope)).toBe(true)
    expect(isVisible('not (false or null)', scope)).toBe(false)
  })

  it.each([
    ['count < 10'],
    ['count >= 1'],
    ['count + 1 == 6'],
    ['-count == 5'],
    ["upper(title) == 'HELLO'"],
    ["upper (title) == 'HELLO'"],
    ['now()'],
    ['departments:IsManager(id)'],
    ['title.length == 5'],
    ["status == 'open' && done"],
    ['status = 1'],
    ["status in (date('2020-01-01'))"],
    ['status in (title)'],
    ['status in (null)'],
    ['status in ()'],
    ["status in ('open'"],
    ['status == 1 == 1'],
    ['status == 1 != 2'],
    ["status == 'open' extra"],
    ['(urgent'],
    ['urgent)'],
    ['status is'],
    ['status is not'],
    ['status is true'],
    ["'unterminated"],
    ['title #'],
    ['and'],
    [''],
    ['   '],
  ])('hides the button for the unsupported %s', (expression) => {
    expect(isVisible(expression, scope)).toBe(false)
  })

  it.each([
    ['missing == 1'],
    ['missing is null'],
    ['urgent or missing'],
    ['done and missing'],
    ['items == 1'],
    ['items is null'],
    ['not missing'],
  ])('hides the button when %s reads a name the row does not hold', (expression) => {
    expect(isVisible(expression, scope)).toBe(false)
  })

  it('hides the button when a non-boolean value is an operand of and, or or not', () => {
    expect(isVisible('status and urgent', scope)).toBe(false)
    expect(isVisible('urgent or status', scope)).toBe(false)
    expect(isVisible('not status', scope)).toBe(false)
    expect(isVisible("not 'open'", scope)).toBe(false)
  })

  it('hides the button when the final value is not a boolean', () => {
    expect(isVisible('status', scope)).toBe(false)
    expect(isVisible('5', scope)).toBe(false)
  })

  it('hides the button for a field the user may not read, which the row does not hold', () => {
    expect(isVisible("secret == 'x'", { id: 'row-1', values: {} })).toBe(false)
  })

  it('reads id as the record id', () => {
    expect(isVisible('id is not null', scope)).toBe(true)
    expect(isVisible('id is not null', { id: null, values: {} })).toBe(false)
  })
})
