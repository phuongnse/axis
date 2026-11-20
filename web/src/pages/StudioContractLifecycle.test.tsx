import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import type { ContractEdge, ContractState } from './contractView'
import StudioContractLifecycle from './StudioContractLifecycle'

const states: ContractState[] = [
  { name: 'open', initial: true, deletable: true, points: [] },
  { name: 'checked', initial: false, deletable: false, points: [] },
  { name: 'closed', initial: false, deletable: false, points: [] },
]

const edges: ContractEdge[] = [
  { id: 'check', name: 'check', from: 'open', to: 'checked', guard: true },
  { id: 'reopen', name: 'reopen', from: 'checked', to: 'open', guard: false },
  { id: 'close', name: 'close', from: 'checked', to: 'closed', guard: false },
]

function renderLifecycle() {
  return render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioContractLifecycle states={states} edges={edges} />
      </TextProvider>
    </ThemeModeProvider>,
  )
}

/** The x of a state's node, from the translate of its wrapper. The layout is computed by ELK, so it is awaited. */
async function stateX(name: string): Promise<number> {
  const node = (
    await screen.findByTestId(`studio-contract-state-${name}`, undefined, { timeout: 5000 })
  ).closest<HTMLElement>('.react-flow__node')!
  const match = /translate\((-?[\d.]+)px,\s*(-?[\d.]+)px\)/.exec(node.style.transform)
  expect(match, `the transform of ${name}: ${node.style.transform}`).not.toBeNull()
  return Number(match![1])
}

describe('StudioContractLifecycle', () => {
  it('lays the states out from left to right', async () => {
    renderLifecycle()

    const open = await stateX('open')
    const checked = await stateX('checked')
    const closed = await stateX('closed')

    expect(open).toBeLessThan(checked)
    expect(checked).toBeLessThan(closed)
  })

  it('draws each transition as a route with its label, and no React Flow attribution', async () => {
    const { container } = renderLifecycle()

    const lifecycle = await screen.findByTestId('studio-contract-lifecycle')
    await stateX('open')

    expect(within(lifecycle).getByLabelText('open to checked, check, Guard')).toBeInTheDocument()
    expect(within(lifecycle).getByLabelText('checked to open, reopen, No guard')).toBeInTheDocument()
    expect(within(lifecycle).getByText('check, Guard')).toBeInTheDocument()
    expect(container.querySelector('.react-flow__attribution')).toBeNull()
  })
})
