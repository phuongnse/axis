import { render, screen } from '@testing-library/react'
import { useContext, type ReactNode } from 'react'
import { afterEach, describe, expect, it } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { ColumnGrid, ColumnGridCell } from './ColumnGrid'
import { ContentInsetContext } from './contentInsetContext'
import { PartBoundary } from './SectionParts'
import { SidePanel } from './SidePanel'
import { ShellAsideContext } from './shellAsideContext'
import { ShellSidebar } from './shellSidebar'
import { ShellSidebarContext } from './shellSidebarContext'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

/** Shows the inset that the context gives the element that reads it. */
function InsetReader({ testId }: { testId: string }) {
  const inset = useContext(ContentInsetContext)
  return <span data-testid={testId}>{inset}</span>
}

/** The elements that the shell's sidebar and right column draw into, which the tests append and remove. */
const targets: HTMLElement[] = []

function targetElement() {
  const element = document.createElement('div')
  document.body.append(element)
  targets.push(element)
  return element
}

afterEach(() => {
  targets.splice(0).forEach((element) => element.remove())
})

/** Renders what is given under a page whose content inset is 24. */
function renderOnPage(element: ReactNode) {
  return render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <ContentInsetContext.Provider value={24}>{element}</ContentInsetContext.Provider>
      </TextProvider>
    </ThemeModeProvider>,
  )
}

describe('the content inset that a page gives what it holds', () => {
  it('gives a reader with no reset the inset of the page', () => {
    renderOnPage(<InsetReader testId="page" />)

    expect(screen.getByTestId('page')).toHaveTextContent('24')
  })

  it('gives a reader inside a PartBoundary no inset, beside a reader that keeps the inset of the page', () => {
    renderOnPage(
      <>
        <InsetReader testId="page" />
        <PartBoundary>
          <InsetReader testId="boundary" />
        </PartBoundary>
      </>,
    )

    expect(screen.getByTestId('page')).toHaveTextContent('24')
    expect(screen.getByTestId('boundary')).toHaveTextContent('0')
  })

  it('gives a reader inside a ColumnGridCell no inset, beside a reader that keeps the inset of the page', () => {
    renderOnPage(
      <>
        <InsetReader testId="page" />
        <ColumnGrid>
          <ColumnGridCell width={6}>
            <InsetReader testId="cell" />
          </ColumnGridCell>
        </ColumnGrid>
      </>,
    )

    expect(screen.getByTestId('page')).toHaveTextContent('24')
    expect(screen.getByTestId('cell')).toHaveTextContent('0')
  })

  it('gives a reader inside the sidebar no inset, beside a reader that keeps the inset of the page', () => {
    const sidebar = targetElement()
    renderOnPage(
      <ShellSidebarContext.Provider value={{ target: sidebar, collapsed: false, claim: () => () => {} }}>
        <InsetReader testId="page" />
        <ShellSidebar>
          <InsetReader testId="sidebar" />
        </ShellSidebar>
      </ShellSidebarContext.Provider>,
    )

    expect(screen.getByTestId('page')).toHaveTextContent('24')
    expect(screen.getByTestId('sidebar')).toHaveTextContent('0')
  })

  it('gives a reader inside a side panel no inset, beside a reader that keeps the inset of the page', () => {
    const aside = targetElement()
    renderOnPage(
      <ShellAsideContext.Provider value={{ target: aside, claim: () => () => {} }}>
        <InsetReader testId="page" />
        <SidePanel id="panel" title="Panel" testId="panel">
          <InsetReader testId="panel-body" />
        </SidePanel>
      </ShellAsideContext.Provider>,
    )

    expect(screen.getByTestId('page')).toHaveTextContent('24')
    expect(screen.getByTestId('panel-body')).toHaveTextContent('0')
  })
})
