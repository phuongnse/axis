import { cp, mkdtemp, readFile, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { expect, test } from "@playwright/test";
import { startServer } from "../activate";
import { pickOption } from "../select";
import { e2eApp, serviceDesk } from "../server-settings";
import { useSession } from "../sessions";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

const dashboardId = "8ae8ab97-77c5-48e9-a798-a912b5bb3cf3";
const allTicketsId = "4ff52d55-8194-45c4-901b-bdc18e269096";

// The main server's Studio edits the purchase request copy only. This server's Studio folder is a copy of the service
// desk sample, whose Dashboard page has KPIs, a chart and a table.
test.describe("Studio draws a page on the Design tab", () => {
  let copy: string;
  let server: Awaited<ReturnType<typeof startServer>>;

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), "axis-studio-page-design-"));
    await cp(serviceDesk, copy, { recursive: true });
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

  test("an author sees the dashboard's widgets with the application's texts and no request goes to the application's API", async ({
    page,
  }) => {
    await useSession(page, "hoa");
    const applicationRequests: string[] = [];
    page.on("request", (request) => {
      const { pathname } = new URL(request.url());
      if (pathname.startsWith("/api/apps/")) {
        applicationRequests.push(`${request.method()} ${pathname}`);
      }
    });

    await page.goto(
      `${server.url}/studio?app=ServiceDesk&resource=${dashboardId}`,
    );
    await page.getByRole("tab", { name: "Design", exact: true }).click();

    const canvas = page.getByTestId("studio-page-canvas");
    await expect(canvas.getByTestId("kpi-tile").first()).toContainText(
      "Open tickets",
    );
    await expect(canvas.getByTestId("chart-widget")).toContainText(
      "Open tickets by category",
    );
    expect(applicationRequests).toEqual([]);
  });

  test("a widget that does not compile draws as a marked placeholder with its diagnostic on the Design tab", async ({
    page,
  }) => {
    await useSession(page, "hoa");
    await page.goto(
      `${server.url}/studio?app=ServiceDesk&resource=${dashboardId}`,
    );
    await page.getByRole("tab", { name: "JSON", exact: true }).click();
    const json = page.getByRole("textbox", { name: "JSON", exact: true });
    const draft = JSON.parse(await json.inputValue());
    draft.widgets[0].columns[1].widgets[0].dataSource = "MissingSource";
    await json.fill(JSON.stringify(draft));
    await page.getByRole("tab", { name: "Design", exact: true }).click();

    const canvas = page.getByTestId("studio-page-canvas");
    const chart = canvas.locator(
      '[data-testid="studio-canvas-widget"][data-path="/widgets/0/columns/1/widgets/0"]',
    );
    await expect(chart).toHaveAttribute("data-marked", "true");
    await expect(chart.getByTestId("studio-canvas-placeholder")).toContainText(
      "This widget does not compile: chart",
    );
    await expect(
      chart.getByTestId("studio-canvas-placeholder-diagnostic"),
    ).toContainText("The data source 'MissingSource' was not found.");

    const kpi = canvas.locator(
      '[data-testid="studio-canvas-widget"][data-path="/widgets/0/columns/0/widgets/0"]',
    );
    await expect(kpi).not.toHaveAttribute("data-marked");
  });

  test("an author selects a row action on the canvas and adds a navigate action that maps the record, and no request goes to the application's API", async ({
    page,
  }) => {
    await useSession(page, "hoa");
    const applicationRequests: string[] = [];
    page.on("request", (request) => {
      const { pathname } = new URL(request.url());
      if (pathname.startsWith("/api/apps/")) {
        applicationRequests.push(`${request.method()} ${pathname}`);
      }
    });

    await page.goto(
      `${server.url}/studio?app=ServiceDesk&resource=${allTicketsId}`,
    );
    await page.getByRole("tab", { name: "Design", exact: true }).click();

    const canvas = page.getByTestId("studio-page-canvas");
    // The Form tab stays mounted while it is hidden, so the panel is the Design tab's own.
    const panel = page.getByTestId("studio-design-panel");
    const rows = panel.getByTestId("studio-field-/widgets/0/rowActions");
    await canvas.locator('[data-action="rowActions/1"]').first().click();
    await expect(rows.getByTestId("studio-action-1")).toHaveAttribute(
      "aria-current",
      "true",
    );
    await expect(
      canvas.locator('[data-action="rowActions/1"]').first(),
    ).toHaveAttribute("aria-current", "true");

    await rows
      .getByRole("button", { name: "Add navigate action", exact: true })
      .click();
    await expect(rows.getByTestId("studio-action-2")).toHaveAttribute(
      "aria-current",
      "true",
    );
    const action = "studio-field-/widgets/0/rowActions/2";
    await pickOption(
      panel.getByTestId(`${action}/page`).getByRole("combobox"),
      "TicketDetail",
    );
    const label = panel.getByTestId(`${action}/label`);
    await label
      .getByRole("textbox", { name: "en", exact: true })
      .fill("Inspect");
    await label
      .getByRole("textbox", { name: "vi", exact: true })
      .fill("Xem kỹ");
    await label
      .getByRole("button", { name: "Write texts", exact: true })
      .click();
    await expect(label.getByTestId("studio-label-written")).toBeVisible();
    const parameter = panel.getByTestId(`${action}/parameters/ticket`);
    await expect(parameter).toContainText("ticket");
    await parameter.getByRole("textbox").fill("id");
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByTestId("studio-saved")).toBeVisible();

    const saved = JSON.parse(
      await readFile(path.join(copy, "pages", "all-tickets.json"), "utf8"),
    ) as { widgets: { rowActions: unknown[] }[] };
    expect(saved.widgets[0].rowActions[2]).toMatchObject({
      type: "navigate",
      page: "TicketDetail",
      parameters: { ticket: "id" },
    });
    expect(applicationRequests).toEqual([]);
  });
});
