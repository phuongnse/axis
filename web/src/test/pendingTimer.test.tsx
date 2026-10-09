import { render, screen } from '@testing-library/react'
import { useEffect, useState } from 'react'
import { expect, it } from 'vitest'

function LateTimer() {
  const [width, setWidth] = useState<number>()
  useEffect(() => {
    setTimeout(() => setWidth(window.innerWidth), 400)
  }, [])
  return <p>Width: {width ?? 'unknown'}</p>
}

// Passes only if the run reports no unhandled error when the timer fires after this file ends.
it('lets a timer left pending by an unmounted component fire before teardown', () => {
  render(<LateTimer />)

  expect(screen.getByText('Width: unknown')).toBeInTheDocument()
})
