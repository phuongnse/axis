import { describe, expect, it, vi } from 'vitest'
import { fetchRecords, parseRecordJson } from './records'

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
})
