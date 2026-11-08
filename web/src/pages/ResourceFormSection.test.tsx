import { ConfigProvider } from 'antd'
import { render, screen, within } from '@testing-library/react'
import { createPortal } from 'react-dom'
import { describe, expect, it } from 'vitest'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { ResourceFormSection, ResourceFormSections } from './ResourceFormSection'

const texts = {}
const catalogs = [{ texts, fallbackTexts: texts }]

function renderSections(sections: React.ReactNode) {
  return render(
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <ResourceFormSections>{sections}</ResourceFormSections>
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
}

describe('ResourceFormSection', () => {
  it('finds a section by its title and shows its line and its fields', () => {
    renderSections(
      <ResourceFormSection testName="basics" heading={{ title: 'Basics', line: 'What it is called.' }}>
        <span>Fields</span>
      </ResourceFormSection>,
    )

    const section = screen.getByRole('region', { name: 'Basics' })
    expect(section).toBe(screen.getByTestId('studio-section-basics'))
    expect(within(section).getByRole('heading', { name: 'Basics' })).toBeVisible()
    expect(within(section).getByText('What it is called.')).toBeVisible()
    expect(within(section).getByText('Fields')).toBeVisible()
  })

  it('draws a section with no heading as its fields alone', () => {
    renderSections(
      <ResourceFormSection testName="rest">
        <span>Fields</span>
      </ResourceFormSection>,
    )

    expect(screen.getByTestId('studio-section-rest')).toHaveTextContent('Fields')
    expect(screen.queryByRole('heading')).toBeNull()
    expect(screen.queryByRole('region')).toBeNull()
  })

  it('shows its action beside the heading', () => {
    renderSections(
      <ResourceFormSection
        testName="items"
        heading={{ title: 'Items', line: 'The list.' }}
        action={<button type="button">Add</button>}
      >
        <span>Fields</span>
      </ResourceFormSection>,
    )

    expect(within(screen.getByRole('region', { name: 'Items' })).getByRole('button', { name: 'Add' })).toBeVisible()
  })

  it('gives a function child a slot in the heading to draw into', () => {
    renderSections(
      <ResourceFormSection testName="items" heading={{ title: 'Items', line: 'The list.' }}>
        {(slot) => (
          <>
            <span>Fields</span>
            {slot !== null && createPortal(<button type="button">Add item</button>, slot)}
          </>
        )}
      </ResourceFormSection>,
    )

    const section = screen.getByRole('region', { name: 'Items' })
    expect(within(section).getByRole('button', { name: 'Add item' })).toBeVisible()
    expect(within(section).getByText('Fields')).toBeVisible()
  })
})
