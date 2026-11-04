import { access, cp, mkdtemp, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../test";
import { startServer } from "../activate";
import { confirmDialog, waitForDialog } from "../dialogs";
import { pickOption, pickOptions } from "../select";
import { approvalsApp, e2eApp } from "../server-settings";
import { useSession } from "../sessions";
import { openKind } from "../studio-resources";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The approvals contract of the Request entity has the protected states approved and rejected. The second server's
// Studio edits a copy of the approvals fixture, so the main E2E server's Studio folders stay as the other specs expect.
test.describe("Studio adds states and transitions to a module contract as a layer", () => {
  test.describe.configure({ mode: "serial" });

  let copy: string;
  let server: Awaited<ReturnType<typeof startServer>>;

  test.beforeAll(async () => {
    copy = await mkdtemp(
      path.join(os.tmpdir(), "axis-studio-module-contract-"),
    );
    await cp(approvalsApp, copy, { recursive: true });
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

  async function openContract(page: Page) {
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=ApprovalsApp`);
    await openKind(page, "contract");
    await page
      .getByTestId("studio-group-module-contracts")
      .getByRole("button", { name: "approvals/RequestContract", exact: true })
      .click();
    await expect(page.getByTestId("studio-contract-add-only")).toBeVisible();
  }

  async function addState(page: Page, name: string) {
    await page.getByTestId("studio-contract-add-state").click();
    const dialog = await waitForDialog(page);
    await dialog.getByRole("textbox", { name: "Name", exact: true }).fill(name);
    await confirmDialog(page, "Add");
  }

  async function addTransition(
    page: Page,
    name: string,
    from: string,
    to: string,
  ) {
    await page.getByTestId("studio-contract-add-transition").click();
    const dialog = await waitForDialog(page);
    await dialog.getByRole("textbox", { name: "Name", exact: true }).fill(name);
    await pickOptions(
      dialog.getByRole("combobox", { name: "From", exact: true }),
      [from],
    );
    await pickOption(
      dialog.getByRole("combobox", { name: "To", exact: true }),
      to,
    );
    await confirmDialog(page, "Add");
  }

  test("an author adds an onHold state and its transitions, saves, and all three show as Added", async ({
    page,
  }) => {
    const layerFile = path.join(
      copy,
      "customizations",
      "approvals",
      "RequestContract.json",
    );
    await openContract(page);
    await expect(page.getByTestId("studio-save")).toBeDisabled();
    await expect(page.getByRole("button", { name: /^Remove / })).toHaveCount(0);

    await addState(page, "onHold");
    await addTransition(page, "hold", "submitted", "onHold");
    await addTransition(page, "resume", "onHold", "submitted");
    await page.getByTestId("studio-save").click();

    await expect(page.getByTestId("studio-saved")).toBeVisible();
    for (const name of ["onHold", "hold", "resume"]) {
      await expect(
        page.getByRole("note", { name: `Added ${name}`, exact: true }),
      ).toBeVisible();
    }
    await expect(
      page.getByRole("button", { name: "Remove submit", exact: true }),
    ).toHaveCount(0);
    await expect(access(layerFile)).resolves.toBeUndefined();
  });

  test("an author adds a transition into the protected approved state and sees AXC0370 in the editor", async ({
    page,
  }) => {
    await openContract(page);
    await expect(
      page.getByRole("note", { name: "Added onHold", exact: true }),
    ).toBeVisible();

    await addTransition(page, "fastApprove", "returned", "approved");
    await page.getByTestId("studio-save").click();

    await expect(page.getByTestId("studio-saved")).toBeVisible();
    await expect(
      page.getByRole("note", { name: "Added fastApprove", exact: true }),
    ).toBeVisible();
    await expect(page.getByTestId("studio-contract-diagnostics")).toContainText(
      "AXC0370",
    );
  });
});
