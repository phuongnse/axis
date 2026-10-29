import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { identityTwoStepContext } from '../designSystem/fixtures'
import type { TwoStepContext } from './signInContext'
import { TwoStepPage } from './TwoStepPage'

const context: TwoStepContext = { ...identityTwoStepContext, realmName: 'Contoso', loginAction: '/verify?code=1' }

function codeForm(container: HTMLElement) {
  return container.querySelector('form') as HTMLFormElement
}

function boxes(container: HTMLElement) {
  return Array.from(codeForm(container).querySelectorAll<HTMLInputElement>('input:not([type="hidden"])'))
}

describe('TwoStepPage', () => {
  it('asks for the code in six boxes under the title and the organization', () => {
    const { container } = render(<TwoStepPage context={context} />)

    expect(screen.getByText('Two-step verification')).toBeInTheDocument()
    expect(screen.getByText('Contoso')).toBeInTheDocument()
    expect(screen.getByText('Verification code')).toBeInTheDocument()
    expect(boxes(container)).toHaveLength(6)
    expect(screen.getByRole('button', { name: 'Verify' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Try another way' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to sign in' })).toHaveAttribute('href', context.restartUrl)
  })

  it('keeps only digits, one in each box', async () => {
    const { container } = render(<TwoStepPage context={context} />)

    await userEvent.type(boxes(container)[0], '12a3')

    const values = boxes(container).map((input) => input.value)
    expect(values.join('')).toBe('123')
    expect(values.slice(0, 3)).toEqual(['1', '2', '3'])
  })

  it('posts otp and login to the login action as a real form', async () => {
    const { container } = render(<TwoStepPage context={context} />)
    const form = codeForm(container)

    expect(form).toHaveAttribute('method', 'post')
    expect(form).toHaveAttribute('action', '/verify?code=1')
    expect(form.querySelector('input[name="login"]')).toHaveValue('Sign In')

    await userEvent.type(boxes(container)[0], '123456')

    expect(form.querySelector('input[name="otp"]')).toHaveValue('123456')
  })

  it('clears the code when a digit is deleted', async () => {
    const { container } = render(<TwoStepPage context={context} />)
    const form = codeForm(container)

    await userEvent.type(boxes(container)[0], '123456')
    await userEvent.type(boxes(container)[5], '{Backspace}')

    expect(form.querySelector('input[name="otp"]')).not.toHaveValue('123456')
  })

  it('asks for the whole code before it posts', async () => {
    const { container } = render(<TwoStepPage context={context} />)

    await userEvent.type(boxes(container)[0], '123')
    await userEvent.click(screen.getByRole('button', { name: 'Verify' }))

    // The same sentence heads the page and, as the rule's message, shows under the boxes.
    await waitFor(() => expect(screen.getAllByText(context.texts.message)).toHaveLength(2))
  })

  it('posts tryAnotherWay=on from its own form, only when Keycloak offers another way', () => {
    const offered = render(<TwoStepPage context={context} />)
    const forms = offered.container.querySelectorAll('form')
    expect(forms).toHaveLength(2)
    expect(forms[1]).toHaveAttribute('method', 'post')
    expect(forms[1]).toHaveAttribute('action', '/verify?code=1')
    expect(forms[1].querySelector('input[name="tryAnotherWay"]')).toHaveValue('on')
    offered.unmount()

    const none = render(<TwoStepPage context={{ ...context, tryAnotherWay: false }} />)
    expect(none.container.querySelectorAll('form')).toHaveLength(1)
    expect(screen.queryByRole('button', { name: 'Try another way' })).not.toBeInTheDocument()
  })

  it('shows an error above the form', () => {
    render(<TwoStepPage context={{ ...context, error: 'Invalid authenticator code.' }} />)

    expect(screen.getByRole('alert')).toHaveTextContent('Invalid authenticator code.')
  })

  it('shows no alert without an error', () => {
    render(<TwoStepPage context={context} />)

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
