import { render, screen, within } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { fetchSites, type SiteListItem } from '../platform/site'
import type { StudioReleaseChange, StudioReleases, StudioResourceItem } from '../platform/studio'
import { studioKinds } from '../platform/studioKinds'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioOverview } from './StudioOverview'

vi.mock('../platform/site', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../platform/site')>()),
  fetchSites: vi.fn(),
}))

const english = platformEnglish as Record<string, string>

function resource(id: string, kind: string, name: string, file: string): StudioResourceItem {
  return { id, kind, name, file, version: 'v', usedBy: 0 }
}

function site(path: string, title: string, application: string): SiteListItem {
  return {
    path,
    titleKey: `${path}.title`,
    titles: { en: title },
    descriptions: null,
    application: { name: application, titles: { en: application }, descriptions: null },
  }
}

const live: StudioReleases = {
  active: { releaseId: 'r1', number: 1, contentHash: 'hash-1' },
  folderContentHash: 'hash-1',
  changes: [],
  items: [],
}

const withEntity = [resource('e1', 'entity', 'PurchaseRequest', 'entities/purchase-request.json')]

function renderOverview(props: Partial<ComponentProps<typeof StudioOverview>> = {}) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter>
          <StudioOverview
            application="PurchaseRequests"
            title="Purchase requests"
            description="Runs the purchases."
            resources={withEntity}
            moduleKinds={new Set()}
            history={undefined}
            {...props}
          />
        </MemoryRouter>
      </TextProvider>
    </ThemeModeProvider>,
  )
}

function section(name: string) {
  return within(screen.getByRole('region', { name }))
}

beforeEach(() => {
  localStorage.setItem('axis.themeMode', 'light')
  vi.mocked(fetchSites).mockResolvedValue([])
})

afterEach(() => {
  vi.restoreAllMocks()
  localStorage.clear()
})

describe('Get started', () => {
  it('leads with the four steps while the application has no entity', () => {
    renderOverview({ resources: [] })

    const steps = section('Get started').getAllByRole('link')
    expect(steps.map((link) => link.getAttribute('href'))).toEqual([
      '/studio?app=PurchaseRequests&kind=entity',
      '/studio?app=PurchaseRequests&kind=page',
      '/studio?app=PurchaseRequests&kind=site',
      '/studio?app=PurchaseRequests&view=releases',
    ])
    expect(steps[0]).toHaveTextContent(english['shell.studio.overview.start.entity'])
  })

  it('is gone once the application has an entity', () => {
    renderOverview()

    expect(screen.queryByRole('region', { name: 'Get started' })).not.toBeInTheDocument()
  })
})

describe('Live', () => {
  it('is left out until the release history loads with a live release', () => {
    renderOverview({ history: { ...live, active: null, changes: null } })

    expect(screen.queryByRole('region', { name: 'Live' })).not.toBeInTheDocument()
    expect(fetchSites).not.toHaveBeenCalled()
  })

  it('shows the sites of this application as tiles that open in a new tab', async () => {
    vi.mocked(fetchSites).mockResolvedValue([
      site('requests', 'Requests', 'PurchaseRequests'),
      site('leave', 'Leave', 'LeaveRequests'),
      site('portal', 'Portal', 'PurchaseRequests'),
    ])
    renderOverview({ history: live })

    const tiles = await section('Live').findAllByRole('link')
    expect(tiles.map((tile) => tile.getAttribute('aria-label'))).toEqual([
      'Portal, opens in a new tab',
      'Requests, opens in a new tab',
    ])
    for (const tile of tiles) {
      expect(tile).toHaveAttribute('target', '_blank')
      expect(tile).toHaveAttribute('rel', 'noopener noreferrer')
    }
  })

  it('says so when the live release has no site of this application', async () => {
    vi.mocked(fetchSites).mockResolvedValue([site('leave', 'Leave', 'LeaveRequests')])
    renderOverview({ history: live })

    expect(await section('Live').findByText('This release has no site.')).toBeInTheDocument()
  })

  it('shows a warning when the sites do not load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(fetchSites).mockRejectedValue(new Error('down'))
    renderOverview({ history: live })

    expect(await section('Live').findByText('The live sites did not load.')).toBeInTheDocument()
  })

  it('shows the same warning when the session has ended', async () => {
    vi.mocked(fetchSites).mockResolvedValue(null)
    renderOverview({ history: live })

    expect(await section('Live').findByText('The live sites did not load.')).toBeInTheDocument()
  })
})

