import type { ValidateFunction } from 'ajv'

/**
 * What a string property names: a resource kind, or a part of the resource named by `of`, a JSON Pointer or a
 * Relative JSON Pointer from the property.
 */
export type ReferenceMark =
  | string
  | {
      part: 'field' | 'step' | 'parameter' | 'processParameter' | 'column' | 'outcome' | 'operation'
      of: string
    }

/** The part of JSON Schema that Studio's form reads. Other keywords are ignored. */
export interface JsonSchema {
  type?: 'string' | 'integer' | 'number' | 'boolean' | 'object' | 'array'
  'x-axis-reference'?: ReferenceMark
  properties?: Record<string, JsonSchema>
  required?: string[]
  items?: JsonSchema
  enum?: unknown[]
  const?: unknown
  $ref?: string
  $defs?: Record<string, JsonSchema>
  oneOf?: JsonSchema[]
  anyOf?: JsonSchema[]
  allOf?: JsonSchema[]
  additionalProperties?: boolean | JsonSchema
  /** The schema of a map's keys. A mark on it makes the keys names of a part. */
  propertyNames?: JsonSchema
  minimum?: number
  maximum?: number
  /** Marks a string that holds an expression, which Studio shows in the expression editor. */
  'x-axis-expression'?: boolean
}

// These are the files the server embeds, so the form and the server's compile cannot drift apart.
// Each schema is its own chunk, loaded when a resource of that kind is selected.
const loaders = import.meta.glob<JsonSchema>('../../../src/Axis.Configuration/Schemas/*.schema.json', {
  import: 'default',
})

const loaderByKind = new Map(
  Object.entries(loaders).map(([file, load]) => [
    file.slice(file.lastIndexOf('/') + 1).replace('.schema.json', ''),
    load,
  ]),
)

// The validators are generated when the SPA is built, see web/schemaValidators.ts. Only the JSON view loads them.
const validatorLoaders = import.meta.glob<ValidateFunction>('../../../src/Axis.Configuration/Schemas/*.schema.json', {
  query: '?validator',
  import: 'default',
})

const validatorByKind = new Map(
  Object.entries(validatorLoaders).map(([file, load]) => [
    file.slice(file.lastIndexOf('/') + 1).replace(/\.schema\.json.*$/, ''),
    load,
  ]),
)

/** Loads the JSON Schema of a resource kind, such as `entity` or `dataSource`. It is `undefined` for a kind without one. */
export async function loadKindSchema(kind: string): Promise<JsonSchema | undefined> {
  return loaderByKind.get(kind)?.()
}

/** Loads the validator of a resource kind's JSON Schema. It is `undefined` for a kind without one. */
export async function loadKindValidator(kind: string): Promise<ValidateFunction | undefined> {
  return validatorByKind.get(kind)?.()
}

/** Follows a local `#/$defs/<name>` reference. A node without a reference, or with one it cannot find, comes back as it is. */
export function resolveSchema(node: JsonSchema, root: JsonSchema): JsonSchema {
  let current = node
  for (let depth = 0; current.$ref !== undefined && depth < 16; depth++) {
    const match = /^#\/\$defs\/(.+)$/.exec(current.$ref)
    const target = match ? root.$defs?.[match[1]] : undefined
    if (target === undefined) {
      return current
    }
    current = target
  }
  return current
}
