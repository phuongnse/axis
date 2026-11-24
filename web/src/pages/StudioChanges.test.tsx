import { ConfigProvider } from 'antd'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  changeAuthors,
  type StudioChangeAuthor,
  type StudioLiveRevision,
  type StudioRevisionChange,
} from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { designTokensFor } from '../platform/designTokens'
import { ThemeModeProvider } from '../platform/theme'
import { StudioChanges, type StudioChangedFile } from './StudioChanges'

const texts = {
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
  'shell.studio.changes.showDiffName': 'Show changes to {name}',
  'shell.studio.changes.hideDiffName': 'Hide changes to {name}',
  'shell.studio.changes.linesAdded.one': '1 line added',
  'shell.studio.changes.linesAdded.many': '{count} lines added',
  'shell.studio.changes.linesRemoved.one': '1 line removed',
  'shell.studio.changes.linesRemoved.many': '{count} lines removed',
  'shell.studio.releases.system': 'System',
  'shell.studio.changes.changedBy': 'Changed by {authors}',
  'shell.person.you': '(you)',
  'shell.studio.changes.aiRequest': 'the AI request “{name}”',
  'shell.studio.changes.packageFile': 'Package file',
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
    name: 'notes/old.json',
    before: '{"gone":true}',
    after: null,
  },
]

/** A package's files: a changed adapter, an added file and a removed file, each as a text before and after. */
const packageFiles: StudioRevisionChange[] = [
  {
    file: 'packages/approval-rules/adapter.py',
    change: 'changed',
    id: null,
    kind: null,
    name: null,
    before: 'def limit():\n    return 1000\n',
    after: 'def limit():\n    return 5000\n',
  },
  {
    file: 'packages/approval-rules/new.py',
    change: 'added',
    id: null,
    kind: null,
    name: null,
    before: null,
    after: 'def fresh():\n    return 1\n',
  },
  {
    file: 'packages/approval-rules/old.py',
    change: 'removed',
    id: null,
    kind: null,
    name: null,
    before: 'def gone():\n    return 0\n',
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
  const { colour, radius } = designTokensFor('light')

  it('shows each file with its name, kind, change and its JSON as a line diff', async () => {
    renderChanges()

    expect(screen.getAllByTestId('studio-changes-file')).toHaveLength(3)

    const added = fileSection('roles/approver.json')
    expect(within(added).getByText('Approver')).toBeVisible()
    expect(within(added).getByText('Role')).toBeVisible()
    expect(within(added).getByText('Added')).toBeVisible()
    expect(await within(added).findByRole('textbox', { name: 'Approver' })).toBeVisible()
    expect(added.querySelector('.cm-removedFile')).toBeNull()
    expect(added.querySelector('.cm-changedLine')).toHaveTextContent('{')
    expect(added).toHaveTextContent('"name": "Approver"')

    const changed = fileSection('roles/buyer.json')
    expect(within(changed).getByText('Buyer')).toBeVisible()
    expect(within(changed).getByText('Role')).toBeVisible()
    expect(within(changed).getByText('Changed')).toBeVisible()
    expect(await within(changed).findByRole('textbox', { name: 'Buyer' })).toBeVisible()
    await waitFor(() => expect(changed.querySelector('.cm-deletedLine')).toHaveTextContent('"label": "first"'))
    expect(changed.querySelector('.cm-changedLine')).toHaveTextContent('"label": "second"')

    const removed = fileSection('notes/old.json')
    expect(within(removed).getByText('Removed')).toBeVisible()
    expect(await within(removed).findByRole('textbox', { name: 'notes/old.json' })).toBeVisible()
    expect(removed.querySelector('.cm-removedFile')).not.toBeNull()
    expect(removed).toHaveTextContent('"gone": true')
  })

  it('draws no side-by-side JSON', async () => {
    renderChanges()

    await within(fileSection('roles/buyer.json')).findByRole('textbox', { name: 'Buyer' })
    expect(screen.queryByTestId('json-diff-before')).toBeNull()
    expect(screen.queryByTestId('json-diff-after')).toBeNull()
  })

  it('shows the em dash as the kind of a file that has none', () => {
    renderChanges()

    expect(fileSection('notes/old.json')).toHaveTextContent(/—.*Removed/)
  })

  it('draws each file as a bordered block, open at first, with its line counts in a header', async () => {
    const before = 'keep\nold1\nold2\nold3\nend\n'
    const after = 'keep\nnew1\nnew2\nnew3\nnew4\nnew5\nnew6\nnew7\nnew8\nend\n'
    renderChanges([{ ...packageFiles[0], before, after }])

    const section = fileSection('packages/approval-rules/adapter.py')
    expect(section).toHaveStyle({ border: `1px solid ${colour.borderSubtle}`, borderRadius: `${radius.base}px` })
    expect(
      within(section).getByRole('button', {
        name: 'Hide changes to packages/approval-rules/adapter.py',
        expanded: true,
      }),
    ).toBeVisible()
    expect(within(section).getByText('+8')).toBeVisible()
    expect(within(section).getByText('−3')).toBeVisible()
    expect(within(section).getByText('8 lines added, 3 lines removed')).toBeInTheDocument()
    await within(section).findByRole('textbox', { name: 'packages/approval-rules/adapter.py' })
    await waitFor(() => expect(section.querySelectorAll('.cm-deletedLine')).toHaveLength(3))
    expect(section.querySelectorAll('.cm-changedLine')).toHaveLength(8)
  })

  it('shows no count of zero: a file added whole has no removed count', async () => {
    renderChanges([packageFiles[1]])

    const section = fileSection('packages/approval-rules/new.py')
    expect(within(section).getByText('+2')).toBeVisible()
    expect(within(section).queryByText(/−/)).toBeNull()
    expect(within(section).getByText('2 lines added')).toBeInTheDocument()
    await within(section).findByRole('textbox', { name: 'packages/approval-rules/new.py' })
  })

  it('draws the focus ring round the block while its diff has focus, and the editor draws none', async () => {
    const { container } = renderChanges([packageFiles[0]])

    await screen.findByRole('textbox', { name: 'packages/approval-rules/adapter.py' })
    const style = Array.from(container.querySelectorAll('style'))
      .map((element) => element.textContent)
      .join('\n')
    expect(style).toContain(
      `[data-diff-file]:has(.cm-editor:focus-within) { outline: 2px solid ${colour.focusRing}; outline-offset: 2px; }`,
    )
  })

  it('shows no Changed by line without the option', () => {
    renderChanges()

    expect(screen.queryByTestId('studio-changes-by')).toBeNull()
  })
})