describe('Changes since the live release', () => {
  it('is left out while the history loads or when it fails', () => {
    const { unmount } = render(
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <MemoryRouter>
            <StudioOverview
              application="PurchaseRequests"
              title="Purchase requests"
              description={null}
              resources={withEntity}
              moduleKinds={new Set()}
              history="failed"
            />
          </MemoryRouter>
        </TextProvider>
      </ThemeModeProvider>,
    )
    expect(screen.queryByRole('region', { name: 'Changes since the live release' })).not.toBeInTheDocument()
    unmount()

    renderOverview({ history: undefined })
    expect(screen.queryByRole('region', { name: 'Changes since the live release' })).not.toBeInTheDocument()
  })

  it('says the application is not published yet when there is no release', () => {
    renderOverview({ history: { ...live, active: null, changes: null } })

    expect(
      section('Changes since the live release').getByText('Publish makes this application live.'),
    ).toBeInTheDocument()
  })

  it('says everything is live when no change is left', () => {
    renderOverview({ history: live })

    expect(section('Changes since the live release').getByText('Everything is live.')).toBeInTheDocument()
  })

  it('lists each change with its kind and a link to a resource that still exists', () => {
    const changes: StudioReleaseChange[] = [
      { path: 'entities/purchase-request.json', change: 'changed', kind: null, name: null, titles: null, locale: null },
      {
        path: 'entities/gone.json',
        change: 'removed',
        kind: 'entity',
        name: 'Gone',
        titles: null,
        locale: null,
      },
    ]
    renderOverview({ history: { ...live, changes } })

    const rows = section('Changes since the live release').getAllByTestId('studio-change-row')
    expect(rows).toHaveLength(2)
    expect(rows[0]).toHaveTextContent('Gone')
    expect(rows[0]).toHaveTextContent('Entity')
    expect(rows[0]).toHaveTextContent('Removed')
    expect(within(rows[0]).queryByRole('link')).not.toBeInTheDocument()
    expect(rows[1]).toHaveTextContent('PurchaseRequest')
    expect(rows[1]).toHaveTextContent('Entity')
    expect(rows[1]).toHaveTextContent('Changed')
    expect(within(rows[1]).getByRole('link', { name: 'PurchaseRequest' })).toHaveAttribute(
      'href',
      '/studio?app=PurchaseRequests&resource=e1',
    )
  })

  it('links to the releases list', () => {
    renderOverview({ history: live })

    expect(section('Changes since the live release').getByRole('link', { name: 'All releases' })).toHaveAttribute(
      'href',
      '/studio?app=PurchaseRequests&view=releases',
    )
  })
})

describe('More you can add', () => {
  it('names each kind the application lacks, with its purpose and when', () => {
    renderOverview()

    const more = section('More you can add')
    expect(more.getByText(english['shell.studio.overview.more.seed.purpose'])).toBeInTheDocument()
    expect(more.getByText(english['shell.studio.overview.more.seed.when'])).toBeInTheDocument()
    expect(more.getByRole('link', { name: 'Seeds' }).getAttribute('href')).toBe(
      '/studio?app=PurchaseRequests&kind=seed',
    )
  })

  it('leaves out a kind the application has, as a resource or as a module', () => {
    renderOverview({
      resources: [...withEntity, resource('s1', 'seed', 'Start', 'seeds/start.json')],
      moduleKinds: new Set(['connector']),
    })

    const more = section('More you can add')
    expect(more.queryByText(english['shell.studio.overview.more.seed.purpose'])).not.toBeInTheDocument()
    expect(more.queryByText(english['shell.studio.overview.more.connector.purpose'])).not.toBeInTheDocument()
    expect(more.getByText(english['shell.studio.overview.more.webhook.purpose'])).toBeInTheDocument()
  })

  it('leaves out the kinds Get started leads to while it shows', () => {
    renderOverview({ resources: [] })

    const more = section('More you can add')
    expect(more.queryByText(english['shell.studio.overview.more.entity.purpose'])).not.toBeInTheDocument()
    expect(more.queryByText(english['shell.studio.overview.more.page.purpose'])).not.toBeInTheDocument()
    expect(more.queryByText(english['shell.studio.overview.more.site.purpose'])).not.toBeInTheDocument()
    expect(more.getByText(english['shell.studio.overview.more.seed.purpose'])).toBeInTheDocument()
  })

  it('is left out when the application has every kind', () => {
    renderOverview({
      moduleKinds: new Set(studioKinds.map((entry) => entry.kind)),
    })

    expect(screen.queryByRole('region', { name: 'More you can add' })).not.toBeInTheDocument()
  })
})
