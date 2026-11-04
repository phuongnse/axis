import { access, cp, mkdtemp, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { csrfHeaders } from "../csrf";
import { confirmDialog } from "../dialogs";
import { expect, test } from "../test";
import { startServer } from "../activate";
import {
  checklistsModule,
  e2eApp,
  moduleApp,
  modulesPath,
} from "../server-settings";
import { pickOption } from "../select";
import { useSession } from "../sessions";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The module app pins the fixture module checklists, which the sample modules folder does not hold. The second server
// reads a Modules:Path of its own, and Studio edits a copy of the module app, so the serial policy and contract specs
// never see a layer file.
const reviewId = "06bf5c2d-df18-4d48-9dc6-66396e30f300";

test.describe("Studio edits a module process as a layer", () => {
  let copy: string;
  let modules: string;
  let server: Awaited<ReturnType<typeof startServer>>;

  test.beforeAll(async () => {
    copy = await mkdtemp(
      path.join(os.tmpdir(), "axis-studio-module-process-app-"),
    );
    modules = await mkdtemp(
      path.join(os.tmpdir(), "axis-studio-module-process-modules-"),
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

  test("an author inserts a step into checklists/ChecklistReview and saves it as a layer, then Reset all brings back the module's process", async ({
    page,
  }) => {
    const layerFile = path.join(
      copy,
      "customizations",
      "checklists",
      "ChecklistReview.json",
    );
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=ModuleApp`);

    await page
      .getByTestId("studio-group-module-processes")
      .getByRole("button", { name: "checklists/ChecklistReview", exact: true })
      .click();

    await expect(page.getByTestId("studio-process-design")).toBeVisible();
    await expect(page.getByTestId("studio-module-layer")).toHaveCount(0);
    await expect(
      page.locator('[data-testid^="studio-graph-node-"]'),
    ).toHaveCount(2);

    // An end step is the one palette step that starts valid, so the layer compiles and the module process stays open.
    await page.getByTestId("studio-process-palette-end").click();
    await page.getByTestId("studio-graph-node-0").click();
    await pickOption(
      page.getByTestId("studio-field-/steps/0/otherwise").getByRole("combobox"),
      "end1",
    );
    await page.getByTestId("studio-save").click();

    await expect(page.getByTestId("studio-saved")).toBeVisible();
    await expect(page.getByTestId("studio-module-layer")).toBeVisible();
    await expect(
      page.getByRole("note", { name: "Changed check", exact: true }),
    ).toBeVisible();
    await expect(page.getByTestId("studio-graph-node-2")).toHaveAccessibleName(
      "end1 · end · Added",
    );
    await expect(page.getByTestId("studio-graph-node-0")).toHaveAccessibleName(
      /· Changed$/,
    );
    await expect(
      page.locator('[data-testid^="studio-graph-node-"]'),
    ).toHaveCount(3);
    await expect(access(layerFile)).resolves.toBeUndefined();

    await page.getByTestId("studio-module-reset-all").click();
    await confirmDialog(page, "Reset all");

    await expect(page.getByTestId("studio-module-layer")).toHaveCount(0);
    await expect(access(layerFile)).rejects.toMatchObject({ code: "ENOENT" });
    await expect(
      page.locator('[data-testid^="studio-graph-node-"]'),
    ).toHaveCount(2);
  });

  test("a requester cannot write a module process", async ({ page }) => {
    await useSession(page, "requester");
    await page.goto(`${server.url}/studio?app=ModuleApp`);
    await expect(page.getByTestId("studio-forbidden")).toBeVisible();

    const response = await page.request.put(
      `${server.url}/api/studio/apps/ModuleApp/modules/checklists/resources/${reviewId}`,
      {
        headers: { ...csrfHeaders, "If-None-Match": "*" },
        data: { id: reviewId, kind: "process", name: "ChecklistReview" },
      },
    );
    expect(response.status()).toBe(403);
  });
});
