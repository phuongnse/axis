import { render, screen } from '@testing-library/react'
import { Button, ConfigProvider, Select } from 'antd'
import { describe, expect, it } from 'vitest'
import { antConfigFor } from '../platform/antTheme'
import { designTokensFor } from '../platform/designTokens'
import { withAntdStyles } from './antdStyles'

function renderDangerButton() {
  return render(
    <ConfigProvider {...antConfigFor('light')}>
      <Button danger type="text">
        Delete
      </Button>
      <Select options={[{ value: 'open', label: 'Open' }]} />
    </ConfigProvider>,
  )
}

describe('without antd styles', () => {
  it('renders antd components with no style rule in jsdom', () => {
    renderDangerButton()
    const button = screen.getByRole('button', { name: 'Delete' })

    expect(screen.getByRole('combobox')).toBeInTheDocument()
    const rules = [...document.querySelectorAll('style')].reduce(
      (count, style) => count + (style.sheet?.cssRules.length ?? 0),
      0,
    )
    expect(rules).toBe(0)
    expect(getComputedStyle(button).getPropertyValue('--ant-color-error')).toBe('')
  })
})

describe('with antd styles', () => {
  withAntdStyles()

  it('computes the dangerText colour of a danger button', () => {
    renderDangerButton()

    expect(getComputedStyle(screen.getByRole('button', { name: 'Delete' })).getPropertyValue('--ant-color-error')).toBe(
      designTokensFor('light').colour.dangerText,
    )
  })
})
