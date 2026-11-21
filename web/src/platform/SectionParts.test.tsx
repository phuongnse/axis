import { ConfigProvider } from 'antd'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { createPortal } from 'react-dom'
import { describe, expect, it } from 'vitest'
import { AxisButton } from './AxisButton'
import { designTokensFor } from './designTokens'
import { SectionPart, SectionParts } from './SectionParts'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const texts = { 'shell.section.show': 'Show section {title}', 'shell.section.hide': 'Hide section {title}' }
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

  it('gives a part the test id it names in place of the section test id', () => {
    renderSections(
      <SectionPart testId="console-tenant" heading={{ title: 'Tenant' }}>
        <span>Fields</span>
      </SectionPart>,
    )

    const section = screen.getByRole('region', { name: 'Tenant' })
    expect(section).toBe(screen.getByTestId('console-tenant'))
    expect(screen.queryByTestId('studio-section-console-tenant')).toBeNull()
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

  it('draws a title-row action as a borderless text button, the main and destructive ones in their colour', () => {
    renderSections(
      <SectionPart
        testName="rows"
        heading={{ title: 'Rows' }}
        action={
          <>
            <AxisButton role="secondary">Add row</AxisButton>
            <AxisButton role="main">Import</AxisButton>
            <AxisButton role="destructive">Clear</AxisButton>
          </>
        }
      >
        <span>Fields</span>
      </SectionPart>,
    )

    const section = screen.getByRole('region', { name: 'Rows' })
    for (const name of ['Add row', 'Import', 'Clear']) {
      const button = within(section).getByRole('button', { name })
      expect(button).toHaveClass('ant-btn-variant-text')
      expect(button).not.toHaveClass('ant-btn-variant-outlined')
    }
  })

  it('draws nothing in the heading when it has no action', () => {
    renderSections(
      <SectionPart testName="plain" heading={{ title: 'Plain' }} action={null}>
        <span>Fields</span>
      </SectionPart>,
    )

    const heading = screen.getByRole('heading', { name: 'Plain' })
    expect(heading.parentElement!.parentElement!.children).toHaveLength(1)
  })

  it('folds when given a fold: a button before the heading opens a body that is drawn only while open', async () => {
    function Folding() {
      const [open, setOpen] = useState(false)
      return (
        <SectionPart
          testName="totals"
          heading={{ title: 'Totals' }}
          fold={{ open, onToggle: () => setOpen(!open), title: 'Totals' }}
        >
          <span>Fields</span>
        </SectionPart>
      )
    }
    renderSections(<Folding />)

    const section = screen.getByRole('region', { name: 'Totals' })
    const show = within(section).getByRole('button', { name: 'Show section Totals' })
    expect(show).toHaveAttribute('aria-expanded', 'false')
    expect(within(section).queryByText('Fields')).not.toBeInTheDocument()
    expect(show.compareDocumentPosition(within(section).getByRole('heading', { name: 'Totals' }))).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    )

    await userEvent.click(show)

    const hide = within(section).getByRole('button', { name: 'Hide section Totals' })
    expect(hide).toHaveAttribute('aria-expanded', 'true')
    expect(section.querySelector(`#${hide.getAttribute('aria-controls')}`)).toContainElement(
      within(section).getByText('Fields'),
    )

    await userEvent.click(hide)

    expect(within(section).queryByText('Fields')).not.toBeInTheDocument()
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

  it('puts a borderSubtle rule before every part but the first', () => {
    renderSections(
      ['one', 'two', 'three'].map((name) => (
        <SectionPart key={name} testName={name} heading={{ title: name }}>
          <span>Fields</span>
        </SectionPart>
      )),
    )

    const rule = { borderTop: `1px solid ${designTokensFor('light').colour.borderSubtle}` }
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
