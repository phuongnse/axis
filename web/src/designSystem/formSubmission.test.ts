import { afterEach, beforeEach, describe, expect, test } from 'vitest'
import { installFormSubmission } from './formSubmission'

describe('installFormSubmission', () => {
  let uninstall: () => void
  let form: HTMLFormElement
  let input: HTMLInputElement
  let button: HTMLButtonElement
  let submits: SubmitEvent[]

  beforeEach(() => {
    uninstall = installFormSubmission()
    document.body.innerHTML = '<form><input name="title" /><button type="submit">Save</button></form>'
    form = document.querySelector('form')!
    input = form.querySelector('input')!
    button = form.querySelector('button')!
    submits = []
    form.addEventListener('submit', (event) => {
      event.preventDefault()
      submits.push(event as SubmitEvent)
    })
  })

  afterEach(() => {
    uninstall()
    document.body.innerHTML = ''
  })

  test('a click on a submit button fires one submit event and cancels the native submission', () => {
    const click = new MouseEvent('click', { bubbles: true, cancelable: true })
    button.dispatchEvent(click)

    expect(submits).toHaveLength(1)
    expect(submits[0].submitter).toBe(button)
    expect(click.defaultPrevented).toBe(true)
  })

  test('Enter in an input fires one submit event', () => {
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }))

    expect(submits).toHaveLength(1)
  })

  test('Enter in an input of a form with no submit button and one field fires one submit event', () => {
    button.remove()
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }))

    expect(submits).toHaveLength(1)
  })

  test('Enter in a form with no submit button and two fields fires no submit event', () => {
    button.remove()
    form.insertAdjacentHTML('beforeend', '<input name="other" />')
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }))

    expect(submits).toHaveLength(0)
  })

  test('requestSubmit fires one submit event', () => {
    form.requestSubmit()

    expect(submits).toHaveLength(1)
  })

  test('a click an earlier handler cancelled fires no submit event', () => {
    form.addEventListener('click', (event) => event.preventDefault())
    button.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))

    expect(submits).toHaveLength(0)
  })

  test('a click on a disabled submit button fires no submit event', () => {
    button.disabled = true
    button.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))

    expect(submits).toHaveLength(0)
  })

  test('a form that fails its constraint check fires no submit event', () => {
    input.required = true
    form.requestSubmit()

    expect(submits).toHaveLength(0)
  })
})
