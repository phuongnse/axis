import { mkdtemp, readdir, readFile, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { startServer } from "../activate";
import { csrfHeaders } from "../csrf";
import { pickOption } from "../select";
import { e2eApp } from "../server-settings";
import { useSession } from "../sessions";

// A second server starts in beforeAll, and the journey creates an application and publishes it.
test.setTimeout(240_000);

// The journey publishes a new application, which would change the live release of the shared purchase request copy
// that other specs use. So it runs on a second server whose applications folder is a temporary one.
test.describe("Studio declares a page parameter and links to the page", () => {
  test.describe.configure({ mode: "serial" });

  let applications: string;
  let server: Awaited<ReturnType<typeof startServer>>;

  test.beforeAll(async () => {
    applications = await mkdtemp(
      path.join(os.tmpdir(), "axis-studio-page-parameters-"),
    );
    // Index 0 of ActivateOnStartup is the purchase request sample, so it stays out of this server and the live
    // release of the main E2E server is not activated again.
    server = await startServer({
      ActivateOnStartup__0: e2eApp,
      Studio__ApplicationsPath: applications,
    });
  });

  test.afterAll(async () => {
    await server?.stop();
    if (applications) {
      await rm(applications, { recursive: true, force: true });
    }
  });

  /** The folder under the applications folder whose manifest has the name. */
  async function applicationFolder(name: string): Promise<string> {
    for (const folder of await readdir(applications)) {
      try {
        const manifest = JSON.parse(
          await readFile(
            path.join(applications, folder, "application.json"),
            "utf8",
          ),
        );
        if (manifest.name === name) return path.join(applications, folder);
      } catch {
        // A folder without a manifest is not an application.
      }
    }
    throw new Error(`No application called ${name} under ${applications}.`);
  }

  async function pick(page: Page, field: string, option: string) {
    await pickOption(page.getByTestId(field).getByRole("combobox"), option);
  }

  /** Fills the texts of a label in every locale and writes them, so the label's key exists before the save. */
  async function writeTexts(page: Page, label: string, en: string, vi: string) {
    const field = page.getByTestId(label);
    await field.getByRole("textbox", { name: "en", exact: true }).fill(en);
    await field.getByRole("textbox", { name: "vi", exact: true }).fill(vi);
    await field.getByRole("button", { name: "Write texts", exact: true }).click();
    await expect(field.getByTestId("studio-label-written")).toBeVisible();
  }

  async function save(page: Page) {
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByTestId("studio-saved")).toBeVisible();
  }

  test("an author adds a required parameter and a row link that gives it, and a requester follows the link", async ({
    page,
  }) => {
    const name = `Params${Date.now().toString(36)}`;
    const stamp = Date.now().toString(36);
    const title = `Link ${stamp}`;
    await useSession(page, "hoa");
    await page.goto(`${server.url}/studio?app=PurchaseRequests`);

    await page.getByRole("button", { name: "New application", exact: true }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByRole("textbox", { name: "Name", exact: true }).fill(name);
    await dialog.getByRole("radio", { name: /^Request approval/ }).click();
    await dialog.getByRole("button", { name: "Create", exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`app=${name}$`));
    await expect(dialog).toBeHidden();

    await page.getByRole("button", { name: "New resource", exact: true }).click();
    await dialog.getByRole("combobox", { name: "Kind", exact: true }).click();
    await page.getByTitle("page", { exact: true }).click();
    await dialog.getByRole("textbox", { name: "Name", exact: true }).fill("RequestDetail");
    await dialog.getByRole("button", { name: "Create", exact: true }).click();
    // The dialog gives the focus back to the New resource button when its close animation ends, and the pick helpers
    // press Enter, so they start only when it is gone.
    await expect(dialog).toBeHidden();
    const editor = page.getByTestId("studio-page-editor");
    await expect(editor.getByRole("textbox", { name: "Name", exact: true })).toHaveValue(
      "RequestDetail",
    );

    // The names load again, so the entities of the new application are offered below.
    await page.reload();
    await expect(editor.getByRole("textbox", { name: "Name", exact: true })).toHaveValue(
      "RequestDetail",
    );
    await writeTexts(
      page,
      "studio-field-/title",
      `Request detail ${stamp}`,
      `Chi tiết yêu cầu ${stamp}`,
    );

    await page.getByRole("button", { name: "Add parameter", exact: true }).click();
    await page.getByRole("textbox", { name: "Parameter name", exact: true }).fill("request");
    await pick(page, "studio-field-/parameters/0/type", "reference");
    await pick(page, "studio-field-/parameters/0/target", "Request");
    await page.getByRole("switch", { name: "Required", exact: true }).click();
    await save(page);

    // The names list the new parameter after a reload, and the link below offers it.
    await page.reload();
    await page
      .getByTestId("studio-group-page")
      .getByRole("button", { name: "MyRequests", exact: true })
      .click();
    await expect(editor.getByRole("textbox", { name: "Name", exact: true })).toHaveValue(
      "MyRequests",
    );

    await page
      .getByTestId("studio-field-/widgets/0/rowActions")
      .getByRole("button", { name: "Add navigate action", exact: true })
      .click();
    const action = "studio-field-/widgets/0/rowActions/1";
    await pick(page, `${action}/page`, "RequestDetail");
    await writeTexts(page, `${action}/label`, "Details", "Chi tiết");
    const parameter = page.getByTestId(`${action}/parameters/request`);
    await expect(parameter).toContainText("request · reference · required");
    await parameter.getByRole("textbox").fill("id");
    await save(page);

    const folder = await applicationFolder(name);
    const detail = JSON.parse(
      await readFile(path.join(folder, "pages", "request-detail.json"), "utf8"),
    ) as {
      parameters: unknown[];
    };
    expect(detail.parameters).toEqual([
      { name: "request", type: "reference", target: "Request", required: true },
    ]);
    const list = JSON.parse(
      await readFile(path.join(folder, "pages", "my-requests.json"), "utf8"),
    ) as {
      widgets: { rowActions: { parameters?: unknown }[] }[];
    };
    expect(list.widgets[0].rowActions[1].parameters).toEqual({ request: "id" });

    await page.getByRole("button", { name: "Publish", exact: true }).click();
    await page
      .getByRole("dialog")
      .getByRole("button", { name: "Publish", exact: true })
      .click();
    await expect(page.getByTestId("studio-request-status")).toHaveAttribute(
      "data-status",
      "succeeded",
      {
        timeout: 60_000,
      },
    );

    const sites = await readdir(path.join(folder, "sites"));
    const site = JSON.parse(
      await readFile(path.join(folder, "sites", sites[0]), "utf8"),
    ) as { path: string };

    // The requester role holds read access to the page, and the request is the requester's own.
    await useSession(page, "anna");
    const created = await page.request.post(
      `${server.url}/api/apps/${name}/entities/Request/records`,
      {
        data: { values: { title, status: "draft" } },
        headers: csrfHeaders,
      },
    );
    expect(created.status()).toBe(201);
    const { id } = (await created.json()) as { id: string };

    await page.goto(`${server.url}/${site.path}/myrequests`);
    await page
      .getByRole("row", { name: new RegExp(title) })
      .getByRole("button", { name: "Details", exact: true })
      .click();
    await expect(page).toHaveURL(
      `${server.url}/${site.path}/requestdetail?request=${id}`,
    );
    await expect(
      page.getByRole("heading", { name: `Request detail ${stamp}`, exact: true }),
    ).toBeVisible();
  });
});
