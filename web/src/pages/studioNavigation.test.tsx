/// <reference types="node" />
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { ShellNavigation, type ShellNavigationEntry, type ShellNavigationNode } from '../platform/ShellNavigation'
import type { ModuleResourceGroup, ModuleResourceKind, StudioResourceItem } from '../platform/studio'
import { studioActiveEntry, studioNavigation, needsActionFilter } from './studioNavigation'

type Node = ShellNavigationNode

const application = 'purchase-requests'
const reader = 'Linh Nguyen'
const t = (key: string) => (platformEnglish as Record<string, string>)[key] ?? key

function resource(id: string, kind: string): StudioResourceItem {
  return { id, kind, name: null, file: `${id}.json`, version: 'v', usedBy: 0 }
}

function moduleGroup(kind: ModuleResourceKind, ids: string[]): ModuleResourceGroup {
  return { kind, items: ids.map((id) => ({ id, kind, name: `shared/${id}`, module: 'shared' })) }
}

function navigationFor(folder: StudioResourceItem[], modules: ModuleResourceGroup[] = []): Node[] {
  return studioNavigation(application, folder, modules, t)
}

function flatten(nodes: readonly Node[]): ShellNavigationEntry[] {
  return nodes.flatMap((node) => {
    if ('children' in node) return [...node.children]
    return 'entries' in node ? [...node.entries] : [node]
  })
}

function groupOf(nodes: readonly Node[], key: string): ShellNavigationEntry[] {
  const group = nodes.find((node) => node.key === key)
  return group && 'entries' in group ? [...group.entries] : []
}

function childrenOf(nodes: readonly Node[], key: string): ShellNavigationEntry[] {
  const parent = nodes.find((node) => node.key === key)
  return parent && 'children' in parent ? [...parent.children] : []
}

function keysOf(entries: readonly Node[]): string[] {
  return entries.map((entry) => entry.key)
}

const fullFolder: StudioResourceItem[] = [
  resource('purchase-requests', 'application'),
  resource('request', 'entity'),
  resource('seed-data', 'seed'),
  resource('numbers', 'sequence'),
  resource('erp', 'dataSource'),
  resource('add-field', 'migration'),
  resource('home', 'site'),
  resource('request-page', 'page'),
  resource('request-form', 'form'),
  resource('approve', 'process'),
  resource('approval-rule', 'rule'),
  resource('on-submit', 'event'),
  resource('nightly', 'trigger'),
  resource('clerk', 'role'),
  resource('clerk-policy', 'policy'),
  resource('corp', 'identityProvider'),
  resource('crm', 'connector'),
  resource('api', 'endpoint'),
  resource('hook', 'webhook'),
  resource('en', 'text'),
  resource('smoke', 'scenario'),
  resource('leave-contract', 'contract'),
  resource('layer-one', 'layer'),
]

