import { ConfigProvider, Flex, Select, Typography } from 'antd'
import { useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { RowActions } from '../platform/AxisTable'
import type { AxisAction } from '../platform/menuActions'
import { EmptyState } from '../platform/EmptyState'
import { unusedStepName } from '../platform/formLayout'
import { SectionPart } from '../platform/SectionParts'
import { useText } from '../platform/texts'
import { LabelTexts } from './LabelTexts'
import type { ChangeHandler } from './SchemaForm'

type Content = Record<string, unknown>

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** The label and the help of a form's input, from `shell.studio.field.form.<path>`, see docs/reference/frontend.md#platform-field-help. */
function fieldTexts(t: (key: string) => string, path: string) {
  return { label: t(`shell.studio.field.form.${path}`), help: t(`shell.studio.field.form.${path}.help`) }
}

/** The title and the help of a form's group of inputs, which has no one input to label. */
function groupTexts(t: (key: string) => string, path: string) {
  const { label, help } = fieldTexts(t, path)
  return { title: label, help }
}

/** The names of the sections that have one, in the order of the form. */
function sectionNames(sections: unknown): string[] {
  if (!Array.isArray(sections)) {
    return []
  }
  return sections
    .map((section) => (isContent(section) ? section.name : undefined))
    .filter((name): name is string => typeof name === 'string' && name !== '')
}

/**
 * The `steps` of a form as rows, see docs/reference/studio-form-editors.md#form-steps: a title and the sections the step
 * shows, with a "…" menu of Move up, Move down and Delete step, which comes last after a divider. The section picker
 * lists the named sections of the form, so the author picks names and does not type them. Removing the last step removes `steps`, which turns the form back into one
 * page. The rows sit in a part headed with the steps' title and help, and edit the draft through `onChange`. A read-only
 * tab, as while the JSON has an error, shows neither Add step nor a step's menu. A `steps` that is not a list is left to
 * the JSON view.
 */
export function FormSteps({
  steps,
  sections,
  onChange,
}: {
  steps: unknown
  sections: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const { componentDisabled } = ConfigProvider.useConfig()
  // A title editor keeps its texts in its state, so a move or a removal remounts the rows.
  const [generation, setGeneration] = useState(0)
  const heading = { title: t('shell.studio.formSteps.title'), line: t('shell.studio.formSteps.help') }

  if (steps !== undefined && !Array.isArray(steps)) {
    return (
      <SectionPart testName="steps" heading={heading}>
        <div data-testid="studio-field-/steps">
          <Typography.Text type="secondary" data-testid="studio-json-only">
            {t('shell.studio.editInJson')}
          </Typography.Text>
        </div>
      </SectionPart>
    )
  }

  const list: unknown[] = steps ?? []
  const options = sectionNames(sections).map((name) => ({ value: name, label: name }))

  function move(index: number, other: number) {
    const next = [...list]
    ;[next[index], next[other]] = [next[other], next[index]]
    setGeneration((current) => current + 1)
    onChange('/steps', next)
  }

  function remove(index: number) {
    setGeneration((current) => current + 1)
    onChange(list.length === 1 ? '/steps' : `/steps/${index}`, undefined)
  }

  function add() {
    const step = { name: unusedStepName(list), title: { textKey: '' }, sections: [] }
    onChange('/steps', [...list, step])
  }

  function actionsOf(index: number): AxisAction[] {
    return [
      {
        key: 'moveUp',
        labelKey: 'shell.studio.formSteps.moveUp',
        disabled: index === 0,
        onClick: () => move(index, index - 1),
      },
      {
        key: 'moveDown',
        labelKey: 'shell.studio.formSteps.moveDown',
        disabled: index === list.length - 1,
        onClick: () => move(index, index + 1),
      },
      {
        key: 'remove',
        labelKey: 'shell.studio.formSteps.removeStep',
        meaning: 'remove',
        onClick: () => remove(index),
      },
    ]
  }

  return (
    <SectionPart
      testName="steps"
      heading={heading}
      action={
        componentDisabled ? undefined : (
          <AxisButton role="secondary" onClick={add}>
            {t('shell.studio.formSteps.addStep')}
          </AxisButton>
        )
      }
    >
      <div data-testid="studio-field-/steps">
        <Flex vertical gap="middle">
          {list.length === 0 && <EmptyState text={t('shell.studio.formSteps.empty')} />}
          {list.map((step, index) => {
            const pointer = `/steps/${index}`
            const chosen = isContent(step) && Array.isArray(step.sections) ? step.sections : []
            return (
              <Flex
                key={`${generation}-${index}`}
                gap="small"
                align="flex-start"
                data-testid={`studio-field-${pointer}`}
              >
                <Flex vertical gap="small" style={{ flex: 1 }}>
                  <div data-testid={`studio-field-${pointer}/title`}>
                    <AxisFieldGroup {...groupTexts(t, 'steps.title')}>
                      <LabelTexts
                        pointer={`${pointer}/title`}
                        value={isContent(step) ? step.title : undefined}
                        onChange={onChange}
                      />
                    </AxisFieldGroup>
                  </div>
                  <div data-testid={`studio-field-${pointer}/sections`}>
                    <AxisField {...fieldTexts(t, 'steps.sections')}>
                      <Select
                        mode="multiple"
                        virtual={false}
                        value={chosen}
                        options={options}
                        onChange={(next: string[]) => onChange(`${pointer}/sections`, next)}
                      />
                    </AxisField>
                  </div>
                </Flex>
                {componentDisabled ? null : <RowActions actions={actionsOf(index)} />}
              </Flex>
            )
          })}
        </Flex>
      </div>
    </SectionPart>
  )
}
