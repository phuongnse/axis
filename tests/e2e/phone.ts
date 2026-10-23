import { expect, type Page } from '@playwright/test'

// A phone is 375 px wide.
export const phoneWidth = 375

/** Expects the page not to scroll sideways: the document is never wider than the window. */
export async function noSideways(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(phoneWidth)
}
