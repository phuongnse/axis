// The order of the design-system cards, in the groups that D96 agreed and then the Shell group. It has no import,
// so scripts/design-system-header.mjs can load it with Node and write it as the bundle's first line.

/** The cards of the Design System artifact: each group's name and its previews, by their key in `previews`. */
export const cardGroups: readonly { name: string; previews: readonly string[] }[] = [
  {
    name: 'Field inputs',
    previews: [
      'field-text',
      'field-integer',
      'field-decimal',
      'field-boolean',
      'field-date',
      'field-date-time',
      'field-enum',
      'field-reference',
      'field-child-collection',
    ],
  },
  { name: 'Data widgets', previews: ['table', 'form', 'detail'] },
  { name: 'Charts and KPIs', previews: ['chart-bar', 'chart-line', 'chart-pie', 'kpi'] },
  { name: 'Process widgets', previews: ['taskInbox', 'processInspector'] },
  { name: 'Containers', previews: ['columns', 'tabs', 'section'] },
  { name: 'Shared states', previews: ['states'] },
  { name: 'Shell', previews: ['shell', 'signIn', 'identitySignIn'] },
]
