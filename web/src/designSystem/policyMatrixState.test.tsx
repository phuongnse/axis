import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { StudioPolicyMatrixTable } from '../pages/StudioPolicyMatrix'
import { TextProvider } from '../platform/TextProvider'
import type { StudioPolicyMatrix } from '../platform/studio'
import { ThemeModeProvider } from '../platform/theme'
import { application } from './fixtures'
import { previews } from './previews'

const state = previews.studio.find((item) => item.caption === 'Policy matrix')
const matrix = state!.fixtures![`/api/studio/apps/${application}/policy-matrix`]() as StudioPolicyMatrix

describe('the design-system Policy matrix state', () => {
  it('shows the Requester grant from RequesterPolicy as shared, with its Open policy link', async () => {
    const onOpenPolicy = vi.fn()
    render(
      <MemoryRouter>
        <ThemeModeProvider>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <StudioPolicyMatrixTable
              application={application}
              matrix={matrix}
              names={null}
              onOpenPolicy={onOpenPolicy}
              onWritten={vi.fn()}
            />
          </TextProvider>
        </ThemeModeProvider>
      </MemoryRouter>,
    )

    await userEvent.click(screen.getByTestId('studio-matrix-cell-Requester-entity-Request'))

    const grants = screen.getAllByTestId('studio-matrix-grant')
    expect(grants).toHaveLength(1)
    expect(within(grants[0]).getByText('RequesterPolicy')).toBeInTheDocument()
    expect(within(grants[0]).getByText('Shared')).toBeInTheDocument()
    expect(screen.queryByText('No grant.')).not.toBeInTheDocument()

    const requester = matrix.roles.find((role) => role.name === 'Requester')!
    const [grant] = requester.cells.find((cell) => cell.target === 'Request')!.grants
    await userEvent.click(within(grants[0]).getByRole('button', { name: 'Open policy' }))
    expect(onOpenPolicy).toHaveBeenCalledWith(grant.policyId)
  })
})
