import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { optionTexts } from '../test/antd'
import { SiteSignIn } from './SiteSignIn'

function renderPicker(value: unknown, providers = ['accounts', 'company']) {
  const onChange = vi.fn()
  render(
    <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
      <SiteSignIn providers={providers} value={value} onChange={onChange} />
    </TextProvider>,
  )
  return onChange
}

describe('SiteSignIn', () => {
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
    expect(screen.queryByTestId('studio-sign-in-all')).not.toBeInTheDocument()

    await userEvent.click(within(screen.getByTestId('studio-field-/signIn/0')).getByRole('button', { name: 'Remove' }))

    expect(onChange).toHaveBeenLastCalledWith('/signIn', undefined)
  })

  it('says that the site allows every provider while it lists none', () => {
    renderPicker(undefined)

    expect(screen.getByTestId('studio-sign-in-all')).toBeVisible()
  })
})
