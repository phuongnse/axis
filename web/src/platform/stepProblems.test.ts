import { describe, expect, it } from 'vitest'
import { problemText } from './stepProblems'

const texts: Record<string, string> = { 'shell.stepProblem.timeout': 'No answer within {seconds} sec.' }
const t = (key: string) => texts[key] ?? key

describe('problemText', () => {
  it('words a problem in the reader language with its values', () => {
    expect(
      problemText(t, { code: 'timeout', values: { seconds: '60' } }, 'The request timed out after 60 seconds.'),
    ).toBe('No answer within 60 sec.')
  })

  it('falls back to the stored error for a code without words, and to null without either', () => {
    expect(problemText(t, { code: 'unknownCode', values: {} }, 'Service Unavailable')).toBe('Service Unavailable')
    expect(problemText(t, null, null)).toBeNull()
  })
})
