import { access, cp, mkdtemp, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { csrfHeaders } from "../csrf";
import { expect, test } from "../test";
import { startServer } from "../activate";
import { e2eApp, purchaseRequests } from "../server-settings";
import { useSession } from "../sessions";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The purchase request sample uses the department-budgets module. The second server's Studio edits a copy of the
// sample, so the main E2E server's Studio folder stays as the other Studio specs expect it.
const budgetReviewId = "12dffe7c-7ed8-47c0-9ed5-fa9b4251d85e";

test.describe("Studio edits a module form as a layer", () => {
  let copy: string;
  let server: Awaited<ReturnType<typeof startServer>>;

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), "axis-studio-module-form-"));
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

  test("an author moves a field with the keyboard, saves it as a layer and sees it marked Changed", async ({
    page,
  }) => {
    const layerFile = path.join(
      copy,
      "customizations",
      "department-budgets",
      "BudgetReview.json",
    );
    await page.setViewportSize({ width: 1280, height: 1000 });
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=PurchaseRequests`);

    await page
      .getByTestId("studio-group-module-forms")
      .getByRole("button", {
        name: "department-budgets/BudgetReview",
        exact: true,
      })
      .click();

    await expect(page.getByTestId("studio-form-design")).toBeVisible();
    await expect(page.getByTestId("studio-module-layer")).toHaveCount(0);

    const row = (position: number) =>
      page.locator(
        `[data-testid="studio-outline-field"][data-path="/sections/0/fields/${position}"]`,
      );
    await expect(row(3)).toContainText("amount");
    await row(3).focus();
    await page.keyboard.press("Space");
    // The target moves one row for each key, and the next key waits for the outline of the one before.
    await page.keyboard.press("ArrowUp");
    await expect(row(2)).toHaveCSS("outline-width", "2px");
    await page.keyboard.press("ArrowUp");
    await expect(row(1)).toHaveCSS("outline-width", "2px");
    await page.keyboard.press("Space");
    await expect(row(1)).toContainText("amount");

    await page.getByTestId("studio-save").click();

    await expect(page.getByTestId("studio-saved")).toBeVisible();
    await expect(page.getByTestId("studio-module-layer")).toBeVisible();
    await expect(
      page.getByRole("note", { name: "Changed amount", exact: true }),
    ).toBeVisible();
    await expect(access(layerFile)).resolves.toBeUndefined();
  });

  test("a requester cannot write a module form through the save route", async ({
    page,
  }) => {
    await useSession(page, "requester");

    const response = await page.request.put(
      `${server.url}/api/studio/apps/PurchaseRequests/modules/department-budgets/resources/${budgetReviewId}`,
      {
        headers: { ...csrfHeaders, "If-None-Match": "*" },
        data: {
          id: budgetReviewId,
          kind: "form",
          name: "BudgetReview",
          formatVersion: 1,
          entity: "Budget",
          sections: [],
        },
      },
    );
    expect(response.status()).toBe(403);
  });
});
