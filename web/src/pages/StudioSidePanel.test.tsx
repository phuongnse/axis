import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ShellAsideContext } from '../platform/shellAsideContext'
import { TextProvider } from '../platform/TextProvider'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { StudioSidePanel } from './StudioSidePanel'

/** Draws the panel into a column of its own, with its list as tall as `contentHeight` in a 300 px high box. */
function renderPanel(contentHeight: number) {
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(300)
  vi.spyOn(HTMLElement.prototype, 'scrollHeight', 'get').mockReturnValue(contentHeight)
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
  const column = document.createElement('div')
  document.body.append(column)
  render(
    <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
      <ShellAsideContext.Provider value={{ target: column, claim: () => () => {} }}>
        <StudioSidePanel id="panel" title="Ask AI" closeKey="shell.studio.ai.close" onClose={() => {}}>
          <p>The list</p>
        </StudioSidePanel>
      </ShellAsideContext.Provider>
    </TextProvider>,
  )
  return screen.getByTestId('studio-side-panel-list')
}

// Proposed: a wheel over a side panel whose list fits scrolls the page, see the review on #3263.
describe('StudioSidePanel', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('leaves the scroll to the page while its list fits', () => {
    expect(renderPanel(200).style.overscrollBehavior).toBe('auto')
  })

  it('keeps the scroll at the end of a list that overflows from the page', () => {
    expect(renderPanel(900).style.overscrollBehavior).toBe('contain')
  })
})
