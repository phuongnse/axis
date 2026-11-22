import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { emptyDescription } from '../test/antd'
import { StudioModulePolicy } from './StudioModulePolicy'

const policy = {
  roles: ['reviewer'],
  grants: [
    { name: 'readRequests', entity: 'PurchaseRequest', actions: ['read'] },
    { name: 'openRequests', dataSource: 'OpenRequests', actions: ['read'] },
  ],
}

function renderPolicy(value: Record<string, unknown> = policy) {
  const onChange = vi.fn()
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioModulePolicy value={value} onChange={onChange} />
      </TextProvider>
    </ThemeModeProvider>,
  )
  return onChange
}

describe('StudioModulePolicy', () => {
  it('draws the actions of each grant as a group named Actions with its help', () => {
    renderPolicy()

    for (const index of [0, 1]) {
      const grant = screen.getByTestId(`studio-module-policy-grant-${index}`)
      const group = within(grant).getByRole('group', {
        name: 'Actions',
        description: platformEnglish['shell.studio.matrix.actions.help'],
      })
      expect(within(group).getByRole('checkbox', { name: 'read' })).toBeChecked()
    }
  })

  it('sends the grant its new actions when an action is ticked', async () => {
    const onChange = renderPolicy()
    const group = within(screen.getByTestId('studio-module-policy-grant-1')).getByRole('group', { name: 'Actions' })

    await userEvent.click(within(group).getByRole('checkbox', { name: 'export' }))

    expect(onChange).toHaveBeenCalledWith('/grants/1/actions', ['read', 'export'])
  })

  it('names the target of each grant by its kind', () => {
    renderPolicy()

    expect(within(screen.getByTestId('studio-module-policy-grant-0')).getByText('Entity PurchaseRequest')).toBeVisible()
    expect(
      within(screen.getByTestId('studio-module-policy-grant-1')).getByText('Data source OpenRequests'),
    ).toBeVisible()
  })

  it('deletes a grant with its Delete button, and leaves the others', async () => {
    const onChange = renderPolicy()

    await userEvent.click(
      within(screen.getByTestId('studio-module-policy-grant-0')).getByRole('button', { name: 'Delete grant' }),
    )

    expect(onChange).toHaveBeenCalledWith('/grants', [policy.grants[1]])
  })

  it('turns off the Delete button when the policy has one grant left', () => {
    renderPolicy({ ...policy, grants: [policy.grants[0]] })

    expect(screen.getByRole('button', { name: 'Delete grant' })).toBeDisabled()
  })

  it('says "No grants yet." where the grants would be, when the policy has none', () => {
    renderPolicy({ ...policy, grants: [] })

    expect(emptyDescription('No grants yet.', screen.getByTestId('studio-module-policy-grants'))).toBeVisible()
  })

  it('joins the roles of the policy with the list format of the language', () => {
    renderPolicy({ ...policy, roles: ['reviewer', 'approver'] })

    expect(screen.getByText('reviewer and approver')).toBeVisible()
  })
})
