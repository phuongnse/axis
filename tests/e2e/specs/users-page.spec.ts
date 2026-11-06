import type { Page } from "@playwright/test";
import { expect, test } from "../test";
import { csrfHeaders } from "../csrf";
import { waitForDialog } from "../dialogs";
import { logInAtKeycloak } from "../keycloak";
import { useSession } from "../sessions";

// The leave requests sample, which no journey of the auditor touches. The dev realm gives the user the id `auditor`.
const leaveRequestsId = "6613186d-8c6f-4e98-b911-6acc1104a5a2";
const auditorId = "auditor";

test("an administrator opens Users from the app switcher and finds a user by email", async ({
  page,
}) => {
  await useSession(page, "administrator");
  await page.goto("/");

  await page
    .getByRole("banner")
    .getByRole("button", { name: "Switch to", exact: true })
    .click();
  await page.getByRole("menuitem", { name: "Users", exact: true }).click();

  await expect(page).toHaveURL(/\/users$/);
  const main = page.getByRole("main");
  await expect(
    main.getByRole("cell", { name: "Administrator User", exact: true }),
  ).toBeVisible();
  await expect(
    main.getByRole("cell", { name: "auditor@axis.local", exact: true }),
  ).toBeVisible();

  await main
    .getByRole("searchbox", { name: "Search by name or email", exact: true })
    .fill("auditor@axis.local");
  await page.keyboard.press("Enter");

  await expect(
    main.getByRole("row").filter({ hasText: "Auditor User" }),
  ).toHaveCount(1);
  await expect(
    main.getByRole("cell", { name: "Administrator User", exact: true }),
  ).toHaveCount(0);
});

test("a person without the administrator role is told the page is for administrators", async ({
  page,
}) => {
  await useSession(page, "author");
  await page.goto("/users");

  await expect(
    page.getByText("This page is for administrators.", { exact: true }),
  ).toBeVisible();
});

test("an administrator reads the warning, confirms the start of an export and downloads the ZIP", async ({
  page,
}) => {
  // The dev stack is not a cell, so the flag and the export routes are stubbed. The server tests cover the real routes.
  const exportId = "6f1c0d52-8d0e-4a47-9c1b-2f7e5a9d4c01";
  const started: string[] = [];
  const exported = (state: string) => ({
    id: exportId,
    tenantId: "dev",
    state,
    createdAt: new Date().toISOString(),
    expiresAt:
      state === "succeeded"
        ? new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString()
        : null,
  });
  await page.route("**/api/me", async (route) => {
    const response = await route.fetch();
    await route.fulfill({
      response,
      json: { ...(await response.json()), tenantExports: true },
    });
  });
  await page.route("**/api/tenant/exports", async (route) => {
    started.push(route.request().method());
    await route.fulfill({ status: 202, json: exported("queued") });
  });
  await page.route(`**/api/tenant/exports/${exportId}`, async (route) => {
    if (
      (await route.request().headerValue("accept"))?.includes("application/zip")
    ) {
      await route.fulfill({
        status: 200,
        contentType: "application/zip",
        headers: {
          "Content-Disposition": `attachment; filename=axis-${exportId}.zip`,
        },
        body: Buffer.from("PK"),
      });
    } else {
      await route.fulfill({ json: exported("succeeded") });
    }
  });

  await useSession(page, "administrator");
  await page.goto("/users");

  const section = page.getByTestId("tenant-export");
  await expect(
    section.getByRole("heading", { name: "Export data", exact: true }),
  ).toBeVisible();
  await expect(
    section.getByText(
      "The file holds every value in clear, sensitive ones included. Keep it somewhere safe.",
      { exact: true },
    ),
  ).toBeVisible();

  await section
    .getByRole("button", { name: "Start export…", exact: true })
    .click();
  const dialog = await waitForDialog(page, "Start the export?");
  expect(started).toEqual([]);
  await dialog.getByRole("button", { name: "Start", exact: true }).click();
  await expect.poll(() => started).toEqual(["POST"]);

  const download = section.getByRole("button", {
    name: "Download ZIP",
    exact: true,
  });
  await expect(download).toBeVisible();
  const downloading = page.waitForEvent("download");
  await download.click();
  expect((await downloading).suggestedFilename()).toBe(`axis-${exportId}.zip`);
});

test.describe("roles of a user", () => {
  // A failed run must not leave the role given.
  test.afterEach(async ({ page }) => {
    await useSession(page, "administrator");
    await page.request.post(`/api/users/${auditorId}/remove-role`, {
      headers: csrfHeaders,
      data: { applicationId: leaveRequestsId, role: "employee" },
    });
  });

  test("an administrator opens a user, gives an application role after a confirmation and removes it again", async ({
    page,
  }) => {
    await useSession(page, "administrator");
    await page.goto("/users");
    const main = page.getByRole("main");
    await main
      .getByRole("searchbox", { name: "Search by name or email", exact: true })
      .fill("auditor@axis.local");
    await page.keyboard.press("Enter");
    await main.getByRole("row").filter({ hasText: "Auditor User" }).click();

    const panel = page.getByTestId("user-roles");
    await expect(
      panel.getByText("Auditor User", { exact: true }),
    ).toBeVisible();
    const section = panel.getByRole("region", {
      name: "LeaveRequests",
      exact: true,
    });
    const role = section
      .getByTestId("user-role")
      .filter({ hasText: "employee" });
    await expect(role).toBeVisible();
    await expect(role.getByText("Given", { exact: true })).toHaveCount(0);

    await role.getByRole("button", { name: "Give", exact: true }).click();
    const giveDialog = await waitForDialog(page, "Give this role?");
    await expect(giveDialog).toContainText("Role: employee");
    await expect(giveDialog).toContainText("In: LeaveRequests");
    await giveDialog.getByRole("button", { name: "Give", exact: true }).click();
    await expect(role.getByText("Given", { exact: true })).toBeVisible();

    await role.getByRole("button", { name: "Remove", exact: true }).click();
    const removeDialog = await waitForDialog(page, "Remove this role?");
    await removeDialog
      .getByRole("button", { name: "Remove", exact: true })
      .click();
    await expect(role.getByText("Given", { exact: true })).toHaveCount(0);
    await expect(
      role.getByRole("button", { name: "Give", exact: true }),
    ).toBeVisible();
  });
});

