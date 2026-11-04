import { access, cp, mkdtemp, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { csrfHeaders } from "../csrf";
import { confirmDialog } from "../dialogs";
import { expect, test } from "../test";
import { startServer } from "../activate";
import { e2eApp, purchaseRequests } from "../server-settings";
import { pickOption } from "../select";
import { useSession } from "../sessions";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The purchase request sample uses the department-budgets module. The second server's Studio edits a copy of the sample, so the
// main E2E server's Studio folder stays as the other Studio specs expect it. BudgetForm is the module page with a name of
// its own, because a layer on Budgets is refused: the module also has a Budgets data source.
const budgetFormId = "87f084d6-acd9-477c-ae73-e35b3f8e92b6";

test.describe("Studio edits a module page as a layer", () => {
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

  test("an author adds a navigate action to department-budgets/BudgetForm and saves it as a layer, then Reset all brings back the module's page", async ({
    page,
  }) => {
    const layerFile = path.join(
      copy,
      "customizations",
      "department-budgets",
      "BudgetForm.json",
    );
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=PurchaseRequests`);

    await page
      .getByTestId("studio-group-module-pages")
      .getByRole("button", {
        name: "department-budgets/BudgetForm",
        exact: true,
      })
      .click();

    await expect(page.getByTestId("studio-page-design")).toBeVisible();
    await expect(page.getByTestId("studio-canvas-frame").first()).toBeVisible();
    await expect(page.getByTestId("studio-module-layer")).toHaveCount(0);
    await expect(page.getByTestId("studio-module-read-only")).toHaveCount(0);

    // The module's form widget has no actions yet, so the layer adds the list to the widget and marks the widget.
    await page
      .getByTestId("studio-field-/widgets/0/actions")
      .getByRole("button", { name: "Add navigate action", exact: true })
      .click();
    await pickOption(
      page
        .getByTestId("studio-field-/widgets/0/actions/0/page")
        .getByRole("combobox"),
      "PurchaseRequests",
    );
    await page
      .getByTestId("studio-field-/widgets/0/actions/0/label")
      .getByRole("textbox", { name: "Text key", exact: true })
      .fill("department-budgets.actions.new");
    await page.getByTestId("studio-save").click();

    await expect(page.getByTestId("studio-saved")).toBeVisible();
    await expect(page.getByTestId("studio-module-layer")).toBeVisible();
    await expect(
      page.getByRole("note", { name: "Changed budgetForm", exact: true }),
    ).toBeVisible();
    await expect(access(layerFile)).resolves.toBeUndefined();

    await page.getByTestId("studio-module-reset-all").click();
    await confirmDialog(page, "Reset all");

    await expect(page.getByTestId("studio-module-layer")).toHaveCount(0);
    await expect(
      page.getByRole("note", { name: /^(Added|Changed) / }),
    ).toHaveCount(0);
    await expect(access(layerFile)).rejects.toMatchObject({ code: "ENOENT" });
  });

  test("a requester cannot write a module page", async ({ page }) => {
    await useSession(page, "requester");
    await page.goto(`${server.url}/studio?app=PurchaseRequests`);
    await expect(page.getByTestId("studio-forbidden")).toBeVisible();

    const response = await page.request.put(
      `${server.url}/api/studio/apps/PurchaseRequests/modules/department-budgets/resources/${budgetFormId}`,
      {
        headers: { ...csrfHeaders, "If-None-Match": "*" },
        data: { id: budgetFormId, kind: "page", name: "BudgetForm" },
      },
    );
    expect(response.status()).toBe(403);
  });
});
