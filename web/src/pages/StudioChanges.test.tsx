import { ConfigProvider } from 'antd'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { StudioRevisionChange } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioChanges, type StudioChangeFile } from './StudioChanges'

const texts = {
  'shell.studio.history.before': 'Before',
  'shell.studio.history.after': 'After',
  'shell.studio.overview.change.added': 'Added',
  'shell.studio.overview.change.changed': 'Changed',
  'shell.studio.overview.change.removed': 'Removed',
  'shell.studio.nav.kindOne.role': 'Role',
  'shell.studio.module.added': 'Added',
  'shell.studio.module.changed': 'Changed',
  'shell.studio.module.removed': 'Removed',
  'shell.studio.contract.addedName': '{name}, added by this application',
  'shell.studio.module.changedName': '{name}, changed by this application',
  'shell.studio.module.removedName': '{name}, removed by this application',
  'shell.studio.changes.showDiff': 'Show the diff',
  'shell.studio.changes.hideDiff': 'Hide the diff',
  'shell.studio.changes.showDiffName': 'Show the diff of {name}',
  'shell.studio.changes.hideDiffName': 'Hide the diff of {name}',
}
const catalogs = [{ texts, fallbackTexts: texts }]

const files: StudioRevisionChange[] = [
  {
    file: 'roles/approver.json',
    change: 'added',
    id: null,
    kind: 'role',
    name: 'Approver',
    before: null,
    after: '{"name":"Approver"}',
  },
  {
    file: 'roles/buyer.json',
    change: 'changed',
    id: null,
    kind: 'role',
    name: 'Buyer',
    before: '{"label":"first"}',
    after: '{"label":"second"}',
  },
  {
    file: 'notes/old.json',
    change: 'removed',
    id: null,
    kind: null,
    name: 'Old',
    before: '{"gone":true}',
    after: null,
  },
]

function renderChanges(changed: StudioChangeFile[] = files, options: { folded?: boolean } = {}) {
  return render(
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <StudioChanges files={changed} resources={[]} folded={options.folded} />
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
}

/** A layer file that adds `supplier`, changes `amount` and removes `notes` of the module's entity. */
const layerFile: StudioChangeFile = {
  file: 'customizations/purchase-request.json',
  change: 'changed',
  id: 'layer-1',
  kind: null,
  name: null,
  before: '{"target":"modules/purchasing/entities/request.json"}',
  after: '{"target":"modules/purchasing/entities/request.json"}',
  module: {
    id: 'entity-1',
    kind: 'entity',
    name: 'PurchaseRequest',
    file: 'modules/purchasing/entities/request.json',
    before: '{"name":"PurchaseRequest","fields":[{"name":"amount"},{"name":"notes"}]}',
    after: '{"name":"PurchaseRequest","fields":[{"name":"amount","type":"number"},{"name":"supplier"}]}',
    marks: [
      { path: '/fields/1', mark: 'added' },
      { path: '/fields/0', mark: 'changed' },
    ],
    removed: [{ list: '/fields', key: 'notes', content: null }],
  },
}

function fileSection(file: string): HTMLElement {
  const section = screen.getAllByTestId('studio-changes-file').find((candidate) => candidate.dataset.file === file)
  if (!section) {
    throw new Error(`No section for ${file}`)
  }
  return section
}

describe('StudioChanges', () => {
  it('shows each file with its name, kind, change and its JSON before and after', () => {
    renderChanges()

    expect(screen.getAllByTestId('studio-changes-file')).toHaveLength(3)

    const added = within(fileSection('roles/approver.json'))
    expect(added.getByText('Approver')).toBeVisible()
    expect(added.getByText('Role')).toBeVisible()
    expect(added.getByText('Added')).toBeVisible()
    expect(added.getByTestId('json-diff-before')).toHaveTextContent('—')
    expect(added.getByTestId('json-diff-after')).toHaveTextContent('"name": "Approver"')

    const changed = within(fileSection('roles/buyer.json'))
    expect(changed.getByText('Buyer')).toBeVisible()
    expect(changed.getByText('Role')).toBeVisible()
    expect(changed.getByText('Changed')).toBeVisible()
    expect(changed.getByTestId('json-diff-before')).toHaveTextContent('"label": "first"')
    expect(changed.getByTestId('json-diff-after')).toHaveTextContent('"label": "second"')

    const removed = within(fileSection('notes/old.json'))
    expect(removed.getByText('Removed')).toBeVisible()
    expect(removed.getByTestId('json-diff-before')).toHaveTextContent('"gone": true')
    expect(removed.getByTestId('json-diff-after')).toHaveTextContent('—')
  })

  it('shows the em dash as the kind of a file that has none', () => {
    renderChanges()

    const removed = fileSection('notes/old.json')
    expect(removed).toHaveTextContent(/^.*—.*Removed/)
    expect(within(removed).getByText('Before')).toBeVisible()
    expect(within(removed).getByText('After')).toBeVisible()
  })
})

describe('StudioChanges folded', () => {
  it('starts each file closed, and opens its JSON when Show the diff is used', async () => {
    renderChanges(files, { folded: true })

    const removed = fileSection('notes/old.json')
    expect(within(removed).queryByTestId('json-diff-before')).not.toBeInTheDocument()
    const show = within(removed).getByRole('button', { name: 'Show the diff of notes/old.json', expanded: false })
    await userEvent.click(show)
    expect(within(removed).getByTestId('json-diff-before')).toHaveTextContent('"gone": true')
    expect(
      within(removed).getByRole('button', { name: 'Hide the diff of notes/old.json', expanded: true }),
    ).toBeVisible()
  })

  it('shows the module resource a layer file changes, named by it, with its marks and no reset', async () => {
    renderChanges([layerFile], { folded: true })

    const section = fileSection(layerFile.file)
    expect(within(section).getByText('PurchaseRequest')).toBeVisible()
    expect(within(section).getByRole('note', { name: 'supplier, added by this application' })).toHaveTextContent(
      'Added',
    )
    expect(within(section).getByRole('note', { name: 'amount, changed by this application' })).toHaveTextContent(
      'Changed',
    )
    expect(within(section).getByRole('note', { name: 'notes, removed by this application' })).toHaveTextContent(
      'Removed',
    )
    expect(within(section).queryByRole('button', { name: /Reset/ })).not.toBeInTheDocument()

    await userEvent.click(within(section).getByRole('button', { name: 'Show the diff of PurchaseRequest' }))
    expect(within(section).getByTestId('json-diff-after')).toHaveTextContent('"supplier"')
    expect(within(section).getByTestId('json-diff-after')).not.toHaveTextContent('"target"')
  })
})
