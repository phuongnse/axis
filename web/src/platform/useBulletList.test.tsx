import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { colourTokens, spacingTokens } from './designTokens'
import { useBulletList } from './useBulletList'

function Points() {
  const bullets = useBulletList()
  return (
    <ul data-testid="outer" style={bullets.list}>
      <li data-testid="outer-item" style={bullets.item}>
        First
        <ul data-testid="inner" style={bullets.list}>
          <li data-testid="inner-item" style={bullets.item}>
            Nested
          </li>
        </ul>
      </li>
    </ul>
  )
}

describe('useBulletList', () => {
  it('draws a disc bullet at both depths, with no margin and an indent of spacing-xl', () => {
    render(<Points />)

    for (const list of [screen.getByTestId('outer'), screen.getByTestId('inner')]) {
      expect(list).toHaveStyle({
        listStyleType: 'disc',
        listStylePosition: 'outside',
        margin: '0px',
        paddingInlineStart: `${spacingTokens.xl}px`,
      })
    }
  })

  it('colours each item in the text colour, so its bullet takes it too', () => {
    render(<Points />)

    const outer = screen.getByTestId('outer-item')
    expect(outer).toHaveStyle({ color: colourTokens.text.light })
    expect(within(outer).getByTestId('inner-item')).toHaveStyle({ color: colourTokens.text.light })
  })
})
