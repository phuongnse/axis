import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { CodeBlock } from './CodeBlock'
import { ThemeModeProvider } from './theme'

const longLine = 'a'.repeat(500)

function renderBlock(wrap?: boolean, plain?: boolean) {
  render(
    <ThemeModeProvider>
      <CodeBlock text={longLine} testId="block" wrap={wrap} plain={plain} />
    </ThemeModeProvider>,
  )
  return screen.getByTestId('block')
}

describe('CodeBlock', () => {
  it('scrolls long lines sideways by default', () => {
    const block = renderBlock()

    expect(block.style.overflowX).toBe('auto')
    expect(block.style.whiteSpace).not.toBe('pre-wrap')
  })

  it('wraps long lines and does not scroll sideways with wrap', () => {
    const block = renderBlock(true)

    expect(block.style.whiteSpace).toBe('pre-wrap')
    expect(block.style.overflowWrap).toBe('anywhere')
    expect(block.style.overflowX).not.toBe('auto')
  })

  it('draws no border, radius or background with plain', () => {
    const block = renderBlock(false, true)

    expect(block.style.border).toBe('')
    expect(block.style.borderRadius).toBe('')
    expect(block.style.background).toBe('')
  })

  it('draws its border, radius and background by default', () => {
    const block = renderBlock()

    expect(block.style.border).not.toBe('')
    expect(block.style.borderRadius).not.toBe('')
  })
})
