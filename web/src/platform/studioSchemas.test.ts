import { describe, expect, it } from 'vitest'
import { loadKindSchema, type JsonSchema } from './studioSchemas'

/** The node at a JSON Pointer such as `/$defs/field/properties/expression`. */
function nodeAt(root: JsonSchema, pointer: string): JsonSchema {
  let node: unknown = root
  for (const segment of pointer.split('/').slice(1)) {
    node = (node as Record<string, unknown> | undefined)?.[segment]
  }
  if (typeof node !== 'object' || node === null) {
    throw new Error(`The schema has no node at ${pointer}.`)
  }
  return node as JsonSchema
}

const expressionNodes: [kind: string, pointer: string][] = [
  ['entity', '/$defs/validation/properties/expression'],
  ['entity', '/$defs/field/properties/expression'],
  ['entity', '/$defs/field/properties/default'],
  ['dataSource', '/properties/filter'],
  ['dataSource', '/$defs/measure/properties/filter'],
  ['rule', '/properties/expression'],
  ['trigger', '/properties/when'],
  ['policy', '/$defs/grant/properties/filter'],
  ['migration', '/$defs/change/properties/value'],
  ['process', '/$defs/startCondition/properties/expression'],
  ['process', '/$defs/branch/properties/when'],
  ['process', '/$defs/assignee/properties/user'],
  ['process', '/$defs/step/properties/set/additionalProperties'],
  ['process', '/$defs/step/properties/input/additionalProperties'],
  ['process', '/$defs/step/properties/fields/additionalProperties'],
  ['endpoint', '/properties/values/additionalProperties'],
  ['page', '/$defs/navigateParameters/additionalProperties'],
  ['page', '/$defs/action/properties/record'],
  ['page', '/$defs/rowAction/properties/visibleWhen'],
  ['page', '/$defs/rowAction/properties/record'],
  ['page', '/$defs/navigateAction/properties/record'],
]

describe('the x-axis-expression keyword', () => {
  it.each(expressionNodes)('marks the expression of a %s at %s', async (kind, pointer) => {
    const schema = await loadKindSchema(kind)
    expect(nodeAt(schema!, pointer)['x-axis-expression']).toBe(true)
  })

  it('does not mark the output values of a process step, which hold field names', async () => {
    const schema = await loadKindSchema('process')
    expect(nodeAt(schema!, '/$defs/step/properties/output/additionalProperties')['x-axis-expression']).toBeUndefined()
  })
})