describe('studioNavigation', () => {
  it('lists Overview, Releases, History and Settings, then the groups in order, with Other last', () => {
    expect(keysOf(navigationFor(fullFolder))).toEqual([
      'overview',
      'releases',
      'history',
      'settings',
      'group:data',
      'group:pages',
      'group:logic',
      'group:access',
      'group:integration',
      'group:texts',
      'group:tests',
      'group:other',
    ])
  })

  it('lists the entries that concern the whole application in the order of the workspace doc', () => {
    // Vitest runs from the web folder, as `npm test --prefix web` does.
    const doc = readFileSync(join(process.cwd(), '../docs/reference/studio-workspace.md'), 'utf8').replace(/\s+/g, ' ')
    const sentence = /Under the row come (.+?), which concern/.exec(doc)
    expect(sentence).not.toBeNull()
    const documented = sentence![1].split(/, | and /)

    const built = studioNavigation(application, fullFolder, [], t, reader)
      .filter((node) => !('entries' in node))
      .map((node) => node.label)

    expect(built).toEqual(documented)
  })

  it('lists the kinds of each group in order, with the policy matrix ending Access and Packages ending Integration', () => {
    const nav = navigationFor(fullFolder)

    expect(keysOf(groupOf(nav, 'group:data'))).toEqual([
      'kind:entity',
      'kind:seed',
      'kind:sequence',
      'kind:dataSource',
      'kind:migration',
    ])
    expect(keysOf(groupOf(nav, 'group:pages'))).toEqual(['kind:site', 'kind:page', 'kind:form'])
    expect(keysOf(groupOf(nav, 'group:logic'))).toEqual(['kind:process', 'kind:rule', 'kind:event', 'kind:trigger'])
    expect(keysOf(groupOf(nav, 'group:access'))).toEqual([
      'kind:role',
      'kind:policy',
      'kind:identityProvider',
      'policies',
    ])
    expect(keysOf(groupOf(nav, 'group:integration'))).toEqual([
      'kind:connector',
      'kind:endpoint',
      'kind:webhook',
      'packages',
    ])
    expect(keysOf(groupOf(nav, 'group:texts'))).toEqual(['kind:text'])
    expect(keysOf(groupOf(nav, 'group:tests'))).toEqual(['kind:scenario'])
  })

  it('keeps the policy matrix as the only Access entry when the application has no role or policy', () => {
    const nav = navigationFor([resource('purchase-requests', 'application'), resource('request', 'entity')])

    expect(keysOf(groupOf(nav, 'group:access'))).toEqual(['policies'])
    expect(groupOf(nav, 'group:access')[0].label).toBe('Policy matrix')
  })

  it('keeps Packages as the only Integration entry when the application has no integration resource', () => {
    const nav = navigationFor([resource('purchase-requests', 'application'), resource('request', 'entity')])

    expect(keysOf(groupOf(nav, 'group:integration'))).toEqual(['packages'])
    expect(groupOf(nav, 'group:integration')[0].label).toBe('Packages')
  })

  it('leaves out a kind with no folder or module resource, and keeps a kind only a module has', () => {
    const nav = navigationFor(
      [resource('purchase-requests', 'application'), resource('request', 'entity')],
      [moduleGroup('process', ['approve'])],
    )

    expect(keysOf(groupOf(nav, 'group:data'))).toEqual(['kind:entity'])
    expect(keysOf(groupOf(nav, 'group:logic'))).toEqual(['kind:process'])
    expect(flatten(nav).map((entry) => entry.key)).not.toContain('kind:rule')
  })

  it('puts contract, layer and unknown kinds in Other, sorted, and never the manifest', () => {
    const nav = navigationFor([
      resource('purchase-requests', 'application'),
      resource('zeta-things', 'zeta'),
      resource('leave-contract', 'contract'),
      resource('alpha-things', 'alpha'),
      resource('layer-one', 'layer'),
    ])

    expect(keysOf(groupOf(nav, 'group:other'))).toEqual(['kind:alpha', 'kind:contract', 'kind:layer', 'kind:zeta'])
    expect(groupOf(nav, 'group:other').map((entry) => entry.label)).toEqual(['alpha', 'Contracts', 'Layers', 'zeta'])
    expect(flatten(nav).map((entry) => entry.key)).not.toContain('kind:application')
  })

  it('lists AI requests right after History for a reader, with All requests and Needs action', () => {
    const nav = studioNavigation(application, fullFolder, [], t, reader)

    expect(keysOf(nav).slice(0, 5)).toEqual(['overview', 'releases', 'history', 'requests', 'settings'])
    expect(nav.find((node) => node.key === 'requests')?.label).toBe('AI requests')
    expect(childrenOf(nav, 'requests').map((entry) => [entry.key, entry.label, entry.icon])).toEqual([
      ['requests:all', 'All requests', 'list'],
      ['requests:needsAction', 'Needs action', 'tasks'],
    ])
  })

  it('points All requests at the table and Needs action at the table filtered to the reader', () => {
    const [all, needs] = childrenOf(studioNavigation(application, fullFolder, [], t, reader), 'requests')
    const allParams = new URL(all.path, 'http://studio.test').searchParams
    const needsParams = new URL(needs.path, 'http://studio.test').searchParams

    expect(all.path).toBe('/studio?app=purchase-requests&view=requests')
    expect(needsParams.get('app')).toBe(application)
    expect(needsParams.get('view')).toBe('requests')
    expect(needsParams.get('filter')).toBe(JSON.stringify(needsActionFilter(reader)))
    expect(allParams.has('filter')).toBe(false)
  })

  it('shows no AI requests entry without a reader, so a person without studioAi sees none', () => {
    const nav = navigationFor(fullFolder)

    expect(nav.map((node) => node.key)).not.toContain('requests')
    expect(
      flatten(nav)
        .map((entry) => entry.key)
        .filter((key) => key.startsWith('requests')),
    ).toEqual([])
  })

  it('leaves Settings out when the folder has no manifest', () => {
    const nav = navigationFor([resource('request', 'entity')])

    expect(keysOf(nav)).toEqual(['overview', 'releases', 'history', 'group:data', 'group:access', 'group:integration'])
  })

  it('points each entry at its view, kind or manifest', () => {
    const entries = flatten(navigationFor(fullFolder))
    const pathOf = (key: string) => entries.find((entry) => entry.key === key)?.path

    expect(pathOf('overview')).toBe('/studio?app=purchase-requests')
    expect(pathOf('releases')).toBe('/studio?app=purchase-requests&view=releases')
    expect(pathOf('history')).toBe('/studio?app=purchase-requests&view=history')
    expect(pathOf('settings')).toBe('/studio?app=purchase-requests&resource=purchase-requests')
    expect(pathOf('kind:entity')).toBe('/studio?app=purchase-requests&kind=entity')
    expect(pathOf('policies')).toBe('/studio?app=purchase-requests&view=policies')
    expect(pathOf('packages')).toBe('/studio?app=purchase-requests&view=packages')
  })

  it('shows no entry with a digit in its text, and collapsed entries as icons with a tooltip', async () => {
    const entries = navigationFor(fullFolder)
    const { unmount } = render(
      <MemoryRouter>
        <ShellNavigation entries={entries} />
      </MemoryRouter>,
    )
    for (const item of screen.getAllByRole('menuitem')) {
      expect(item.textContent).not.toMatch(/\d/)
    }
    unmount()

    render(
      <MemoryRouter>
        <ShellNavigation entries={entries} collapsed />
      </MemoryRouter>,
    )
    for (const item of screen.getAllByRole('menuitem')) {
      expect(item.querySelector('.anticon[aria-hidden="true"]')).not.toBeNull()
    }
    await userEvent.hover(screen.getByRole('menuitem', { name: 'Entities' }))
    await waitFor(() =>
      expect(screen.getAllByRole('tooltip').map((tooltip) => tooltip.textContent)).toContain('Entities'),
    )
  })
})

