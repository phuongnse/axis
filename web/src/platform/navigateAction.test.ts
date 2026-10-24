import { afterEach, describe, expect, it, vi } from 'vitest'
import { navigatePath, navigateTarget, navigateValue } from './navigateAction'
import type { NavigateAction } from './site'

const scope = {
  id: 'row-1',
  values: { Title: 'Hello', Count: '3', Done: false, Note: null, id: 'field-id', Items: [{ a: '1' }] },
}

function action(overrides: Partial<NavigateAction> = {}): NavigateAction {
  return {
    type: 'navigate',
    page: 'TicketDetail',
    labelKey: 'x.open',
    parameters: {},
    record: null,
    opensForm: false,
    paths: {},
    visibleWhen: null,
    ...overrides,
  }
}

describe('navigateValue', () => {
  it('matches a field name ignoring case', () => {
    expect(navigateValue('title', scope)).toBe('Hello')
    expect(navigateValue(' TITLE ', scope)).toBe('Hello')
  })

  it('reads id from the record id before a field of that name', () => {
    expect(navigateValue('id', scope)).toBe('row-1')
    expect(navigateValue('ID', scope)).toBe('row-1')
  })

  it('reads literals', () => {
    expect(navigateValue("'it''s'", null)).toBe("it's")
    expect(navigateValue('42', null)).toBe('42')
    expect(navigateValue('-1.5', null)).toBe('-1.5')
    expect(navigateValue('true', null)).toBe(true)
    expect(navigateValue('FALSE', null)).toBe(false)
    expect(navigateValue('null', null)).toBeNull()
  })

  it('returns undefined for what it cannot work out', () => {
    expect(navigateValue('concat(a, b)', scope)).toBeUndefined()
    expect(navigateValue('missing', scope)).toBeUndefined()
    expect(navigateValue('items', scope)).toBeUndefined()
    expect(navigateValue('title', null)).toBeUndefined()
    expect(navigateValue('id', { id: null, values: {} })).toBeUndefined()
  })
})

describe('navigatePath', () => {
  it('puts parameters in the query in declaration order', () => {
    expect(navigatePath('e2e', action({ parameters: { ticket: 'id', title: 'title', flag: 'true' } }), scope)).toBe(
      '/e2e/ticketdetail?ticket=row-1&title=Hello&flag=true',
    )
  })

  it('leaves out a parameter whose value is null', () => {
    expect(navigatePath('e2e', action({ parameters: { note: 'note', ticket: 'id' } }), scope)).toBe(
      '/e2e/ticketdetail?ticket=row-1',
    )
  })

  it('opens the record with record', () => {
    expect(navigatePath('e2e', action({ page: 'NoteForm', opensForm: true, record: 'id' }), scope)).toBe(
      '/e2e/noteform/row-1',
    )
  })

  it('opens a form page without record at /new', () => {
    expect(navigatePath('e2e', action({ page: 'NoteForm', opensForm: true }), null)).toBe('/e2e/noteform/new')
  })

  it('returns null when record is null or not a string', () => {
    expect(navigatePath('e2e', action({ record: 'note' }), scope)).toBeNull()
    expect(navigatePath('e2e', action({ record: 'done' }), scope)).toBeNull()
    expect(navigatePath('e2e', action({ record: "''" }), scope)).toBeNull()
  })

  it('returns null when an expression is unsupported', () => {
    expect(navigatePath('e2e', action({ parameters: { a: 'concat(x)' } }), scope)).toBeNull()
    expect(navigatePath('e2e', action({ record: 'concat(x)' }), scope)).toBeNull()
    expect(navigatePath('e2e', action({ parameters: { a: 'id' } }), null)).toBeNull()
  })

  it('resolves literals without a scope', () => {
    expect(navigatePath('e2e', action({ parameters: { kind: "'open'" } }), null)).toBe('/e2e/ticketdetail?kind=open')
  })
})

