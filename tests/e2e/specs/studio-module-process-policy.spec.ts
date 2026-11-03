import { cp, mkdtemp, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "../test";
import { startServer } from "../activate";
import { checklistsModule, e2eApp, moduleApp, modulesPath } from "../server-settings";
import { useSession } from "../sessions";
import { chooseTheme } from "../shell";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The module app pins the fixture module checklists, which the sample modules folder does not hold. The second server
// reads a Modules:Path of its own, with the sample modules and checklists, and Studio edits a copy of the module app.
test.describe("Studio opens a module's processes, policies and contracts read-only", () => {
  test.describe.configure({ mode: "serial" });

  let copy: string;
  let modules: string;
  let server: Awaited<ReturnType<typeof startServer>>;

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), "axis-studio-module-process-policy-app-"));
    modules = await mkdtemp(path.join(os.tmpdir(), "axis-studio-module-process-policy-modules-"));
    await cp(moduleApp, copy, { recursive: true });
    await cp(modulesPath, modules, { recursive: true });
    await cp(checklistsModule, path.join(modules, "checklists", "1.0.0"), { recursive: true });
    server = await startServer({
      ActivateOnStartup__0: e2eApp,
      Studio__Folders__0: copy,
      Modules__Path: modules,
    });
  });

  test.afterAll(async () => {
    await server?.stop();
    if (copy) {
      await rm(copy, { recursive: true, force: true });
    }
    if (modules) {
      await rm(modules, { recursive: true, force: true });
    }
  });

  test("an author sees the graph of checklists/ChecklistReview with no way to change it", async ({
    page,
  }) => {
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=ModuleApp`);

    await page
      .getByTestId("studio-group-module-processes")
      .getByRole("button", { name: "checklists/ChecklistReview", exact: true })
      .click();

    const details = page.getByTestId("studio-resource-details");
    await expect(page.getByTestId("studio-process-graph")).toBeVisible();
    await expect(page.locator('[data-testid^="studio-graph-node-"]')).toHaveCount(2);
    await expect(details.getByRole("alert")).toContainText("read-only");
    await expect(page.locator('[data-testid^="studio-process-palette"]')).toHaveCount(0);
    await expect(page.locator(".react-flow__handle.connectable")).toHaveCount(0);
    await expect(page.locator('[data-testid^="studio-graph-handle-"]:visible')).toHaveCount(0);
    await expect(page.getByTestId("studio-process-design")).toHaveCount(0);
    for (const name of ["Save", "Delete", "Remove step", "Remove transition"]) {
      await expect(page.getByRole("button", { name, exact: true })).toHaveCount(0);
    }

    await page.getByTestId("studio-process-graph").focus();
    await page.keyboard.press("Delete");
    await expect(page.locator('[data-testid^="studio-graph-node-"]')).toHaveCount(2);
  });

  test("an author sees the grants of checklists/ChecklistAccess with no way to change them", async ({
    page,
  }) => {
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=ModuleApp`);

    await page
      .getByTestId("studio-group-module-policies")
      .getByRole("button", { name: "checklists/ChecklistAccess", exact: true })
      .click();

    const details = page.getByTestId("studio-resource-details");
    const rows = page.getByTestId("studio-module-policy-grants").locator("tbody tr");
    await expect(rows).toHaveCount(2);
    await expect(rows.nth(0)).toContainText("Checklist");
    await expect(rows.nth(0)).toContainText("read");
    await expect(rows.nth(1)).toContainText("Checklists");
    await expect(rows.nth(1)).toContainText("read");
    await expect(details.getByRole("alert")).toContainText("read-only");
    for (const role of ["textbox", "combobox", "checkbox"] as const) {
      await expect(details.getByRole(role)).toHaveCount(0);
    }
    for (const name of ["Save", "Delete"]) {
      await expect(page.getByRole("button", { name, exact: true })).toHaveCount(0);
    }
  });

  test("an author sees the lifecycle and named points of checklists/ChecklistContract", async ({ page }) => {
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=ModuleApp`);

    const openContract = async () => {
      await page
        .getByTestId("studio-group-module-contracts")
        .getByRole("button", { name: "checklists/ChecklistContract", exact: true })
        .click();
    };
    const details = page.getByTestId("studio-resource-details");
    const expectLifecycleAndPoints = async () => {
      await expect(details.getByRole("alert")).toContainText("read-only");
      await expect(page.getByTestId("studio-contract-lifecycle")).toBeVisible();
      await expect(page.locator('[data-testid^="studio-contract-state-"]')).toHaveCount(3);
      for (const point of [
        "Checklist/transitions/check",
        "Checklist/states/closed/initial",
        "Checklist/validations/titleSet",
        "Checklist/effects/followUpCreated",
        "Checklist/ceiling/reviewer",
      ]) {
        await expect(details.getByText(point, { exact: true })).toBeVisible();
      }
    };
    const expectNoViolations = async () => {
      const { violations } = await new AxeBuilder({ page }).include('[data-testid="studio-resource-details"]').analyze();
      expect(violations).toEqual([]);
    };

    await openContract();
    await expectLifecycleAndPoints();
    await expectNoViolations();

    await chooseTheme(page, "Dark");
    await page.reload();
    await openContract();
    await expectLifecycleAndPoints();
    await expectNoViolations();
  });
});
