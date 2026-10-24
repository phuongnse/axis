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
});
