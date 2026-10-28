import { expect, test } from "../test";
import { sessionFile } from "../sessions";

// The table and the KPI need a grant, so these journeys start signed in as the employee requester.
test.use({ storageState: sessionFile("requester") });

test("a columns widget shows a table and a titled section side by side, in the ratio of their widths", async ({
  page,
}) => {
  await page.goto("/e2e/noteoverview");

  // The table is wider than its column, so it scrolls inside the column. The column is what to compare.
  const table = page
    .getByTestId("column")
    .filter({ has: page.getByRole("table") });
  const section = page.getByRole("region", { name: "Totals", exact: true });
  await expect(table.getByRole("table")).toBeVisible();
  await expect(section.getByTestId("kpi-tile")).toBeVisible();
  const tableBox = await table.boundingBox();
  const sectionBox = await section.boundingBox();
  expect(tableBox).not.toBeNull();
  expect(sectionBox).not.toBeNull();

  // The section is to the right of the table, starts level with it and is about half as wide.
  expect(sectionBox!.x).toBeGreaterThan(tableBox!.x + tableBox!.width);
  expect(Math.abs(sectionBox!.y - tableBox!.y)).toBeLessThan(8);
  expect(tableBox!.width / sectionBox!.width).toBeGreaterThan(1.7);
  expect(tableBox!.width / sectionBox!.width).toBeLessThan(2.3);
});

test("a columns widget stacks its table and its section in file order at phone width", async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto("/e2e/noteoverview");

  const column = page
    .getByTestId("column")
    .filter({ has: page.getByRole("table") });
  const section = page.getByRole("region", { name: "Totals", exact: true });
  await expect(column.getByRole("table").first()).toBeVisible();
  await expect(section.getByTestId("kpi-tile")).toBeVisible();
  const columnBox = await column.boundingBox();
  // The columns have the same padding, so the left edges of the two columns are compared.
  const sectionColumn = page
    .getByTestId("column")
    .filter({ has: section });
  const sectionBox = await sectionColumn.boundingBox();
  expect(columnBox).not.toBeNull();
  expect(sectionBox).not.toBeNull();

  // The section is below the table's column and starts at the same left edge.
  expect(sectionBox!.y).toBeGreaterThanOrEqual(
    columnBox!.y + columnBox!.height - 1,
  );
  expect(Math.abs(sectionBox!.x - columnBox!.x)).toBeLessThan(2);
  // The page does not scroll sideways.
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth),
  ).toBeLessThanOrEqual(375);
});

// The narrow-width breakpoint is 992 px, breakpointTokens.narrowWidth in web/src/platform/designTokens.ts. The
// numbers are repeated here because e2e does not import web code.
test("a columns widget stacks its columns at 991 px and sits them side by side at 992 px", async ({
  page,
}) => {
  const table = page
    .getByTestId("column")
    .filter({ has: page.getByRole("table") });
  const section = page.getByRole("region", { name: "Totals", exact: true });
  const sectionColumn = page.getByTestId("column").filter({ has: section });

  await page.setViewportSize({ width: 991, height: 900 });
  await page.goto("/e2e/noteoverview");
  await expect(table.getByRole("table")).toBeVisible();
  await expect(section.getByTestId("kpi-tile")).toBeVisible();
  const stackedTable = await table.boundingBox();
  const stackedSection = await sectionColumn.boundingBox();
  expect(stackedTable).not.toBeNull();
  expect(stackedSection).not.toBeNull();
  expect(stackedSection!.y).toBeGreaterThanOrEqual(
    stackedTable!.y + stackedTable!.height - 1,
  );
  expect(Math.abs(stackedSection!.x - stackedTable!.x)).toBeLessThan(2);

  await page.setViewportSize({ width: 992, height: 900 });
  await page.goto("/e2e/noteoverview");
  await expect(table.getByRole("table")).toBeVisible();
  await expect(section.getByTestId("kpi-tile")).toBeVisible();
  const sideTable = await table.boundingBox();
  const sideSection = await sectionColumn.boundingBox();
  expect(sideTable).not.toBeNull();
  expect(sideSection).not.toBeNull();
  // The columns touch: the gutter is padding inside each column, so the section can start on the table's right edge.
  expect(sideSection!.x).toBeGreaterThanOrEqual(
    sideTable!.x + sideTable!.width - 1,
  );
  expect(Math.abs(sideSection!.y - sideTable!.y)).toBeLessThan(8);
});

test("a tabs widget keeps the selected tab in the URL, across a reload and the back button", async ({
  page,
}) => {
  await page.goto("/e2e/notetabs");

  const notesTab = page.getByRole("tab", { name: "Notes", exact: true });
  const totalsTab = page.getByRole("tab", { name: "Totals", exact: true });
  await expect(notesTab).toHaveAttribute("aria-selected", "true");
  await expect(page.getByRole("table")).toBeVisible();

  await totalsTab.click();

  await expect(page).toHaveURL(/\?tab=2$/);
  await expect(totalsTab).toHaveAttribute("aria-selected", "true");
  await expect(page.getByTestId("kpi-tile")).toBeVisible();

  await page.reload();

  await expect(page).toHaveURL(/\?tab=2$/);
  await expect(totalsTab).toHaveAttribute("aria-selected", "true");
  await expect(page.getByTestId("kpi-tile")).toBeVisible();

  await page.goBack();

  await expect(notesTab).toHaveAttribute("aria-selected", "true");
  expect(new URL(page.url()).searchParams.has("tab")).toBe(false);
  await expect(page.getByRole("table")).toBeVisible();
});
