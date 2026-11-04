/**
 * The kinds of resource a module may hold, in the order Studio lists them. It mirrors `ModuleChecker._allowedKinds`
 * in the compiler, and `moduleKinds.test.ts` fails when the two differ.
 */
export const moduleKinds = [
  'entity',
  'dataSource',
  'rule',
  'sequence',
  'form',
  'page',
  'process',
  'event',
  'text',
  'seed',
  'policy',
  'trigger',
  'scenario',
  'contract',
] as const

export type ModuleKind = (typeof moduleKinds)[number]
