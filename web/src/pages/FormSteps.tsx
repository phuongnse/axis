import { Button, Card, Flex, Input, Select, Typography } from 'antd'
import { useContext, useState } from 'react'
import { unusedStepName } from '../platform/formLayout'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { LabelTexts } from './LabelTexts'
import { LabelTextsContext } from './labelTextsContext'
import type { ChangeHandler } from './SchemaForm'

type Content = Record<string, unknown>

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
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

function textKeyOf(step: unknown): string {
  const title = isContent(step) ? step.title : undefined
  return isContent(title) && typeof title.textKey === 'string' ? title.textKey : ''
}

/**
 * The `steps` of a form as cards: a title, the sections the step shows, and buttons to move or remove it. The section
 * picker lists the named sections of the form, so the author picks names and does not type them. Removing the last step
 * removes `steps`, which turns the form back into one page. The cards edit the draft through `onChange`. A `steps`
 * that is not a list is left to the JSON view.
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
  const names = useContext(StudioNamesContext)
  const label = useContext(LabelTextsContext)
  // A title editor keeps its texts in its state, so a move or a removal remounts the cards.
  const [generation, setGeneration] = useState(0)

  if (steps !== undefined && !Array.isArray(steps)) {
    return (
      <div data-testid="studio-field-/steps">
        <div>{t('shell.studio.formSteps.title')}</div>
        <Typography.Text type="secondary" data-testid="studio-json-only">
          {t('shell.studio.editInJson')}
        </Typography.Text>
      </div>
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

  return (
    <div data-testid="studio-field-/steps">
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.formSteps.title')}</Typography.Text>
        {list.map((step, index) => {
          const pointer = `/steps/${index}`
          const chosen = isContent(step) && Array.isArray(step.sections) ? step.sections : []
          return (
            <Card key={`${generation}-${index}`} size="small" data-testid={`studio-field-${pointer}`}>
              <Flex vertical gap="small">
                <div data-testid={`studio-field-${pointer}/title`}>
                  <div>{t('shell.studio.formSteps.step')}</div>
                  {names !== undefined && label !== null ? (
                    <LabelTexts
                      pointer={`${pointer}/title`}
                      value={isContent(step) ? step.title : undefined}
                      onChange={onChange}
                    />
                  ) : (
                    <Input
                      aria-label={t('shell.studio.formSteps.step')}
                      value={textKeyOf(step)}
                      onChange={(event) => onChange(`${pointer}/title`, { textKey: event.target.value })}
                    />
                  )}
                </div>
                <div data-testid={`studio-field-${pointer}/sections`}>
                  <div>{t('shell.studio.formSteps.sections')}</div>
                  <Select
                    mode="multiple"
                    virtual={false}
                    aria-label={t('shell.studio.formSteps.sections')}
                    style={{ minWidth: 240 }}
                    value={chosen}
                    options={options}
                    onChange={(next: string[]) => onChange(`${pointer}/sections`, next)}
                  />
                </div>
                <Flex gap="small">
                  <Button disabled={index === 0} onClick={() => move(index, index - 1)}>
                    {t('shell.studio.formSteps.moveUp')}
                  </Button>
                  <Button disabled={index === list.length - 1} onClick={() => move(index, index + 1)}>
                    {t('shell.studio.formSteps.moveDown')}
                  </Button>
                  <Button onClick={() => remove(index)}>{t('shell.studio.formSteps.removeStep')}</Button>
                </Flex>
              </Flex>
            </Card>
          )
        })}
        <div>
          <Button onClick={add}>{t('shell.studio.formSteps.addStep')}</Button>
        </div>
      </Flex>
    </div>
  )
}