describe('StudioChanges with Changed by', () => {
  const application = 'PurchaseRequests'
  // The reader is `author`, Ana, so a revision by `author` is "Ana (you)".
  const changedBy = (names: Map<string, string>) => ({
    application,
    authors: (file: LiveFile) => changeAuthors(file.by, names),
  })

  beforeEach(() => {
    // PersonName asks who the reader is, so the reader's name is "Ana (you)" and every other name is plain.
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async (_input: string) =>
          new Response(JSON.stringify({ id: 'author', displayName: 'Ana', roles: [], users: false }), { status: 200 }),
      ),
    )
  })

  it('names the reader, each person and the AI request in the order of first change, the request as a link', async () => {
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
    await waitFor(() =>
      expect(line).toHaveTextContent('Changed by Ana (you), Sam Lee, and the AI request “Add a field.”'),
    )
    expect(within(line).getByRole('link', { name: 'the AI request “Add a field.”' })).toHaveAttribute(
      'href',
      '/studio?app=PurchaseRequests&view=requests&request=r1',
    )
  })

  it('names the person who accepted a request whose name is not known', async () => {
    const buyer: LiveFile = { ...files[1], titles: null, by: [revision('c', 'author', 'Ana', 'r1')] }
    renderWith([buyer], changedBy(new Map()))

    const line = within(fileSection('roles/buyer.json')).getByTestId('studio-changes-by')
    await waitFor(() => expect(line).toHaveTextContent(/^Changed by Ana \(you\)$/))
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
  it('starts each file closed, and opens its line diff when Show changes is used', async () => {
    renderChanges(files, { folded: true })

    const removed = fileSection('notes/old.json')
    expect(within(removed).queryByRole('textbox')).not.toBeInTheDocument()
    const show = within(removed).getByRole('button', { name: 'Show changes to notes/old.json', expanded: false })
    await userEvent.click(show)
    expect(await within(removed).findByRole('textbox', { name: 'notes/old.json' })).toBeVisible()
    expect(removed).toHaveTextContent('"gone": true')
    expect(
      within(removed).getByRole('button', { name: 'Hide changes to notes/old.json', expanded: true }),
    ).toBeVisible()
  })

  it('keeps the diff in the page, hidden, and shows the counts while folded', async () => {
    renderChanges([files[2]], { folded: true })

    const removed = fileSection('notes/old.json')
    expect(within(removed).getByText('−3')).toBeVisible()
    expect(await within(removed).findByRole('textbox', { name: 'notes/old.json', hidden: true })).not.toBeVisible()
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

    await userEvent.click(within(section).getByRole('button', { name: 'Show changes to PurchaseRequest' }))
    const editor = await within(section).findByRole('textbox', { name: 'PurchaseRequest' })
    expect(editor).toBeVisible()
    expect(section).toHaveTextContent('"supplier"')
    expect(section).not.toHaveTextContent('"target"')
  })

  it('shows Changed by under the header of a folded file', () => {
    const buyer: LiveFile = { ...files[1], titles: null, by: [revision('c', null, 'Axis')] }
    renderWith(
      [buyer],
      { application: 'PurchaseRequests', authors: (file: LiveFile) => changeAuthors(file.by, new Map()) },
      true,
    )

    expect(within(fileSection('roles/buyer.json')).getByTestId('studio-changes-by')).toBeVisible()
  })
})

