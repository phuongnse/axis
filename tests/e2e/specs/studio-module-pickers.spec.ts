import { cp, mkdtemp, readFile, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { expect, test } from "../test";
import type { Page } from "@playwright/test";
import { startServer } from "../activate";
import { pickOption } from "../select";
import { checklistsModule, e2eApp, moduleApp, modulesPath } from "../server-settings";
import { useSession } from "../sessions";
import { openFormTab } from "../studio-resources";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The module app pins the fixture module checklists, which the sample modules folder does not hold. The second server
// reads a Modules:Path of its own, with the sample modules and checklists, and Studio edits a copy of the module app.
test.describe("Studio's pickers offer the pages and processes of a module", () => {
  test.describe.configure({ mode: "serial" });

  let copy: string;
  let modules: string;
  let server: Awaited<ReturnType<typeof startServer>>;

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), "axis-studio-module-pickers-app-"));
    modules = await mkdtemp(path.join(os.tmpdir(), "axis-studio-module-pickers-modules-"));
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

  async function save(page: Page) {
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByTestId("studio-saved")).toBeVisible();
  }

  test("an author picks a module page in a site's navigation", async ({ page }) => {
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=ModuleApp`);

    await page
      .getByTestId("studio-group-site")
      .getByRole("button", { name: "ModuleSite", exact: true })
      .click();
    await openFormTab(page);
    await pickOption(
      page.getByTestId("studio-field-/navigation/0/page").getByRole("combobox"),
      "checklists/Checklists",
    );
    await save(page);

    const site = JSON.parse(
      await readFile(path.join(copy, "sites", "module-site.json"), "utf8"),
    ) as { navigation: { page: string }[] };
    expect(site.navigation[0].page).toBe("checklists/Checklists");
  });

  test("an author picks a module process in a form's startProcess action", async ({ page }) => {
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=ModuleApp`);

    await page.getByTestId("studio-group-page").getByRole("button", { name: "Items", exact: true }).click();
    await openFormTab(page);
    await pickOption(
      page.getByTestId("studio-field-/widgets/0/actions/0/process").getByRole("combobox"),
      "checklists/ChecklistReview",
    );
    await save(page);

    const items = JSON.parse(await readFile(path.join(copy, "pages", "items.json"), "utf8")) as {
      widgets: { actions: { process: string }[] }[];
    };
    expect(items.widgets[0].actions[0].process).toBe("checklists/ChecklistReview");
  });
});
