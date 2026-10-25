import type { Locator, Page } from '@playwright/test'

/**
 * Drags `source` with the mouse to the point of `target` that is `fraction` of its height down: a small move starts the
 * drag, then steps reach the point. `designer` is the test id of the Design tab, 'studio-page-design' or
 * 'studio-form-design'. It is scrolled to the top of the window first, and both points are measured after that, because
 * the page scrolls while it loads and a mouse outside the window hits nothing.
 */
export async function dragTo(page: Page, designer: string, source: Locator, target: Locator, fraction = 0.5) {
  await page.getByTestId(designer).evaluate((element) => element.scrollIntoView({ block: 'start' }))
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
