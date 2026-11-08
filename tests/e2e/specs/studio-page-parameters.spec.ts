import { randomUUID } from "node:crypto";
import { mkdtemp, mkdir, readdir, readFile, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { expect, test } from "../test";
import type { Page } from "@playwright/test";
import { startServer } from "../activate";
import { confirmDialog, waitForDialog } from "../dialogs";
import { pickAction, pickOption } from "../select";
import { e2eApp } from "../server-settings";
import { useSession } from "../sessions";
import { createResource, openFormTab, openNewApplication } from "../studio-resources";

// A second server starts in beforeAll, and the journey creates an application and publishes it. Publishing Approvals
// runs the contract scenarios of both built-in modules, which takes longer than the other Studio journeys.
test.setTimeout(480_000);

const departmentId = "3f1c2a7e-5b8d-4e61-9a0c-7d2e4b6f8a19";

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

  /** The Add widget picker of the page's own widget list, not of a list inside a container. */
  function addWidgetPicker(page: Page) {
    return page
      .getByTestId("studio-widget-list-/widgets")
      .locator("xpath=..")
      .locator("xpath=./div[not(@data-testid)]")
      .getByRole("combobox");
  }

  /** Fills the texts of a label in every locale, which the next Save writes before it saves the label's key. */
  async function fillTexts(page: Page, label: string, en: string, vi: string) {
    const field = page.getByTestId(label);
    await field.getByRole("textbox", { name: "en", exact: true }).fill(en);
    await field.getByRole("textbox", { name: "vi", exact: true }).fill(vi);
  }

  async function writeResource(folder: string, file: string, value: unknown) {
    await mkdir(path.dirname(path.join(folder, file)), { recursive: true });
    await writeFile(path.join(folder, file), JSON.stringify(value, null, 2));
  }

  async function save(page: Page) {
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByTestId("studio-saved")).toBeVisible();
  }

  test("an author adds a required department parameter and a row link that gives it, and a requester follows the link", async ({
    page,
  }) => {
    const name = `Params${Date.now().toString(36)}`;
    const stamp = Date.now().toString(36);
    await useSession(page, "author");
    await page.goto(`${server.url}/studio?app=PurchaseRequests`);

    await openNewApplication(page);
    const dialog = await waitForDialog(page);
    await dialog.getByRole("textbox", { name: "Name", exact: true }).fill(name);
    await dialog.getByRole("radio", { name: /^Approvals/ }).click();
    await dialog.getByRole("button", { name: "Create", exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`app=${name}$`));
    await expect(dialog).toBeHidden();

    const folder = await applicationFolder(name);
    await writeResource(folder, "seeds/parameter-departments.json", {
      id: randomUUID(),
      kind: "seed",
      name: "ParameterDepartments",
      formatVersion: 1,
      entity: "organization/Department",
      records: [
        {
          id: departmentId,
          values: { code: "PARAMS", name: `Parameters ${stamp}` },
        },
      ],
    });
    // Members of Approvals read departments through the entity, not the departments data source the table reads, so
    // the employee role gets that read here, as the purchase request sample does.
    await writeResource(folder, "policies/employee-access.json", {
      id: randomUUID(),
      kind: "policy",
      name: "EmployeeAccess",
      formatVersion: 1,
      roles: ["employee"],
      grants: [
        {
          name: "departmentsRead",
          dataSource: "organization/Departments",
          actions: ["read"],
        },
      ],
    });

    await createResource(page, "page", "DepartmentDetail");
    await openFormTab(page);
    const editor = page.getByTestId("studio-page-editor");
    await expect(editor.getByRole("textbox", { name: "Name", exact: true })).toHaveValue(
      "DepartmentDetail",
    );

    // The names load again, so the entities of the new application are offered below.
    await page.reload();
    await openFormTab(page);
    await expect(editor.getByRole("textbox", { name: "Name", exact: true })).toHaveValue(
      "DepartmentDetail",
    );
    await fillTexts(
      page,
      "studio-field-/title",
      `Department detail ${stamp}`,
      `Chi tiết phòng ban ${stamp}`,
    );

    await page.getByRole("button", { name: "Add parameter", exact: true }).click();
    await page.getByRole("textbox", { name: "Parameter name", exact: true }).fill("department");
    await pick(page, "studio-field-/parameters/0/type", "reference");
    await pick(page, "studio-field-/parameters/0/target", "organization/Department");
    await page.getByRole("switch", { name: "Required", exact: true }).click();
    await save(page);

    await createResource(page, "page", "DepartmentList");
    await openFormTab(page);

    // The names load again, so the parameter of DepartmentDetail is offered by the row action below.
    await page.reload();
    await openFormTab(page);
    await expect(editor.getByRole("textbox", { name: "Name", exact: true })).toHaveValue(
      "DepartmentList",
    );
    await fillTexts(
      page,
      "studio-field-/title",
      `Departments ${stamp}`,
      `Phòng ban ${stamp}`,
    );

    await pickAction(
      addWidgetPicker(page),
      "table",
      page.getByTestId("studio-widget-/widgets/1"),
    );
    await page.getByTestId("studio-widget-/widgets/1").click();
    await pick(page, "studio-field-/widgets/1/dataSource", "organization/Departments");

    await page
      .getByTestId("studio-field-/widgets/1/rowActions")
      .getByRole("button", { name: "Add navigate action", exact: true })
      .click();
    const action = "studio-field-/widgets/1/rowActions/0";
    await pick(page, `${action}/page`, "DepartmentDetail");
    await fillTexts(page, `${action}/label`, "Details", "Chi tiết");
    const parameter = page.getByTestId(`${action}/parameters/department`);
    await expect(parameter).toContainText("department, reference, required");
    await parameter.getByRole("textbox").fill("id");
    await save(page);

    const detail = JSON.parse(
      await readFile(path.join(folder, "pages", "department-detail.json"), "utf8"),
    ) as { parameters: unknown[] };
    expect(detail.parameters).toEqual([
      { name: "department", type: "reference", target: "organization/Department", required: true },
    ]);
    const list = JSON.parse(
      await readFile(path.join(folder, "pages", "department-list.json"), "utf8"),
    ) as {
      widgets: { rowActions?: { parameters?: unknown }[] }[];
    };
    expect(list.widgets[1].rowActions?.[0].parameters).toEqual({ department: "id" });

    await page.getByRole("button", { name: "Publish…", exact: true }).click();
    await confirmDialog(page, "Publish");
    await expect(page.getByTestId("studio-request-status")).toHaveAttribute(
      "data-status",
      "succeeded",
      {
        timeout: 300_000,
      },
    );

    const sites = await readdir(path.join(folder, "sites"));
    const site = JSON.parse(
      await readFile(path.join(folder, "sites", sites[0]), "utf8"),
    ) as { path: string };

    await useSession(page, "requester");
    await page.goto(`${server.url}/${site.path}/departmentlist`);
    // The first navigate action of a table is the link of its first column, here the department code.
    await page
      .getByRole("row", { name: new RegExp(`Parameters ${stamp}`) })
      .getByRole("link", { name: "PARAMS", exact: true })
      .click();
    await expect(page).toHaveURL(
      `${server.url}/${site.path}/departmentdetail?department=${departmentId}`,
    );
    await expect(
      page.getByRole("heading", { name: `Department detail ${stamp}`, exact: true }),
    ).toBeVisible();
  });
});
