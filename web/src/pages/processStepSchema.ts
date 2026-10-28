import type { JsonSchema } from '../platform/studioSchemas'
import { requiredStepKeys } from './processPalette'
import { setOwn } from '../platform/ownProperty'

type Content = Record<string, unknown>

/** The `then` of a conditional, whose properties may be `false`, which `JsonSchema` does not allow. */
interface Then {
  required?: string[]
  properties?: Record<string, unknown>
}

/** An `allOf` item of the step schema, which `JsonSchema` does not type: an `if` with its `then`. */
interface Conditional {
  if?: { properties?: Record<string, { const?: unknown }> }
  then?: Then
}

/** The `then` of the conditional of the step schema that tests `type` alone, or `undefined` when there is none. */
function typeConditional(schema: JsonSchema, type: string): Then | undefined {
  for (const item of (schema.$defs?.step?.allOf ?? []) as Conditional[]) {
    const tested = Object.keys(item.if?.properties ?? {})
    if (tested.length === 1 && tested[0] === 'type' && item.if?.properties?.type?.const === type) {
      return item.then
    }
  }
  return undefined
}

/** The step types of the process schema. */
export function stepTypes(schema: JsonSchema): string[] {
  return (schema.$defs?.step?.properties?.type?.enum ?? []).filter((type): type is string => typeof type === 'string')
}

/**
 * The keys the process schema forbids for a step type. Only the conditional that tests `type` alone counts: the ones
 * that test the operation as well depend on its name, and the operation panel handles those.
 */
export function forbiddenStepKeys(schema: JsonSchema, type: string): string[] {
  return Object.entries(typeConditional(schema, type)?.properties ?? {})
    .filter(([, forbidden]) => forbidden === false)
    .map(([key]) => key)
}

/** The keys the process schema requires for a step type. */
export function requiredByType(schema: JsonSchema, type: string): string[] {
  return typeConditional(schema, type)?.required ?? []
}

/** The step with its type changed: the schema's forbidden keys for the new type go and its required keys are added empty. */
export function withType(step: Content, type: string, schema: JsonSchema): Content {
  const forbidden = new Set(forbiddenStepKeys(schema, type))
  const next: Content = {}
  for (const [key, value] of Object.entries(step)) {
    if (!forbidden.has(key)) {
      setOwn(next, key, value)
    }
  }
  next.type = type
  for (const [key, initial] of Object.entries(structuredClone(requiredStepKeys[type] ?? {}))) {
    if (next[key] === undefined) {
      setOwn(next, key, initial)
    }
  }
  return next
}
