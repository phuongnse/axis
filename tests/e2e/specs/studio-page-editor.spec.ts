import { cp, mkdtemp, readFile, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { startServer } from "../activate";
import { pickOption } from "../select";
import { e2eApp, purchaseRequests } from "../server-settings";
import { useSession } from "../sessions";
import { createResource } from "../studio-resources";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The journey saves two pages in steps. It runs on a second server over a folder of its own, copied from the committed
// sample, so no other spec's writes reach it.
test.describe("Studio builds pages in the page editor", () => {
  test.describe.configure({ mode: "serial" });

  let copy: string;
  let server: Awaited<ReturnType<typeof startServer>>;

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), "axis-studio-page-editor-"));
    await cp(purchaseRequests, copy, { recursive: true });
    // Index 0 of ActivateOnStartup is the purchase request sample, so it stays out of this server and the live
    // release of the main E2E server is not activated again.
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

  async function createPage(page: Page, name: string) {
    await createResource(page, "page", name);
    // The editor of the page that was open stays on screen until the new page replaces it, so wait for the name.
    await expect(
      page
        .getByTestId("studio-page-editor")
        .getByRole("textbox", { name: "Name" }),
    ).toHaveValue(name);
  }

  /** Fills the texts of a label in every locale and writes them, so the label's key exists before the save. */
  async function writeTexts(page: Page, label: string, en: string, vi: string) {
    const field = page.getByTestId(label);
    await field.getByRole("textbox", { name: "en", exact: true }).fill(en);
    await field.getByRole("textbox", { name: "vi", exact: true }).fill(vi);
    await field.getByRole("button", { name: "Write texts" }).click();
    await expect(field.getByTestId("studio-label-written")).toBeVisible();
  }

  async function pick(page: Page, field: string, option: string) {
    await pickOption(page.getByTestId(field).getByRole("combobox"), option);
  }

  async function save(page: Page) {
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByTestId("studio-saved")).toBeVisible();
  }

  test("an author builds a form page with an action and a table page that opens it with a row navigate, with no new diagnostic", async ({
    page,
  }) => {
    await useSession(page, "hoa");
    const diagnostics = async () =>
      (await (
        await page.request.get(
          `${server.url}/api/studio/apps/PurchaseRequests/diagnostics`,
        )
      ).json()) as unknown;
    const before = await diagnostics();

    await page.goto(`${server.url}/studio?app=PurchaseRequests`);
    await createPage(page, "ReviewForm");
    await writeTexts(
      page,
      "studio-field-/title",
      "Review form",
      "Biểu mẫu xem xét",
    );
    await pick(page, "studio-field-/widgets/0/type", "form");
    await pick(page, "studio-field-/widgets/0/entity", "PurchaseRequest");
    await page.getByRole("button", { name: "Add action" }).click();
    await pick(
      page,
      "studio-field-/widgets/0/actions/0/process",
      "PurchaseRequestApproval",
    );
    await writeTexts(
      page,
      "studio-field-/widgets/0/actions/0/label",
      "Send for review",
      "Gửi xem xét",
    );
    await save(page);

    await createPage(page, "ReviewList");
    // The names load again, so the new form page is offered below.
    await page.reload();
    await expect(
      page
        .getByTestId("studio-page-editor")
        .getByRole("textbox", { name: "Name" }),
    ).toHaveValue("ReviewList");
    await writeTexts(
      page,
      "studio-field-/title",
      "Review list",
      "Danh sách xem xét",
    );
    await pick(page, "studio-field-/widgets/0/type", "table");
    await pick(
      page,
      "studio-field-/widgets/0/dataSource",
      "PurchaseRequestList",
    );
    await page
      .getByTestId("studio-field-/widgets/0/rowActions")
      .getByRole("button", { name: "Add navigate action" })
      .click();
    await pick(page, "studio-field-/widgets/0/rowActions/0/page", "ReviewForm");
    await writeTexts(
      page,
      "studio-field-/widgets/0/rowActions/0/label",
      "Open",
      "Mở",
    );
    await page
      .getByTestId("studio-field-/widgets/0/rowActions/0/record")
      .getByRole("textbox")
      .fill("id");
    await save(page);

    expect(await diagnostics()).toEqual(before);
    const form = JSON.parse(
      await readFile(path.join(copy, "pages", "review-form.json"), "utf8"),
    ) as {
      widgets: unknown[];
    };
    expect(form.widgets).toEqual([
      {
        type: "form",
        entity: "PurchaseRequest",
        actions: [
          {
            type: "startProcess",
            process: "PurchaseRequestApproval",
            label: { textKey: expect.stringMatching(/^pages\./) },
          },
        ],
      },
    ]);
    const list = JSON.parse(
      await readFile(path.join(copy, "pages", "review-list.json"), "utf8"),
    ) as {
      widgets: unknown[];
    };
    expect(list.widgets).toEqual([
      {
        type: "table",
        dataSource: "PurchaseRequestList",
        rowActions: [
          {
            type: "navigate",
            page: "ReviewForm",
            label: { textKey: expect.stringMatching(/^pages\./) },
            record: "id",
          },
        ],
      },
    ]);
  });
});
