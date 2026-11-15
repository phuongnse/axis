/// <reference types="node" />
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import {
  ShellNavigation,
  type ShellNavigationEntry,
  type ShellNavigationNode,
  type ShellNavigationParent,
} from '../platform/ShellNavigation'
import type { ModuleResourceGroup, ModuleResourceKind, StudioResourceItem } from '../platform/studio'
import { needsActionAddress } from './authoringRequests'
import { studioActiveEntry, studioNavigation } from './studioNavigation'

type Node = ShellNavigationNode

const application = 'purchase-requests'
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
  return nodes.flatMap((node) => ('entries' in node ? node.entries : 'children' in node ? node.children : [node]))
}

function groupOf(nodes: readonly Node[], key: string): ShellNavigationEntry[] {
  const group = nodes.find((node) => node.key === key)
  return group && 'entries' in group ? [...group.entries] : []
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

    const built = navigationFor(fullFolder)
      .filter((node): node is ShellNavigationEntry => !('entries' in node))
      .map((entry) => entry.label)

    expect(built).toEqual(documented)
  })

  it('lists the kinds of each group in order, with the policy matrix ending Access', () => {
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
    expect(keysOf(groupOf(nav, 'group:integration'))).toEqual(['kind:connector', 'kind:endpoint', 'kind:webhook'])
    expect(keysOf(groupOf(nav, 'group:texts'))).toEqual(['kind:text'])
    expect(keysOf(groupOf(nav, 'group:tests'))).toEqual(['kind:scenario'])
  })

  it('keeps the policy matrix as the only Access entry when the application has no role or policy', () => {
    const nav = navigationFor([resource('purchase-requests', 'application'), resource('request', 'entity')])

    expect(keysOf(groupOf(nav, 'group:access'))).toEqual(['policies'])
    expect(groupOf(nav, 'group:access')[0].label).toBe('Policy matrix')
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

  it('leaves Settings out when the folder has no manifest', () => {
    const nav = navigationFor([resource('request', 'entity')])

    expect(keysOf(nav)).toEqual(['overview', 'releases', 'history', 'group:data', 'group:access'])
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

// Proposed: AI requests is a group of the table's views, see the review on #3263.
// Proposed: AI requests opens to the views of its table, see the review on #3263.
describe('the AI requests entry', () => {
  it('follows History and opens to the whole list and Needs action, for a person who may ask AI', () => {
    const nav = studioNavigation(application, [resource('request', 'entity')], [], t, true, 'u-1')
    const keys = nav.map((node) => node.key)
    expect(keys.indexOf('group:requests')).toBe(keys.indexOf('history') + 1)
    const requests = nav.find((node) => node.key === 'group:requests') as ShellNavigationParent

    expect(requests.label).toBe('AI requests')
    expect(requests.children.map((entry) => entry.label)).toEqual(['All requests', 'Needs action'])
    expect(requests.children[0].path).toBe(`/studio?app=${application}&view=requests`)
    expect(requests.children[1].path).toBe(needsActionAddress(application, 'u-1'))
  })

  it('opens by itself while one of its views is the page, and opens and folds on a click', async () => {
    const nav = studioNavigation(application, [], [], t, true, 'u-1')
    render(
      <MemoryRouter>
        <ShellNavigation entries={nav} activeKey="requests:needsAction" />
      </MemoryRouter>,
    )

    const parent = screen.getByRole('menuitem', { name: 'AI requests' })
    expect(parent).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('link', { name: 'Needs action' })).toHaveAttribute('aria-current', 'page')
    await userEvent.click(parent)
    expect(parent).toHaveAttribute('aria-expanded', 'false')
  })

  it('marks Needs action while the table shows exactly its filter, and the whole list otherwise', () => {
    const needsAction = new URLSearchParams(needsActionAddress(application, 'u-1').split('?')[1])
    expect(studioActiveEntry(needsAction, [], [], 'u-1')).toBe('requests:needsAction')

    needsAction.set('filter', JSON.stringify({ all: [{ field: 'who', op: 'eq', value: 'u-1' }] }))
    expect(studioActiveEntry(needsAction, [], [], 'u-1')).toBe('requests')
    expect(studioActiveEntry(new URLSearchParams(`app=${application}&view=requests`), [], [], 'u-1')).toBe('requests')
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

  it('marks Overview otherwise', () => {
    expect(studioActiveEntry(new URLSearchParams(`app=${application}`), [], [])).toBe('overview')
  })
})
