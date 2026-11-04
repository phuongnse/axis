import { cp, mkdtemp, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { expect, test } from "../test";
import { startServer } from "../activate";
import { e2eApp, purchaseRequests } from "../server-settings";
import { useSession } from "../sessions";
import { openKind } from "../studio-resources";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The purchase request sample uses the department-budgets module. The second server's Studio edits a copy of the sample, so the
// main E2E server's Studio folder stays as the other Studio specs expect it.
test.describe("Studio draws a module page read-only", () => {
  let copy: string;
  let server: Awaited<ReturnType<typeof startServer>>;

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), "axis-studio-module-page-"));
    await cp(purchaseRequests, copy, { recursive: true });
    server = await startServer({
      ActivateOnStartup__0: e2eApp,
      Studio__Folders__0: copy,
    });
  });

  test.afterAll(async () => {
    await server?.stop();
    if (copy) {
      await rm(copy, { recursive: true, force: true });
    }
  });

  test("an author sees department-budgets/Budgets drawn on the canvas with no way to change it", async ({
    page,
  }) => {
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=PurchaseRequests`);

    await openKind(page, "page");
    await page
      .getByTestId("studio-group-module-pages")
      .getByRole("button", { name: "department-budgets/Budgets", exact: true })
      .click();

    await expect(page.getByTestId("studio-page-canvas")).toBeVisible();
    await expect(page.getByTestId("studio-canvas-frame").first()).toBeVisible();
    await expect(page.getByTestId("studio-module-read-only")).toBeVisible();
    for (const testId of [
      "studio-design-panel",
      "studio-palette",
      "studio-data-panel",
      "studio-page-editor",
    ]) {
      await expect(page.getByTestId(testId)).toHaveCount(0);
    }
    await expect(page.getByRole("button", { name: "Save", exact: true })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Delete", exact: true })).toHaveCount(0);
  });
});
