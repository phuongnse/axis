import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { contrast } from '../test/contrast'
import { designTokensFor } from './designTokens'
import { StepsBar } from './StepsBar'
import { ThemeModeProvider } from './theme'

const sevenTitles = ['One', 'Two', 'Three', 'Four', 'Five', 'Six', 'Seven']

describe('StepsBar in dark mode', () => {
  beforeEach(() => localStorage.setItem('axis.themeMode', 'dark'))
  afterEach(() => localStorage.clear())

  it('draws the current number at 4.5:1 or more on its circle, not in the surface colour', () => {
    render(
      <ThemeModeProvider>
        <StepsBar titles={['Basics', 'Extras']} current={1} />
      </ThemeModeProvider>,
    )

    const circle = within(screen.getAllByRole('listitem')[1]).getByText('2')
    expect(contrast(circle.style.color, circle.style.backgroundColor)).toBeGreaterThanOrEqual(4.5)
    const surface = document.createElement('span')
    surface.style.color = designTokensFor('dark').colour.surface
    expect(circle.style.color).not.toBe(surface.style.color)
  })
})

describe('StepsBar', () => {
  it('shows every title below seven steps and marks the current step', () => {
    render(<StepsBar titles={['Basics', 'Extras']} current={1} />)

    const items = screen.getAllByRole('listitem')
    expect(within(items[0]).getByText('Basics')).toBeVisible()
    expect(within(items[1]).getByText('Extras')).toBeVisible()
    expect(items[0]).not.toHaveAttribute('aria-current')
    expect(items[1]).toHaveAttribute('aria-current', 'step')
  })

  it('shows a check mark in place of the number for a finished step below seven steps', () => {
    render(<StepsBar titles={['Basics', 'Extras', 'Review']} current={1} />)

    const items = screen.getAllByRole('listitem')
    expect(items[0].querySelector('.anticon-check')).toBeInTheDocument()
    expect(within(items[0]).queryByText('1')).toBeNull()
    expect(items[1].querySelector('.anticon-check')).toBeNull()
    expect(within(items[1]).getByText('2')).toBeInTheDocument()
  })

  it('shows only the current title from seven steps, and each other step as a number with its title', () => {
    render(<StepsBar titles={sevenTitles} current={2} />)

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

  it('stacks the steps in a column with `vertical`, one step on each line', () => {
    render(<StepsBar titles={['Basics', 'Extras', 'Review']} current={1} vertical />)

    expect(screen.getByRole('list')).toHaveStyle({ flexDirection: 'column' })
    const items = screen.getAllByRole('listitem')
    expect(items).toHaveLength(3)
    expect(within(items[0]).getByText('Basics')).toBeVisible()
    expect(within(items[1]).getByText('Extras')).toBeVisible()
    expect(within(items[2]).getByText('Review')).toBeVisible()
    expect(items[1]).toHaveAttribute('aria-current', 'step')
  })

  it.each([false, true])('draws no margin outside the bar with vertical %s', (vertical) => {
    render(<StepsBar titles={['Basics', 'Extras']} current={0} vertical={vertical} />)

    const style = getComputedStyle(screen.getByRole('list'))
    expect([style.marginTop, style.marginRight, style.marginBottom, style.marginLeft]).toEqual([
      '0px',
      '0px',
      '0px',
      '0px',
    ])
  })
})

describe('StepsBar with onPick', () => {
  it('makes each step a button named by its title, and a click picks its index', async () => {
    const onPick = vi.fn()
    render(<StepsBar titles={['Basics', 'Extras', 'Review']} current={0} onPick={onPick} />)

    const bar = screen.getByTestId('steps-bar')
    expect(within(bar).getAllByRole('button')).toHaveLength(3)
    expect(within(bar).getByRole('button', { name: 'Basics' })).toBeInTheDocument()
    expect(within(bar).getByRole('button', { name: 'Review' })).toBeInTheDocument()
    await userEvent.click(within(bar).getByRole('button', { name: 'Extras' }))

    expect(onPick).toHaveBeenCalledTimes(1)
    expect(onPick).toHaveBeenCalledWith(1)
  })

  it('keeps the one aria-current on the step shown, and adds none to a button', () => {
    render(<StepsBar titles={['Basics', 'Extras']} current={1} onPick={() => {}} />)

    const bar = screen.getByTestId('steps-bar')
    expect(bar.querySelectorAll('[aria-current]')).toHaveLength(1)
    expect(screen.getAllByRole('listitem')[1]).toHaveAttribute('aria-current', 'step')
    expect(screen.getByRole('button', { name: 'Extras' })).not.toHaveAttribute('aria-current')
  })

  it('draws the steps of seven or more as buttons with no focusable circle inside', () => {
    render(<StepsBar titles={sevenTitles} current={2} onPick={() => {}} />)

    const bar = screen.getByTestId('steps-bar')
    expect(bar.querySelectorAll('[tabindex]')).toHaveLength(0)
    expect(within(bar).getAllByRole('button')).toHaveLength(sevenTitles.length)
    expect(within(bar).getByRole('button', { name: 'Five' })).toBeInTheDocument()
  })

  it('wraps each step, its button or its title, with what `entry` returns', () => {
    render(
      <StepsBar
        titles={['Basics', 'Extras']}
        current={0}
        onPick={() => {}}
        entry={(index, content) => <span data-testid={`entry-${index}`}>{content}</span>}
      />,
    )

    expect(within(screen.getByTestId('entry-1')).getByRole('button', { name: 'Extras' })).toBeInTheDocument()
  })

  it('draws no button without onPick', () => {
    render(<StepsBar titles={['Basics', 'Extras']} current={0} />)

    expect(screen.queryByRole('button')).toBeNull()
  })
})
