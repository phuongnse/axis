import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Link, MemoryRouter } from 'react-router'
import { withAntdStyles } from '../test/antdStyles'
import { winningRule } from '../test/cssRules'
import { AxisButton } from './AxisButton'
import { FilledText } from './FilledText'
import { ThemeModeProvider } from './theme'

withAntdStyles()

/** A link in running text, a link that stands alone and a link in a menu, see docs/reference/actions-and-controls.md#opening-things. */
function renderLinks() {
  render(
    <ThemeModeProvider initialMode="light">
      <MemoryRouter>
        <p>
          <FilledText text="See {target}." parts={{ target: <Link to="/a">Inside</Link> }} />
        </p>
        <Link to="/b">Alone</Link>
        <ul role="menu">
          <li role="none">
            <Link to="/c">Entry</Link>
          </li>
        </ul>
      </MemoryRouter>
    </ThemeModeProvider>,
  )
  return {
    inside: screen.getByRole('link', { name: 'Inside' }),
    alone: screen.getByRole('link', { name: 'Alone' }),
    entry: screen.getByRole('link', { name: 'Entry' }),
  }
}

describe('links', () => {
  it('underlines a link in a FilledText sentence, at rest and when hovered', () => {
    const { inside } = renderLinks()

    expect(winningRule(inside, 'text-decoration')).toBe('underline')
    expect(winningRule(inside, 'text-decoration', { hovered: true })).toBe('underline')
  })

  it('leaves a standalone link without an underline until it is hovered', () => {
    const { alone } = renderLinks()

    expect(winningRule(alone, 'text-decoration')).toBe('none')
    expect(winningRule(alone, 'text-decoration', { hovered: true })).toBe('underline')
  })

  it('never underlines a menu entry, even when it is hovered', () => {
    const { entry } = renderLinks()

    expect(winningRule(entry, 'text-decoration')).toBe('none')
    expect(winningRule(entry, 'text-decoration', { hovered: true })).toBe('none')
  })
})

describe('a button with an address', () => {
  it('draws its link with no text decoration', () => {
    render(
      <ThemeModeProvider initialMode="light">
        <MemoryRouter>
          <AxisButton role="secondary" href="/x">
            Open
          </AxisButton>
        </MemoryRouter>
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('link', { name: 'Open' }).style.textDecoration).toBe('none')
  })

  it('sets no text decoration on a button with no address', () => {
    render(
      <ThemeModeProvider initialMode="light">
        <AxisButton role="secondary">Save</AxisButton>
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('button', { name: 'Save' }).style.textDecoration).toBe('')
  })
})
