import { expect, test } from "@playwright/test";
import { sessionFile } from "../sessions";

// The table and the KPI need a grant, so these journeys start signed in as the employee anna.
test.use({ storageState: sessionFile("anna") });

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
