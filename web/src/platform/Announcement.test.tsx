import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Announcement } from './Announcement'

describe('Announcement', () => {
  it('is a polite status region hidden from the eye that keeps its text for a screen reader', () => {
    render(<Announcement>Saved</Announcement>)

    const region = screen.getByRole('status')
    expect(region).toHaveTextContent('Saved')
    expect(region).toHaveAttribute('aria-live', 'polite')
    expect(region).not.toHaveAttribute('aria-hidden')
    expect(region).toHaveStyle({ position: 'absolute', width: '1px', height: '1px', overflow: 'hidden' })
  })

  it('is in the document before it has any text, so later text is announced', () => {
    render(<Announcement testId="announcement" />)

    const region = screen.getByTestId('announcement')
    expect(region).toHaveAttribute('role', 'status')
    expect(region).toBeEmptyDOMElement()
  })
})
