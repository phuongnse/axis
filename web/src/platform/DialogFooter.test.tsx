import { ConfigProvider } from 'antd'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { AxisButton } from './AxisButton'
import { DialogFooter } from './DialogFooter'
import { designTokensFor } from './designTokens'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const catalogs = [{ texts: {}, fallbackTexts: {} }]

function renderFooter(props: Partial<Parameters<typeof DialogFooter>[0]> = {}) {
  const onFinish = vi.fn()
  const onCancel = vi.fn()
  render(
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <DialogFooter finishText="Delete" cancelText="Cancel" onFinish={onFinish} onCancel={onCancel} {...props} />
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
  return { onFinish, onCancel }
}

describe('DialogFooter', () => {
  it('draws a destructive finish as a solid danger fill', () => {
    renderFooter({ destructive: true })

    expect(screen.getByRole('button', { name: 'Delete' })).toHaveStyle({
      background: designTokensFor('light').colour.danger,
    })
  })

  it('draws a finish that is not destructive with no inline background', () => {
    renderFooter()

    expect(screen.getByRole('button', { name: 'Delete' }).style.background).toBe('')
  })

  it('calls onCancel from Cancel and onFinish from the finish button', async () => {
    const { onFinish, onCancel } = renderFooter()

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(onCancel).toHaveBeenCalledOnce()
    expect(onFinish).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    expect(onFinish).toHaveBeenCalledOnce()
  })

  it('draws no Cancel when cancelText is left out, and the finish still calls onFinish', async () => {
    const { onFinish } = renderFooter({ cancelText: undefined })

    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual(['Delete'])
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    expect(onFinish).toHaveBeenCalledOnce()
  })

  it('draws the other actions before Cancel in the same row', () => {
    renderFooter({ actions: <AxisButton role="secondary">Reset</AxisButton> })

    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual(['Reset', 'Cancel', 'Delete'])
  })

  it('disables the finish button with finishDisabled', async () => {
    const { onFinish } = renderFooter({ finishDisabled: true })

    expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    expect(onFinish).not.toHaveBeenCalled()
  })
})
