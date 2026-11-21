import { screen, within } from '@testing-library/react'
import { expect } from 'vitest'

/**
 * Checks that the page's band holds its title alone, see docs/reference/shell.md#page-header: the title as the page's
 * only level-one heading, with no navigation and no link inside the band.
 */
export function expectTitleAloneInBand(title: string) {
  const band = screen.getByTestId('page-band')

  expect(within(band).getByRole('heading', { level: 1, name: title })).toBeInTheDocument()
  expect(within(band).queryAllByRole('navigation')).toHaveLength(0)
  expect(within(band).queryAllByRole('link')).toHaveLength(0)
  expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1)
}
