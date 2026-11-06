import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type {
  StudioRelaxedPoint,
  StudioRelease,
  StudioReleaseItem,
  StudioReleasePartChange,
  StudioReleases,
  StudioResourceItem,
} from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioReleasePage } from './StudioReleasePage'

const approve: StudioRelaxedPoint = {
  module: 'department-budgets',
  point: 'Budget/transitions/approve',
  reason: 'Approvers decide alone',
  moduleTitle: 'Department budgets',
  part: 'transition',
  entityTitles: { en: 'Budget line' },
  titles: { en: 'Approve' },
}

const others: StudioRelaxedPoint = {
  module: 'department-budgets',
  point: 'Budget/ceiling/others',
  reason: 'Every other role may read',
  moduleTitle: 'Department budgets',
  part: 'ceiling',
  entityTitles: null,
  titles: null,
}

function item(
  releaseId: string,
  number: number,
  activatedAt: string,
  details: Partial<StudioReleaseItem> = {},
): StudioReleaseItem {
  return {
    releaseId,
    number,
    contentHash: `hash-${number}`,
    activatedAt,
    actor: 'user-1',
    actorName: 'Author User',
    label: null,
    note: null,
    requestId: null,
    offStandard: false,
    relaxed: [],
    ...details,
  }
}

// Release 3 was live from 2 May until release 2 replaced it, and was live again from 4 May until release 4.
const history: StudioReleases = {
  active: { releaseId: 'r4', number: 4, contentHash: 'hash-4' },
  folderContentHash: 'hash-4',
  changes: [],
  items: [
    item('r1', 1, '2026-05-01T10:00:00Z', { label: 'First' }),
    item('r3', 3, '2026-05-02T10:00:00Z', {
      label: 'Third',
      note: 'Third note',
      offStandard: true,
      relaxed: [approve, others],
    }),
    item('r2', 2, '2026-05-03T10:00:00Z', { actor: 'startup', actorName: null }),
    item('r3', 3, '2026-05-04T10:00:00Z', {
      actorName: null,
      actor: 'operator',
      offStandard: true,
      relaxed: [approve],
    }),
    item('r4', 4, '2026-05-05T10:00:00Z'),
  ],
}

const resources: StudioResourceItem[] = [
  { id: 'budget', kind: 'entity', name: 'Budget', file: 'entities/budget.json', version: 'v', usedBy: 0 },
  { id: 'home', kind: 'page', name: 'Home', file: 'pages/home.json', version: 'v', usedBy: 0 },
]

const release: StudioRelease = {
  number: 3,
  previous: 4,
  previousIsLive: true,
  live: false,
  changes: [
    { path: 'entities/budget.json', change: 'added', kind: 'entity', name: null, titles: null, locale: null },
    { path: 'pages/home.json', change: 'changed', kind: 'page', name: null, titles: null, locale: null },
    { path: 'forms/old.json', change: 'removed', kind: 'form', name: 'Old', titles: null, locale: null },
  ],
  partChanges: [],
  modules: [{ id: 'department-budgets', label: 'Department budgets', version: '1.2.0', previousVersion: '1.1.0' }],
  packages: [{ id: 'pdf-export', label: 'PDF export', version: '2.0.0', previousVersion: null }],
  blockers: [],
}

function answer(body: StudioRelease | number) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () =>
      typeof body === 'number' ? new Response(null, { status: body }) : new Response(JSON.stringify(body)),
    ),
  )
}

function renderPage(props: Partial<Parameters<typeof StudioReleasePage>[0]> = {}) {
  const onActivate = vi.fn()
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter>
          <StudioReleasePage
            application="Department Budgets"
            number="3"
            history={history}
            resources={resources}
            canActivate
            busy={false}
            onActivate={onActivate}
            {...props}
          />
        </MemoryRouter>
      </TextProvider>
    </ThemeModeProvider>,
  )
  return onActivate
}

const time = (value: string) =>
  new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(value))

function section(name: string) {
  return within(screen.getByRole('region', { name }))
}

beforeEach(() => localStorage.setItem('axis.themeMode', 'light'))

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

function fact(name: string) {
  const label = screen.getByText(name, { selector: '.ant-descriptions-item-label' })
  return within(label.closest('.ant-descriptions-item') as HTMLElement)
}

