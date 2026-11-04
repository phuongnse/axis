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
test.describe("Studio opens a module's contracts read-only", () => {
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
