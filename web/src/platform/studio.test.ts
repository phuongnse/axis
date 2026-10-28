import { afterEach, describe, expect, it, vi } from 'vitest'
import { assignLocation } from './navigation'
import {
  checkStudioExpression,
  fetchStudioPolicyMatrix,
  fetchStudioResource,
  filterResources,
  folderDiffers,
  groupByKind,
  previewSiteUrl,
  putStudioTexts,
  releaseRows,
  ServerUnreachableError,
  type StudioReleaseItem,
  type StudioReleases,
  type StudioResourceItem,
  waitForServer,
} from './studio'

vi.mock('./navigation')

afterEach(() => vi.unstubAllGlobals())

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
    const groups = groupByKind(items)

    expect(groups.map((group) => group.kind)).toEqual(['application', 'entity', 'form'])
    expect(groups[1].items.map((item) => item.name)).toEqual(['Department', 'PurchaseRequest'])
  })

  it('sorts an item without a name by its file', () => {
    const groups = groupByKind([
      resource('1', 'entity', null, 'entities/b.json'),
      resource('2', 'entity', 'zeta', 'entities/a.json'),
      resource('3', 'entity', null, 'entities/a.json'),
    ])

    expect(groups[0].items.map((item) => item.id)).toEqual(['3', '1', '2'])
  })

  it('returns no group for no resources', () => {
    expect(groupByKind([])).toEqual([])
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
  }
}

describe('releaseRows', () => {
  const items = [
    release('r2', 2, '2026-05-02T10:00:00Z'),
    release('r1', 1, '2026-05-01T10:00:00Z'),
    release('r2', 2, '2026-05-03T10:00:00Z'),
    release('r3', 3, '2026-05-03T10:00:00Z'),
  ]

  it('sorts newest first, keeping the server order on equal times', () => {
    const rows = releaseRows({ active: null, folderContentHash: null, items })

    expect(rows.map((row) => row.number)).toEqual([2, 3, 2, 1])
    expect(rows.map((row) => row.active)).toEqual([false, false, false, false])
  })

  it('marks only the newest row of the active release', () => {
    const rows = releaseRows({
      active: { releaseId: 'r2', number: 2, contentHash: 'hash-2' },
      folderContentHash: null,
      items,
    })

    expect(rows.map((row) => row.active)).toEqual([true, false, false, false])
  })
})

describe('folderDiffers', () => {
  const active = { releaseId: 'r1', number: 1, contentHash: 'hash-1' }
  const history = (overrides: Partial<StudioReleases>): StudioReleases => ({
    active,
    folderContentHash: 'hash-1',
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
