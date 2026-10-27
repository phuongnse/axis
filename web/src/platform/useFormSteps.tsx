import { Steps } from 'antd'
import { useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import { FormCanvasContext } from './formCanvas'
import type { FormMetadata } from './site'
import { useText } from './texts'
import type { Section, useRecordForm } from './useRecordForm'

const connector = 48
const compactFrom = 7
// Every flex level of a step may shrink below its content, so a long title can give way.
const shrinkable = { minWidth: 0 } as const
const ellipsis = {
  display: 'block',
  maxWidth: 160,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
} as const

const focusable = 'input:not([type=hidden]), textarea, [role=combobox], button'

/**
 * The wizard of a form that declares `steps`: one step's sections at a time, under a progress bar
 * of the step titles. Next asks the server to check the step's shown, editable fields before it
 * moves on, and stays when one has an error. Back never checks. Moving keeps every typed value,
 * because the values live in `recordForm`. A step whose fields the form state answer all hides is
 * skipped and left out of the progress bar. After a move, focus goes to the first input of the new
 * step. A form without steps is one page: `fields` holds every section and `last` is always true.
 *
 * Inside the Studio form canvas, the canvas picks the step. Hidden steps are not skipped and focus does not move.
 */
export function useFormSteps(
  form: FormMetadata | null,
  sections: readonly Section[],
  recordForm: ReturnType<typeof useRecordForm>,
) {
  const t = useText()
  const canvas = useContext(FormCanvasContext)
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
  const visible = steps.map((_, index) => index).filter((index) => canvas !== null || !hiddenStep(index))
  // A current step that became hidden moves to the next visible one, or to the last visible one.
  const index = canvas
    ? Math.max(0, Math.min(canvas.step, steps.length - 1))
    : visible.includes(current)
      ? current
      : (visible.find((i) => i > current) ?? visible.at(-1) ?? current)
  const position = visible.indexOf(index)
  const last = !active || position === visible.length - 1 || position < 0

  useEffect(() => {
    if (canvas !== null || previous.current === index) {
      return
    }
    previous.current = index
    const inputs = [...(container.current?.querySelectorAll<HTMLElement>(focusable) ?? [])]
    inputs.find((element) => !element.matches(':disabled, [readonly]'))?.focus()
  }, [index, canvas])

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

  // Proposed: each step is as wide as its title, joined to the next by a short line, so the bar sits on the left
  // and never stretches across the form. With seven or more only the current step shows its title. A long title
  // ends in an ellipsis and shows whole on hover.
  const shown = Math.max(position, 0)
  const compact = visible.length >= compactFrom
  const progress: ReactNode = active && (
    <Steps
      data-testid="form-steps"
      current={shown}
      items={visible.map((step, at) => {
        const title = t(steps[step].titleKey)
        return {
          // A step shrinks when the bar runs out of room, so its title ends in an ellipsis instead of overflowing.
          // The current step keeps its title whole.
          style: at === shown ? { flex: 'none' } : { flex: '0 1 auto', minWidth: 0 },
          styles: {
            wrapper: shrinkable,
            section: shrinkable,
            header: shrinkable,
            title: shrinkable,
            rail: at === visible.length - 1 ? undefined : { flex: `0 0 ${connector}px` },
          },
          title:
            compact && at !== shown ? (
              ''
            ) : (
              <span title={title} style={ellipsis}>
                {title}
              </span>
            ),
        }
      })}
    />
  )

  const fields: ReactNode = active ? (
    <div ref={container}>{recordForm.fieldsFor(steps[index].sections)}</div>
  ) : (
    recordForm.fields
  )

  return { active, last, first: position <= 0, progress, fields, next, back, openFirstWith, checking }
}
