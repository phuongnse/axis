import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { TwoStepTexts } from './signInContext'
import { TwoStepPage } from './TwoStepPage'

const texts: TwoStepTexts = {
  title: 'Two-step verification',
  message: 'Enter the 6-digit code from your authenticator app.',
  code: 'Verification code',
  verify: 'Verify',
  tryAnotherWay: 'Try another way',
  backToSignIn: 'Back to sign in',
}

describe('TwoStepPage', () => {
  it('asks for the code in six boxes under the title and the organization', () => {
    const { container } = render(<TwoStepPage texts={texts} organization="Contoso" locales={[]} />)

    expect(screen.getByText('Two-step verification')).toBeInTheDocument()
    expect(screen.getByText('Contoso')).toBeInTheDocument()
    expect(screen.getByText('Verification code')).toBeInTheDocument()
    expect(container.querySelectorAll('input')).toHaveLength(6)
    expect(screen.getByRole('button', { name: 'Try another way' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Back to sign in' })).toBeInTheDocument()
  })

  it('keeps only digits, one in each box', async () => {
    const { container } = render(<TwoStepPage texts={texts} organization="Contoso" locales={[]} />)

    await userEvent.type(container.querySelectorAll('input')[0], '12a3')

    const values = Array.from(container.querySelectorAll('input')).map((input) => input.value)
    expect(values.join('')).toBe('123')
    expect(values.slice(0, 3)).toEqual(['1', '2', '3'])
  })
})
