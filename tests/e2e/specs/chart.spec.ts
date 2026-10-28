import { expect, test } from "../test";
import { csrfHeaders } from "../csrf";
import { sessionFile } from "../sessions";

// The data source needs a grant, so this journey starts signed in as the employee requester.
test.use({ storageState: sessionFile("requester") });

// Specs run in parallel against one database, so the groups change between runs. The check covers
// the chart's structure and headers; Vitest covers the exact groups, values and notes.
test("a chart page shows the chart by its title and its data as a table", async ({
  page,
  request,
}) => {
  // The chart draws nothing without a group, so the test makes sure that one note exists.
  const created = await request.post("/api/apps/E2eApp/entities/Note/records", {
    data: { values: { title: `Chart ${Date.now()}`, amount: 4 } },
    headers: csrfHeaders,
  });
  expect(created.status()).toBe(201);

  await page.goto("/e2e/notesbycategory");

  await expect(
    page.getByRole("img", { name: "Notes per category", exact: true }),
  ).toBeVisible();
  const table = page.getByRole("table", { name: "Notes per category", exact: true });
  await expect(table.getByRole("columnheader")).toHaveText([
    "Category",
    "Notes",
    "Amount total",
  ]);
});
