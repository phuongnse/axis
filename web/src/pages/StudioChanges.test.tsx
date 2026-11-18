import { ConfigProvider } from 'antd'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import {
  changeAuthors,
  type StudioChangeAuthor,
  type StudioLiveRevision,
  type StudioRevisionChange,
} from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioChanges, type StudioChangedFile } from './StudioChanges'

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
  'shell.studio.releases.system': 'System',
  'shell.studio.changes.changedBy': 'Changed by {authors}',
  'shell.studio.changes.you': 'you',
  'shell.studio.changes.aiRequest': 'the AI request “{name}”',
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

/** A layer file that adds `supplier`, changes `amount` and removes `notes` of the module's entity. */
const layerFile: StudioChangedFile = {
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

/** A live diff's file: its fields with the revisions that changed it since the live release, newest first. */
type LiveFile = StudioChangedFile & { by: StudioLiveRevision[] }

function renderWith<File extends StudioChangedFile>(
  changed: File[],
  changedBy?: { application: string; authors: (file: File) => StudioChangeAuthor[] },
  folded = false,
) {
  return render(
    <MemoryRouter>
      <ThemeModeProvider initialMode="light">
        <ConfigProvider theme={{ token: { motion: false } }}>
          <TextProvider catalogs={catalogs} development={false}>
            <StudioChanges files={changed} resources={[]} changedBy={changedBy} folded={folded} />
          </TextProvider>
        </ConfigProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
}

function renderChanges(changed: StudioChangedFile[] = files, options: { folded?: boolean } = {}) {
  return renderWith(changed, undefined, options.folded)
}

/** A revision of a live diff's `by`: the author's id, or null for a revision the server made, and an AI request, if any. */
function revision(
  id: string,
  userId: string | null,
  author: string,
  authoringRequest: string | null = null,
): StudioLiveRevision {
  return {
    id,
    summary: 'Edit',
    author,
    userId,
    client: userId === null ? null : 'studio',
    request: null,
    time: '2026-11-18T10:00:00Z',
    authoringRequest,
  }
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

  it('shows no Changed by line without the option', () => {
    renderChanges()

    expect(screen.queryByTestId('studio-changes-by')).toBeNull()
  })
})

describe('StudioChanges with Changed by', () => {
  const application = 'PurchaseRequests'
  // The reader is `author`, so a revision by `author` is "you".
  const changedBy = (names: Map<string, string>) => ({
    application,
    authors: (file: LiveFile) => changeAuthors(file.by, 'author', names),
  })

  it('names the reader, each person and the AI request in the order of first change, the request as a link', () => {
    // Newest first: the accept, then Sam Lee twice, then the reader, so the reader made the first change.
    const by = [
      revision('c', 'author', 'Ana', 'r1'),
      revision('b', 'u2', 'Sam Lee'),
      revision('a2', 'u2', 'Sam Lee'),
      revision('a1', 'author', 'Ana'),
    ]
    const buyer: LiveFile = { ...files[1], titles: null, by }
    renderWith([buyer], changedBy(new Map([['r1', 'Add a field.']])))

    const line = within(fileSection('roles/buyer.json')).getByTestId('studio-changes-by')
    expect(line).toHaveTextContent('Changed by you, Sam Lee, and the AI request “Add a field.”')
    expect(within(line).getByRole('link', { name: 'the AI request “Add a field.”' })).toHaveAttribute(
      'href',
      '/studio?app=PurchaseRequests&view=requests&request=r1',
    )
  })

  it('names the person who accepted a request whose name is not known', () => {
    const buyer: LiveFile = { ...files[1], titles: null, by: [revision('c', 'author', 'Ana', 'r1')] }
    renderWith([buyer], changedBy(new Map()))

    const line = within(fileSection('roles/buyer.json')).getByTestId('studio-changes-by')
    expect(line).toHaveTextContent(/^Changed by you$/)
    expect(within(line).queryByRole('link')).toBeNull()
  })

  it('names a revision the server made System, and shows no line for a file no revision changed', () => {
    const buyer: LiveFile = { ...files[1], titles: null, by: [revision('c', null, 'Axis')] }
    const approver: LiveFile = { ...files[0], titles: null, by: [] }
    renderWith([buyer, approver], changedBy(new Map()))

    expect(within(fileSection('roles/buyer.json')).getByTestId('studio-changes-by')).toHaveTextContent(
      /^Changed by System$/,
    )
    expect(within(fileSection('roles/approver.json')).queryByTestId('studio-changes-by')).toBeNull()
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
