import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { moduleKinds } from './moduleKinds'
import { assignLocation } from './navigation'
import { QuotaRefusedError } from './quota'
import {
  changeRows,
  checkStudioExpression,
  fetchStudioPolicyMatrix,
  fetchStudioRelease,
  fetchStudioResource,
  filterResources,
  folderDiffers,
  groupByKind,
  moduleResources,
  sortReferences,
  previewSiteUrl,
  putStudioTexts,
  releaseList,
  releasePublisher,
  resourceTitle,
  revisionAuthor,
  revisionClient,
  ServerUnreachableError,
  studioAppDescription,
  studioAppTitle,
  undoStudioRevision,
  type StudioReference,
  type StudioReferenceNode,
  type StudioReleaseChange,
  type StudioRevision,
  type StudioReleaseItem,
  type StudioReleases,
  type StudioNames,
  type StudioResourceItem,
  waitForServer,
} from './studio'

vi.mock('./navigation')

afterEach(() => vi.unstubAllGlobals())

const english = platformEnglish as Record<string, string>
const t = (key: string) => english[key] ?? key

function resource(id: string, kind: string, name: string | null, file: string): StudioResourceItem {
  return { id, kind, name, file, version: 'v', usedBy: 0 }
}

describe('fetchStudioResource', () => {
  it('reports a request without an answer and a 504 as an unreachable server', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockRejectedValueOnce(new TypeError('Failed to fetch'))
        .mockResolvedValueOnce(new Response(null, { status: 504 })),
    )

    await expect(fetchStudioResource('PurchaseRequests', 'a')).rejects.toBeInstanceOf(ServerUnreachableError)
    await expect(fetchStudioResource('PurchaseRequests', 'a')).rejects.toBeInstanceOf(ServerUnreachableError)
  })

  it('reports a 500 as an ordinary error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(null, { status: 500 })))

    const error = await fetchStudioResource('PurchaseRequests', 'a').catch((reason: unknown) => reason)

    expect(error).toBeInstanceOf(Error)
    expect(error).not.toBeInstanceOf(ServerUnreachableError)
  })
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200 })
}

describe('fetchStudioRelease', () => {
  it('loads the release at its number and answers null for a number the history lacks', async () => {
    const release = { number: 3, previous: 2, previousIsLive: true, live: false }
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(json(release))
      .mockResolvedValueOnce(new Response(null, { status: 404 }))
      .mockResolvedValueOnce(new Response(null, { status: 500 }))
    vi.stubGlobal('fetch', fetchMock)

    expect(await fetchStudioRelease('Purchase Requests', '3')).toEqual(release)
    expect(String(fetchMock.mock.calls[0][0])).toContain('/api/studio/apps/Purchase%20Requests/releases/3')
    expect(await fetchStudioRelease('Purchase Requests', '99')).toBeNull()
    await expect(fetchStudioRelease('Purchase Requests', '3')).rejects.toBeInstanceOf(Error)
  })
})

describe('fetchStudioPolicyMatrix', () => {
  it('loads the matrix of the application', async () => {
    const matrix = { targets: [], roles: [] }
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(matrix), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(fetchStudioPolicyMatrix('Purchase Requests')).resolves.toEqual(matrix)
    expect(fetchMock).toHaveBeenCalledWith('/api/studio/apps/Purchase%20Requests/policy-matrix', expect.anything())
  })

  it('throws on a failed answer', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(null, { status: 500 })),
    )

    await expect(fetchStudioPolicyMatrix('PurchaseRequests')).rejects.toThrow('failed with status 500')
  })
})

