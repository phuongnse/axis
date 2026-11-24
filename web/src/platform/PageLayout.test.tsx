import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider } from 'antd'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { ContentInsetContext } from './contentInsetContext'
import { spacingTokens } from './designTokens'
import { PageActions, PageLayout, PageName } from './PageLayout'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

describe('PageLayout', () => {
  it.each(['light', 'dark'])('shows the title as a heading and the content in %s mode', (mode) => {
    localStorage.setItem('axis.themeMode', mode)

    render(
      <ThemeModeProvider>
        <PageLayout title="Purchase requests">
          <p>Page content</p>
        </PageLayout>
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('heading', { name: 'Purchase requests' })).toBeInTheDocument()
    expect(screen.getByText('Page content')).toBeInTheDocument()
  })

  it('makes the title the level-one heading of the page', () => {
    render(
      <ThemeModeProvider>
        <PageLayout title="Purchase requests">
          <p>Page content</p>
        </PageLayout>
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('heading', { level: 1, name: 'Purchase requests' })).toBeInTheDocument()
  })

  it.each([24, 16])(
    'draws the heading alone in a band with spacing-md above and below it and its sides and top reach the content inset of %dpx',
    (inset) => {
      render(
        <ThemeModeProvider>
          <ContentInsetContext.Provider value={inset}>
            <PageLayout title="Users" band>
              <p>Page content</p>
            </PageLayout>
          </ContentInsetContext.Provider>
        </ThemeModeProvider>,
      )

      const band = screen.getByTestId('page-band')
      expect(within(band).getByRole('heading', { level: 1, name: 'Users' })).toBeInTheDocument()
      expect(within(band).queryByText('Page content')).not.toBeInTheDocument()
      expect(band).toHaveStyle({
        marginTop: `-${inset}px`,
        marginLeft: `-${inset}px`,
        marginRight: `-${inset}px`,
        paddingTop: `${spacingTokens.md}px`,
        paddingBottom: `${spacingTokens.md}px`,
        paddingLeft: `${inset}px`,
        paddingRight: `${inset}px`,
      })
    },
  )

  it('draws no band without band', () => {
    render(
      <ThemeModeProvider>
        <PageLayout title="Users" />
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('heading', { level: 1, name: 'Users' })).toBeInTheDocument()
    expect(screen.queryByTestId('page-band')).not.toBeInTheDocument()
  })

  it('draws the tags beside the title and the line under it, and no line when it is empty', () => {
    const { rerender } = render(
      <ThemeModeProvider>
        <PageLayout title="Purchase requests" tags={<span>Live</span>} line="Release" />
      </ThemeModeProvider>,
    )

    const heading = screen.getByTestId('page-heading')
    expect(within(heading).getByRole('heading', { level: 1, name: 'Purchase requests' })).toBeInTheDocument()
    expect(within(heading).getByText('Live')).toBeInTheDocument()
    expect(within(heading).getByText('Release')).toBeInTheDocument()

    rerender(
      <ThemeModeProvider>
        <PageLayout title="Purchase requests" line="" />
      </ThemeModeProvider>,
    )
    expect(screen.queryByText('Release')).not.toBeInTheDocument()
  })

  it('draws the actions of a component deep in the page in the slot on the right of the heading', async () => {
    render(
      <ThemeModeProvider>
        <PageLayout title="Purchase requests">
          <section>
            <div>
              <PageActions>
                <button type="button">Activate…</button>
              </PageActions>
            </div>
          </section>
        </PageLayout>
      </ThemeModeProvider>,
    )

    const actions = await screen.findByTestId('page-actions')
    expect(await within(actions).findByRole('button', { name: 'Activate…' })).toBeInTheDocument()
    expect(within(screen.getByTestId('page-heading')).getByRole('button', { name: 'Activate…' })).toBeInTheDocument()
  })

  it('draws the actions where they stand outside a page', () => {
    render(
      <PageActions>
        <button type="button">Activate…</button>
      </PageActions>,
    )

    expect(screen.getByRole('button', { name: 'Activate…' })).toBeInTheDocument()
  })

  it('draws the actions where they stand inside a page when asked, not on its heading', () => {
    render(
      <ThemeModeProvider>
        <PageLayout title="Purchase requests">
          <PageActions inPlace>
            <button type="button">Activate…</button>
          </PageActions>
        </PageLayout>
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('button', { name: 'Activate…' })).toBeInTheDocument()
    expect(within(screen.getByTestId('page-actions')).queryByRole('button')).not.toBeInTheDocument()
  })

  it('folds the fold actions of PageActions into one More actions menu in the heading slot', async () => {
    const texts = { 'shell.actionRow.more': 'More actions' }
    const onBeta = vi.fn()
    render(
      <ThemeModeProvider>
        <ConfigProvider theme={{ token: { motion: false } }}>
          <TextProvider catalogs={[{ texts, fallbackTexts: texts }]} development={false}>
            <PageLayout title="Purchase requests">
              <PageActions
                fold={[
                  { key: 'b', label: 'Beta', onClick: onBeta },
                  { key: 'c', label: 'Gamma' },
                ]}
              >
                <button type="button">Alpha</button>
              </PageActions>
            </PageLayout>
          </TextProvider>
        </ConfigProvider>
      </ThemeModeProvider>,
    )

    const actions = await screen.findByTestId('page-actions')
    expect(await within(actions).findByRole('button', { name: 'Alpha' })).toBeInTheDocument()
    expect(within(actions).queryByRole('button', { name: 'Beta' })).not.toBeInTheDocument()
    await userEvent.click(within(actions).getByRole('button', { name: 'More actions' }))
    const menu = await screen.findByRole('menu', { name: 'More actions' })
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent),
    ).toEqual(['Beta', 'Gamma'])
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'Beta' }))
    expect(onBeta).toHaveBeenCalledTimes(1)
  })

  it('names the heading through PageName, with its line, and gives the title back when it unmounts', () => {
    function Named({ show }: { show: boolean }) {
      return show ? <PageName name="Buy paper" line="Note" /> : null
    }
    function Page() {
      const [show, setShow] = useState(true)
      return (
        <ThemeModeProvider>
          <PageLayout title="Purchase requests">
            <button type="button" onClick={() => setShow(false)}>
              Hide name
            </button>
            <Named show={show} />
          </PageLayout>
        </ThemeModeProvider>
      )
    }
    render(<Page />)

    expect(screen.getByRole('heading', { level: 1, name: 'Buy paper' })).toBeInTheDocument()
    expect(within(screen.getByTestId('page-heading')).getByText('Note')).toBeInTheDocument()

    act(() => screen.getByRole('button', { name: 'Hide name' }).click())

    expect(screen.getByRole('heading', { level: 1, name: 'Purchase requests' })).toBeInTheDocument()
    expect(screen.queryByText('Note')).not.toBeInTheDocument()
  })

  it('draws nothing and names no heading outside a page', () => {
    render(<PageName name="Buy paper" line="Note" />)

    expect(screen.queryByRole('heading')).not.toBeInTheDocument()
    expect(screen.queryByText('Note')).not.toBeInTheDocument()
  })
})
