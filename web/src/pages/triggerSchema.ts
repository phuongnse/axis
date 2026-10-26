import type { JsonSchema } from '../platform/studioSchemas'

/** The keys of `parameters` name a parameter of the trigger's process. */
const processParameterKeys: JsonSchema = {
  type: 'string',
  'x-axis-reference': { part: 'processParameter', of: '/process' },
}

/**
 * The trigger schema for a draft. The `parameters` of a scheduled job without a data source, and of a data change
 * trigger that holds `delete`, take their keys from the process's parameters. A scheduled job gives each a literal
 * of the parameter's type, and a delete trigger gives each an expression over the deleted record. A delete trigger
 * does not offer `fields`, unless the file already has the list. Any other draft gets the schema as it is.
 */
export function triggerSchema(schema: JsonSchema, draft: Record<string, unknown>): JsonSchema {
  const parameters = schema.properties?.parameters
  if (parameters === undefined) {
    return schema
  }
  if (draft.type === 'dataChange' && Array.isArray(draft.on) && draft.on.includes('delete')) {
    const properties = Object.entries(schema.properties ?? {}).flatMap(([name, property]): [string, JsonSchema][] => {
      if (name === 'fields' && draft.fields === undefined) {
        return []
      }
      return name === 'parameters'
        ? [
            [
              name,
              {
                ...property,
                propertyNames: processParameterKeys,
                additionalProperties: { type: 'string', 'x-axis-expression': true },
              },
            ],
          ]
        : [[name, property]]
    })
    return { ...schema, properties: Object.fromEntries(properties) }
  }
  if (draft.type === 'schedule' && draft.dataSource === undefined) {
    return {
      ...schema,
      properties: { ...schema.properties, parameters: { ...parameters, propertyNames: processParameterKeys } },
    }
  }
  return schema
}
