/** A state of a contract's lifecycle with the named points that exist for it. */
export interface ContractState {
  name: string
  initial: boolean
  deletable: boolean
  points: string[]
}

/** A transition of a contract's lifecycle with its guard, or `null` when it has none, and its named point. */
export interface ContractTransition {
  name: string
  from: string[]
  to: string
  guard: string | null
  point: string
}

/** One arrow of the state diagram: a transition from one state to its target. */
export interface ContractEdge {
  id: string
  name: string
  from: string
  to: string
  guard: boolean
}

export interface ContractValidation {
  name: string
  field: string
  expression: string
  point: string
}

export interface ContractEffect {
  name: string
  on: string
  entity: string
  point: string
}

/** A ceiling entry. `slot` is a role slot, or `others` for the roles mapped to no slot. */
export interface ContractCeilingRow {
  slot: string
  actions: string[]
  fields: string[]
  filter: string
  point: string
}

export interface ContractView {
  entity: string
  states: ContractState[]
  transitions: ContractTransition[]
  edges: ContractEdge[]
  validations: ContractValidation[]
  effects: ContractEffect[]
  ceiling: ContractCeilingRow[]
}

function record(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : {}
}

function list(value: unknown): Record<string, unknown>[] {
  return Array.isArray(value) ? value.map(record) : []
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

function strings(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : []
}

function ceilingRows(entity: string, slot: string, entries: unknown): ContractCeilingRow[] {
  return list(entries).map((entry) => ({
    slot,
    actions: strings(entry.actions),
    fields: strings(entry.fields),
    filter: text(entry.filter),
    point: `${entity}/ceiling/${slot}`,
  }))
}

/**
 * What a module contract file says, as the contract view shows it. A missing or odd value is left out or empty. A state
 * lists the named points that exist: `…/initial` only when it is not initial, and `…/deletable` only when it is not
 * deletable, as the compiler's points are. `edges` holds one arrow for each pair of a `from` state and the `to` state.
 */
export function contractView(content: Record<string, unknown>): ContractView {
  const entity = text(content.entity)
  const lifecycle = record(content.lifecycle)
  const states = list(lifecycle.states).map((state): ContractState => {
    const name = text(state.name)
    const initial = state.initial === true
    const deletable = state.deletable === true
    return {
      name,
      initial,
      deletable,
      points: [
        ...(initial ? [] : [`${entity}/states/${name}/initial`]),
        ...(deletable ? [] : [`${entity}/states/${name}/deletable`]),
      ],
    }
  })
  const transitions = list(lifecycle.transitions).map((transition): ContractTransition => ({
    name: text(transition.name),
    from: strings(transition.from),
    to: text(transition.to),
    guard: typeof transition.guard === 'string' ? transition.guard : null,
    point: `${entity}/transitions/${text(transition.name)}`,
  }))
  const ceiling = record(content.ceiling)
  return {
    entity,
    states,
    transitions,
    edges: transitions.flatMap((transition) =>
      transition.from.map((from) => ({
        id: `${transition.name}:${from}`,
        name: transition.name,
        from,
        to: transition.to,
        guard: transition.guard !== null,
      })),
    ),
    validations: list(content.validations).map((validation) => ({
      name: text(validation.name),
      field: text(validation.field),
      expression: text(validation.expression),
      point: `${entity}/validations/${text(validation.name)}`,
    })),
    effects: list(content.effects).map((effect) => ({
      name: text(effect.name),
      on: text(effect.on),
      entity: text(record(effect.create).entity),
      point: `${entity}/effects/${text(effect.name)}`,
    })),
    ceiling: [
      ...Object.entries(record(ceiling.slots)).flatMap(([slot, entries]) => ceilingRows(entity, slot, entries)),
      ...ceilingRows(entity, 'others', ceiling.others),
    ],
  }
}
