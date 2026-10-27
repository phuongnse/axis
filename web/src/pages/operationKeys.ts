type Content = Record<string, unknown>

export const builtInOperations = ['updateRecord', 'callConnector', 'raiseEvent', 'createRecord', 'notify']

export const packageChoice = 'package'

/** Every key that an operation step can have besides its name, type and `next`. */
const operationKeys = [
  'set',
  'connector',
  'input',
  'output',
  'retry',
  'event',
  'entity',
  'fields',
  'to',
  'channels',
  'message',
  'open',
]

export const forbiddenByOperation: Record<string, string[]> = {
  updateRecord: [
    'connector',
    'input',
    'output',
    'retry',
    'event',
    'entity',
    'fields',
    'to',
    'channels',
    'message',
    'open',
  ],
  callConnector: ['set', 'event', 'entity', 'fields', 'to', 'channels', 'message', 'open'],
  raiseEvent: ['set', 'connector', 'input', 'output', 'retry', 'entity', 'to', 'channels', 'message', 'open'],
  createRecord: ['connector', 'input', 'output', 'retry', 'event', 'fields', 'to', 'channels', 'message', 'open'],
  notify: ['set', 'connector', 'input', 'output', 'retry', 'event', 'entity', 'fields'],
  [packageChoice]: ['set', 'connector', 'event', 'entity', 'fields', 'to', 'channels', 'message', 'open'],
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

/** The keys an operation forbids, from the conditionals of the process schema. A package operation contains a slash. */
export function forbiddenOperationKeys(operation: string): string[] {
  if (builtInOperations.includes(operation)) {
    return forbiddenByOperation[operation]
  }
  return operation.includes('/') ? forbiddenByOperation[packageChoice] : []
}

export function without(step: Content, keys: string[]): Content {
  const forbidden = new Set(keys)
  return Object.fromEntries(Object.entries(step).filter(([key]) => !forbidden.has(key)))
}

/**
 * The step with its operation changed: the keys the new operation forbids go and nothing is added. Every other key,
 * including unknown keys, stays. An operation the schema has no rule for removes nothing.
 */
export function withOperation(step: Content, operation: string): Content {
  return { ...without(step, forbiddenOperationKeys(operation)), operation }
}

/** The keys of an operation step that the panel does not show: those its operation forbids, or all for an unknown one. */
export function operationJsonOnlyKeys(step: Content): string[] {
  const operation = text(step.operation)
  const known = builtInOperations.includes(operation) || operation.includes('/')
  const forbidden = known ? forbiddenOperationKeys(operation) : operationKeys
  return forbidden.filter((key) => step[key] !== undefined)
}