describe('studioActiveEntry', () => {
  const moduleItems = moduleGroup('process', ['approve']).items

  it('marks the kind of an open resource, ignoring letter case', () => {
    const active = studioActiveEntry(
      new URLSearchParams(`app=${application}&resource=Request`),
      [resource('request', 'entity')],
      [],
    )

    expect(active).toBe('kind:entity')
  })

  it('marks the kind of an open module item', () => {
    const active = studioActiveEntry(new URLSearchParams(`app=${application}&resource=Approve`), [], moduleItems)

    expect(active).toBe('kind:process')
  })

  it('marks Settings for the manifest', () => {
    const active = studioActiveEntry(
      new URLSearchParams(`app=${application}&resource=purchase-requests`),
      [resource('purchase-requests', 'application')],
      [],
    )

    expect(active).toBe('settings')
  })

  it('marks the kind of a kind list', () => {
    expect(studioActiveEntry(new URLSearchParams(`app=${application}&kind=page`), [], [])).toBe('kind:page')
  })

  it('marks Releases, History and the policy matrix from the view', () => {
    expect(studioActiveEntry(new URLSearchParams(`app=${application}&view=releases`), [], [])).toBe('releases')
    expect(studioActiveEntry(new URLSearchParams(`app=${application}&view=release&release=3`), [], [])).toBe('releases')
    expect(studioActiveEntry(new URLSearchParams(`app=${application}&view=history`), [], [])).toBe('history')
    expect(studioActiveEntry(new URLSearchParams(`app=${application}&view=history&revision=abc`), [], [])).toBe(
      'history',
    )
    expect(studioActiveEntry(new URLSearchParams(`app=${application}&view=policies`), [], [])).toBe('policies')
  })

  it('marks Packages for the list and for a package page', () => {
    expect(studioActiveEntry(new URLSearchParams(`app=${application}&view=packages`), [], [])).toBe('packages')
    expect(studioActiveEntry(new URLSearchParams(`app=${application}&view=packages&package=tools`), [], [])).toBe(
      'packages',
    )
  })

  it('marks no entry on the publish page', () => {
    expect(studioActiveEntry(new URLSearchParams(`app=${application}&view=publish`), [], [])).toBe('')
  })

  it('marks Overview otherwise', () => {
    expect(studioActiveEntry(new URLSearchParams(`app=${application}`), [], [])).toBe('overview')
  })

  it('marks All requests for the AI requests table with no filter, and for a request page, not Overview', () => {
    expect(studioActiveEntry(new URLSearchParams(`app=${application}&view=requests`), [], [], reader)).toBe(
      'requests:all',
    )
    expect(studioActiveEntry(new URLSearchParams(`app=${application}&view=requests&request=r1`), [], [], reader)).toBe(
      'requests:all',
    )
  })

  describe('Needs action', () => {
    const viewOf = (extra: Record<string, string>, filter?: unknown) =>
      new URLSearchParams({
        app: application,
        view: 'requests',
        ...extra,
        ...(filter === undefined ? {} : { filter: JSON.stringify(filter) }),
      })
    const active = (params: URLSearchParams, name: string | null = reader) => studioActiveEntry(params, [], [], name)

    it('marks Needs action for exactly its filter, and search, sort and paging do not count', () => {
      expect(active(viewOf({}, needsActionFilter(reader)))).toBe('requests:needsAction')
      expect(active(viewOf({ search: 'invoice', page: '2', pageSize: '50' }, needsActionFilter(reader)))).toBe(
        'requests:needsAction',
      )
    })

    it('reads the filter as the table reads it, whatever the order of its keys', () => {
      const reordered = new URLSearchParams(
        `app=${application}&view=requests&filter=${encodeURIComponent(
          JSON.stringify({
            all: [
              { op: 'eq', value: reader, field: 'author' },
              {
                any: [
                  { value: 'awaitingApproval', op: 'eq', field: 'state' },
                  { value: 'ready', field: 'state', op: 'eq' },
                ],
              },
            ],
          }),
        )}`,
      )

      expect(active(reordered)).toBe('requests:needsAction')
    })

    it('marks All requests for no filter, a changed filter, another reader and an extra condition', () => {
      expect(active(viewOf({}))).toBe('requests:all')
      expect(active(viewOf({}, { all: [{ field: 'state', op: 'eq', value: 'ready' }] }))).toBe('requests:all')
      expect(active(viewOf({}, needsActionFilter('Minh Tran')))).toBe('requests:all')
      const withTitle = {
        all: [
          { field: 'author', op: 'eq', value: reader },
          {
            any: [
              { field: 'state', op: 'eq', value: 'awaitingApproval' },
              { field: 'state', op: 'eq', value: 'ready' },
            ],
          },
          { field: 'title', op: 'contains', value: 'invoice' },
        ],
      }
      expect(active(viewOf({}, withTitle))).toBe('requests:all')
    })

    it('marks All requests on a request page, even with the filter in the address', () => {
      expect(active(viewOf({ request: 'r1' }, needsActionFilter(reader)))).toBe('requests:all')
    })

    it('marks All requests with no reader, when there is no Needs action entry to mark', () => {
      expect(active(viewOf({}, needsActionFilter(reader)), null)).toBe('requests:all')
    })
  })
})
