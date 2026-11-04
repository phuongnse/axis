import { access, cp, mkdtemp, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { csrfHeaders } from "../csrf";
import { confirmDialog } from "../dialogs";
import { expect, test } from "../test";
import { startServer } from "../activate";
import { e2eApp, leaveRequests } from "../server-settings";
import { useSession } from "../sessions";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The leave request sample uses the organization module. The second server's Studio edits a copy of the sample, so the
// main E2E server's Studio folder stays as the other Studio specs expect it.
const departmentId = "25b6e7c1-48dc-47fb-85e6-a2cbbd9d5c95";

test.describe("Studio edits a module entity as a layer", () => {
  let copy: string;
  let server: Awaited<ReturnType<typeof startServer>>;

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), "axis-studio-module-entity-"));
    await cp(leaveRequests, copy, { recursive: true });
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

  test("an author adds a field and saves it as a layer, then Reset all deletes the layer", async ({
    page,
  }) => {
    const layerFile = path.join(
      copy,
      "customizations",
      "organization",
      "Department.json",
    );
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=LeaveRequests`);

    await page
      .getByTestId("studio-group-module-entities")
      .getByRole("button", { name: "organization/Department", exact: true })
      .click();

    await expect(page.getByTestId("studio-entity-designer")).toBeVisible();
    await expect(page.getByTestId("studio-module-read-only")).toHaveCount(0);
    await expect(page.getByTestId("studio-module-layer")).toHaveCount(0);
    await expect(page.getByTestId("studio-save")).toBeDisabled();

    await page.getByRole("button", { name: "Add field", exact: true }).click();
    await page
      .getByTestId("studio-field-/fields/5/name")
      .getByRole("textbox")
      .fill("costCentre");
    await page.getByTestId("studio-save").click();

    await expect(page.getByTestId("studio-saved")).toBeVisible();
    await expect(page.getByTestId("studio-module-layer")).toBeVisible();
    await expect(access(layerFile)).resolves.toBeUndefined();

    await page.getByTestId("studio-module-reset-all").click();
    await confirmDialog(page, "Reset all");

    await expect(page.getByTestId("studio-module-layer")).toHaveCount(0);
    await expect(access(layerFile)).rejects.toMatchObject({
      code: "ENOENT",
    });
    await expect(page.getByTestId("studio-field-/fields/5/name")).toHaveCount(
      0,
    );
  });

  test("an author adds a field, sees it marked Added, and Reset to module removes it and the layer", async ({
    page,
  }) => {
    const layerFile = path.join(
      copy,
      "customizations",
      "organization",
      "Department.json",
    );
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=LeaveRequests`);

    await page
      .getByTestId("studio-group-module-entities")
      .getByRole("button", { name: "organization/Department", exact: true })
      .click();
    await expect(page.getByTestId("studio-entity-designer")).toBeVisible();

    await page.getByRole("button", { name: "Add field", exact: true }).click();
    await page
      .getByTestId("studio-field-/fields/5/name")
      .getByRole("textbox")
      .fill("costCentre");
    await page.getByTestId("studio-save").click();

    await expect(page.getByTestId("studio-saved")).toBeVisible();
    await expect(
      page.getByRole("note", { name: "Added costCentre", exact: true }),
    ).toBeVisible();

    await page
      .getByRole("button", { name: "Reset to module costCentre", exact: true })
      .click();

    await expect(page.getByTestId("studio-field-/fields/5")).toHaveCount(0);
    await expect(page.getByTestId("studio-module-layer")).toHaveCount(0);
    await expect(access(layerFile)).rejects.toMatchObject({
      code: "ENOENT",
    });
  });

  test("a requester sees the forbidden page and cannot write a module resource", async ({
    page,
  }) => {
    await useSession(page, "requester");
    await page.goto(`${server.url}/studio?app=LeaveRequests`);
    await expect(page.getByTestId("studio-forbidden")).toBeVisible();

    const response = await page.request.put(
      `${server.url}/api/studio/apps/LeaveRequests/modules/organization/resources/${departmentId}`,
      {
        headers: { ...csrfHeaders, "If-None-Match": "*" },
        data: { id: departmentId, kind: "entity", name: "Department" },
      },
    );
    expect(response.status()).toBe(403);
  });
});
