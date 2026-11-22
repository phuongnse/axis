import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { StudioNames } from '../platform/studio'
import { StudioNamesContext } from '../platform/studioNames'
import { ThemeModeProvider } from '../platform/theme'
import { BindingChoice, PageDataPanel } from './PageDataPanel'

const names = {
  locales: ['en', 'vi'],
  entities: [{ id: 'e1', name: 'Supplier', module: null, fields: [] }],
  dataSources: [{ id: 'd1', name: 'SupplierList', module: null, parameters: [], columns: [], groupBy: null }],
} as unknown as StudioNames

function withTexts(children: ReactNode) {
  return (
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioNamesContext.Provider value={names}>{children}</StudioNamesContext.Provider>
      </TextProvider>
    </ThemeModeProvider>
  )
}

describe('PageDataPanel', () => {
  it('draws each name as a secondary button and picks it on a click', async () => {
    const onPick = vi.fn()
    render(withTexts(<PageDataPanel onPick={onPick} />))

    const name = screen.getByTestId('studio-data-entity-Supplier')
    expect(name).toHaveClass('ant-btn-variant-outlined')

    await userEvent.click(name)
    expect(onPick).toHaveBeenLastCalledWith({ kind: 'entity', name: 'Supplier' })
  })
})

describe('BindingChoice', () => {
  it('draws each widget type that can show the binding as a secondary button, and Cancel as one', async () => {
    const onChoose = vi.fn()
    const onCancel = vi.fn()
    render(
      withTexts(
        <BindingChoice
          binding={{ kind: 'entity', name: 'Supplier' }}
          widgets={[]}
          list="/widgets"
          onChoose={onChoose}
          onCancel={onCancel}
        />,
      ),
    )

    expect(screen.getByTestId('studio-binding-table')).toHaveClass('ant-btn-variant-outlined')
    await userEvent.click(screen.getByTestId('studio-binding-form'))
    expect(onChoose).toHaveBeenLastCalledWith('form')

    const cancel = screen.getByRole('button', { name: 'Cancel' })
    expect(cancel).toHaveClass('ant-btn-variant-outlined')
    await userEvent.click(cancel)
    expect(onCancel).toHaveBeenCalled()
  })
})
