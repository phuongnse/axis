import { expect, type Locator, type Page } from '@playwright/test'

/**
 * Drags `source` with the mouse to the point of `target` that is `fraction` of its height down: a small move starts the
 * drag, then steps reach the point. `designer` is the test id of the Design tab, 'studio-page-design' or
 * 'studio-form-design'. It is scrolled to the top of the window first, and both points are measured after that, because
 * the page scrolls while it loads and a mouse outside the window hits nothing. On a page designer it first waits until
 * the canvas has drawn the latest draft, and until the target holds still, because a redraw can move the target. A point
 * in the top fifth of the window moves to 40% down first, because the drag library scrolls the window while the pointer
 * stays in that fifth.
 */
export async function dragTo(page: Page, designer: string, source: Locator, target: Locator, fraction = 0.5) {
  await page.getByTestId(designer).evaluate((element) => element.scrollIntoView({ block: 'start' }))
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
  const to = (await target.boundingBox())!
  const startX = from.x + from.width / 2
  const startY = from.y + from.height / 2
  await page.mouse.move(startX, startY)
  await page.mouse.down()
  await page.mouse.move(startX + 10, startY + 10)
  await page.mouse.move(to.x + to.width / 2, to.y + to.height * fraction, { steps: 15 })
  await page.mouse.up()
}
