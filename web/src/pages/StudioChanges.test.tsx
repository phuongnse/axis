import { ConfigProvider } from 'antd'
import { render, screen, within } from '@testing-library/react'
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

/** A live diff's file: its fields with the revisions that changed it since the live release, newest first. */
type LiveFile = StudioChangedFile & { by: StudioLiveRevision[] }

function renderWith<File extends StudioChangedFile>(
  changed: File[],
  changedBy?: { application: string; authors: (file: File) => StudioChangeAuthor[] },
) {
  return render(
    <MemoryRouter>
      <ThemeModeProvider initialMode="light">
        <ConfigProvider theme={{ token: { motion: false } }}>
          <TextProvider catalogs={catalogs} development={false}>
            <StudioChanges files={changed} resources={[]} changedBy={changedBy} />
          </TextProvider>
        </ConfigProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
}

function renderChanges() {
  return renderWith(files)
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