describe('StudioReleasePage', () => {
  it('shows the heading, the facts, the relaxed points, the changes, the parts and the activations', async () => {
    answer(release)
    renderPage()

    expect(await screen.findByRole('heading', { name: 'Release 3' })).toBeInTheDocument()
    expect(screen.getByTestId('release-off-standard')).toHaveTextContent('Off-standard')
    expect(screen.queryByText('Live')).not.toBeInTheDocument()
    expect(screen.queryByTestId('release-relaxed-point')).not.toBeInTheDocument()

    expect(fact('Label').getByText('Third')).toBeInTheDocument()
    expect(fact('Status').getByText(`Replaced on ${time('2026-05-05T10:00:00Z')}`)).toBeInTheDocument()
    expect(fact('Published').getByText(time('2026-05-02T10:00:00Z'))).toBeInTheDocument()
    expect(fact('Published by').getByText('Author User')).toBeInTheDocument()
    expect(fact('Note').getByText('Third note')).toBeInTheDocument()

    const points = section('Relaxed points').getAllByTestId('studio-release-relaxed-point')
    expect(section('Relaxed points').getByText('Department budgets')).toBeInTheDocument()
    expect(points).toHaveLength(2)
    expect(points[0]).toHaveTextContent('Approve')
    expect(points[0]).toHaveTextContent('Transition on Budget line')
    expect(points[0]).toHaveTextContent('Approvers decide alone')
    expect(points[1]).toHaveTextContent('Other roles')
    expect(points[1]).toHaveTextContent('Role ceiling on Budget')
    expect(points[1]).toHaveTextContent('Every other role may read')

    const changes = section('If you activate it, compared with live release 4')
    const rows = changes.getAllByTestId('studio-change-row')
    expect(rows).toHaveLength(3)
    expect(within(rows[0]).getByRole('link', { name: 'Budget' })).toHaveAttribute(
      'href',
      '/studio?app=Department+Budgets&resource=budget',
    )
    expect(within(rows[1]).queryByRole('link')).not.toBeInTheDocument()
    expect(rows[1]).toHaveTextContent('Changed')
    expect(within(rows[2]).queryByRole('link')).not.toBeInTheDocument()
    expect(rows[2]).toHaveTextContent('Removed')

    const parts = section('Modules and packages')
    expect(parts.getByText('Department budgets')).toBeInTheDocument()
    expect(parts.getByText('1.2.0')).toBeInTheDocument()
    expect(parts.getByText('PDF export')).toBeInTheDocument()
    expect(parts.getByText('Package')).toBeInTheDocument()
    expect(parts.getByText('2.0.0')).toBeInTheDocument()

    const activations = section('Activations').getAllByTestId('studio-release-activation')
    expect(activations).toHaveLength(2)
    const cells = (row: HTMLElement) =>
      within(row)
        .getAllByRole('cell')
        .map((cell) => cell.textContent)
    expect(cells(activations[0])).toEqual([
      time('2026-05-04T10:00:00Z'),
      'The platform operator',
      '2',
      time('2026-05-05T10:00:00Z'),
    ])
    expect(cells(activations[1])).toEqual([
      time('2026-05-02T10:00:00Z'),
      'Author User',
      '1',
      time('2026-05-03T10:00:00Z'),
    ])
  })

  it('titles the changes of a live release by the release below it and says it is live since its last activation', async () => {
    answer({ ...release, number: 4, previous: 3, previousIsLive: false, live: true })
    renderPage({ number: '4' })

    expect(await screen.findByRole('heading', { name: 'Release 4' })).toBeInTheDocument()
    expect(screen.getByText('Live')).toBeInTheDocument()
    expect(screen.queryByTestId('release-off-standard')).not.toBeInTheDocument()
    expect(fact('Status').getByText(`Live since ${time('2026-05-05T10:00:00Z')}`)).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Changes from release 3' })).toBeInTheDocument()
    expect(within(screen.getByTestId('studio-release-activation')).getAllByRole('cell')[3]).toHaveTextContent('Now')
  })

  it('says there is nothing to compare with for the first release, and leaves out relaxed points without a module', async () => {
    answer({ ...release, number: 1, previous: null, changes: null, partChanges: null, modules: [], packages: [] })
    renderPage({ number: '1' })

    await screen.findByRole('heading', { name: 'Release 1' })
    expect(section('Changes').getByText(/nothing to compare with/)).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Relaxed points' })).not.toBeInTheDocument()
    expect(section('Modules and packages').getByText('This release pins no module or package.')).toBeInTheDocument()
  })

  it('says no file differs when the release has a previous release and no change', async () => {
    answer({ ...release, changes: [] })
    renderPage()

    await screen.findByRole('heading', { name: 'Release 3' })
    expect(screen.getByText('No file differs from release 4.')).toBeInTheDocument()
  })

  it('lists the modules and packages that differ after the files, modules first and each by label', async () => {
    const partChanges: StudioReleasePartChange[] = [
      { type: 'package', id: 'pdf', label: 'PDF export', change: 'added', version: '1.0.0', previousVersion: null },
      {
        type: 'module',
        id: 'organization',
        label: 'Organization',
        change: 'upgraded',
        version: '1.2.0',
        previousVersion: '1.1.0',
      },
      { type: 'module', id: 'audit', label: 'Audit', change: 'removed', version: null, previousVersion: '1.0.0' },
    ]
    answer({ ...release, partChanges })
    renderPage()

    await screen.findByRole('heading', { name: 'Release 3' })
    const changes = section('If you activate it, compared with live release 4')
    expect(changes.getAllByTestId('studio-change-row')).toHaveLength(3)
    const rows = changes.getAllByTestId('studio-change-part')
    expect(rows.map((row) => row.textContent)).toEqual([
      'AuditModuleRemoved, was 1.0.0',
      'OrganizationModuleUpgraded from 1.1.0 to 1.2.0',
      'PDF exportPackageAdded at 1.0.0',
    ])
  })

  it('says no module or package differs when none does', async () => {
    answer(release)
    renderPage()

    await screen.findByRole('heading', { name: 'Release 3' })
    expect(screen.getByText('No module or package differs.')).toBeInTheDocument()
    expect(screen.queryByTestId('studio-change-part')).not.toBeInTheDocument()
  })

  it('shows the module and package rows under the no-file line when no file differs', async () => {
    answer({
      ...release,
      changes: [],
      partChanges: [
        {
          type: 'module',
          id: 'organization',
          label: 'Organization',
          change: 'upgraded',
          version: '1.2.0',
          previousVersion: '1.1.0',
        },
      ],
    })
    renderPage()

    await screen.findByRole('heading', { name: 'Release 3' })
    expect(screen.getByText('No file differs from release 4.')).toBeInTheDocument()
    const rows = screen.getAllByTestId('studio-change-part')
    expect(rows).toHaveLength(1)
    expect(rows[0]).toHaveTextContent('OrganizationModuleUpgraded from 1.1.0 to 1.2.0')
  })

  it('says a release follows every contract when it pins a module and relaxed none', async () => {
    answer({ ...release, number: 4, previous: 3, previousIsLive: false, live: true })
    renderPage({ number: '4' })

    await screen.findByRole('heading', { name: 'Release 4' })
    expect(
      section('Relaxed points').getByText('This release follows every contract of its modules.'),
    ).toBeInTheDocument()
  })

  it('shows one line and a link to every release for a number the route answers 404 for', async () => {
    answer(404)
    renderPage({ number: '99' })

    const missing = await screen.findByTestId('studio-release-not-found')
    expect(missing).toHaveTextContent('This release is not in the history.')
    expect(within(missing).getByRole('link', { name: 'All releases' })).toHaveAttribute(
      'href',
      '/studio?app=Department+Budgets&view=releases',
    )
  })

  it('shows a line of its own when the release does not load, so a server fault is not a missing release', async () => {
    answer(500)
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    renderPage()

    const failed = await screen.findByTestId('studio-release-failed')
    expect(failed).toHaveTextContent('This release could not be loaded.')
    expect(screen.queryByTestId('studio-release-not-found')).not.toBeInTheDocument()
    expect(within(failed).getByRole('link', { name: 'All releases' })).toBeInTheDocument()
  })

  it('shows a spinner until the release history loads', () => {
    answer(release)
    renderPage({ history: undefined })

    expect(screen.queryByRole('heading')).not.toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('asks before it activates a release that is not live, with the list dialog, and sends the release id', async () => {
    answer(release)
    const onActivate = renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Activate…' }))
    expect(screen.getByTestId('studio-activate-release')).toHaveTextContent('Release 3 replaces live release 4.')
    expect(onActivate).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Activate' }))

    await waitFor(() => expect(onActivate).toHaveBeenCalledWith('r3'))
  })

  it('turns Activate… off while a request runs', async () => {
    answer(release)
    renderPage({ busy: true })

    expect(await screen.findByRole('button', { name: 'Activate…' })).toBeDisabled()
  })

  it('has no Activate… for the live release or for a person who cannot publish', async () => {
    answer({ ...release, number: 4, previous: 3, previousIsLive: false, live: true })
    renderPage({ number: '4' })
    await screen.findByRole('heading', { name: 'Release 4' })
    expect(screen.queryByRole('button', { name: 'Activate…' })).not.toBeInTheDocument()
  })

  it('has no Activate… for a person who cannot publish', async () => {
    answer(release)
    renderPage({ canActivate: false })
    await screen.findByRole('heading', { name: 'Release 3' })
    expect(screen.queryByRole('button', { name: 'Activate…' })).not.toBeInTheDocument()
  })
})
