import { expect, type Locator, type Page } from '@playwright/test'

/** Waits until the page canvas has drawn the latest draft: the redraw after an edit moves what is below it. */
export async function waitForCanvas(page: Page) {
  await expect(page.locator('[data-testid="studio-page-canvas"][aria-busy="true"]')).toHaveCount(0)
}

/**
 * Scrolls `element` to the top of the window, just below the shell's sticky header, which covers the top of the window
 * and would take the mouse of a drag that starts or aims there.
 */
export async function scrollBelowHeader(element: Locator) {
  await element.evaluate((node) => {
    const header = document.querySelector('header')
    node.scrollIntoView({ block: 'start' })
    window.scrollBy(0, -(header?.getBoundingClientRect().height ?? 0))
  })
}

/**
 * Drags `source` with the mouse to the point of `target` that is `fraction` of its height down: a small move starts the
 * drag, then steps reach the point. `designer` is the test id of the Design tab, 'studio-page-design' or
 * 'studio-form-design'. It is scrolled to the top of the window, below the header, first, and both points are measured after that, because
 * the page scrolls while it loads and a mouse outside the window hits nothing. On a page designer it first waits until
 * the canvas has drawn the latest draft, and until the target holds still, because a redraw can move the target. After
 * the mouse reaches the point, a page designer aims again until the canvas marks the target with `data-drop-over`, and
 * only then releases the mouse, because the target can move during the drag. After the release, a page designer waits
 * until the canvas has drawn the dropped draft, so that a click that follows does not meet a layout that is moving. A point in the top fifth of the window
 * moves to 40% down first, because the drag library scrolls the window while the pointer stays in that fifth. A point in
 * the bottom fifth does the same once the drag has started: the window scrolls it to 40% down while the pointer is still
 * near the source, because the library would scroll the target away from the pointer between the aim and the release.
 */
export async function dragTo(page: Page, designer: string, source: Locator, target: Locator, fraction = 0.5) {
  await scrollBelowHeader(page.getByTestId(designer))
  await expect(
    page.getByTestId(designer).locator('[data-testid="studio-page-canvas"][aria-busy="true"]'),
  ).toHaveCount(0)
  let last = ''
  await expect(async () => {
    const box = JSON.stringify(await target.boundingBox())
    const same = box === last
    last = box
    expect(same).toBe(true)
  }).toPass({ intervals: [100] })
  const height = await page.evaluate(() => window.innerHeight)
  const reach = (await target.boundingBox())!
  const reachY = reach.y + reach.height * fraction
  if (reachY < height * 0.2) {
    await page.evaluate((by) => window.scrollBy(0, by), reachY - height * 0.4)
  }
  const from = (await source.boundingBox())!
  const startX = from.x + from.width / 2
  const startY = from.y + from.height / 2
  await page.mouse.move(startX, startY)
  await page.mouse.down()
  await page.mouse.move(startX + 10, startY + 10)
  const below = (await target.boundingBox())!
  const belowY = below.y + below.height * fraction
  if (belowY > height * 0.8) {
    await page.evaluate((by) => window.scrollBy(0, by), belowY - height * 0.4)
  }
  const to = (await target.boundingBox())!
  await page.mouse.move(to.x + to.width / 2, to.y + to.height * fraction, { steps: 15 })
  if (designer === 'studio-page-design') {
    // A redraw or the drag library's scroll can move the target while the mouse travels, so aim again until it is the
    // drop target.
    await expect(async () => {
      const box = (await target.boundingBox())!
      await page.mouse.move(box.x + box.width / 2, box.y + box.height * fraction, { steps: 2 })
      await expect(target).toHaveAttribute('data-drop-over', 'true', { timeout: 500 })
    }).toPass({ timeout: 10_000 })
  }
  await page.mouse.up()
  if (designer === 'studio-page-design') {
    await waitForCanvas(page)
  }
}
