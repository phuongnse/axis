// Unit test of the sample package payment-terms, see docs/reference/extension-packages.md#sample-and-test-packages.
// It runs in the installed copy under artifacts/extensions, where dayjs is in node_modules.
import assert from 'node:assert/strict'
import test from 'node:test'
import dayjs from 'dayjs'
import utc from 'dayjs/plugin/utc.js'
import { dueDate } from './index.js'

dayjs.extend(utc)

const cases = [
  ['a month end', '2026-01-31T10:00:00Z', '2026-03-31'],
  ['a leap year', '2028-01-30T00:00:00Z', '2028-02-29'],
  ['a year that is not a leap year', '2027-01-30T00:00:00Z', '2027-03-31'],
  ['an offset that moves the UTC day', '2026-01-01T20:00:00-05:00', '2026-02-28'],
]

for (const [name, from, expected] of cases) {
  test(`dueDate is the last day of the month 30 days after ${name}`, () => {
    const { dueDate: actual } = dueDate({ from })

    assert.equal(actual, expected)
    assert.equal(actual, dayjs.utc(from).add(30, 'day').endOf('month').format('YYYY-MM-DD'))
  })
}