describe('navigateTarget', () => {
  const customersPath = '/api/apps/E2eApp/entities/Customer/records'
  const orderScope = { id: 'order-1', values: { customer: 'c-1', title: 'Hello', missing: null, done: false } }
  const paths = { customer: customersPath, 'customer.parent': customersPath }

  const customerJson = (id: string, values: Record<string, string | null>) =>
    JSON.stringify({ id, version: 1, values, labels: {} })

  /** Stubs `fetch` with a handler per URL and returns the requested URLs. */
  function stubRecords(handler: (url: string) => Response) {
    const fetchMock = vi.fn(async (input: string) => handler(input))
    vi.stubGlobal('fetch', fetchMock)
    return () => fetchMock.mock.calls.map(([url]) => url)
  }

  afterEach(() => vi.unstubAllGlobals())

  it('reads each record of a two-hop path and puts the value in the query', async () => {
    const requested = stubRecords((url) =>
      url === `${customersPath}/c-1`
        ? new Response(customerJson('c-1', { parent: 'c-2', name: 'Child' }))
        : new Response(customerJson('c-2', { name: 'Acme' })),
    )
    const open = navigateTarget('e2e', action({ parameters: { name: 'customer.parent.name' }, paths }), orderScope)

    expect(await open!()).toBe('/e2e/ticketdetail?name=Acme')
    expect(requested()).toEqual([`${customersPath}/c-1`, `${customersPath}/c-2`])
  })

  it('matches the names along a path ignoring case', async () => {
    stubRecords(() => new Response(customerJson('c-1', { Name: 'Acme' })))
    const open = navigateTarget('e2e', action({ parameters: { name: 'CUSTOMER.name' }, paths }), orderScope)

    expect(await open!()).toBe('/e2e/ticketdetail?name=Acme')
  })

  it('uses a path as the record id', async () => {
    stubRecords(() => new Response(customerJson('c-1', { parent: 'c-2' })))
    const open = navigateTarget(
      'e2e',
      action({ page: 'CustomerForm', opensForm: true, record: 'customer.parent', paths }),
      orderScope,
    )

    expect(await open!()).toBe('/e2e/customerform/c-2')
  })

  it('leaves out a parameter whose reference is null, without a request', async () => {
    const requested = stubRecords(() => new Response('{}', { status: 500 }))
    const open = navigateTarget(
      'e2e',
      action({ parameters: { name: 'missing.name', title: 'title' }, paths: { missing: customersPath } }),
      orderScope,
    )

    expect(await open!()).toBe('/e2e/ticketdetail?title=Hello')
    expect(requested()).toEqual([])
  })

  it('leaves out a parameter whose reference is null at a later hop', async () => {
    stubRecords(() => new Response(customerJson('c-1', { parent: null })))
    const open = navigateTarget('e2e', action({ parameters: { name: 'customer.parent.name' }, paths }), orderScope)

    expect(await open!()).toBe('/e2e/ticketdetail')
  })

  it('opens no page when a read is refused, a record is missing or a field is missing', async () => {
    for (const answer of [
      () => new Response('{}', { status: 403 }),
      () => new Response('{}', { status: 404 }),
      () => new Response('{}', { status: 500 }),
      () => new Response(customerJson('c-1', { code: 'A' })),
    ]) {
      stubRecords(answer)
      const open = navigateTarget('e2e', action({ parameters: { name: 'customer.name' }, paths }), orderScope)

      expect(await open!()).toBeNull()
    }
  })

  it('opens no page when the request fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('network')))
    const open = navigateTarget('e2e', action({ parameters: { name: 'customer.name' }, paths }), orderScope)

    expect(await open!()).toBeNull()
  })

  it('opens no page when a path used as record ends at null', async () => {
    stubRecords(() => new Response(customerJson('c-1', { parent: null })))
    const open = navigateTarget('e2e', action({ record: 'customer.parent', paths }), orderScope)

    expect(await open!()).toBeNull()
  })

  it('reads a record once when several expressions pass through it', async () => {
    const requested = stubRecords(() => new Response(customerJson('c-1', { name: 'Acme', code: 'A1' })))
    const open = navigateTarget(
      'e2e',
      action({ parameters: { name: 'customer.name', code: 'customer.code' }, paths }),
      orderScope,
    )

    expect(await open!()).toBe('/e2e/ticketdetail?name=Acme&code=A1')
    expect(requested()).toHaveLength(1)
  })

  it('hides the button when paths has no entry for a hop', () => {
    expect(navigateTarget('e2e', action({ parameters: { name: 'customer.name' } }), orderScope)).toBeNull()
    expect(
      navigateTarget(
        'e2e',
        action({ parameters: { name: 'customer.parent.name' }, paths: { customer: customersPath } }),
        orderScope,
      ),
    ).toBeNull()
  })

  it('hides the button when the first name is not in the scope, is id, or is not a reference value', () => {
    const open = (expression: string, scope: typeof orderScope | null = orderScope) =>
      navigateTarget('e2e', action({ parameters: { name: expression }, paths }), scope)

    expect(open('other.name')).toBeNull()
    expect(open('id.name')).toBeNull()
    expect(open('done.name')).toBeNull()
    expect(open('customer.name', null)).toBeNull()
  })

  it('hides the button when another expression is unsupported', () => {
    expect(
      navigateTarget('e2e', action({ parameters: { name: 'customer.name', a: 'concat(title)' }, paths }), orderScope),
    ).toBeNull()
  })

  it('opens an address without a path as navigatePath does, and hides it when it cannot be built', async () => {
    const open = navigateTarget('e2e', action({ parameters: { title: 'title' } }), orderScope)

    expect(await open!()).toBe('/e2e/ticketdetail?title=Hello')
    expect(navigateTarget('e2e', action({ record: 'missing' }), orderScope)).toBeNull()
  })
})
