import { cp, mkdtemp, readFile, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { expect, test } from "../test";
import type { Page } from "@playwright/test";
import { startServer } from "../activate";
import { e2eApp, purchaseRequests } from "../server-settings";
import { useSession } from "../sessions";
import { openTab } from "../studio-resources";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The journeys save a resource, so they run on a second server over a copy of the committed sample, with a folder of
// revisions of its own. No other spec's writes reach either.
test.describe("Studio shows the history of an application and undoes a revision", () => {
  test.describe.configure({ mode: "serial" });

  let copy: string;
  let revisions: string;
  let server: Awaited<ReturnType<typeof startServer>>;
  const roleFile = "roles/employee.json";

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), "axis-studio-history-"));
    await cp(purchaseRequests, copy, { recursive: true });
    revisions = await mkdtemp(path.join(os.tmpdir(), "axis-studio-revisions-"));
    // Index 0 of ActivateOnStartup is the purchase request sample, so it stays out of this server and the live
    // release of the main E2E server is not activated again.
    server = await startServer({
      ActivateOnStartup__0: e2eApp,
      Studio__Folders__0: copy,
      Studio__RevisionsPath: revisions,
    });
  });

  test.afterAll(async () => {
    await server?.stop();
    for (const folder of [copy, revisions]) {
      if (folder) {
        await rm(folder, { recursive: true, force: true });
      }
    }
  });

  async function readRole() {
    return JSON.parse(await readFile(path.join(copy, roleFile), "utf8")) as {
      id: string;
      label: { textKey: string };
    };
  }

  /** Saves the role with a new label text key through the JSON tab, so each save is one revision. */
  async function saveRole(page: Page, textKey: string) {
    const role = await readRole();
    role.label.textKey = textKey;
    await page.goto(
      `${server.url}/studio?app=PurchaseRequests&resource=${role.id}`,
    );
    await openTab(page, "JSON");
    await page
      .getByRole("textbox", { name: "JSON", exact: true })
      .fill(JSON.stringify(role, null, 2));
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByTestId("studio-saved")).toBeVisible();
  }

  async function openHistory(page: Page) {
    await page
      .getByRole("navigation", { name: "Menu", exact: true })
      .locator('a[href$="&view=history"]')
      .click();
    await expect(page).toHaveURL(/[?&]view=history(&|$)/);
  }

  test("an author saves twice, reads the diff of the last save and undoes it", async ({
    page,
  }) => {
    await useSession(page, "author");
    await saveRole(page, "role.employee.first");
    await saveRole(page, "role.employee.second");

    await openHistory(page);
    const rows = page.getByTestId("studio-revision-row");
    await expect(rows).toHaveCount(3);
    await expect(rows.last()).toContainText("System");
    await expect(rows.last().getByRole("button")).toHaveCount(0);
    await expect(rows.first()).toContainText("Studio");

    await rows.first().getByTestId("studio-revision-link").click();
    await expect(page).toHaveURL(/[?&]revision=[0-9a-f]{40}(&|$)/);
    const change = page.getByTestId("studio-revision-change");
    await expect(change).toHaveCount(1);
    await expect(change).toHaveAttribute("data-file", roleFile);
    await expect(change.getByTestId("studio-revision-before")).toContainText(
      "role.employee.first",
    );
    await expect(change.getByTestId("studio-revision-after")).toContainText(
      "role.employee.second",
    );

    await openHistory(page);
    await rows
      .first()
      .getByRole("button", { name: /^Undo /, exact: false })
      .click();
    await expect(rows).toHaveCount(4);
    await expect(
      rows.first().getByTestId("studio-revision-link"),
    ).toContainText(/^Undo /);
    await expect
      .poll(async () => (await readRole()).label.textKey)
      .toBe("role.employee.first");
  });

  test("an author undoes a revision that a later one blocks and the message names the later one", async ({
    page,
  }) => {
    await useSession(page, "author");
    await saveRole(page, "role.employee.third");
    await saveRole(page, "role.employee.fourth");
    const before = await readFile(path.join(copy, roleFile), "utf8");

    await openHistory(page);
    const rows = page.getByTestId("studio-revision-row");
    await expect(rows).toHaveCount(6);
    const lastSave = await rows.first().getAttribute("data-id");
    expect(lastSave).toMatch(/^[0-9a-f]{40}$/);

    await rows
      .nth(1)
      .getByRole("button", { name: /^Undo /, exact: false })
      .click();

    const blocked = page.getByTestId("studio-history-blocked");
    await expect(blocked).toBeVisible();
    await expect(blocked.getByTestId("studio-history-blocker")).toHaveCount(1);
    await expect(blocked.getByTestId("studio-history-blocker")).toHaveAttribute(
      "data-id",
      lastSave!,
    );
    await expect(rows).toHaveCount(6);
    expect(await readFile(path.join(copy, roleFile), "utf8")).toBe(before);
  });
});
