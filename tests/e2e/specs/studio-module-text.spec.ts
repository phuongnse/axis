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
import { openKind } from "../studio-resources";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The module app pins the fixture module checklists, which the sample modules folder does not hold. The second server
// reads a Modules:Path of its own, and Studio edits a copy of the module app, so the other module specs never see a
// layer file of this one.
const textId = "4e445503-3116-44cc-a16e-e2e24fc54144";
const reviewerKey = "checklists.roles.reviewer";

test.describe("Studio edits a module text as a layer", () => {
  let copy: string;
  let modules: string;
  let server: Awaited<ReturnType<typeof startServer>>;

  test.beforeAll(async () => {
    copy = await mkdtemp(
      path.join(os.tmpdir(), "axis-studio-module-text-app-"),
    );
    modules = await mkdtemp(
      path.join(os.tmpdir(), "axis-studio-module-text-modules-"),
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

  test("an author changes a text of checklists/TextsEn and saves it as a layer, then Reset to module brings back the module's text", async ({
    page,
  }) => {
    const layerFile = path.join(
      copy,
      "customizations",
      "checklists",
      "TextsEn.json",
    );
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=ModuleApp`);

    await openKind(page, "text");
    await page
      .getByTestId("studio-group-module-texts")
      .getByRole("button", { name: "checklists/TextsEn", exact: true })
      .click();

    const row = page.locator(
      `[data-testid="studio-text-row"][data-key="${reviewerKey}"]`,
    );
    await expect(
      row.getByRole("textbox", { name: "en", exact: true }),
    ).toHaveValue("Reviewer");
    await expect(page.getByTestId("studio-module-layer")).toHaveCount(0);

    await row.getByRole("textbox", { name: "en", exact: true }).fill("Checker");
    await page.getByTestId("studio-save").click();

    await expect(page.getByTestId("studio-saved")).toBeVisible();
    await expect(page.getByTestId("studio-module-layer")).toBeVisible();
    await expect(
      page.getByRole("note", { name: `Changed ${reviewerKey}`, exact: true }),
    ).toBeVisible();
    await expect(
      row.getByRole("textbox", { name: "en", exact: true }),
    ).toHaveValue("Checker");
    await expect(access(layerFile)).resolves.toBeUndefined();

    await page
      .getByRole("button", {
        name: `Reset to module ${reviewerKey}`,
        exact: true,
      })
      .click();

    await expect(page.getByRole("note")).toHaveCount(0);
    await expect(page.getByTestId("studio-module-layer")).toHaveCount(0);
    await expect(
      row.getByRole("textbox", { name: "en", exact: true }),
    ).toHaveValue("Reviewer");
    await expect(access(layerFile)).rejects.toMatchObject({ code: "ENOENT" });
  });

  test("a requester cannot write a module text", async ({ page }) => {
    await useSession(page, "requester");
    await page.goto(`${server.url}/studio?app=ModuleApp`);
    await expect(page.getByTestId("studio-forbidden")).toBeVisible();

    const response = await page.request.put(
      `${server.url}/api/studio/apps/ModuleApp/modules/checklists/resources/${textId}`,
      {
        headers: { ...csrfHeaders, "If-None-Match": "*" },
        data: { id: textId, kind: "text", name: "TextsEn" },
      },
    );
    expect(response.status()).toBe(403);
  });
});
