import { access, cp, mkdtemp, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { csrfHeaders } from "../csrf";
import { expect, test } from "../test";
import { startServer } from "../activate";
import {
  checklistsModule,
  e2eApp,
  moduleApp,
  modulesPath,
} from "../server-settings";
import { useSession } from "../sessions";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The module app pins the fixture module checklists, which the sample modules folder does not hold. The second server
// reads a Modules:Path of its own, and Studio edits a copy of the module app, so no other spec sees the layer file.
const accessId = "a847334e-14b3-4ec2-8760-df70208cd482";

test.describe("Studio edits a module policy as a layer", () => {
  let copy: string;
  let modules: string;
  let server: Awaited<ReturnType<typeof startServer>>;

  test.beforeAll(async () => {
    copy = await mkdtemp(
      path.join(os.tmpdir(), "axis-studio-module-policy-app-"),
    );
    modules = await mkdtemp(
      path.join(os.tmpdir(), "axis-studio-module-policy-modules-"),
    );
    await cp(moduleApp, copy, { recursive: true });
    await cp(modulesPath, modules, { recursive: true });
    await cp(checklistsModule, path.join(modules, "checklists", "1.0.0"), {
      recursive: true,
    });
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

  test("an author ticks an action on a grant of checklists/ChecklistAccess and saves it as a layer, then resets the grant", async ({
    page,
  }) => {
    const layerFile = path.join(
      copy,
      "customizations",
      "checklists",
      "ChecklistAccess.json",
    );
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=ModuleApp`);

    await page
      .getByTestId("studio-group-module-policies")
      .getByRole("button", { name: "checklists/ChecklistAccess", exact: true })
      .click();

    const grant = page.getByTestId("studio-module-policy-grant-1");
    await expect(grant).toContainText("checklistsRead");
    await expect(page.getByTestId("studio-module-layer")).toHaveCount(0);

    await grant.getByRole("checkbox", { name: "export", exact: true }).check();
    await page.getByTestId("studio-save").click();

    await expect(page.getByTestId("studio-saved")).toBeVisible();
    await expect(page.getByTestId("studio-module-layer")).toBeVisible();
    await expect(
      page.getByRole("note", { name: "Changed checklistsRead", exact: true }),
    ).toBeVisible();
    await expect(
      grant.getByRole("checkbox", { name: "export", exact: true }),
    ).toBeChecked();
    await expect(access(layerFile)).resolves.toBeUndefined();

    await page
      .getByRole("button", {
        name: "Reset to module checklistsRead",
        exact: true,
      })
      .click();

    await expect(page.getByTestId("studio-module-layer")).toHaveCount(0);
    await expect(access(layerFile)).rejects.toMatchObject({ code: "ENOENT" });
    await expect(
      grant.getByRole("checkbox", { name: "export", exact: true }),
    ).not.toBeChecked();
  });

  test("a requester cannot write a module policy", async ({ page }) => {
    await useSession(page, "requester");
    await page.goto(`${server.url}/studio?app=ModuleApp`);
    await expect(page.getByTestId("studio-forbidden")).toBeVisible();

    const response = await page.request.put(
      `${server.url}/api/studio/apps/ModuleApp/modules/checklists/resources/${accessId}`,
      {
        headers: { ...csrfHeaders, "If-None-Match": "*" },
        data: { id: accessId, kind: "policy", name: "ChecklistAccess" },
      },
    );
    expect(response.status()).toBe(403);
  });
});
