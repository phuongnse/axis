import { cp, mkdtemp, rm } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { expect, test } from "@playwright/test";
import { startServer } from "../activate";
import { e2eApp, serviceDesk } from "../server-settings";
import { useSession } from "../sessions";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

const dashboardId = "8ae8ab97-77c5-48e9-a798-a912b5bb3cf3";
const ticketEditId = "1db765b0-483d-456b-9939-17e2a36de3b6";

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
});
