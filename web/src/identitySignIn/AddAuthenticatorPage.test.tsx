import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { identityAddAuthenticatorContext, identityQrCode } from '../designSystem/fixtures'
import { AddAuthenticatorPage } from './AddAuthenticatorPage'
import type { AddAuthenticatorContext } from './signInContext'

const context: AddAuthenticatorContext = {
  ...identityAddAuthenticatorContext,
  realmName: 'Contoso',
  loginAction: '/add?code=1',
}

function form(container: HTMLElement) {
  return container.querySelector('form') as HTMLFormElement
}

function boxes(container: HTMLElement) {
  // The six code boxes come before the device name.
  return Array.from(form(container).querySelectorAll<HTMLInputElement>('input:not([type="hidden"])')).slice(0, 6)
}

describe('AddAuthenticatorPage', () => {
  it('shows the QR code and the key to type by hand under the title and the organization', () => {
    render(<AddAuthenticatorPage context={context} />)

    expect(screen.getByText('Add an authenticator')).toBeInTheDocument()
    expect(screen.getByText('Contoso')).toBeInTheDocument()
    expect(screen.getByRole('img', { name: context.texts.qrCode })).toHaveAttribute(
      'src',
      `data:image/png;base64,${identityQrCode}`,
    )
    expect(screen.getByText(context.texts.manualKey)).toBeInTheDocument()
    expect(screen.getByText(context.manualKey)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add authenticator' })).toBeInTheDocument()
  })

  it('posts totp, totpSecret and userLabel to the login action as a real form', async () => {
    const { container } = render(<AddAuthenticatorPage context={context} />)

    expect(form(container)).toHaveAttribute('method', 'post')
    expect(form(container)).toHaveAttribute('action', '/add?code=1')
    expect(boxes(container)).toHaveLength(6)
    expect(form(container).querySelector('input[name="totpSecret"]')).toHaveValue(context.secret)

    await userEvent.type(boxes(container)[0], '123456')
    await userEvent.type(screen.getByLabelText('Device name'), 'Phone')

    expect(form(container).querySelector('input[name="totp"]')).toHaveValue('123456')
    expect(form(container).querySelector('input[name="userLabel"]')).toHaveValue('Phone')
  })

  it('posts mode only when the context has one', () => {
    const none = render(<AddAuthenticatorPage context={context} />)
    expect(form(none.container).querySelector('input[name="mode"]')).not.toBeInTheDocument()
    none.unmount()

    const manual = render(<AddAuthenticatorPage context={{ ...context, mode: 'manual' }} />)
    expect(form(manual.container).querySelector('input[name="mode"]')).toHaveValue('manual')
  })

  it('asks for a device name only when the person already has an authenticator', async () => {
    const { unmount } = render(<AddAuthenticatorPage context={context} />)
    await userEvent.click(screen.getByRole('button', { name: 'Add authenticator' }))
    // Only the code is missing, so the device name raises no message.
    await waitFor(() => expect(screen.getByText(context.texts.codeRequired)).toBeInTheDocument())
    expect(screen.queryByText(context.texts.deviceNameRequired)).not.toBeInTheDocument()
    unmount()

    render(<AddAuthenticatorPage context={{ ...context, deviceNameRequired: true }} />)
    await userEvent.click(screen.getByRole('button', { name: 'Add authenticator' }))
    await waitFor(() => expect(screen.getByText(context.texts.deviceNameRequired)).toBeInTheDocument())
  })

  it('asks for the whole code before it posts', async () => {
    const { container } = render(<AddAuthenticatorPage context={context} />)

    await userEvent.type(boxes(container)[0], '123')
    await userEvent.click(screen.getByRole('button', { name: 'Add authenticator' }))

    await waitFor(() => expect(screen.getByText(context.texts.codeRequired)).toBeInTheDocument())
  })

  it('shows an error above the form', () => {
    render(<AddAuthenticatorPage context={{ ...context, error: 'Invalid authenticator code.' }} />)

    expect(screen.getByRole('alert')).toHaveTextContent('Invalid authenticator code.')
  })

  it('shows no alert without an error', () => {
    render(<AddAuthenticatorPage context={context} />)

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
