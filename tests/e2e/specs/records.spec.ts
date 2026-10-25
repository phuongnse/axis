import { expect, test } from '../test'
import { csrfHeaders } from '../csrf'
import { sessionFile } from '../sessions'

// The record API needs a session with a grant, so these journeys start signed in as the employee anna.
test.use({ storageState: sessionFile('anna') })

// Specs run in parallel against one database, so this test reads only the record it creates.
test('a record of the E2E application is created and read through the record API', async ({ request }) => {
  const created = await request.post('/api/apps/E2eApp/entities/Note/records', {
    data: { values: { title: 'Smoke test', done: false } },
    headers: csrfHeaders,
  })
  expect(created.status()).toBe(201)
  const record = await created.json()

  const read = await request.get(`/api/apps/E2eApp/entities/Note/records/${record.id}`)

  expect(read.status()).toBe(200)
  expect(await read.json()).toEqual({
    id: record.id,
    version: 1,
    values: {
      title: 'Smoke test',
      code: null,
      priority: null,
      amount: null,
      done: false,
      dueOn: null,
      dueAt: null,
      status: null,
      category: null,
      // Every create has a session now, so the creator is the signed-in user.
      createdBy: 'anna',
      lines: [],
    },
    // A collection whose rows have references holds one label object per row, none here.
    labels: { lines: [] },
  })
})