describe('putStudioTexts', () => {
  it('puts every locale to the key route and answers the new versions and diagnostics', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(json({ resources: [{ id: 'a', locale: 'en', version: 'v2' }], diagnostics: [] }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await putStudioTexts('Shop', 'pages.my tasks.title', { en: 'Tasks', vi: 'Công việc' })

    expect(result.resources).toEqual([{ id: 'a', locale: 'en', version: 'v2' }])
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/studio/apps/Shop/texts/pages.my%20tasks.title')
    expect(init.method).toBe('PUT')
    expect(JSON.parse(init.body)).toEqual({ texts: { en: 'Tasks', vi: 'Công việc' } })
  })

  it('throws on a 400 and reports a 503 as an unreachable server', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(new Response(null, { status: 400 }))
        .mockResolvedValueOnce(new Response(null, { status: 503 })),
    )

    const error = await putStudioTexts('Shop', 'k', { en: 'a' }).catch((reason: unknown) => reason)
    expect(error).toBeInstanceOf(Error)
    expect(error).not.toBeInstanceOf(ServerUnreachableError)
    await expect(putStudioTexts('Shop', 'k', { en: 'a' })).rejects.toBeInstanceOf(ServerUnreachableError)
  })
})

describe('checkStudioExpression', () => {
  it('answers the type and diagnostics of an expression check', async () => {
    const fetchMock = vi.fn(async (_input: string, _init?: RequestInit) => json({ type: 'boolean', diagnostics: [] }))
    vi.stubGlobal('fetch', fetchMock)

    const check = await checkStudioExpression('PurchaseRequests', 'r', '/validations/0/expression', 'a > 0')

    expect(check).toEqual({ type: 'boolean', diagnostics: [] })
    expect(fetchMock.mock.calls[0][0]).toBe('/api/studio/apps/PurchaseRequests/expressions/check')
    expect(JSON.parse(fetchMock.mock.calls[0][1]!.body as string)).toEqual({
      resourceId: 'r',
      path: '/validations/0/expression',
      expression: 'a > 0',
    })
  })

  it.each([400, 401, 503])('answers null for %i and does not ask to sign in', async (status) => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(null, { status })),
    )

    await expect(checkStudioExpression('PurchaseRequests', 'r', '/p', 'a')).resolves.toBeNull()
    expect(assignLocation).not.toHaveBeenCalled()
  })

  it('rejects when the server does not answer', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Promise.reject(new TypeError('Failed to fetch'))),
    )

    await expect(checkStudioExpression('PurchaseRequests', 'r', '/p', 'a')).rejects.toThrow(TypeError)
  })
})

