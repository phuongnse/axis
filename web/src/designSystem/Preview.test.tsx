import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Preview } from './Preview'

describe('Preview', () => {
  it("wraps the State control's options onto more lines", async () => {
    render(
      <Preview
        states={[
          { caption: 'One', draw: () => <p>One</p>, frameHeight: 100 },
          { caption: 'Two', draw: () => <p>Two</p>, frameHeight: 100 },
        ]}
      />,
    )

    // The row of options holds the radio buttons of the control, so it is the row that wraps.
    const row = (await screen.findByRole('radio', { name: 'One' })).closest('label')!.parentElement!
    expect(getComputedStyle(row).flexWrap).toBe('wrap')
  })
})
