import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { graphImageName, graphSvg, svgToPng } from './graphImage'

describe('graphImageName', () => {
  it.each([
    ['PurchaseApproval', 'purchase-approval'],
    ['orders/ShipOrder', 'ship-order'],
    ['', 'process'],
    ['orders/', 'process'],
    [undefined, 'process'],
    [42, 'process'],
  ])('names %j %s', (name, expected) => {
    expect(graphImageName(name)).toBe(expected)
  })
})

describe('svgToPng', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'Image',
      class {
        onload: (() => void) | null = null
        set src(_: string) {
          queueMicrotask(() => this.onload?.())
        }
      },
    )
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({ drawImage() {} } as never)
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(function (
      this: HTMLCanvasElement,
      callback,
      type,
    ) {
      callback(new Blob([`${this.width}x${this.height}`], { type }))
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('draws at twice the size', async () => {
    const png = await svgToPng('<svg/>', 800, 300)

    expect(png.type).toBe('image/png')
    expect(await png.text()).toBe('1600x600')
  })

  it('draws at less when a side would pass the limit of a canvas', async () => {
    const png = await svgToPng('<svg/>', 20000, 400)

    expect(await png.text()).toBe('16384x327')
  })

  it('fails when the canvas makes no blob', async () => {
    vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation((callback) => callback(null))

    await expect(svgToPng('<svg/>', 10, 10)).rejects.toThrow('PNG')
  })
})

describe('graphSvg', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('inlines the font files of the families it draws in, resolved against their stylesheet, and no others', async () => {
    vi.stubGlobal('SVGImageElement', class {})
    const fetched: string[] = []
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: URL) => {
        fetched.push(String(url))
        return { ok: true, blob: async () => new Blob(['font'], { type: 'font/woff2' }) }
      }),
    )
    // jsdom's style sheets keep no `src` of a font face, so the sheet is a stand-in with the rules a browser gives.
    const face = (family: string, file: string) => ({
      cssText: `@font-face { font-family: "${family}"; src: url(/assets/${file}) format("woff2"); }`,
      style: { getPropertyValue: () => `"${family}"` },
    })
    vi.spyOn(document, 'styleSheets', 'get').mockReturnValue([
      {
        href: `${window.location.origin}/assets/index.css`,
        cssRules: [face('Drawn Face', 'drawn.woff2'), face('Other Face', 'other.woff2')],
      },
    ] as never)
    const viewport = document.createElement('div')
    viewport.style.fontFamily = "'Drawn Face', sans-serif"
    viewport.textContent = 'Submit'
    document.body.append(viewport)

    const svg = await graphSvg(viewport, { x: 0, y: 0, width: 100, height: 50 }, '#ffffff')

    viewport.remove()
    expect(fetched).toEqual([`${window.location.origin}/assets/drawn.woff2`])
    expect(svg).toContain('Drawn Face')
    expect(svg).toContain('data:font/woff2;base64,')
    expect(svg).not.toContain('Other Face')
  })
})