describe('waitForServer', () => {
  it('resolves once /api/me answers, after a request without an answer and a 503', async () => {
    const fetchMock = vi
      .fn()
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(new Response(null, { status: 503 }))
      .mockResolvedValueOnce(new Response(null, { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await waitForServer(new AbortController().signal, [0])

    expect(fetchMock).toHaveBeenCalledTimes(3)
    expect(fetchMock).toHaveBeenLastCalledWith('/api/me', expect.anything())
  })

  it('does not wait once the signal aborts', async () => {
    const controller = new AbortController()
    vi.stubGlobal('fetch', vi.fn())
    const waiting = waitForServer(controller.signal, [60_000])

    controller.abort()

    await expect(waiting).rejects.toThrow()
    expect(fetch).not.toHaveBeenCalled()
  })
})

const items = [
  resource('aaaa0001-0000-4000-8000-000000000000', 'entity', 'PurchaseRequest', 'entities/purchase-request.json'),
  resource('bbbb0002-0000-4000-8000-000000000000', 'form', 'PurchaseRequestForm', 'forms/purchase-request.json'),
  resource('cccc0003-0000-4000-8000-000000000000', 'entity', 'Department', 'entities/department.json'),
  resource('dddd0004-0000-4000-8000-000000000000', 'application', null, 'application.json'),
]

describe('resourceTitle', () => {
  const entity = {
    ...resource('1', 'entity', 'PurchaseRequest', 'entities/purchase-request.json'),
    titles: { en: 'Purchase request', vi: 'Yêu cầu mua hàng' },
  }

  it('names a resource by its label in the UI locale', () => {
    expect(resourceTitle(entity, 'vi', t)).toBe('Yêu cầu mua hàng')
  })

  it('falls back to the en label when the UI locale has no label', () => {
    expect(resourceTitle(entity, 'fr', t)).toBe('Purchase request')
  })

  it('names a text resource by its language in the UI locale', () => {
    const text = { ...resource('2', 'text', 'TextsVi', 'texts/vi.json'), locale: 'vi' }

    expect(resourceTitle(text, 'en', t)).toBe('Vietnamese')
  })

  it('names a text resource by its tag when the browser cannot name the language', () => {
    const text = { ...resource('2', 'text', 'TextsX', 'texts/x.json'), locale: 'not a tag' }

    expect(resourceTitle(text, 'en', t)).toBe('not a tag')
  })

  it('names a resource without titles by its name, then by its kind, never by its file', () => {
    expect(resourceTitle(resource('3', 'form', 'ReviewForm', 'forms/review.json'), 'en', t)).toBe('ReviewForm')
    expect(resourceTitle(resource('4', 'form', null, 'forms/review.json'), 'en', t)).toBe('Form')
  })
})

describe('filterResources', () => {
  it('keeps every resource for an empty or blank search', () => {
    expect(filterResources(items, '')).toEqual(items)
    expect(filterResources(items, '   ')).toEqual(items)
  })

  it('matches part of a name in any letter case', () => {
    expect(filterResources(items, '  purchaserequest').map((item) => item.name)).toEqual([
      'PurchaseRequest',
      'PurchaseRequestForm',
    ])
    expect(filterResources(items, 'DEPART').map((item) => item.name)).toEqual(['Department'])
  })

  it('matches part of an id in any letter case', () => {
    expect(filterResources(items, 'BBBB0002').map((item) => item.name)).toEqual(['PurchaseRequestForm'])
  })

  it('does not match the file or the kind', () => {
    expect(filterResources(items, 'entities/')).toEqual([])
    expect(filterResources(items, 'form')).toEqual([items[1]])
  })
})

describe('groupByKind', () => {
  it('makes one group per kind, sorted by kind, with the items sorted by name or file', () => {
    const groups = groupByKind(items, 'en')

    expect(groups.map((group) => group.kind)).toEqual(['application', 'entity', 'form'])
    expect(groups[1].items.map((item) => item.name)).toEqual(['Department', 'PurchaseRequest'])
  })

  it('sorts an item without a name by its file', () => {
    const groups = groupByKind(
      [
        resource('1', 'entity', null, 'entities/b.json'),
        resource('2', 'entity', 'zeta', 'entities/a.json'),
        resource('3', 'entity', null, 'entities/a.json'),
      ],
      'en',
    )

    expect(groups[0].items.map((item) => item.id)).toEqual(['3', '1', '2'])
  })

  it('sorts the items by the shown name when it is given', () => {
    const groups = groupByKind(
      [resource('1', 'entity', 'A', 'entities/a.json'), resource('2', 'entity', 'B', 'entities/b.json')],
      'en',
      (item) => (item.id === '1' ? 'Zeta' : 'Alpha'),
    )

    expect(groups[0].items.map((item) => item.id)).toEqual(['2', '1'])
  })

  it('sorts the shown names in the locale, so Đ follows D in Vietnamese', () => {
    const groups = groupByKind(
      [
        resource('1', 'entity', 'Đơn hàng', 'entities/1.json'),
        resource('2', 'entity', 'Dự án', 'entities/2.json'),
        resource('3', 'entity', 'Bảng', 'entities/3.json'),
      ],
      'vi',
      (item) => item.name ?? '',
    )

    expect(groups[0].items.map((item) => item.name)).toEqual(['Bảng', 'Dự án', 'Đơn hàng'])
  })

  it('returns no group for no resources', () => {
    expect(groupByKind([], 'en')).toEqual([])
  })
})

describe('moduleResources', () => {
  const lists = {
    entity: 'entities',
    dataSource: 'dataSources',
    rule: 'rules',
    sequence: 'sequences',
    form: 'forms',
    page: 'pages',
    process: 'processes',
    event: 'events',
    text: 'textResources',
    seed: 'seeds',
    policy: 'policies',
    trigger: 'triggers',
    scenario: 'scenarios',
    contract: 'contracts',
  } as const
  // One module item and one own item for each kind.
  const names = Object.fromEntries(
    moduleKinds.map((kind) => [
      lists[kind],
      [
        { id: `own-${kind}`, name: `Own${kind}`, module: null },
        { id: `checklists-${kind}`, name: `checklists/${kind}`, module: 'checklists' },
      ],
    ]),
  ) as unknown as StudioNames

  it('returns one group for each module kind, in the order of the kind list, with the module id', () => {
    const groups = moduleResources(names, '', 'en')

    expect(groups.map((group) => group.kind)).toEqual([...moduleKinds])
    expect(groups.flatMap((group) => group.items)).toEqual(
      moduleKinds.map((kind) => ({ id: `checklists-${kind}`, kind, name: `checklists/${kind}`, module: 'checklists' })),
    )
  })

  it('leaves out the application own resources', () => {
    const ids = moduleResources(names, '', 'en').flatMap((group) => group.items.map((item) => item.id))

    expect(ids).toHaveLength(moduleKinds.length)
    expect(ids.filter((id) => id.startsWith('own-'))).toEqual([])
  })

  it('applies the search to the name and the id, ignoring letter case and spaces, and drops empty groups', () => {
    expect(
      moduleResources(names, '  CHECKLISTS/PROCESS ', 'en').map((group) => group.items.map((item) => item.id)),
    ).toEqual([['checklists-process']])
    expect(moduleResources(names, 'CHECKLISTS-POLICY', 'en').map((group) => group.kind)).toEqual(['policy'])
    expect(moduleResources(names, ' CHECKLISTS-SEED ', 'en').map((group) => group.kind)).toEqual(['seed'])
    expect(moduleResources(names, 'checklists/scenario', 'en').map((group) => group.kind)).toEqual(['scenario'])
    expect(moduleResources(names, 'nothing', 'en')).toEqual([])
  })

  it('sorts the items of a group by name in the locale', () => {
    const entity = (id: string, name: string) => ({ id, name: `checklists/${name}`, module: 'checklists' })
    const unsorted = {
      ...names,
      entities: [entity('3', 'Đơn hàng'), entity('2', 'Dự án'), entity('1', 'Bảng')],
    } as unknown as StudioNames

    const groups = moduleResources(unsorted, '', 'vi')

    expect(groups.find((group) => group.kind === 'entity')?.items.map((item) => item.id)).toEqual(['1', '2', '3'])
  })

  it('returns no group before the names are loaded', () => {
    expect(moduleResources(null, '', 'en')).toEqual([])
  })
})

function release(releaseId: string, number: number, activatedAt: string): StudioReleaseItem {
  return {
    releaseId,
    number,
    contentHash: `hash-${number}`,
    activatedAt,
    actor: 'author',
    actorName: null,
    label: null,
    note: null,
    requestId: null,
    offStandard: false,
    relaxed: [],
  }
}

describe('releaseList', () => {
  const items = [
    release('r2', 2, '2026-05-02T10:00:00Z'),
    { ...release('r1', 1, '2026-05-01T10:00:00Z'), label: 'First', note: 'Initial', actorName: 'Author User' },
    { ...release('r1', 1, '2026-05-03T10:00:00Z'), label: null, note: null, actor: 'operator' },
    release('r3', 3, '2026-05-03T10:00:00Z'),
  ]

  it('has one row for each release, the highest number first', () => {
    const rows = releaseList({ active: null, folderContentHash: null, changes: null, items })

    expect(rows.map((row) => row.number)).toEqual([3, 2, 1])
    expect(rows.map((row) => row.live)).toEqual([false, false, false])
  })

  it('takes the details of a release from its first activation', () => {
    const rows = releaseList({ active: null, folderContentHash: null, changes: null, items })

    expect(rows[2]).toMatchObject({
      releaseId: 'r1',
      label: 'First',
      note: 'Initial',
      publishedAt: '2026-05-01T10:00:00Z',
      actor: 'author',
      actorName: 'Author User',
    })
  })

  it('marks the live release', () => {
    const rows = releaseList({
      active: { releaseId: 'r1', number: 1, contentHash: 'hash-1' },
      folderContentHash: null,
      changes: null,
      items,
    })

    expect(rows.map((row) => row.live)).toEqual([false, false, true])
  })
})

describe('releasePublisher', () => {
  const t = (key: string) => key
  const row = (actor: string, actorName: string | null) => ({
    ...releaseList({
      active: null,
      folderContentHash: null,
      changes: null,
      items: [release('r1', 1, '2026-05-01T10:00:00Z')],
    })[0],
    actor,
    actorName,
  })

  it('names the person when there is a name', () => {
    expect(releasePublisher(row('user-1', 'Author User'), t)).toBe('Author User')
  })

  it('says System for a startup activation and the operator for an operator one', () => {
    expect(releasePublisher(row('startup', null), t)).toBe('shell.studio.releases.system')
    expect(releasePublisher(row('operator', null), t)).toBe('shell.studio.releases.operator')
  })

  it('never shows an id: an unknown actor and a user without a name are someone no longer known', () => {
    expect(releasePublisher(row('unknown', null), t)).toBe('shell.studio.releases.unknown')
    expect(releasePublisher(row('user-1', null), t)).toBe('shell.studio.releases.unknown')
  })
})

describe('changeRows', () => {
  const change = (overrides: Partial<StudioReleaseChange> & Pick<StudioReleaseChange, 'path' | 'change'>) => ({
    kind: null,
    name: null,
    titles: null,
    locale: null,
    ...overrides,
  })

  it('puts the manifest first, named Settings and with no kind', () => {
    const rows = changeRows(
      [
        change({ path: 'application.json', change: 'changed', kind: 'application' }),
        change({ path: 'x.json', change: 'added' }),
      ],
      [resource('z', 'entity', 'Zeta', 'entities/zeta.json')],
      'en',
      t,
    )

    expect(rows[0]).toMatchObject({ name: 'Settings', kind: null, change: 'changed' })
  })

  it('orders the rows by the sidebar kind and then by name', () => {
    const rows = changeRows(
      [
        change({ path: 'pages/alpha.json', change: 'changed' }),
        change({ path: 'entities/beta.json', change: 'added' }),
        change({ path: 'entities/alpha.json', change: 'added' }),
      ],
      [
        resource('p', 'page', 'Alpha', 'pages/alpha.json'),
        resource('b', 'entity', 'Beta', 'entities/beta.json'),
        resource('a', 'entity', 'Alpha', 'entities/alpha.json'),
      ],
      'en',
      t,
    )

    expect(rows.map((row) => row.name)).toEqual(['Alpha', 'Beta', 'Alpha'])
    expect(rows.map((row) => row.kind)).toEqual(['entity', 'entity', 'page'])
  })

  it('orders Vietnamese names in one kind with D before Đ, and Settings first', () => {
    const rows = changeRows(
      [
        change({ path: 'entities/dong.json', change: 'added' }),
        change({ path: 'entities/du.json', change: 'added' }),
        change({ path: 'application.json', change: 'changed', kind: 'application' }),
      ],
      [resource('1', 'entity', 'Đơn hàng', 'entities/dong.json'), resource('2', 'entity', 'Dự án', 'entities/du.json')],
      'vi',
      t,
    )

    expect(rows.map((row) => row.name)).toEqual(['Settings', 'Dự án', 'Đơn hàng'])
  })

  it('links an added or changed row to its resource and takes its kind from the resource', () => {
    const rows = changeRows(
      [change({ path: 'entities/zeta.json', change: 'changed' })],
      [resource('z', 'entity', 'Zeta', 'entities/zeta.json')],
      'en',
      t,
    )

    expect(rows).toEqual([
      { key: 'changed:entities/zeta.json', name: 'Zeta', kind: 'entity', change: 'changed', resourceId: 'z' },
    ])
  })

  it('names a removed row from the release, with no link, since the resource is gone', () => {
    const rows = changeRows(
      [change({ path: 'entities/gone.json', change: 'removed', kind: 'entity', name: 'Gone' })],
      [resource('g', 'entity', 'Gone', 'entities/gone.json')],
      'en',
      t,
    )

    expect(rows).toEqual([
      { key: 'removed:entities/gone.json', name: 'Gone', kind: 'entity', change: 'removed', resourceId: null },
    ])
  })

  it('names a row by its path when its kind is unknown, and puts it last', () => {
    const rows = changeRows(
      [change({ path: 'notes/readme.md', change: 'added' }), change({ path: 'entities/zeta.json', change: 'added' })],
      [resource('z', 'entity', 'Zeta', 'entities/zeta.json')],
      'en',
      t,
    )

    expect(rows.map((row) => row.name)).toEqual(['Zeta', 'notes/readme.md'])
    expect(rows[1]).toMatchObject({ kind: null, resourceId: null })
  })
})

describe('folderDiffers', () => {
  const active = { releaseId: 'r1', number: 1, contentHash: 'hash-1' }
  const history = (overrides: Partial<StudioReleases>): StudioReleases => ({
    active,
    folderContentHash: 'hash-1',
    changes: null,
    items: [],
    ...overrides,
  })

  it('is false when the folder has the active release content', () => {
    expect(folderDiffers(history({}))).toBe(false)
  })

  it('is true when the folder compiles to other content', () => {
    expect(folderDiffers(history({ folderContentHash: 'other' }))).toBe(true)
  })

  it('is false when no release is active, or the folder has errors', () => {
    expect(folderDiffers(history({ active: null, folderContentHash: 'other' }))).toBe(false)
    expect(folderDiffers(history({ folderContentHash: null }))).toBe(false)
  })
})

describe('previewSiteUrl', () => {
  it('keeps the protocol and the port of the page and puts the site path after the preview host', () => {
    expect(previewSiteUrl('preview.localhost', 'purchasing', { protocol: 'http:', port: '5173' })).toBe(
      'http://preview.localhost:5173/purchasing',
    )
    expect(previewSiteUrl('preview.example.test', 'leave', { protocol: 'https:', port: '' })).toBe(
      'https://preview.example.test/leave',
    )
  })
})

describe('studioAppTitle and studioAppDescription', () => {
  const app = { name: 'Leave', titles: { en: 'Leave requests', vi: 'Nghỉ phép' }, descriptions: { en: 'Time off.' } }

  it('take the text of the locale', () => {
    expect(studioAppTitle(app, 'vi')).toBe('Nghỉ phép')
    expect(studioAppDescription({ ...app, descriptions: { en: 'Time off.', vi: 'Nghỉ.' } }, 'vi')).toBe('Nghỉ.')
  })

  it('fall back to en, and the title then to the name', () => {
    expect(studioAppTitle(app, 'de')).toBe('Leave requests')
    expect(studioAppDescription(app, 'vi')).toBe('Time off.')
    expect(studioAppTitle({ name: 'Leave', titles: {}, descriptions: {} }, 'en')).toBe('Leave')
  })

  it('give no description when there is none', () => {
    expect(studioAppDescription({ name: 'Leave', titles: {}, descriptions: {} }, 'en')).toBeNull()
  })
})

describe('undoStudioRevision', () => {
  const blocker: StudioRevision = {
    id: 'b'.repeat(40),
    summary: 'Change entity Request',
    author: 'Author User',
    userId: 'u1',
    client: 'studio',
    request: null,
    time: '2026-05-01T10:00:00Z',
  }
  const problem = (status: number, body: unknown) =>
    new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } })

  it('answers undone with the new revision and the diagnostics on a 200', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(json({ id: 'c'.repeat(40), diagnostics: [] }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await undoStudioRevision('Shop', 'a'.repeat(40))

    expect(result).toEqual({ status: 'undone', id: 'c'.repeat(40), diagnostics: [] })
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`/api/studio/apps/Shop/revisions/${'a'.repeat(40)}/undo`)
    expect(init.method).toBe('POST')
  })

  it('answers blocked with the later revisions on a 409 that lists them', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(
          problem(409, { title: 'A later revision changed the same files.', revisions: [blocker] }),
        ),
    )

    expect(await undoStudioRevision('Shop', 'a'.repeat(40))).toEqual({ status: 'blocked', revisions: [blocker] })
  })

  it('answers start on a 409 with no revisions', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValueOnce(problem(409, { title: 'The start of history cannot be undone.' })),
    )

    expect(await undoStudioRevision('Shop', 'a'.repeat(40))).toEqual({ status: 'start' })
  })

  it('throws a quota refusal, a 404 and an unreachable server as errors', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(problem(403, { reason: 'quota-exceeded', title: 'Storage is full.' }))
        .mockResolvedValueOnce(new Response(null, { status: 404 }))
        .mockResolvedValueOnce(new Response(null, { status: 503 })),
    )

    await expect(undoStudioRevision('Shop', 'a'.repeat(40))).rejects.toBeInstanceOf(QuotaRefusedError)
    const missing = await undoStudioRevision('Shop', 'a'.repeat(40)).catch((reason: unknown) => reason)
    expect(missing).toBeInstanceOf(Error)
    expect(missing).not.toBeInstanceOf(ServerUnreachableError)
    await expect(undoStudioRevision('Shop', 'a'.repeat(40))).rejects.toBeInstanceOf(ServerUnreachableError)
  })
})

