import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  fetchStudioResource,
  filterResources,
  groupByKind,
  ServerUnreachableError,
  type StudioResourceItem,
  waitForServer,
} from './studio'

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
