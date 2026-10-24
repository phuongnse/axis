import { cp, mkdtemp, readFile, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { expect, test, type Locator, type Page } from "@playwright/test";
import { startServer } from "../activate";
import { pickOption } from "../select";
import { e2eApp, serviceDesk } from "../server-settings";
import { useSession } from "../sessions";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

const dashboardId = "8ae8ab97-77c5-48e9-a798-a912b5bb3cf3";
const ticketEditId = "1db765b0-483d-456b-9939-17e2a36de3b6";
const allTicketsId = "4ff52d55-8194-45c4-901b-bdc18e269096";
const ticketDetailId = "e6a7b810-39ed-4751-a0cc-b761ffc812e5";

/**
 * Drags a palette type with the mouse to the point of `target` that is `fraction` of its height down: a small move
 * starts the drag, then steps reach the point. The designer is scrolled to the top of the window first, and both
 * points are measured after that, because the page scrolls while it loads and a mouse outside the window hits nothing.
 */
async function dragType(
  page: Page,
  type: string,
  target: Locator,
  fraction = 0.5,
) {
  await page
    .getByTestId("studio-page-design")
    .evaluate((element) => element.scrollIntoView({ block: "start" }));
  const palette = page.getByTestId(`studio-palette-${type}`);
  const from = (await palette.boundingBox())!;
  const to = (await target.boundingBox())!;
  const startX = from.x + from.width / 2;
  const startY = from.y + from.height / 2;
  await page.mouse.move(startX, startY);
  await page.mouse.down();
  await page.mouse.move(startX + 10, startY + 10);
  await page.mouse.move(to.x + to.width / 2, to.y + to.height * fraction, {
    steps: 15,
  });
  await page.mouse.up();
}

// The main server's Studio edits the purchase request copy only. This server's Studio folder is a copy of the service
// desk sample, whose Dashboard page has KPIs, a chart and a table.
test.describe("Studio draws a page and a form on the Design tab", () => {
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

  test("an author drags a section after the detail, then a table into the section, and saves the tree", async ({
    page,
  }) => {
    // A tall window keeps the palette and the drop areas in view together, away from the edges where a drag scrolls.
    await page.setViewportSize({ width: 1280, height: 1000 });
    await useSession(page, "hoa");
    await page.goto(
      `${server.url}/studio?app=ServiceDesk&resource=${ticketDetailId}`,
    );
    await page.getByRole("tab", { name: "Design", exact: true }).click();

    const detail = page.locator(
      '[data-testid="studio-canvas-widget"][data-path="/widgets/0"]',
    );
    await expect(detail).toBeVisible();
    await dragType(page, "section", detail, 0.75);

    // A section without a widget does not compile, so the canvas draws it as a placeholder with a drop area.
    const list = page.locator(
      '[data-testid="studio-canvas-list"][data-list="/widgets/1/widgets"]',
    );
    await expect(list).toBeVisible();
    await dragType(page, "table", list);

    await expect(page.getByTestId("studio-palette-announcement")).toContainText(
      "Inserted table",
    );
    await page.getByRole("button", { name: "Save", exact: true }).click();

    const file = path.join(copy, "pages", "ticket-detail.json");
    try {
      await expect
        .poll(async () => JSON.parse(await readFile(file, "utf8")).widgets)
        .toEqual([
          { type: "detail", entity: "Ticket", record: "ticket" },
          {
            type: "section",
            title: { textKey: "" },
            widgets: [{ type: "table" }],
          },
        ]);
    } finally {
      // The saved table has no data source and does not compile. The page metadata route answers no page while any file
      // has an error, so the next journeys, which share this folder, would find no canvas.
      await cp(path.join(serviceDesk, "pages", "ticket-detail.json"), file);
    }
  });

  test("an author sees both steps of the ticket form, and the condition of the Resolution field after picking the second", async ({
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
      `${server.url}/studio?app=ServiceDesk&resource=${ticketEditId}`,
    );
    await page.getByRole("tab", { name: "Design", exact: true }).click();

    const steps = page.getByTestId("studio-form-steps");
    await expect(steps).toContainText("Describe the problem");
    await expect(steps).toContainText("Add details");
    const canvas = page.getByTestId("studio-form-canvas");
    await expect(
      canvas.getByRole("textbox", { name: "* Title", exact: true }),
    ).toBeVisible();

    await steps.getByText("Add details", { exact: true }).click();
    await expect(
      canvas
        .getByTestId("studio-form-field")
        .filter({ has: page.getByTestId("studio-condition-mark") })
        .getByTestId("studio-condition-mark")
        .first(),
    ).toContainText("status == 'resolved'");
    expect(applicationRequests).toEqual([]);
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
