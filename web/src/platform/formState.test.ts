import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchFormState, formStatePath } from './formState'

describe('formStatePath', () => {
  it('works out the form state endpoint from the records path of the entity', () => {
    expect(formStatePath('/api/apps/E2eApp/entities/Note/records', 'NoteSections')).toBe(
      '/api/apps/E2eApp/forms/NoteSections/state',
    )
  })
})

describe('fetchFormState', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('posts the body as JSON with the CSRF header and returns the answer', async () => {
    const answer = { sections: [], fields: { title: { visible: true, required: true, readOnly: false } }, errors: {} }
    const fetchMock = vi.fn(async (_input: string, _init?: RequestInit) => new Response(JSON.stringify(answer)))
    vi.stubGlobal('fetch', fetchMock)

    expect(await fetchFormState('/api/apps/E2eApp/forms/NoteSections/state', '{"values":{}}')).toEqual(answer)

    const [[url, init]] = fetchMock.mock.calls
    expect([url, init?.method, init?.body]).toEqual([
      '/api/apps/E2eApp/forms/NoteSections/state',
      'POST',
      '{"values":{}}',
    ])
    expect(new Headers(init?.headers).get('Content-Type')).toBe('application/json')
    expect(new Headers(init?.headers).get('X-Axis-Csrf')).toBe('1')
  })

  it('throws on any other status', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('{}', { status: 403 })),
    )

    await expect(fetchFormState('/state', '{}')).rejects.toThrow('status 403')
  })
})
