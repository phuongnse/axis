import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { TermList } from './TermList'
import { NarrowWidthOverride } from './useNarrowWidth'

const facts = [
  { key: 'record', term: 'Record', value: 'Laptop' },
  { key: 'due', term: 'Due', value: 'Tomorrow' },
  { key: 'owner', term: 'Owner', value: 'Mai' },
]

describe('TermList', () => {
  it('gives each fact a row of its own, with its term over its value', () => {
    render(<TermList terms={facts} />)

    for (const { term, value } of facts) {
      const dt = screen.getByText(term)
      const dd = screen.getByText(value)
      expect(dt.tagName).toBe('DT')
      expect(dd.tagName).toBe('DD')
      expect(dt.parentElement).toBe(dd.parentElement)
      expect(dt.parentElement).toHaveStyle({ gridColumn: 'span 12' })
      expect(dt.compareDocumentPosition(dd) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    }
  })

  it('draws every fact in one wrapping line with row, each value on one line', () => {
    const { container } = render(<TermList terms={facts} row />)

    const list = container.querySelector('dl')!
    expect(list).toHaveStyle({ display: 'flex', flexWrap: 'wrap' })
    expect(list.children).toHaveLength(3)
    for (const fact of Array.from(list.children)) {
      expect(fact.querySelector('dt')).not.toBeNull()
      expect(fact.querySelector('dd')).toHaveStyle({ whiteSpace: 'nowrap' })
      expect((fact as HTMLElement).style.gridColumn).toBe('')
    }
  })

  it('spans a fact over its width in twelfths, and the whole row at narrow width', () => {
    const halves = [
      { key: 'a', term: 'Start', value: '1 May', width: 6 },
      { key: 'b', term: 'End', value: '9 May', width: 6 },
    ]
    const { unmount } = render(<TermList terms={halves} />)

    expect(screen.getByText('Start').parentElement).toHaveStyle({ gridColumn: 'span 6' })
    expect(screen.getByText('End').parentElement).toHaveStyle({ gridColumn: 'span 6' })
    unmount()

    render(
      <NarrowWidthOverride value={true}>
        <TermList terms={halves} />
      </NarrowWidthOverride>,
    )

    expect(screen.getByText('Start').parentElement).toHaveStyle({ gridColumn: 'span 12' })
    expect(screen.getByText('End').parentElement).toHaveStyle({ gridColumn: 'span 12' })
  })
})
