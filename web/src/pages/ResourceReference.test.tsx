import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { designTokensFor } from '../platform/designTokens'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { ResourceReference } from './ResourceReference'

const entity = { kind: 'entity', name: 'PurchaseRequest', titles: { en: 'Purchase requests' } }
const file = 'entities/purchase-request.json'

function renderReference(props: Parameters<typeof ResourceReference>[0]) {
  render(
    <MemoryRouter>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <ResourceReference {...props} />
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'] as const)('ResourceReference in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('shows the title as a link to its href, with the kind muted after it and no file', () => {
    renderReference({ resource: entity, file, href: '/studio?app=Shop&resource=e1' })

    expect(screen.getByRole('link', { name: 'Purchase requests' })).toHaveAttribute(
      'href',
      '/studio?app=Shop&resource=e1',
    )
    expect(screen.getByText('Entity')).toHaveStyle({ color: designTokensFor(mode).colour.textMuted })
    expect(screen.queryByText(file)).not.toBeInTheDocument()
  })

  it('shows the title and kind as plain text, with no link, when there is no href', () => {
    renderReference({ resource: entity, file })

    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.getByText('Purchase requests')).toBeInTheDocument()
    expect(screen.getByText('Entity')).toBeInTheDocument()
  })

  it('shows the muted file and no link when there is no resource', () => {
    renderReference({ resource: undefined, file, href: '/studio?app=Shop&resource=e1' })

    expect(screen.getByText(file)).toBeInTheDocument()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })
})
