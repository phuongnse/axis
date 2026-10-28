import { expect, test } from "../test";
import { sessionFile } from "../sessions";

// The data source needs a grant, so these journeys start signed in as the employee requester.
test.use({ storageState: sessionFile("requester") });

// Specs run in parallel against one database, so the totals change between runs. The checks match
// the number pattern; Vitest covers the exact formatting and the empty text.
const notes = /^Notes: [\d,]+$/;
const amountTotal = /^Amount total: (-?[\d,]+(\.\d+)?|—No value)$/;

test("a KPI tile shows its value and comparison, and clicking it opens the target page", async ({
  page,
}) => {
  await page.goto("/e2e/notetotals");

  await expect(page.getByTestId("kpi-value")).toHaveText(notes);
  await expect(page.getByTestId("kpi-comparison")).toHaveText(amountTotal);
  const tile = page.getByRole("link", { name: /^Notes: [\d,]+/ });
  await expect(tile).toContainText("Show notes");

  await tile.click();

  await expect(page).toHaveURL(/\/e2e\/notes$/);
});

test("a KPI tile without a comparison or an action shows one value and no link", async ({
  page,
}) => {
  await page.goto("/e2e/notecount");

  await expect(page.getByTestId("kpi-value")).toHaveText(notes);
  await expect(page.getByTestId("kpi-comparison")).toHaveCount(0);
  await expect(page.getByTestId("kpi-tile").getByRole("link")).toHaveCount(0);
  await expect(page.getByRole("link", { name: /^Notes: [\d,]+/ })).toHaveCount(0);
});
