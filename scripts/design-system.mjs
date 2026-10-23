// Saves every design-system preview as a standalone HTML file under artifacts/design-system/.
//
// Usage: node scripts/design-system.mjs (scripts/design-system.sh builds the preview page first)
//
// The preview build in artifacts/design-system-app runs in Chromium with its API calls answered
// from fixtures. Each preview is saved in light and dark mode as <preview>-<mode>.html with its
// styles inlined and its scripts removed, so the file opens anywhere. An index.html links them.
// A console or page error stops the run, so no broken drawing is saved.
import { mkdir, rm, writeFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('..', import.meta.url))
const appFolder = path.join(root, 'artifacts', 'design-system-app')
const outputFolder = path.join(root, 'artifacts', 'design-system')
const origin = 'http://design-system.test'
const { chromium } = createRequire(new URL('../tests/e2e/package.json', import.meta.url))('@playwright/test')

const browser = await chromium.launch()
try {
  const page = await browser.newPage()
  const problems = []
  page.on('console', (message) => {
    if (message.type() === 'error') problems.push(message.text())
  })
  page.on('pageerror', (error) => problems.push(error.message))

  // Serves the preview build from disk, as the Playwright spec does.
  await page.route(`${origin}/**`, async (route) => {
    const { pathname } = new URL(route.request().url())
    const file = path.join(appFolder, pathname === '/' ? 'design-system.html' : pathname)
    if (path.relative(appFolder, file).startsWith('..')) {
      await route.fulfill({ status: 404 })
      return
    }
    await route.fulfill({ path: file }).catch(() => route.fulfill({ status: 404 }))
  })

  await page.goto(`${origin}/design-system.html`)
  await page.getByRole('link').first().waitFor()
  const links = await page.getByRole('link').evaluateAll((anchors) => anchors.map((anchor) => anchor.getAttribute('href')))

  await rm(outputFolder, { recursive: true, force: true })
  await mkdir(outputFolder, { recursive: true })
  const files = []
  for (const link of links) {
    const query = new URLSearchParams(link.slice(link.indexOf('?')))
    const file = `${query.get('preview')}-${query.get('mode')}.html`
    await page.goto(`${origin}/design-system.html${link}`)
    await page.waitForLoadState('networkidle')
    await page.waitForFunction(() => document.querySelector('[aria-busy="true"]') === null)
    await page.evaluate(() => {
      for (const sheet of document.querySelectorAll('link[rel=stylesheet]')) {
        const style = document.createElement('style')
        style.textContent = Array.from(sheet.sheet?.cssRules ?? [], (rule) => rule.cssText).join('\n')
        sheet.replaceWith(style)
      }
      document.querySelectorAll('script, link[rel=modulepreload], link[rel=icon]').forEach((element) => element.remove())
    })
    await writeFile(path.join(outputFolder, file), await page.content())
    files.push({ file, label: `${query.get('preview')} (${query.get('mode')})` })
  }

  if (problems.length > 0) {
    throw new Error(`The previews logged errors:\n${problems.join('\n')}`)
  }

  const items = files.map(({ file, label }) => `    <li><a href="${file}">${label}</a></li>`).join('\n')
  await writeFile(
    path.join(outputFolder, 'index.html'),
    `<!doctype html>\n<html lang="en">\n  <head>\n    <meta charset="UTF-8" />\n    <title>Axis design system</title>\n  </head>\n  <body>\n    <h1>Axis design system</h1>\n    <ul>\n${items}\n    </ul>\n  </body>\n</html>\n`,
  )
  console.log(`Saved ${files.length} previews to ${path.relative(root, outputFolder)}.`)
} finally {
  await browser.close()
}