test.describe("invitations", () => {
  // Invited users stay in the realm, so each run invites an email of its own.
  function inviteeEmail() {
    return `invitee-${Date.now()}-${test.info().workerIndex}@axis.local`;
  }

  async function findRow(page: Page, email: string) {
    const main = page.getByRole("main");
    await main
      .getByRole("searchbox", { name: "Search by name or email", exact: true })
      .fill(email);
    await page.keyboard.press("Enter");
    const row = main.getByRole("row").filter({ hasText: email });
    await expect(row).toHaveCount(1);
    return row;
  }

  test("an administrator invites a person through the form and finds them as invited", async ({
    page,
  }) => {
    const email = inviteeEmail();
    await useSession(page, "administrator");
    await page.goto("/users");
    await page
      .getByRole("main")
      .getByRole("button", { name: "Invite…", exact: true })
      .click();

    const form = await waitForDialog(page, "Invite a person");
    await form.getByLabel("Email", { exact: true }).fill(email);
    await form
      .getByLabel("Display name", { exact: true })
      .fill("Invited Person");
    await form.getByRole("button", { name: "Invite", exact: true }).click();
    const confirmation = await waitForDialog(page, "Invite this person?");
    await expect(confirmation).toContainText(`Email: ${email}`);
    await confirmation
      .getByRole("button", { name: "Invite", exact: true })
      .click();

    await expect(page.getByTestId("users-done")).toHaveText(
      "The invitation was sent.",
    );
    const row = await findRow(page, email);
    await expect(row).toContainText("Invited Person");
    await expect(row).toContainText("Invited");
  });

  test("an administrator resends an invitation from an invited row after a confirmation", async ({
    page,
  }) => {
    const email = inviteeEmail();
    await useSession(page, "administrator");
    const invited = await page.request.post("/api/users/invite", {
      headers: csrfHeaders,
      data: { email, displayName: "Invited Again" },
    });
    expect(invited.status()).toBe(201);
    await page.goto("/users");

    const row = await findRow(page, email);
    await row
      .getByRole("button", { name: "Resend invitation", exact: true })
      .click();
    const confirmation = await waitForDialog(page, "Resend the invitation?");
    await expect(confirmation).toContainText(email);
    await confirmation
      .getByRole("button", { name: "Resend", exact: true })
      .click();

    await expect(page.getByTestId("users-done")).toHaveText(
      "The invitation was sent again.",
    );
    await expect(page.getByTestId("user-roles")).toHaveCount(0);
    await expect(await findRow(page, email)).toContainText("Invited");
  });

  test("an administrator disables an invited person after a confirmation and enables them again", async ({
    browser,
    baseURL,
  }) => {
    // Disabling needs a sign-in at Keycloak that is at most 5 minutes old, and the saved session may be older.
    const context = await browser.newContext({ baseURL });
    try {
      const page = await context.newPage();
      await page.goto("/");
      await logInAtKeycloak(page, "administrator");
      await expect(
        page
          .getByRole("banner")
          .getByRole("button", { name: "Administrator User", exact: true }),
      ).toBeVisible();
      const email = inviteeEmail();
      const invited = await page.request.post("/api/users/invite", {
        headers: csrfHeaders,
        data: { email, displayName: "Disabled Person" },
      });
      expect(invited.status()).toBe(201);
      await page.goto("/users");

      const row = await findRow(page, email);
      await row.getByRole("button", { name: "Disable", exact: true }).click();
      const disableDialog = await waitForDialog(page, "Disable this person?");
      await expect(disableDialog).toContainText(email);
      await disableDialog
        .getByRole("button", { name: "Disable", exact: true })
        .click();
      await expect(page.getByTestId("users-done")).toHaveText(
        "The person was disabled.",
      );
      await expect(await findRow(page, email)).toContainText("Disabled");

      await (
        await findRow(page, email)
      )
        .getByRole("button", { name: "Enable", exact: true })
        .click();
      const enableDialog = await waitForDialog(page, "Enable this person?");
      await expect(enableDialog).toContainText(email);
      await enableDialog
        .getByRole("button", { name: "Enable", exact: true })
        .click();
      await expect(page.getByTestId("users-done")).toHaveText(
        "The person was enabled.",
      );
      await expect(await findRow(page, email)).toContainText("Invited");
    } finally {
      await context.close();
    }
  });
});
