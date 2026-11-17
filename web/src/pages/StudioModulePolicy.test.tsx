import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioModulePolicy } from './StudioModulePolicy'

const policy = {
  roles: ['reviewer'],
  grants: [
    { name: 'readRequests', entity: 'PurchaseRequest', actions: ['read'] },
    { name: 'openRequests', dataSource: 'OpenRequests', actions: ['read'] },
  ],
}

function renderPolicy() {
  const onChange = vi.fn()
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioModulePolicy value={policy} onChange={onChange} />
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
})
