import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import purchaseRequestApproval from '../../../samples/apps/purchase-requests/processes/purchase-request-approval.json'
import type { StudioResourceItem } from '../platform/studio'
import { setAt } from '../platform/jsonPatch'
import {
  loadStudioSchemas,
  slow,
  stubResource,
  renderResource,
  sentPatch,
  expressionView,
  optionOf,
  pick,
} from '../test/studioEditor'

// The schema is a chunk of its own. Loading it here keeps the first test from racing its first lookup.
beforeAll(loadStudioSchemas)

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

const processId = purchaseRequestApproval.id

const processItem: StudioResourceItem = {
  id: processId,
  kind: 'process',
  name: 'PurchaseRequestApproval',
  file: 'processes/purchase-request-approval.json',
  version: 'r1',
  usedBy: 0,
}

/** The purchase request process with an unknown key on the operation `submit` and on the decision's first branch. */
const processContent = {
  ...purchaseRequestApproval,
  steps: purchaseRequestApproval.steps.map((step, index) => {
    if (index === 0) {
      return { ...step, 'x-note': 'keep' }
    }
    if (index === 2) {
      return {
        ...step,
        'x-note': 'keep',
        branches: [
          { when: 'totalAmount >= FinanceThreshold()', next: 'financeApproval', 'x-note': 'keep' },
          { when: 'totalAmount > 0', next: 'markApproved' },
        ],
      }
    }
    return step
  }),
}

function sentSteps(fetchMock: ReturnType<typeof vi.fn>): Record<string, unknown>[] {
  return sentPatch(fetchMock).find((operation) => operation.path === '/steps')!.value as Record<string, unknown>[]
}

