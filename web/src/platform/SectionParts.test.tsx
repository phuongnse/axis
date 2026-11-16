import { ConfigProvider } from 'antd'
import { render, screen, within } from '@testing-library/react'
import { createPortal } from 'react-dom'
import { describe, expect, it } from 'vitest'
import { designTokensFor } from './designTokens'
import { SectionPart, SectionParts } from './SectionParts'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const texts = {}
const catalogs = [{ texts, fallbackTexts: texts }]

function renderSections(sections: React.ReactNode) {
  return render(
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <SectionParts>{sections}</SectionParts>
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
}

describe('SectionPart', () => {
  it('finds a section by its title and shows its line and its fields', () => {
    renderSections(
      <SectionPart testName="basics" heading={{ title: 'Basics', line: 'What it is called.' }}>
        <span>Fields</span>
      </SectionPart>,
    )

    const section = screen.getByRole('region', { name: 'Basics' })
    expect(section).toBe(screen.getByTestId('studio-section-basics'))
    expect(within(section).getByRole('heading', { name: 'Basics', level: 2 })).toBeVisible()
    expect(within(section).getByRole('heading', { name: 'Basics' })).toHaveStyle({ fontSize: '16px' })
    expect(within(section).getByText('What it is called.')).toBeVisible()
    expect(within(section).getByText('Fields')).toBeVisible()
  })

  it('shows a heading with no line as its title alone', () => {
    renderSections(
      <SectionPart testName="live" heading={{ title: 'Live' }}>
        <span>Fields</span>
      </SectionPart>,
    )

    const section = screen.getByRole('region', { name: 'Live' })
    expect(section).toBe(screen.getByTestId('studio-section-live'))
    expect(within(section).getByRole('heading', { name: 'Live' })).toBeVisible()
    expect(section).toHaveTextContent(/^LiveFields$/)
  })

  it('draws a section with no heading as its fields alone', () => {
    renderSections(
      <SectionPart testName="rest">
        <span>Fields</span>
      </SectionPart>,
    )

    expect(screen.getByTestId('studio-section-rest')).toHaveTextContent('Fields')
    expect(screen.queryByRole('heading')).toBeNull()
    expect(screen.queryByRole('region')).toBeNull()
  })

  it('shows its action beside the heading', () => {
    renderSections(
      <SectionPart
        testName="items"
        heading={{ title: 'Items', line: 'The list.' }}
        action={<button type="button">Add</button>}
      >
        <span>Fields</span>
      </SectionPart>,
    )

    expect(within(screen.getByRole('region', { name: 'Items' })).getByRole('button', { name: 'Add' })).toBeVisible()
  })

  it('gives a function child a slot in the heading to draw into', () => {
    renderSections(
      <SectionPart testName="items" heading={{ title: 'Items', line: 'The list.' }}>
        {(slot) => (
          <>
            <span>Fields</span>
            {slot !== null && createPortal(<button type="button">Add item</button>, slot)}
          </>
        )}
      </SectionPart>,
    )

    const section = screen.getByRole('region', { name: 'Items' })
    expect(within(section).getByRole('button', { name: 'Add item' })).toBeVisible()
    expect(within(section).getByText('Fields')).toBeVisible()
  })

  it('puts a divider rule before every part but the first', () => {
    renderSections(
      ['one', 'two', 'three'].map((name) => (
        <SectionPart key={name} testName={name} heading={{ title: name }}>
          <span>Fields</span>
        </SectionPart>
      )),
    )

    const rule = { borderTop: `1px solid ${designTokensFor('light').colour.divider}` }
    const rules = screen.getAllByRole('separator')
    expect(rules).toHaveLength(2)
    expect(screen.getByTestId('studio-section-one').previousElementSibling).toBeNull()
    expect(screen.getByTestId('studio-section-two').previousElementSibling).toBe(rules[0])
    expect(screen.getByTestId('studio-section-three').previousElementSibling).toBe(rules[1])
    expect(rules[0]).toHaveStyle(rule)
    expect(rules[1]).toHaveStyle(rule)
  })

  it('draws no extra rule for a component child that renders nothing, in the middle or at the end', () => {
    const Hidden = () => null
    const part = (name: string) => (
      <SectionPart key={name} testName={name} heading={{ title: name }}>
        <span>Fields</span>
      </SectionPart>
    )
    const { unmount } = renderSections([part('one'), <Hidden key="hidden" />, part('two')])

    expect(screen.getAllByRole('separator')).toHaveLength(1)
    expect(screen.getByTestId('studio-section-two').previousElementSibling).toBe(screen.getByRole('separator'))
    unmount()

    renderSections([part('one'), part('two'), <Hidden key="hidden" />])

    expect(screen.getAllByRole('separator')).toHaveLength(1)
    expect(screen.getByTestId('studio-section-two').previousElementSibling).toBe(screen.getByRole('separator'))
  })

  it('draws exactly one rule above the first part of a column that follows another part', () => {
    renderSections([
      <SectionPart key="one" testName="one" heading={{ title: 'one' }}>
        <span>Fields</span>
      </SectionPart>,
      <SectionParts key="nested">
        <SectionPart testName="two" heading={{ title: 'two' }}>
          <span>Fields</span>
        </SectionPart>
      </SectionParts>,
    ])

    expect(screen.getAllByRole('separator')).toHaveLength(1)
    expect(screen.getByTestId('studio-section-two').previousElementSibling).toBe(screen.getByRole('separator'))
  })

  it('draws no rule for a part nested in the body of another part', () => {
    renderSections([
      <SectionPart key="one" testName="one" heading={{ title: 'one' }}>
        <SectionParts>
          <SectionPart testName="inner" heading={{ title: 'inner' }}>
            <span>Fields</span>
          </SectionPart>
        </SectionParts>
      </SectionPart>,
    ])

    expect(screen.queryByRole('separator')).toBeNull()
  })

  it('draws no rule for a single part', () => {
    renderSections(
      <SectionPart testName="only" heading={{ title: 'Only' }}>
        <span>Fields</span>
      </SectionPart>,
    )

    expect(screen.queryByRole('separator')).toBeNull()
  })

  it('draws no rule for a part that a condition leaves out', () => {
    renderSections([
      false,
      <SectionPart key="one" testName="one">
        <span>Fields</span>
      </SectionPart>,
      null,
    ])

    expect(screen.queryByRole('separator')).toBeNull()
  })

  it('draws a part with no background, border, radius or padding', () => {
    renderSections([
      <SectionPart key="named" testName="named" heading={{ title: 'Named' }}>
        <span>Fields</span>
      </SectionPart>,
      <SectionPart key="unnamed" testName="unnamed">
        <span>Fields</span>
      </SectionPart>,
    ])

    for (const name of ['named', 'unnamed']) {
      const part = screen.getByTestId(`studio-section-${name}`)
      for (const property of ['background', 'border', 'border-radius', 'padding']) {
        expect(part.style.getPropertyValue(property)).toBe('')
      }
    }
  })
})
