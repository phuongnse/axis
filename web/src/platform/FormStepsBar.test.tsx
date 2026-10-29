import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { FormStepsBar } from './FormStepsBar'

const sevenTitles = ['One', 'Two', 'Three', 'Four', 'Five', 'Six', 'Seven']

describe('FormStepsBar', () => {
  it('shows every title below seven steps and marks the current step', () => {
    render(<FormStepsBar titles={['Basics', 'Extras']} current={1} />)

    const items = screen.getAllByRole('listitem')
    expect(within(items[0]).getByText('Basics')).toBeVisible()
    expect(within(items[1]).getByText('Extras')).toBeVisible()
    expect(items[0]).not.toHaveAttribute('aria-current')
    expect(items[1]).toHaveAttribute('aria-current', 'step')
  })

  it('shows a check mark in place of the number for a finished step below seven steps', () => {
    render(<FormStepsBar titles={['Basics', 'Extras', 'Review']} current={1} />)

    const items = screen.getAllByRole('listitem')
    expect(items[0].querySelector('.anticon-check')).toBeInTheDocument()
    expect(within(items[0]).queryByText('1')).toBeNull()
    expect(items[1].querySelector('.anticon-check')).toBeNull()
    expect(within(items[1]).getByText('2')).toBeInTheDocument()
  })

  it('shows only the current title from seven steps, and each other step as a number with its title', () => {
    render(<FormStepsBar titles={sevenTitles} current={2} />)

    const items = screen.getAllByRole('listitem')
    expect(within(items[2]).getByText('Three')).toBeVisible()
    expect(items[2]).toHaveAttribute('aria-current', 'step')
    sevenTitles.forEach((title, index) => {
      if (index === 2) {
        return
      }
      const item = items[index]
      expect(within(item).getByText(title)).toHaveStyle({ position: 'absolute', width: '1px' })
      expect(item.querySelector('[tabindex]')).toHaveAttribute('tabindex', '0')
      expect(item).not.toHaveAttribute('aria-current')
      if (index < 2) {
        expect(item.querySelector('.anticon-check')).toHaveAttribute('aria-hidden', 'true')
        expect(within(item).queryByText(String(index + 1))).toBeNull()
      } else {
        expect(within(item).getByText(String(index + 1))).toHaveAttribute('aria-hidden', 'true')
        expect(item.querySelector('.anticon-check')).toBeNull()
      }
    })
  })
})
