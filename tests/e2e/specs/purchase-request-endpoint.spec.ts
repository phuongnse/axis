import { expect, test } from '../test'
import type { APIRequestContext, Page } from '@playwright/test'
import { useSession } from '../sessions'
import { totp, totpSecret } from '../totp'

// The purchasing system submits purchase requests through the sample's SubmitPurchaseRequest
// endpoint with a bearer token. Specs run in parallel against one database, so each test looks only
// for the request it creates. They use Operations, whose manager is manager-2, so that the per-department
// test keeps the Engineering total fixed.
const appPath = '/api/apps/PurchaseRequests'
const endpoint = `${appPath}/endpoints/SubmitPurchaseRequest`
const tokenUrl = `${process.env.AXIS_E2E_KEYCLOAK_URL}/realms/axis/protocol/openid-connect/token`
const purchasingClient = { client_id: 'purchasing-system', client_secret: 'purchasing-system-development-secret' }

/** Posts `form` to the development realm's token endpoint, and returns the access token. */
async function accessToken(request: APIRequestContext, form: Record<string, string>) {
  const response = await request.post(tokenUrl, { form })
  expect(response.status()).toBe(200)
  return ((await response.json()) as { access_token: string }).access_token
}

/** A client credentials token for the purchasing system, which holds the purchasing-system role. */
function purchasingSystemToken(request: APIRequestContext) {
  return accessToken(request, { grant_type: 'client_credentials', ...purchasingClient })
}

/**
 * The requester's token, issued through the purchasing-system client so that it has the endpoint audience.
 * The requester holds only the employee role.
 */
function annaToken(request: APIRequestContext) {
  return accessToken(request, {
    grant_type: 'password',
    ...purchasingClient,
    username: 'requester',
    password: 'axis',
    otp: totp(totpSecret('requester')),
  })
}

/** The id of the seeded department named `name`, read from the record API. */
async function departmentId(page: Page, name: string) {
  const response = await page.request.get(`${appPath}/entities/Department/records?pageSize=100`)
  expect(response.status()).toBe(200)
  const body = (await response.json()) as { items: { id: string; values: { name: string } }[] }
  const department = body.items.find((item) => item.values.name === name)
  expect(department).toBeDefined()
  return department!.id
}

/** A value made unique to this test run, for a title or an Idempotency-Key. */
function unique(prefix: string) {
  return `${prefix} ${Date.now()}-${test.info().retry}`
}

/**
 * Calls the endpoint with `token` for an Operations request titled `title`. The `request` fixture
 * sends no session cookie, and the call needs no CSRF header.
 */
function submit(
  request: APIRequestContext,
  token: string,
  department: string,
  title: string,
  idempotencyKey?: string,
) {
  return request.post(endpoint, {
    data: {
      input: {
        title,
        department,
        currency: 'VND',
        lineItems: [{ description: 'Desk', quantity: 1, unitPrice: 500 }],
      },
    },
    headers: {
      Authorization: `Bearer ${token}`,
      ...(idempotencyKey === undefined ? {} : { 'Idempotency-Key': idempotencyKey }),
    },
  })
}

/** Opens My tasks until it lists a task for the request titled `title`, and returns its row. */
async function waitForTask(page: Page, title: string) {
  const row = page.getByRole('row', { name: new RegExp(title) })
  // The worker creates the task some time after the start.
  await expect(async () => {
    await page.goto('/purchasing/mytasks?pageSize=100')
    await expect(row).toBeVisible({ timeout: 1_000 })
  }).toPass({ timeout: 30_000 })
  return row
}

type Submitted = { recordId: string; instanceId: string }

test('the purchasing system submits a request through the endpoint, and the department manager gets the approval task', async ({
  page,
  request,
}) => {
  await useSession(page, 'manager-2')
  const department = await departmentId(page, 'Operations')
  const title = unique('Endpoint desk')

  const response = await submit(request, await purchasingSystemToken(request), department, title)
  expect(response.status()).toBe(201)
  const body = (await response.json()) as Submitted
  expect(body.recordId).toBeDefined()
  expect(body.instanceId).toBeDefined()

  const row = await waitForTask(page, title)
  await expect(row.getByRole('link', { name: 'Manager approval', exact: true })).toBeVisible()
})

test('a repeated endpoint call with the same Idempotency-Key returns the first request, and the manager has one task', async ({
  page,
  request,
}) => {
  await useSession(page, 'manager-2')
  const department = await departmentId(page, 'Operations')
  const title = unique('Endpoint repeat')
  const key = unique('endpoint-repeat')
  const token = await purchasingSystemToken(request)

  const first = await submit(request, token, department, title, key)
  expect(first.status()).toBe(201)
  const firstBody = (await first.json()) as Submitted
  const second = await submit(request, token, department, title, key)
  expect(second.status()).toBe(201)
  const secondBody = (await second.json()) as Submitted
  // The worker may move the instance on between the calls, so only the ids are compared.
  expect(secondBody.recordId).toBe(firstBody.recordId)
  expect(secondBody.instanceId).toBe(firstBody.instanceId)

  await waitForTask(page, title)
  const tasks = await page.request.get(`${appPath}/tasks?pageSize=100`)
  expect(tasks.status()).toBe(200)
  const items = ((await tasks.json()) as { items: { subject: { id: string } }[] }).items
  expect(items.filter((item) => item.subject.id === firstBody.recordId)).toHaveLength(1)
})

test('a realm user without the purchasing-system role may not call the endpoint', async ({ page, request }) => {
  await useSession(page, 'manager-2')
  const department = await departmentId(page, 'Operations')

  const response = await submit(request, await annaToken(request), department, unique('Endpoint denied'))
  expect(response.status()).toBe(403)
  expect(((await response.json()) as { title: string }).title).toBe('You have no grant for this action.')
})
