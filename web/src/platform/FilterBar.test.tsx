import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { designTokensFor } from './designTokens'
import { FilterBar } from './FilterBar'
import { TextProvider } from './TextProvider'
import type { DataSourceParameter } from './site'

const parameters: DataSourceParameter[] = [
  { name: 'code', type: 'text', required: false, labelKey: null, values: null, target: null },
  { name: 'owner', type: 'text', required: false, labelKey: null, values: null, target: null },
]

describe('the filter bar', () => {
  it('spaces its filters 24px apart across and down, all in one container', () => {
    render(
      <TextProvider catalogs={[{ texts: {}, fallbackTexts: {} }]} development={false}>
        <FilterBar parameters={parameters} values={{}} errors={{}} onChange={vi.fn()} locale="en" />
      </TextProvider>,
    )

    const code = screen.getByRole('textbox', { name: 'code' }).closest<HTMLElement>('.ant-form-item')
    const owner = screen.getByRole('textbox', { name: 'owner' }).closest<HTMLElement>('.ant-form-item')
    expect(owner?.parentElement).toBe(code?.parentElement)
    expect(code?.parentElement).toHaveStyle({ gap: `${designTokensFor('light').spacing.lg}px` })
  })
})
