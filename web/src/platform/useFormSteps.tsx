import { Steps } from 'antd'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import type { FormMetadata } from './site'
import { useText } from './texts'
import type { Section, useRecordForm } from './useRecordForm'

const focusable = 'input:not([type=hidden]), textarea, [role=combobox], button'

/**
 * The wizard of a form that declares `steps`: one step's sections at a time, under a progress bar
 * of the step titles. Next asks the server to check the step's shown, editable fields before it
 * moves on, and stays when one has an error. Back never checks. Moving keeps every typed value,
 * because the values live in `recordForm`. A step whose fields the form state answer all hides is
 * skipped and left out of the progress bar. After a move, focus goes to the first input of the new
 * step. A form without steps is one page: `fields` holds every section and `last` is always true.
 */
export function useFormSteps(
  form: FormMetadata | null,
  sections: readonly Section[],
  recordForm: ReturnType<typeof useRecordForm>,
) {
  const t = useText()
  const steps = form?.steps ?? []
  const active = steps.length > 0
  const [current, setCurrent] = useState(0)
  const [checking, setChecking] = useState(false)
  const container = useRef<HTMLDivElement>(null)
  const previous = useRef(current)

  const fieldNamesOf = (index: number) =>
    sections
      .filter((section) => section.name !== null && steps[index].sections.includes(section.name))
      .flatMap((section) => section.fields.map(({ field }) => field.name))
  // A step with fields that are all hidden shows nothing, so it is skipped.
  const hiddenStep = (index: number) => {
    const names = fieldNamesOf(index)
    return names.length > 0 && names.every((name) => recordForm.hidden(name))
  }
  const visible = steps.map((_, index) => index).filter((index) => !hiddenStep(index))
  // A current step that became hidden moves to the next visible one, or to the last visible one.
  const index = visible.includes(current) ? current : (visible.find((i) => i > current) ?? visible.at(-1) ?? current)
  const position = visible.indexOf(index)
  const last = !active || position === visible.length - 1 || position < 0

  useEffect(() => {
    if (previous.current === index) {
      return
    }
    previous.current = index
    const inputs = [...(container.current?.querySelectorAll<HTMLElement>(focusable) ?? [])]
    inputs.find((element) => !element.matches(':disabled, [readonly]'))?.focus()
  }, [index])

  const next = async () => {
    if (last || checking) {
      return
    }
    setChecking(true)
    try {
      const names = fieldNamesOf(index).filter((name) => !recordForm.hidden(name) && !recordForm.locked(name))
      if (await recordForm.check(names)) {
        setCurrent(visible[position + 1])
      }
    } finally {
      setChecking(false)
    }
  }

  const back = () => {
    if (position > 0) {
      setCurrent(visible[position - 1])
    }
  }

  // Opens the first step that holds one of the fields, so a failed save shows its message.
  const openFirstWith = (names: readonly string[]) => {
    const first = visible.find((step) => fieldNamesOf(step).some((name) => names.includes(name)))
    if (first !== undefined) {
      setCurrent(first)
    }
  }

  const progress: ReactNode = active && (
    <Steps
      data-testid="form-steps"
      current={Math.max(position, 0)}
      items={visible.map((step) => ({ title: t(steps[step].titleKey) }))}
    />
  )

  const fields: ReactNode = active ? (
    <div ref={container}>{recordForm.fieldsFor(steps[index].sections)}</div>
  ) : (
    recordForm.fields
  )

  return { active, last, first: position <= 0, progress, fields, next, back, openFirstWith, checking }
}
