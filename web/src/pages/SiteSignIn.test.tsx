import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider } from 'antd'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { designTokensFor } from '../platform/designTokens'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { emptyDescription, optionTexts, tabTo } from '../test/antd'
import { withAntdStyles } from '../test/antdStyles'
import { SiteSignIn } from './SiteSignIn'

function renderPicker(value: unknown, providers = ['accounts', 'company'], readOnly = false) {
  const onChange = vi.fn()
  render(
    <ConfigProvider componentDisabled={readOnly}>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <SiteSignIn providers={providers} value={value} onChange={onChange} />
        </TextProvider>
      </ThemeModeProvider>
    </ConfigProvider>,
  )
  return onChange
}

describe('SiteSignIn', () => {
  it('puts Add provider on the right of the heading and nowhere else in the part', () => {
    renderPicker(['company'])

    const part = screen.getByTestId('studio-section-signIn')
    const body = screen.getByTestId('studio-field-/signIn')
    expect(within(part).getAllByRole('button', { name: 'Add provider' })).toHaveLength(1)
    expect(within(body).queryByRole('button', { name: 'Add provider' })).toBeNull()
    expect(
      within(part).getByRole('button', { name: 'Add provider' }).compareDocumentPosition(body) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()
  })

  it('shows no Add provider and no Remove when the editor is read-only', () => {
    renderPicker(['company'], ['accounts', 'company'], true)

    expect(screen.getByTestId('studio-field-/signIn/0')).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Add provider' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Remove' })).toBeNull()
    expect(screen.queryAllByRole('button', { name: 'Actions' })).toHaveLength(0)
  })

  it('lists only the providers it was given', async () => {
    renderPicker(['company'])

    await userEvent.click(within(screen.getByTestId('studio-field-/signIn/0')).getByRole('combobox'))

    expect(optionTexts()).toEqual(['accounts', 'company'])
  })

  it('writes the provider picked in a row at its index', async () => {
    const onChange = renderPicker(['company', ''])

    await userEvent.click(within(screen.getByTestId('studio-field-/signIn/1')).getByRole('combobox'))
    await userEvent.click(await screen.findByTitle('accounts'))

    expect(onChange).toHaveBeenLastCalledWith('/signIn/1', 'accounts')
  })

  it('adds an empty row after the others', async () => {
    const onChange = renderPicker(['company'])

    await userEvent.click(screen.getByRole('button', { name: 'Add provider' }))

    expect(onChange).toHaveBeenLastCalledWith('/signIn', ['company', ''])
  })

  it('removes a row and keeps the others', async () => {
    const onChange = renderPicker(['accounts', 'company'])

    await userEvent.click(within(screen.getByTestId('studio-field-/signIn/0')).getByRole('button', { name: 'Remove' }))

    expect(onChange).toHaveBeenLastCalledWith('/signIn', ['company'])
  })

  it('removes the key with the last row and says that the site then allows every provider', async () => {
    const onChange = renderPicker(['company'])
    expect(screen.queryByText(platformEnglish['shell.studio.site.allProvidersHint'])).toBeNull()

    await userEvent.click(within(screen.getByTestId('studio-field-/signIn/0')).getByRole('button', { name: 'Remove' }))

    expect(onChange).toHaveBeenLastCalledWith('/signIn', undefined)
  })

  it('says that the site allows every provider while it lists none', () => {
    renderPicker(undefined)

    expect(emptyDescription(platformEnglish['shell.studio.site.allProvidersHint'])).toBeVisible()
  })

  describe('with antd styles', () => {
    withAntdStyles()

    beforeEach(() => localStorage.setItem('axis.themeMode', 'dark'))

    it('draws Remove as the trash icon in dangerText and names it on keyboard focus in dark mode', async () => {
      renderPicker(['accounts', 'company'])

      const row = screen.getByTestId('studio-field-/signIn/0')
      const remove = within(row).getByRole('button', { name: 'Remove' })
      expect(remove.querySelector('.anticon-delete')).not.toBeNull()
      expect(remove).toHaveStyle({ color: designTokensFor('dark').colour.dangerText })
      expect(within(row).queryByRole('button', { name: 'Actions' })).toBeNull()
      await tabTo(remove)

      // Other tooltips the tab passed may stay open, so the one of Remove is found among them.
      await waitFor(() => expect(screen.getAllByRole('tooltip').map((tip) => tip.textContent)).toContain('Remove'))
    })
  })
})
