import { ConfigProvider, Form, Input } from 'antd'
import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AxisField } from './AxisField'
import { AxisFieldRow } from './AxisFieldRow'
import { requiredMark } from './requiredMark'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const texts = {}
const catalogs = [{ texts, fallbackTexts: texts }]

function stubWindowWidth(width: number) {
  vi.stubGlobal('matchMedia', (query: string) => {
    const max = /max-width: ([\d.]+)px/.exec(query)
    return {
      matches: max !== null && width <= Number(max[1]),
      media: query,
      onchange: null,
      addListener() {},
      removeListener() {},
      addEventListener() {},
      removeEventListener() {},
      dispatchEvent: () => false,
    }
  })
}

function renderRow() {
  render(
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <Form layout="vertical" requiredMark={requiredMark}>
            <AxisFieldRow title="Length" help="The fewest and the most characters.">
              <AxisField label="Minimum" help="At least." name="min">
                <Input />
              </AxisField>
              <AxisField label="Maximum" help="At most." name="max">
                <Input />
              </AxisField>
            </AxisFieldRow>
          </Form>
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
  const group = screen.getByRole('group', { name: 'Length' })
  const row = screen.getByRole('textbox', { name: 'Minimum' }).closest('div[style*="flex-direction"]') as HTMLElement
  return { group, row }
}

describe('AxisFieldRow', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('puts its two fields in one row at desktop width', () => {
    stubWindowWidth(1280)

    const { group, row } = renderRow()

    expect(group).toHaveAccessibleDescription('The fewest and the most characters.')
    expect(group).toContainElement(row)
    expect(row).toContainElement(screen.getByRole('textbox', { name: 'Maximum' }))
    expect(row.style.flexDirection).toBe('row')
  })

  it('stacks its two fields below the narrow-width breakpoint', () => {
    stubWindowWidth(991)

    const { row } = renderRow()

    expect(row).toContainElement(screen.getByRole('textbox', { name: 'Maximum' }))
    expect(row.style.flexDirection).toBe('column')
  })

  it('requires help', () => {
    expect(
      // @ts-expect-error Every platform field group has help.
      <AxisFieldRow title="Length">
        <Input />
      </AxisFieldRow>,
    ).toBeDefined()
  })
})
