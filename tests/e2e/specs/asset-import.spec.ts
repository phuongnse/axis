import { expect, test } from "../test";
import { waitForDialog } from "../dialogs";
import { sessionFile, useSession } from "../sessions";

// The asset-manager user holds the import and create grants on assets. The auditor user only reads and exports them.
test.use({ storageState: sessionFile("asset-manager") });

// The assets persist in the shared database, so the test names its asset uniquely. The 'zzz' prefix and the
// descending sort put it on the first page.
const assetsPage = "/asset-management/assets?sort=-name";

test("an asset manager imports a CSV, sees its error by row and field, fixes it and commits", async ({
  page,
}) => {
  const name = `zzz Laptop ${Date.now()}-${test.info().retry}`;
  const csv = (cost: number) => ({
    name: "assets.csv",
    mimeType: "text/csv",
    buffer: Buffer.from(`name,purchaseCost\n${name},${cost}\n`),
  });

  await page.goto(assetsPage);
  await page.getByRole("button", { name: "Import", exact: true }).click();
  const dialog = await waitForDialog(page);
  const status = dialog.getByTestId("import-status");

  await dialog.locator("input[type=file]").setInputFiles(csv(-5));
  await dialog.getByRole("button", { name: "Check", exact: true }).click();

  await expect(status).toContainText("Row 1");
  await expect(status).toContainText("Purchase cost");
  await expect(
    dialog.getByRole("button", { name: "Commit", exact: true }),
  ).toHaveCount(0);

  await dialog.locator("input[type=file]").setInputFiles(csv(5));
  await dialog.getByRole("button", { name: "Check", exact: true }).click();
  const commit = dialog.getByRole("button", { name: "Commit", exact: true });
  await expect(commit).toBeVisible();
  await commit.click();

  await expect(dialog).toBeHidden();
  await expect(page.getByTestId("table-action-done")).toHaveText(
    "The records were imported.",
  );
  await expect(page.getByRole("row", { name: new RegExp(name) })).toBeVisible();
});

test("an auditor sees the assets and no Import button", async ({ page }) => {
  await useSession(page, "auditor");

  await page.goto(assetsPage);

  await expect(page.getByRole("table")).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Import", exact: true }),
  ).toHaveCount(0);
});
