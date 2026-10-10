import { describe, expect, it, vi } from 'vitest'
import { newIdempotencyKey, startProcess } from './processes'

const instancesPath = '/api/apps/E2eApp/processes/NoteClose/instances'
const subjectId = '6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7'
const uuidV4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

describe('startProcess', () => {
  it('posts the subject with the key and returns ok on 201', async () => {
    const fetchMock = vi.fn(async (_input: string, _init?: RequestInit) => new Response('{}', { status: 201 }))
    vi.stubGlobal('fetch', fetchMock)

    expect(await startProcess(instancesPath, subjectId, 'key-1')).toEqual({ ok: true })

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(instancesPath)
    expect(init?.method).toBe('POST')
    expect(init?.body).toBe(`{"subjectId":"${subjectId}"}`)
    expect(new Headers(init?.headers).get('Idempotency-Key')).toBe('key-1')
  })

  it.each([400, 404, 409, 422])('returns the problem of a %i as a final answer', async (status) => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(`{"title":"No","status":${status},"errors":{"/subjectId":["x"]}}`, { status })),
    )

    expect(await startProcess(instancesPath, subjectId, 'key-1')).toEqual({
      ok: false,
      problem: { status, title: 'No', errors: { '/subjectId': ['x'] } },
    })
  })

  it('throws on a 5xx, which is no final answer', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('', { status: 503 })),
    )

    await expect(startProcess(instancesPath, subjectId, 'key-1')).rejects.toThrow('503')
  })
})

describe('newIdempotencyKey', () => {
  it('gives a new version 4 UUID each time, also where randomUUID is missing', () => {
    expect(newIdempotencyKey()).toMatch(uuidV4)
    expect(newIdempotencyKey()).not.toBe(newIdempotencyKey())

    // An insecure context has no randomUUID.
    const original = globalThis.crypto
    vi.stubGlobal('crypto', { getRandomValues: (bytes: Uint8Array<ArrayBuffer>) => original.getRandomValues(bytes) })
    expect(newIdempotencyKey()).toMatch(uuidV4)
    expect(newIdempotencyKey()).not.toBe(newIdempotencyKey())
  })
})
