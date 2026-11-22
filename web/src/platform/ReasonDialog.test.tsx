import { ConfigProvider } from 'antd'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ReasonDialog } from './ReasonDialog'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const catalogs = [{ texts: {}, fallbackTexts: {} }]

function renderDialog() {
  const onSend = vi.fn()
  render(
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <ReasonDialog
            title="Rename"
            label="Name"
            finishText="Rename"
            cancelText="Cancel"
            requiredMessage="Enter a name"
            onSend={onSend}
            onClose={vi.fn()}
            onChanged={vi.fn()}
          />
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
  return { onSend }
}

describe('ReasonDialog', () => {
  it('draws its finish button with no danger fill', () => {
    renderDialog()

    expect(screen.getByRole('button', { name: 'Rename' }).style.background).toBe('')
  })

  it('takes the focus to the field and sends nothing when the field is empty', async () => {
    const { onSend } = renderDialog()

    await userEvent.click(screen.getByRole('button', { name: 'Rename' }))

    expect(await screen.findByText('Enter a name')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveFocus()
    expect(onSend).not.toHaveBeenCalled()
  })
})
