import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { contrast } from '../test/contrast'
import { designTokensFor } from './designTokens'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { StepsBar, type StepMark } from './StepsBar'
import { TextProvider } from './TextProvider'
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

describe('StepsBar with items', () => {
  const marks: [StepMark, string, string][] = [
    ['done', 'Done', 'anticon-check'],
    ['now', 'In progress', 'anticon-loading'],
    ['waiting', 'Waiting', 'anticon-clock-circle'],
    ['stopped', 'Stopped', 'anticon-close'],
    ['idle', 'Not reached', 'anticon-minus'],
  ]

  function renderFeed(items: { mark: StepMark; standing?: string; body?: string }[], titles: string[]) {
    render(
      <ThemeModeProvider initialMode="light">
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StepsBar vertical titles={titles} items={items.map((item) => ({ ...item, body: item.body }))} />
        </TextProvider>
      </ThemeModeProvider>,
    )
  }

  it.each(marks)(
    'draws the %s mark as an icon, named "%s" for assistive technology, with the icon hidden',
    (mark, name, icon) => {
      renderFeed([{ mark }], ['Plan'])

      const image = within(screen.getByRole('listitem')).getByRole('img', { name })
      expect(image).toHaveAttribute('data-mark', mark)
      const drawn = image.querySelector(`.${icon}`)
      expect(drawn).toBeInTheDocument()
      expect(drawn).toHaveAttribute('aria-hidden', 'true')
    },
  )

  it('draws the mark in the circle of the small control size, in the colour of what the mark says', () => {
    const tokens = designTokensFor('light')
    renderFeed(
      marks.map(([mark]) => ({ mark })),
      marks.map(([mark]) => mark),
    )

    const colours: Record<StepMark, string> = {
      done: tokens.colour.primaryText,
      now: tokens.colour.primaryText,
      waiting: tokens.colour.warningText,
      stopped: tokens.colour.danger,
      idle: tokens.colour.textMuted,
    }
    for (const [mark, name] of marks) {
      const image = screen.getByRole('img', { name })
      expect(image).toHaveStyle({ width: `${tokens.size.smallControl}px`, borderRadius: '50%' })
      const expected = document.createElement('span')
      expected.style.color = colours[mark]
      expect(image.style.color).toBe(expected.style.color)
    }
  })

  it('shows each title, its standing after it and its body under them', () => {
    renderFeed(
      [
        { mark: 'done', standing: 'Approved on 18 Nov', body: 'The plan' },
        { mark: 'now', body: 'Checking the draft' },
      ],
      ['Plan', 'Work'],
    )

    const [plan, work] = screen.getAllByRole('listitem')
    expect(within(plan).getByText('Plan')).toBeVisible()
    expect(within(plan).getByText('Plan').closest('strong')).not.toBeNull()
    const standing = within(plan).getByText('Approved on 18 Nov')
    expect(standing).toBeVisible()
    expect(
      standing.compareDocumentPosition(within(plan).getByText('Plan')) & Node.DOCUMENT_POSITION_PRECEDING,
    ).toBeTruthy()
    expect(within(plan).getByText('The plan')).toBeVisible()
    expect(within(work).getByText('Work')).toBeVisible()
    expect(within(work).getByText('Checking the draft')).toBeVisible()
    expect(within(work).queryByText('Approved on 18 Nov')).toBeNull()
  })

  it('draws a line after each item but the last', () => {
    renderFeed([{ mark: 'done' }, { mark: 'waiting' }, { mark: 'idle' }], ['Plan', 'Work', 'Result'])

    const [plan, work, result] = screen.getAllByRole('listitem')
    expect(within(plan).getAllByTestId('steps-bar-line')).toHaveLength(1)
    expect(within(work).getAllByTestId('steps-bar-line')).toHaveLength(1)
    expect(within(result).queryByTestId('steps-bar-line')).toBeNull()
    expect(within(plan).getByTestId('steps-bar-line')).toHaveAttribute('aria-hidden', 'true')
  })

  it('marks the item that is now or waiting with aria-current, and no other', () => {
    renderFeed([{ mark: 'done' }, { mark: 'waiting' }, { mark: 'idle' }], ['Plan', 'Work', 'Result'])

    const items = screen.getAllByRole('listitem')
    expect(items.map((item) => item.getAttribute('aria-current'))).toEqual([null, 'step', null])
  })

  it('draws no numbers, no check circle and no button for a step of a feed', () => {
    renderFeed([{ mark: 'done' }, { mark: 'now' }], ['Plan', 'Work'])

    expect(screen.queryByText('1')).toBeNull()
    expect(screen.queryByRole('button')).toBeNull()
  })
})