describe('StudioChanges package files', () => {
  it('draws each package file as the line diff, named by its path and kind, and no JSON', async () => {
    renderChanges(packageFiles)

    const changed = fileSection('packages/approval-rules/adapter.py')
    const editor = await within(changed).findByRole('textbox', { name: 'packages/approval-rules/adapter.py' })
    expect(editor).toBeVisible()
    expect(within(changed).getByText('Package file')).toBeVisible()
    expect(within(changed).getByText('Changed')).toBeVisible()
    await waitFor(() => expect(changed.querySelector('.cm-deletedLine')).toHaveTextContent('return 1000'))
    expect(changed.querySelector('.cm-changedLine')).toHaveTextContent('return 5000')

    const added = fileSection('packages/approval-rules/new.py')
    expect(await within(added).findByRole('textbox', { name: 'packages/approval-rules/new.py' })).toBeVisible()
    expect(within(added).getByText('Added')).toBeVisible()
    expect(added.querySelectorAll('.cm-changedLine')).toHaveLength(2)
    expect(added.querySelector('.cm-changedLine')).toHaveTextContent('def fresh():')
    expect(added.querySelector('.cm-removedFile')).toBeNull()

    const removed = fileSection('packages/approval-rules/old.py')
    expect(await within(removed).findByRole('textbox', { name: 'packages/approval-rules/old.py' })).toBeVisible()
    expect(within(removed).getByText('Removed')).toBeVisible()
    expect(removed.querySelector('.cm-removedFile')).not.toBeNull()
    expect(removed.querySelector('.cm-changedLine')).toHaveTextContent('def gone():')
  })

  it('draws a resource as a line diff too, and puts the package files after the resources, sorted by path', async () => {
    renderChanges([packageFiles[2], packageFiles[0], files[1], packageFiles[1]])

    expect(screen.getAllByTestId('studio-changes-file').map((section) => section.dataset.file)).toEqual([
      'roles/buyer.json',
      'packages/approval-rules/adapter.py',
      'packages/approval-rules/new.py',
      'packages/approval-rules/old.py',
    ])
    await waitFor(() =>
      expect(fileSection('roles/buyer.json').querySelector('.cm-deletedLine')).toHaveTextContent('"label": "first"'),
    )
    await within(fileSection('packages/approval-rules/old.py')).findByRole('textbox', {
      name: 'packages/approval-rules/old.py',
    })
  })

  it('shows a package file behind Show changes when folded, as it shows a resource', async () => {
    renderChanges([packageFiles[0]], { folded: true })

    const section = fileSection('packages/approval-rules/adapter.py')
    expect(within(section).queryByRole('textbox')).not.toBeInTheDocument()
    await userEvent.click(
      within(section).getByRole('button', { name: 'Show changes to packages/approval-rules/adapter.py' }),
    )
    expect(await within(section).findByRole('textbox', { name: 'packages/approval-rules/adapter.py' })).toBeVisible()
  })
})
