import { expect, type Page } from "@playwright/test";

// A phone is 375 px wide.
export const phoneWidth = 375;

/** Expects the page not to scroll sideways: the document is never wider than the window. */
export async function noSideways(page: Page) {
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth),
  ).toBeLessThanOrEqual(phoneWidth);
}

/**
 * Expects the moved lines of every row to sit under the content of the cells that stay, so a cell that wraps never runs
 * under them. See docs/reference/actions-and-controls.md#tables.
 */
export async function movedLinesClear(page: Page) {
  const overlaps = await page.evaluate(() => {
    const found: string[] = [];
    document.querySelectorAll("[data-moved-cells]").forEach((lines) => {
      const top = lines.getBoundingClientRect().top;
      lines
        .closest("tr")
        ?.querySelectorAll("td")
        .forEach((cell) => {
          const content = cell.lastElementChild;
          if (
            content &&
            !cell.contains(lines) &&
            content.getBoundingClientRect().bottom > top + 1
          ) {
            found.push(cell.textContent ?? "");
          }
        });
    });
    return found;
  });
  expect(overlaps).toEqual([]);
}