describe('revisionAuthor and revisionClient', () => {
  const revision = (overrides: Partial<StudioRevision>): StudioRevision => ({
    id: 'a'.repeat(40),
    summary: 'Change entity Request',
    author: 'Author User',
    userId: 'u1',
    client: 'studio',
    request: null,
    time: '2026-05-01T10:00:00Z',
    ...overrides,
  })

  it('names the person, and says System for a revision the server made', () => {
    expect(revisionAuthor(revision({}), t)).toBe('Author User')
    expect(revisionAuthor(revision({ userId: null, author: 'Axis', client: null }), t)).toBe('System')
  })

  it('names Studio for studio, nothing for no client and the id of any other client', () => {
    expect(revisionClient(revision({ client: 'studio' }), t)).toBe('Studio')
    expect(revisionClient(revision({ client: null }), t)).toBeNull()
    expect(revisionClient(revision({ client: 'claude-desktop' }), t)).toBe('claude-desktop')
  })
})

const referenceEntry = (node: StudioReferenceNode): StudioReference => ({
  relation: 'uses',
  file: 'f.json',
  path: '/p',
  node,
})
const referenceResource = (kind: string, name: string): StudioReferenceNode => ({
  type: 'resource',
  id: `${kind}-${name}`,
  kind,
  name,
  file: `${kind}/${name}.json`,
})

describe('sortReferences', () => {
  it('groups resources in the sidebar kind order, then modules and packages, and sorts each group by name', () => {
    const entries = [
      referenceEntry({ type: 'package', id: 'pdf' }),
      referenceEntry({ type: 'module', id: 'organization' }),
      referenceEntry(referenceResource('page', 'Home')),
      referenceEntry({ type: 'text', key: 'common.save' }),
      referenceEntry(referenceResource('entity', 'Đơn hàng')),
      referenceEntry({ type: 'module', id: 'audit' }),
      referenceEntry(referenceResource('entity', 'Dự án')),
    ]

    const sorted = sortReferences(entries, 'vi')

    expect(
      sorted.map((reference) => (reference.node.type === 'resource' ? reference.node.name : reference.node)),
    ).toEqual([
      'Dự án',
      'Đơn hàng',
      'Home',
      { type: 'text', key: 'common.save' },
      { type: 'module', id: 'audit' },
      { type: 'module', id: 'organization' },
      { type: 'package', id: 'pdf' },
    ])
    expect(entries[0].node).toEqual({ type: 'package', id: 'pdf' })
  })
})
