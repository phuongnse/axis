import { kebabName } from './studioStarters'
import type { GraphBounds } from './processLayout'

/** The longest side of a canvas, in px, which a browser may cap lower than most do. */
const canvasLimit = 16384

/** The PNG's pixel ratio: twice the SVG's size, which stays sharp in a printed document. */
const pixelRatio = 2

const unquote = (family: string) => family.trim().replace(/["']/g, '')

/** A file as a `data:` URL. */
function dataUrl(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as string)
    reader.onerror = () => reject(reader.error)
    reader.readAsDataURL(blob)
  })
}

/**
 * The `@font-face` rules of the page for the font families `element` draws in, each file inlined as a `data:` URL, so
 * the image keeps the page's fonts wherever it opens. A file that cannot be read is left out, and its text falls back to
 * the next family. The image library does this too, but it resolves each file's address through a `<base>` element,
 * which the page's CSP does not allow.
 */
async function fontFaces(element: HTMLElement): Promise<string> {
  const used = new Set(getComputedStyle(element).fontFamily.split(',').map(unquote))
  const faces: Promise<string>[] = []
  for (const sheet of document.styleSheets) {
    let rules: CSSRuleList
    try {
      rules = sheet.cssRules
    } catch {
      // A sheet from another origin cannot be read.
      continue
    }
    for (const rule of rules) {
      if (
        !rule.cssText.startsWith('@font-face') ||
        !used.has(unquote((rule as CSSFontFaceRule).style.getPropertyValue('font-family')))
      ) {
        continue
      }
      const base = sheet.href ?? document.baseURI
      faces.push(inlineFiles(rule.cssText, base).catch(() => ''))
    }
  }
  return (await Promise.all(faces)).join('\n')
}

/** `css` with each `url()` that is not a `data:` URL replaced by the file it names, resolved against `base`. */
async function inlineFiles(css: string, base: string): Promise<string> {
  const files = await Promise.all(
    [...css.matchAll(/url\(\s*(["']?)([^"')]+)\1\s*\)/g)].map(async ([, , url]) => {
      if (url!.startsWith('data:')) {
        return [url!, url!] as const
      }
      const response = await fetch(new URL(url!, base))
      if (!response.ok) {
        throw new Error(`${url} could not be read.`)
      }
      return [url!, await dataUrl(await response.blob())] as const
    }),
  )
  return files.reduce((text, [url, data]) => text.replaceAll(url, data), css)
}

/**
 * The SVG text of a drawn graph, from its React Flow `viewport` element, of `box` in the graph's own units, on
 * `background`, see docs/reference/process-designer.md#download. The image library loads here, so only a download
 * pays for it.
 */
export async function graphSvg(viewport: HTMLElement, box: GraphBounds, background: string): Promise<string> {
  const { toSvg } = await import('html-to-image')
  const { width, height } = box
  const url = await toSvg(viewport, {
    width,
    height,
    backgroundColor: background,
    fontEmbedCSS: await fontFaces(viewport),
    style: {
      width: `${width}px`,
      height: `${height}px`,
      transform: `translate(${-box.x}px, ${-box.y}px) scale(1)`,
    },
  })
  return decodeURIComponent(url.slice(url.indexOf(',') + 1))
}

/** Loads an image from `url`, or rejects when the browser cannot draw it. */
function loadImage(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image()
    image.onload = () => resolve(image)
    image.onerror = () => reject(new Error('The SVG could not be drawn.'))
    image.src = url
  })
}

/**
 * The PNG of an SVG of `width` by `height`, drawn at twice the size, or at less when a side would pass the browser's
 * canvas limit, see docs/reference/process-designer.md#download. The SVG loads as a `data:` URL, which the page's CSP
 * allows for images and a `blob:` URL it does not.
 */
export async function svgToPng(svg: string, width: number, height: number): Promise<Blob> {
  const image = await loadImage(`data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`)
  const ratio = Math.min(pixelRatio, canvasLimit / Math.max(width, height))
  const canvas = document.createElement('canvas')
  canvas.width = Math.max(1, Math.floor(width * ratio))
  canvas.height = Math.max(1, Math.floor(height * ratio))
  const context = canvas.getContext('2d')
  if (context === null) {
    throw new Error('The canvas has no 2D context.')
  }
  context.drawImage(image, 0, 0, canvas.width, canvas.height)
  return new Promise((resolve, reject) =>
    canvas.toBlob(
      (blob) => (blob === null ? reject(new Error('The PNG could not be made.')) : resolve(blob)),
      'image/png',
    ),
  )
}

/**
 * The stem of the file a process's graph is saved as, see docs/reference/process-designer.md#download: the kebab case
 * of the text after the last `/` of its name, as a module process is named, and `process` for no name.
 */
export function graphImageName(processName: unknown): string {
  const name = typeof processName === 'string' ? processName.slice(processName.lastIndexOf('/') + 1) : ''
  return name === '' ? 'process' : kebabName(name)
}
