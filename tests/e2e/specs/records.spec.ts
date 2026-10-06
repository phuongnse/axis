import { expect, test } from '@playwright/test'

// Specs run in parallel against one database, so this test reads only the record it creates.
test('a record of the E2E application is created and read through the record API', async ({ request }) => {
  const created = await request.post('/api/apps/E2eApp/entities/Note/records', {
    data: { values: { title: 'Smoke test', done: false } },
  })
  expect(created.status()).toBe(201)
  const record = await created.json()

  const read = await request.get(`/api/apps/E2eApp/entities/Note/records/${record.id}`)

  expect(read.status()).toBe(200)
  expect(await read.json()).toEqual({ id: record.id, version: 1, values: { title: 'Smoke test', done: false } })
})
