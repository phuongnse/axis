import { describe, expect, it, vi } from 'vitest'
import { fetchRecord, fetchRecords, parseRecordJson, RecordQueryProblem, saveRecord } from './records'

describe('parseRecordJson', () => {
  it('keeps the source text of numbers and leaves strings unchanged', () => {
    expect(parseRecordJson('{"a":1250.50,"b":12345678901234567890,"c":"x","d":[1e3,true,null]}')).toEqual({
      a: '1250.50',
      b: '12345678901234567890',
      c: 'x',
      d: ['1e3', true, null],
    })
  })
})

describe('fetchRecords', () => {
  it('keeps the source text of values and turns the metadata back into numbers', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(
            '{"items":[{"id":"a","version":3,"values":{"price":1250.50},"labels":{}}],"page":2,"pageSize":10,"totalCount":45}',
          ),
      ),
    )

    const page = await fetchRecords('/api/apps/E2eApp/entities/Note/records', 'page=2&pageSize=10')

    expect(page).toEqual({
      items: [{ id: 'a', version: 3, values: { price: '1250.50' }, labels: {} }],
      page: 2,
      pageSize: 10,
      totalCount: 45,
    })
  })

  it('throws the errors of a 400 problem by parameter name', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response('{"title":"One or more validation errors occurred.","errors":{"statusFilter":["Bad."]}}', {
            status: 400,
          }),
      ),
    )

    const error: unknown = await fetchRecords('/api/apps/E2eApp/data-sources/NoteList/rows', 'statusFilter=x').catch(
      (caught: unknown) => caught,
    )

    expect(error).toBeInstanceOf(RecordQueryProblem)
    expect((error as RecordQueryProblem).errors).toEqual({ statusFilter: ['Bad.'] })
  })
})

const recordsPath = '/api/apps/E2eApp/entities/Note/records'

describe('fetchRecord', () => {
  it('keeps the source text of values and turns the version back into a number', async () => {
    const fetchMock = vi.fn(
      async (_input: string) => new Response('{"id":"a","version":2,"values":{"price":1250.50},"labels":{}}'),
    )
    vi.stubGlobal('fetch', fetchMock)

    const record = await fetchRecord(recordsPath, 'a')

    expect(fetchMock.mock.calls[0][0]).toBe(`${recordsPath}/a`)
    expect(record).toEqual({ id: 'a', version: 2, values: { price: '1250.50' }, labels: {} })
  })

  it('returns null for an unknown record', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('{}', { status: 404 })),
    )

    expect(await fetchRecord(recordsPath, 'a')).toBeNull()
  })
})

describe('saveRecord', () => {
  it('creates with POST and updates with PATCH, sending the body text as JSON', async () => {
    const fetchMock = vi.fn(
      async (_input: string, _init?: RequestInit) =>
        new Response('{"id":"a","version":1,"values":{"amount":12.50},"labels":{}}', { status: 201 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const created = await saveRecord(recordsPath, null, '{"values":{"amount":12.50}}')
    await saveRecord(recordsPath, 'a', '{"version":1,"values":{}}')

    expect(created).toEqual({ ok: true, record: { id: 'a', version: 1, values: { amount: '12.50' }, labels: {} } })
    const [[createUrl, create], [updateUrl, update]] = fetchMock.mock.calls
    expect([createUrl, create?.method, create?.body]).toEqual([recordsPath, 'POST', '{"values":{"amount":12.50}}'])
    expect(create?.headers).toEqual({ 'Content-Type': 'application/json' })
    expect([updateUrl, update?.method]).toEqual([`${recordsPath}/a`, 'PATCH'])
  })

  it('returns a 400 or 409 problem with its errors, which default to none', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(
          new Response('{"title":"Invalid","status":400,"errors":{"/values/title":["Required."]}}', { status: 400 }),
        )
        .mockResolvedValueOnce(new Response('{"title":"Conflict","status":409}', { status: 409 })),
    )

    expect(await saveRecord(recordsPath, null, '{"values":{}}')).toEqual({
      ok: false,
      problem: { status: 400, title: 'Invalid', errors: { '/values/title': ['Required.'] } },
    })
    expect(await saveRecord(recordsPath, 'a', '{"version":1,"values":{}}')).toEqual({
      ok: false,
      problem: { status: 409, title: 'Conflict', errors: {} },
    })
  })

  it('throws on any other status', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('{}', { status: 500 })),
    )

    await expect(saveRecord(recordsPath, null, '{"values":{}}')).rejects.toThrow('status 500')
  })
})
