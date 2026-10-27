/**
 * Makes forms submit inside a frame sandboxed without `allow-forms`, such as the cards of the Design System
 * artifact, where the browser blocks a form's own submission before its submit event fires. The bundle fires
 * the submit event itself, for a click on a submit button, Enter in an input and `requestSubmit()`, and never
 * lets the browser submit the form. The listeners are on the document, because a dialog renders outside the
 * element a preview mounts into. A click that an earlier handler cancelled submits nothing. Returns the
 * function that removes the listeners and restores `requestSubmit`, for tests.
 */
export function installFormSubmission(): () => void {
  const originalRequestSubmit = HTMLFormElement.prototype.requestSubmit

  /** The browser's constraint check, then the submit event. The browser's own submission never runs. */
  function submit(form: HTMLFormElement, submitter: HTMLElement | null): void {
    const skipsValidation =
      form.noValidate || (submitter !== null && 'formNoValidate' in submitter && !!submitter.formNoValidate)
    if (!skipsValidation && !form.reportValidity()) {
      return
    }
    form.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true, submitter }))
  }

  HTMLFormElement.prototype.requestSubmit = function (submitter?: HTMLElement | null) {
    if (submitter && (!isSubmitButton(submitter) || submitter.form !== this)) {
      throw new DOMException('The specified element is not a submit button of this form.', 'NotFoundError')
    }
    submit(this, submitter ?? null)
  }

  const onClick = (event: MouseEvent) => {
    if (event.defaultPrevented || !(event.target instanceof Element)) {
      return
    }
    const element = event.target.closest('button, input')
    if (!element || !isSubmitButton(element) || element.disabled || !element.form) {
      return
    }
    event.preventDefault()
    submit(element.form, element)
  }

  const onKeyDown = (event: KeyboardEvent) => {
    if (
      event.key !== 'Enter' ||
      event.defaultPrevented ||
      event.isComposing ||
      event.altKey ||
      event.ctrlKey ||
      event.metaKey ||
      event.shiftKey
    ) {
      return
    }
    const input = event.target
    if (!(input instanceof HTMLInputElement) || !input.form || noImplicitSubmission.has(input.type)) {
      return
    }
    event.preventDefault()
    const form = input.form
    const defaultButton = Array.from(form.elements).find(isSubmitButton)
    if (defaultButton) {
      if (!defaultButton.disabled) {
        defaultButton.click()
      }
    } else if (Array.from(form.elements).filter(blocksImplicitSubmission).length <= 1) {
      submit(form, null)
    }
  }

  document.addEventListener('click', onClick)
  document.addEventListener('keydown', onKeyDown)
  return () => {
    document.removeEventListener('click', onClick)
    document.removeEventListener('keydown', onKeyDown)
    HTMLFormElement.prototype.requestSubmit = originalRequestSubmit
  }
}

/** Input types that never submit their form when Enter is pressed in them. */
const noImplicitSubmission = new Set([
  'button',
  'checkbox',
  'color',
  'file',
  'hidden',
  'image',
  'radio',
  'range',
  'reset',
  'submit',
])

/** Input types that count as a field for the browser's rule that a form with no submit button and several fields does not submit on Enter. */
const implicitSubmissionFields = new Set([
  'text',
  'search',
  'url',
  'tel',
  'email',
  'password',
  'date',
  'month',
  'week',
  'time',
  'datetime-local',
  'number',
])

function blocksImplicitSubmission(element: Element): boolean {
  return element instanceof HTMLInputElement && implicitSubmissionFields.has(element.type)
}

function isSubmitButton(element: Element): element is HTMLButtonElement | HTMLInputElement {
  if (element instanceof HTMLButtonElement) {
    return element.type === 'submit'
  }
  return element instanceof HTMLInputElement && (element.type === 'submit' || element.type === 'image')
}
