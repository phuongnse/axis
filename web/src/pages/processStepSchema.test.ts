import { describe, expect, it } from 'vitest'
import raw from '../../../src/Axis.Configuration/Schemas/process.schema.json'
import type { JsonSchema } from '../platform/studioSchemas'
import { requiredStepKeys } from './processPalette'
import { forbiddenStepKeys, requiredByType, stepTypes, withType } from './processStepSchema'

const schema = raw as unknown as JsonSchema

interface Conditional {
  if: { properties: Record<string, unknown> }
  then: { required?: string[]; properties?: Record<string, unknown> }
}

const types = raw.$defs.step.properties.type.enum
const stepKeys = Object.keys(raw.$defs.step.properties)

/** The conditional of the raw schema that tests `type` alone, read without the module under test. */
function conditionalOf(type: string): Conditional['then'] {
  const found = (raw.$defs.step.allOf as Conditional[]).find(
    (item) =>
      Object.keys(item.if.properties).join() === 'type' &&
      (item.if.properties.type as { const: string }).const === type,
  )
  expect(found, `a conditional for ${type}`).toBeDefined()
  return found!.then
}

describe('stepTypes', () => {
  it('lists the enum of the step type, including timer and subProcess', () => {
    expect(stepTypes(schema)).toEqual(types)
    expect(types).toEqual(expect.arrayContaining(['timer', 'subProcess']))
  })

  it('is empty for a schema without step types', () => {
    expect(stepTypes({})).toEqual([])
  })
})

describe.each(types)('a step changed to %s', (type) => {
  const forbidden = Object.entries(conditionalOf(type).properties ?? {})
    .filter(([, value]) => value === false)
    .map(([key]) => key)

  it('reads the forbidden and required keys from the schema', () => {
    expect(forbiddenStepKeys(schema, type).sort()).toEqual([...forbidden].sort())
    expect(requiredByType(schema, type)).toEqual(conditionalOf(type).required ?? [])
  })

  it('loses exactly the forbidden keys and gains the required keys, empty', () => {
    const step = { ...Object.fromEntries(stepKeys.map((key) => [key, `value of ${key}`])), hint: 1 }

    const changed = withType(step, type, schema)

    const expected = new Set([
      ...Object.keys(step).filter((key) => !forbidden.includes(key)),
      ...Object.keys(requiredStepKeys[type]),
    ])
    expect(new Set(Object.keys(changed))).toEqual(expected)
    expect(changed.type).toBe(type)
    expect(changed.hint).toBe(1)
    for (const key of forbidden) {
      expect(changed).not.toHaveProperty(key)
    }
  })

  it('adds a required key that the step lacks, with the empty value of the palette', () => {
    const changed = withType({ name: 'a', type: 'end' }, type, schema)

    expect(changed).toEqual({ name: 'a', ...structuredClone(requiredStepKeys[type]), type })
  })

  it('has the keys the schema requires among the keys the palette starts with', () => {
    for (const key of requiredByType(schema, type)) {
      expect(Object.keys(requiredStepKeys[type])).toContain(key)
    }
  })
})
