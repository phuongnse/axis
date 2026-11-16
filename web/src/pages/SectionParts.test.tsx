import { render, screen } from '@testing-library/react'
import type { ReactElement } from 'react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { ThemeModeProvider } from '../platform/theme'
import { SectionPart, SectionParts } from './SectionParts'

function renderParts(parts: ReactElement) {
  return render(<ThemeModeProvider>{parts}</ThemeModeProvider>)
}

beforeEach(() => localStorage.setItem('axis.themeMode', 'light'))

afterEach(() => localStorage.clear())

describe('SectionParts', () => {
  it('gives each part a level 5 heading and a region named by it', () => {
    renderParts(
      <SectionParts>
        <SectionPart title="First">one</SectionPart>
        <SectionPart title="Second">two</SectionPart>
      </SectionParts>,
    )

    expect(screen.getAllByRole('heading', { level: 5 }).map((heading) => heading.textContent)).toEqual([
      'First',
      'Second',
    ])
    expect(screen.getByRole('region', { name: 'First' })).toHaveTextContent('one')
    expect(screen.getByRole('region', { name: 'Second' })).toHaveTextContent('two')
  })

  it('puts one rule between two parts and none before the first', () => {
    renderParts(
      <SectionParts>
        <SectionPart title="First">one</SectionPart>
        <SectionPart title="Second">two</SectionPart>
      </SectionParts>,
    )

    expect(screen.getAllByRole('separator')).toHaveLength(1)
    const column = screen.getByRole('separator').parentElement as HTMLElement
    expect(column.children[0]).toBe(screen.getByRole('region', { name: 'First' }))
    expect(column.children[1]).toBe(screen.getByRole('separator'))
    expect(column.children[2]).toBe(screen.getByRole('region', { name: 'Second' }))
  })

  it('adds no rule for a part the page leaves out', () => {
    renderParts(
      <SectionParts>
        <SectionPart title="First">one</SectionPart>
        {false}
        {null}
        <SectionPart title="Second">two</SectionPart>
        <SectionPart title="Third">three</SectionPart>
      </SectionParts>,
    )

    expect(screen.getAllByRole('heading', { level: 5 })).toHaveLength(3)
    expect(screen.getAllByRole('separator')).toHaveLength(2)
  })

  it('has no rule for a single part', () => {
    renderParts(
      <SectionParts>
        <SectionPart title="Only">one</SectionPart>
      </SectionParts>,
    )

    expect(screen.queryByRole('separator')).not.toBeInTheDocument()
  })
})
