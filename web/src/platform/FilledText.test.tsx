import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { FilledText } from './FilledText'

const parts = { count: <strong>3</strong>, folder: <a href="/x">Docs</a> }

describe('FilledText', () => {
  it('puts each node at its placeholder in the word order of an en text', () => {
    const { container } = render(<FilledText text="{count} files in {folder}" parts={parts} />)

    expect(container.textContent).toBe('3 files in Docs')
    const strong = container.querySelector('strong')!
    const link = container.querySelector('a')!
    expect(strong.compareDocumentPosition(link) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('puts each node at its placeholder in the word order of a vi text', () => {
    const { container } = render(<FilledText text="Trong {folder} có {count} tệp" parts={parts} />)

    expect(container.textContent).toBe('Trong Docs có 3 tệp')
    const strong = container.querySelector('strong')!
    const link = container.querySelector('a')!
    expect(link.compareDocumentPosition(strong) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('keeps a placeholder with no node as written', () => {
    const { container } = render(<FilledText text="Hello {who}, see {unknown}." parts={{ who: <b>Ann</b> }} />)

    expect(container.textContent).toBe('Hello Ann, see {unknown}.')
  })

  it('does not draw a node whose placeholder is not in the text', () => {
    const { container, queryByText } = render(<FilledText text="Nothing to fill here." parts={parts} />)

    expect(queryByText('Docs')).toBeNull()
    expect(queryByText('3')).toBeNull()
    expect(container.textContent).toBe('Nothing to fill here.')
  })

  it('draws the node at each place of a repeated placeholder', () => {
    const { container } = render(<FilledText text="{x} and {x}" parts={{ x: <em>a</em> }} />)

    expect(container.querySelectorAll('em')).toHaveLength(2)
    expect(container.textContent).toBe('a and a')
  })

  it('does not read an inherited name as a node', () => {
    const { container } = render(<FilledText text="{toString}" parts={{}} />)

    expect(container.textContent).toBe('{toString}')
  })
})
