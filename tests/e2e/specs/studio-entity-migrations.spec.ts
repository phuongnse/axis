import { randomUUID } from "node:crypto";
import { mkdir, readdir, readFile, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { expect, test } from "../test";
import type { Page, TestInfo } from "@playwright/test";
import { startServer } from "../activate";
import { confirmDialog, waitForDialog } from "../dialogs";
import { useSession } from "../sessions";

// Each journey starts a second server and publishes once, which takes a while.
test.setTimeout(240_000);

/** Slows the page's animations, so a click that does not wait for a dialog to open always lands during its zoom. */
async function slowAnimations(page: Page) {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send("Animation.enable");
  await cdp.send("Animation.setPlaybackRate", { playbackRate: 0.2 });
}

/**
 * A throwaway application with the entity Order, which has the fields `number` and `note`, and a server whose
 * Studio edits it. A migration in the purchase request sample would stop the committed sample from activating
 * again, so the journeys do not use it. The application is published once, so Studio compares with a live release.
 */
async function publishedOrder(page: Page, testInfo: TestInfo, prefix: string) {
  const name = `${prefix}${Date.now().toString(36)}`;
  const folder = testInfo.outputPath(name);
  const orderId = randomUUID();
  await rm(folder, { recursive: true, force: true });
  await mkdir(path.join(folder, "entities"), { recursive: true });
  await writeFile(
    path.join(folder, "application.json"),
    JSON.stringify({
      id: randomUUID(),
      kind: "application",
      name,
      formatVersion: 1,
    }),
  );
  await writeFile(
    path.join(folder, "entities", "order.json"),
    JSON.stringify({
      id: orderId,
      kind: "entity",
      name: "Order",
      formatVersion: 1,
      fields: [
        { name: "number", type: "text", required: true, maxLength: 20 },
        { name: "note", type: "text" },
      ],
    }),
  );
  const server = await startServer({ Studio__Folders__0: folder });
  try {
    await useSession(page, "hoa");
    await slowAnimations(page);
    await page.goto(`${server.url}/studio?app=${name}`);
    await page.getByRole("button", { name: "Publish", exact: true }).click();
    await confirmDialog(page, "Publish");
    await expect(page.getByTestId("studio-request-status")).toHaveAttribute(
      "data-status",
      "succeeded",
      {
        timeout: 60_000,
      },
    );
    await page.getByTestId(`studio-resource-${orderId}`).click();
    await expect(page.getByTestId("studio-entity-designer")).toBeVisible();
  } catch (error) {
    await server.stop();
    throw error;
  }
  return { server, folder };
}

/** The changes of the one migration file in the folder. */
async function savedMigration(folder: string) {
  const files = await readdir(path.join(folder, "migrations"));
  expect(files).toHaveLength(1);
  const migration = JSON.parse(
    await readFile(path.join(folder, "migrations", files[0]), "utf8"),
  ) as {
    name: string;
    entity: string;
    changes: unknown[];
  };
  expect(migration.entity).toBe("Order");
  expect(migration.name).toMatch(/^OrderChange\d{14}$/);
  return migration.changes;
}

test.describe("Studio adds a migration for a renamed, removed or converted field", () => {
  // The journeys publish to the one test database, so they run one after the other.
  test.describe.configure({ mode: "serial" });

  test("an author renames a published field, keeps its data and validates without AXC0015", async ({
    page,
  }, testInfo) => {
    const { server, folder } = await publishedOrder(
      page,
      testInfo,
      "RenameField",
    );
    try {
      await page
        .getByTestId("studio-field-/fields/1/name")
        .getByRole("textbox")
        .fill("remark");
      await page.getByRole("button", { name: "Save", exact: true }).click();
      const question = page.getByTestId("studio-migration-question");
      await expect(question).toContainText("note → remark");
      await confirmDialog(page, "Keep data");

      await expect(page.getByTestId("studio-saved")).toBeVisible();
      expect(await savedMigration(folder)).toEqual([
        { type: "renameField", from: "note", to: "remark" },
      ]);

      await page.getByRole("button", { name: "Validate", exact: true }).click();
      await expect(page.getByTestId("studio-request-status")).toHaveAttribute(
        "data-status",
        "succeeded",
        {
          timeout: 60_000,
        },
      );
      await expect(page.getByTestId("studio-diagnostics")).not.toContainText(
        "AXC0015",
      );
    } finally {
      await server.stop();
    }
  });

  test("an author removes a published field, drops its data and the save adds a dropField migration", async ({
    page,
  }, testInfo) => {
    const { server, folder } = await publishedOrder(
      page,
      testInfo,
      "DropField",
    );
    try {
      await page
        .getByTestId("studio-field-/fields/1")
        .getByRole("button", { name: "Remove field", exact: true })
        .click();
      await page.getByRole("button", { name: "Save", exact: true }).click();
      await expect(page.getByTestId("studio-migration-question")).toContainText(
        "note",
      );
      await confirmDialog(page, "Drop data");

      await expect(page.getByTestId("studio-saved")).toBeVisible();
      expect(await savedMigration(folder)).toEqual([
        { type: "dropField", field: "note" },
      ]);
    } finally {
      await server.stop();
    }
  });

  test("an author narrows a published maxLength, adds a convert expression and validates without AXC0016", async ({
    page,
  }, testInfo) => {
    const { server, folder } = await publishedOrder(
      page,
      testInfo,
      "ConvertField",
    );
    try {
      await page
        .getByTestId("studio-field-/fields/0/maxLength")
        .getByRole("spinbutton")
        .fill("10");
      await page.getByRole("button", { name: "Save", exact: true }).click();
      await expect(page.getByTestId("studio-migration-question")).toContainText(
        "number",
      );
      const dialog = await waitForDialog(page);
      await dialog
        .getByRole("textbox", { name: "Convert expression", exact: true })
        .fill("number");
      await confirmDialog(page, "Add convert");

      await expect(page.getByTestId("studio-saved")).toBeVisible();
      expect(await savedMigration(folder)).toEqual([
        { type: "convert", field: "number", value: "number" },
      ]);

      await page.getByRole("button", { name: "Validate", exact: true }).click();
      await expect(page.getByTestId("studio-request-status")).toHaveAttribute(
        "data-status",
        "succeeded",
        {
          timeout: 60_000,
        },
      );
      await expect(page.getByTestId("studio-diagnostics")).not.toContainText(
        "AXC0016",
      );
    } finally {
      await server.stop();
    }
  });
});
