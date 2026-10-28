import { cp, mkdtemp, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { expect, test } from "../test";
import { startServer } from "../activate";
import { e2eApp, leaveRequests } from "../server-settings";
import { useSession } from "../sessions";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The leave request sample uses the departments module. The second server's Studio edits a copy of the sample, so the
// main E2E server's Studio folder stays as the other Studio specs expect it.
test.describe("Studio opens a module entity read-only", () => {
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

  test("an author sees the fields of departments/Department with no way to change them", async ({
    page,
  }) => {
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=LeaveRequests`);

    await page
      .getByTestId("studio-group-module-entities")
      .getByRole("button", { name: "departments/Department", exact: true })
      .click();

    await expect(page.getByTestId("studio-entity-designer")).toBeVisible();
    await expect(page.getByTestId("studio-module-read-only")).toBeVisible();
    for (const [index, name] of ["code", "name", "manager"].entries()) {
      const field = page
        .getByTestId(`studio-field-/fields/${index}/name`)
        .getByRole("textbox");
      await expect(field).toHaveValue(name);
      await expect(field).toBeDisabled();
    }
    await expect(page.getByRole("button", { name: /save/i })).toHaveCount(0);
    await expect(page.getByRole("button", { name: /delete/i })).toHaveCount(0);
    await expect(page.getByRole("button", { name: /add field/i })).toHaveCount(0);
  });
});
