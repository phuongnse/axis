import { afterEach, describe, expect, it } from 'vitest'
import { focusFirstError } from './focusFirstError'

/** A root of inputs, each marked `aria-invalid` when `invalid` is set, and disabled when `disabled` is set. */
function boxes(...inputs: { id: string; invalid?: boolean; disabled?: boolean }[]) {
  const root = document.createElement('div')
  for (const input of inputs) {
    const box = document.createElement('input')
    box.id = input.id
    if (input.invalid === true) box.setAttribute('aria-invalid', 'true')
    box.disabled = input.disabled === true
    root.append(box)
  }
  document.body.append(root)
  return root
}

afterEach(() => document.body.replaceChildren())

describe('focusFirstError', () => {
  it('focuses the first box marked aria-invalid in its root and says it did', () => {
    const root = boxes({ id: 'plain' }, { id: 'first', invalid: true }, { id: 'second', invalid: true })

    expect(focusFirstError(root)).toBe(true)
    expect(document.activeElement).toBe(root.querySelector('#first'))
  })

  it('skips a disabled box that is marked, as it cannot take the focus', () => {
    const root = boxes({ id: 'off', invalid: true, disabled: true }, { id: 'on', invalid: true })

    expect(focusFirstError(root)).toBe(true)
    expect(document.activeElement).toBe(root.querySelector('#on'))
  })

  it('leaves the focus where it was and says so when no box is marked', () => {
    const root = boxes({ id: 'kept' }, { id: 'clear', invalid: false })
    const kept = root.querySelector<HTMLElement>('#kept')!
    kept.focus()

    expect(focusFirstError(root)).toBe(false)
    expect(document.activeElement).toBe(kept)
  })

  it('looks only in its root, and finds nothing without one', () => {
    const outside = boxes({ id: 'outside', invalid: true })
    const root = boxes({ id: 'inside' })
    const before = document.activeElement

    expect(focusFirstError(root)).toBe(false)
    expect(focusFirstError(null)).toBe(false)
    expect(document.activeElement).toBe(before)
    expect(outside.querySelector('#outside')).not.toBeNull()
  })
})
