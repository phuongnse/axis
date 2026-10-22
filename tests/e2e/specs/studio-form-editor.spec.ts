import { cp, mkdtemp, readFile, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { startServer } from "../activate";
import { pickOptions } from "../select";
import { e2eApp, purchaseRequests } from "../server-settings";
import { useSession } from "../sessions";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The journey saves a form. It runs on a second server over a folder of its own, copied from the committed sample, so
// no other spec's writes reach it.
test.describe("Studio edits form conditions and steps", () => {
  let copy: string;
  let server: Awaited<ReturnType<typeof startServer>>;

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), "axis-studio-form-editor-"));
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

  /** Fills the texts of a label in every locale and writes them, so the label's key exists before the save. */
  async function writeTexts(page: Page, label: string, en: string, vi: string) {
    const field = page.getByTestId(label);
    await field.getByRole("textbox", { name: "en", exact: true }).fill(en);
    await field.getByRole("textbox", { name: "vi", exact: true }).fill(vi);
    await field.getByRole("button", { name: "Write texts", exact: true }).click();
    await expect(field.getByTestId("studio-label-written")).toBeVisible();
  }

  test("an author checks a new requiredWhen before saving, then adds two reordered steps, with no new diagnostic", async ({
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
    const list = await page.request.get(
      `${server.url}/api/studio/apps/PurchaseRequests/resources`,
    );
    const { items } = (await list.json()) as {
      items: { id: string; file: string }[];
    };
    const review = items.find(
      (item) => item.file === "forms/purchase-request-review.json",
    )!;

    await page.goto(
      `${server.url}/studio?app=PurchaseRequests&resource=${review.id}`,
    );
    const condition = page.getByTestId(
      "studio-field-/sections/1/fields/0/requiredWhen",
    );
    const editor = condition.getByRole("textbox");
    await editor.fill("totalAmount + 1");
    await expect(
      condition.getByTestId("studio-expression-error"),
    ).toBeVisible();
    await editor.fill("totalAmount > 1000");
    await expect(condition.getByTestId("studio-expression-error")).toHaveCount(
      0,
    );
    await expect(condition.getByTestId("studio-expression-type")).toContainText(
      "boolean",
    );

    await page
      .getByTestId("studio-field-/sections/0/name")
      .getByRole("textbox")
      .fill("request");
    await page
      .getByTestId("studio-field-/sections/1/name")
      .getByRole("textbox")
      .fill("decision");

    const steps = page.getByTestId("studio-field-/steps");
    await steps.getByRole("button", { name: "Add step", exact: true }).click();
    await writeTexts(page, "studio-field-/steps/0/title", "Request", "Yêu cầu");
    await pickOptions(
      page.getByTestId("studio-field-/steps/0/sections").getByRole("combobox"),
      ["request"],
    );
    await steps.getByRole("button", { name: "Add step", exact: true }).click();
    await writeTexts(
      page,
      "studio-field-/steps/1/title",
      "Decision",
      "Quyết định",
    );
    await pickOptions(
      page.getByTestId("studio-field-/steps/1/sections").getByRole("combobox"),
      ["decision"],
    );
    await page
      .getByTestId("studio-field-/steps/1")
      .getByRole("button", { name: "Move up", exact: true })
      .click();

    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByTestId("studio-saved")).toBeVisible();

    expect(await diagnostics()).toEqual(before);
    const saved = JSON.parse(
      await readFile(
        path.join(copy, "forms", "purchase-request-review.json"),
        "utf8",
      ),
    ) as {
      sections: { name: string; fields: { requiredWhen?: string }[] }[];
      steps: unknown[];
    };
    expect(saved.sections[1].fields[0].requiredWhen).toBe("totalAmount > 1000");
    expect(saved.steps).toEqual([
      { title: { textKey: expect.any(String) }, sections: ["decision"] },
      { title: { textKey: expect.any(String) }, sections: ["request"] },
    ]);
  });
});