describe('StudioEditor for a process', () => {
  it('edits a branch and the otherwise of a decision, keeping the unknown keys', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)

    const when = await screen.findByTestId('studio-field-/steps/2/branches/0/when', {}, slow)
    const view = await expressionView(when)
    act(() => view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: 'totalAmount > 5' } }))
    await pick(screen.getByTestId('studio-field-/steps/2/branches/1/next'), 'markRejected')
    await pick(screen.getByTestId('studio-field-/steps/2/otherwise'), 'markRejected')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([
      { op: 'replace', path: '/steps/2/branches/0/when', value: 'totalAmount > 5' },
      { op: 'replace', path: '/steps/2/branches/1/next', value: 'markRejected' },
      { op: 'replace', path: '/steps/2/otherwise', value: 'markRejected' },
    ])
  })

  it('adds a branch, keeping the unknown keys of the others', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)

    const branches = await screen.findByTestId('studio-field-/steps/2/branches', {}, slow)
    await userEvent.click(within(branches).getByRole('button', { name: 'Add branch' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([
      {
        op: 'replace',
        path: '/steps/2/branches',
        value: [...processContent.steps[2].branches!, { when: '', next: '' }],
      },
    ])
  })

  it('removes a branch, keeping the unknown keys of the others', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)

    const second = await screen.findByTestId('studio-field-/steps/2/branches/1', {}, slow)
    await userEvent.click(within(second).getByRole('button', { name: 'Remove branch' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([
      { op: 'replace', path: '/steps/2/branches', value: [processContent.steps[2].branches![0]] },
    ])
  })

  it('renames the end step with a patch of only its name', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)

    const name = within(await screen.findByTestId('studio-step-9', {}, slow)).getByRole('textbox', {
      name: 'Step name',
    })
    await userEvent.type(name, '2')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([{ op: 'replace', path: '/steps/9/name', value: 'done2' }])
  })

  it('adds a step of the chosen type with its required keys empty', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)

    await userEvent.click(await screen.findByRole('combobox', { name: 'Add step' }, slow))
    await userEvent.click(await optionOf('decision'))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const steps = sentSteps(fetchMock)
    expect(steps).toHaveLength(11)
    expect(steps.slice(0, 10)).toEqual(processContent.steps)
    expect(steps[10]).toEqual({ name: '', type: 'decision', branches: [], otherwise: '' })
  })

  it('removes a step and leaves the transitions that name it', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)

    const card = await screen.findByTestId('studio-step-4', {}, slow)
    expect(card).toHaveTextContent('markApproved')
    await userEvent.click(within(card).getByRole('button', { name: 'Remove step' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const steps = sentSteps(fetchMock)
    expect(steps).toEqual(processContent.steps.filter((step) => step.name !== 'markApproved'))
    expect(steps[2].otherwise).toBe('markApproved')
    expect(sentPatch(fetchMock)).toHaveLength(1)
  })

  it('moves a step up and down in file order', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)

    const first = await screen.findByTestId('studio-step-0', {}, slow)
    expect(within(first).getByRole('button', { name: 'Move up' })).toBeDisabled()
    expect(within(screen.getByTestId('studio-step-9')).getByRole('button', { name: 'Move down' })).toBeDisabled()
    await userEvent.click(within(first).getByRole('button', { name: 'Move down' }))
    await userEvent.click(within(screen.getByTestId('studio-step-9')).getByRole('button', { name: 'Move up' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    // Steps that swap places in an array of the same length are patched item by item, so apply the patch.
    let moved: unknown = processContent
    for (const operation of sentPatch(fetchMock)) {
      moved = setAt(moved, operation.path, operation.op === 'remove' ? undefined : operation.value)
    }
    const steps = (moved as typeof processContent).steps
    const [submit, managerApproval, ...rest] = processContent.steps
    const [markRejected, done] = [rest[rest.length - 2], rest[rest.length - 1]]
    expect(steps).toEqual([managerApproval, submit, ...rest.slice(0, -2), done, markRejected])
  })

  it.each(['/steps/1/outcomes/0/label/textKey', '/steps/1/label/textKey'])(
    'opens the form at the field for the path %s inside a task',
    async (path) => {
      stubResource(processItem, processContent)
      renderResource(processItem, undefined, vi.fn(), path)

      await screen.findByTestId('studio-step-1', {}, slow)
      await waitFor(() => expect(screen.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'true'))
    },
  )

  it('opens the form at the transition of an outcome for its path', async () => {
    stubResource(processItem, processContent)
    renderResource(processItem, undefined, vi.fn(), '/steps/1/outcomes/0/next')

    await screen.findByTestId('studio-step-1', {}, slow)
    await waitFor(() => expect(screen.getByRole('tab', { name: 'Form' })).toHaveAttribute('aria-selected', 'true'))
  })

  it('changes a step type: forbidden keys go, required keys are added empty, the others stay', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)

    const card = await screen.findByTestId('studio-step-0', {}, slow)
    await pick(within(card).getByTestId('studio-field-/steps/0/type'), 'decision')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    expect(sentPatch(fetchMock)).toEqual([
      { op: 'replace', path: '/steps/0/type', value: 'decision' },
      { op: 'remove', path: '/steps/0/operation' },
      { op: 'remove', path: '/steps/0/set' },
      { op: 'remove', path: '/steps/0/next' },
      { op: 'add', path: '/steps/0/branches', value: [] },
      { op: 'add', path: '/steps/0/otherwise', value: '' },
    ])
  })

  it('changes a step to a task with its required keys empty and keeps the unknown key', async () => {
    const { fetchMock } = stubResource(processItem, processContent)
    renderResource(processItem)

    const card = await screen.findByTestId('studio-step-0', {}, slow)
    await pick(within(card).getByTestId('studio-field-/steps/0/type'), 'task')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await screen.findByTestId('studio-saved')
    const operations = sentPatch(fetchMock)
    expect(operations.map((operation) => operation.path)).toEqual([
      '/steps/0/type',
      '/steps/0/operation',
      '/steps/0/set',
      '/steps/0/next',
      '/steps/0/label',
      '/steps/0/assignee',
      '/steps/0/form',
      '/steps/0/outcomes',
    ])
    expect(operations.slice(4).map((operation) => operation.value)).toEqual([{ textKey: '' }, {}, '', []])
  })
})
