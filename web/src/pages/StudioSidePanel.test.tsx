import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { ShellAsideContext } from '../platform/shellAsideContext'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioSidePanel } from './StudioSidePanel'

let aside: HTMLDivElement | undefined

/** Renders a panel in the shell's right column, which is `aside`. */
function renderPanel(onClose = vi.fn()) {
  aside = document.createElement('div')
  document.body.append(aside)
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <ShellAsideContext.Provider value={{ target: aside, claim: () => () => {} }}>
          <StudioSidePanel
            id="panel"
            title="Problems"
            closeNameKey="shell.studio.problems.close"
            onClose={onClose}
            testId="panel-test"
            closeTestId="panel-close"
          >
            <p>Body text</p>
          </StudioSidePanel>
        </ShellAsideContext.Provider>
      </TextProvider>
    </ThemeModeProvider>,
  )
  return onClose
}

afterEach(() => {
  aside?.remove()
  aside = undefined
})

describe('StudioSidePanel', () => {
  it('is drawn in the right column, named by its title', () => {
    renderPanel()

    expect(aside).toContainElement(screen.getByRole('complementary', { name: 'Problems' }))
    expect(screen.getByTestId('panel-test')).toHaveAttribute('id', 'panel')
  })

  it('draws its title and close button outside the body, which scrolls on its own', () => {
    renderPanel()

    const body = screen.getByText('Body text').parentElement!
    expect(body).toHaveStyle({ overflowY: 'auto' })
    expect(body).not.toContainElement(screen.getByRole('heading', { name: 'Problems' }))
    expect(body).not.toContainElement(screen.getByTestId('panel-close'))
  })

  it('runs onClose from the close button', async () => {
    const onClose = renderPanel()

    await userEvent.click(screen.getByRole('button', { name: 'Close problems' }))

    expect(onClose).toHaveBeenCalledTimes(1)
  })
})
