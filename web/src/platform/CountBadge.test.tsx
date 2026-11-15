import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { CountBadge } from './CountBadge'

// Proposed: one count on an icon button, see the review on #3263.
describe('CountBadge', () => {
  it('shows the count on the small badge of Ant Design', () => {
    render(
      <CountBadge count={3}>
        <button type="button">Filter</button>
      </CountBadge>,
    )

    const count = screen.getByText('3').closest('.ant-badge-count')!
    expect(count).toHaveClass('ant-badge-count-sm')
  })

  it('shows 9+ past nine, and nothing for none', () => {
    const { rerender, container } = render(
      <CountBadge count={12}>
        <button type="button">Bell</button>
      </CountBadge>,
    )
    expect(container).toHaveTextContent('9+')

    rerender(
      <CountBadge count={0}>
        <button type="button">Bell</button>
      </CountBadge>,
    )
    expect(container).not.toHaveTextContent('0')
  })
})
