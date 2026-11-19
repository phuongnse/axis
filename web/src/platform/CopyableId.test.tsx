import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { CopyableId } from './CopyableId'
import { TextProvider } from './TextProvider'

const texts = { 'shell.copy.copy': 'Copy', 'shell.copy.copied': 'Copied' }
const id = '0af7651916cd43dd8448eb211c80319c'

function renderId() {
  const writeText = vi.fn().mockResolvedValue(undefined)
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
  render(
    <TextProvider catalogs={[{ texts, fallbackTexts: texts }]} development={false}>
      <CopyableId id={id} />
    </TextProvider>,
  )
  return writeText
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe('CopyableId', () => {
  it('shows the first 8 characters in the code font', () => {
    renderId()

    const shown = screen.getByText('0af76519')
    expect(shown.tagName).toBe('CODE')
    expect(screen.queryByText(id)).not.toBeInTheDocument()
  })

  it('copies the whole id', () => {
    const writeText = renderId()

    fireEvent.click(screen.getByRole('button', { name: 'Copy' }))

    expect(writeText).toHaveBeenCalledExactlyOnceWith(id)
  })

  it('says Copy on hover and Copied after the click', async () => {
    renderId()
    const button = screen.getByRole('button', { name: 'Copy' })

    await userEvent.hover(button)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Copy')

    await userEvent.click(button)

    expect(await screen.findByText('Copied')).toBeInTheDocument()
    expect(screen.getByRole('tooltip')).toHaveTextContent('Copied')
  })

  it('does not say Copied when the clipboard refuses', async () => {
    const writeText = renderId()
    writeText.mockRejectedValue(new Error('denied'))
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})

    await userEvent.click(screen.getByRole('button', { name: 'Copy' }))

    expect(warn).toHaveBeenCalled()
    expect(screen.queryByText('Copied')).not.toBeInTheDocument()
  })
})
