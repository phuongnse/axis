import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { ResourceReference } from './ResourceReference'

const entity = { kind: 'entity', name: 'PurchaseRequest', titles: { en: 'Purchase requests' } }
const file = 'entities/purchase-request.json'

function renderReference(props: Parameters<typeof ResourceReference>[0]) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <ResourceReference {...props} />
      </TextProvider>
    </ThemeModeProvider>,
  )
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('ResourceReference in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('shows a link named by the title, with the kind after it and no file', async () => {
    const onOpen = vi.fn()
    renderReference({ resource: entity, file, href: '/studio?app=PurchaseRequests&resource=r' })

    await userEvent.click(screen.getByRole('button', { name: 'Purchase requests' }))
    expect(onOpen).toHaveBeenCalledOnce()
    expect(screen.getByText('Entity')).toBeInTheDocument()
    expect(screen.queryByText(file)).not.toBeInTheDocument()
  })

  it('shows the title and kind as plain text when nothing can open the resource', () => {
    renderReference({ resource: entity, file })

    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.getByText('Purchase requests')).toBeInTheDocument()
    expect(screen.getByText('Entity')).toBeInTheDocument()
  })

  it('shows the muted file and no button when there is no resource', () => {
    renderReference({ resource: undefined, file, href: '/studio?app=PurchaseRequests&resource=r' })

    expect(screen.getByText(file)).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })
})
