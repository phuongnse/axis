import { cp, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
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
const ticketTriageId = "e5650c79-b0cb-449b-a5c2-1bd8d9acd452";
const ticketResolutionId = "e9311296-cc52-47fd-ac30-01d9051fab1d";

/**
 * Drags `source`, a palette type or a data panel name, with the mouse to the point of `target` that is `fraction` of its height
 * down: a small move starts the drag, then steps reach the point. The designer is scrolled to the top of the window first,
 * and both points are measured after that, because the page scrolls while it loads and a mouse outside the window hits
 * nothing.
 */
async function dragFrom(
  page: Page,
  source: Locator,
  target: Locator,
  fraction = 0.5,
) {
  await page
    .getByTestId("studio-page-design")
    .evaluate((element) => element.scrollIntoView({ block: "start" }));
  const from = (await source.boundingBox())!;
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

/**
 * Drags a form palette field with the mouse to the middle of `target`, as `dragType` does for a page: a small move
 * starts the drag, then steps reach the point, both measured after the designer is scrolled to the top.
 */
async function dragField(page: Page, name: string, target: Locator) {
  await dragPointer(
    page,
    page.getByTestId(`studio-form-palette-${name}`),
    target,
  );
}

/** Drags `source` with the mouse to the middle of `target`: a small move starts the drag, then steps reach the point. */
async function dragPointer(page: Page, source: Locator, target: Locator) {
  await page
    .getByTestId("studio-form-design")
    .evaluate((element) => element.scrollIntoView({ block: "start" }));
  const from = (await source.boundingBox())!;
  const to = (await target.boundingBox())!;
  const startX = from.x + from.width / 2;
  const startY = from.y + from.height / 2;
  await page.mouse.move(startX, startY);
  await page.mouse.down();
  await page.mouse.move(startX + 10, startY + 10);
  await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, {
    steps: 15,
  });
  await page.mouse.up();
}

/** Drags a palette type or a data panel name, by its test id, to the point of `target` that is `fraction` of its height down. */
function dragType(page: Page, testId: string, target: Locator, fraction = 0.5) {
  return dragFrom(page, page.getByTestId(testId), target, fraction);
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
    await dragType(page, "studio-palette-section", detail, 0.75);

    // A section without a widget does not compile, so the canvas draws it as a placeholder with a drop area.
    const list = page.locator(
      '[data-testid="studio-canvas-list"][data-list="/widgets/1/widgets"]',
    );
    await expect(list).toBeVisible();
    await dragType(page, "studio-palette-table", list);

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

  test("an author binds a table to the Ticket entity with the keyboard, and the canvas draws its columns", async ({
    page,
  }) => {
    await useSession(page, "hoa");
    await page.goto(
      `${server.url}/studio?app=ServiceDesk&resource=${ticketDetailId}`,
    );
    await page.getByRole("tab", { name: "Design", exact: true }).click();
    await expect(
      page.locator(
        '[data-testid="studio-canvas-widget"][data-path="/widgets/0"]',
      ),
    ).toBeVisible();

    await page.getByTestId("studio-data-entity-Ticket").focus();
    await page.keyboard.press("Enter");
    const choice = page.getByTestId("studio-binding-choice");
    await expect(choice).toBeVisible();
    await choice.getByTestId("studio-binding-table").focus();
    await page.keyboard.press("Enter");

    await expect(page.getByTestId("studio-palette-announcement")).toContainText(
      "Inserted table",
    );
    const table = page.locator(
      '[data-testid="studio-canvas-widget"][data-path="/widgets/1"]',
    );
    await expect(
      table.getByRole("columnheader", { name: "Title", exact: true }),
    ).toBeVisible();
  });

  test("an author drags a grouped data source onto the canvas, picks a chart and saves the bound chart", async ({
    page,
  }) => {
    // A tall window keeps the data panel and the drop areas in view together, away from the edges where a drag scrolls.
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
    await dragType(
      page,
      "studio-data-dataSource-OpenTicketsByCategory",
      detail,
      0.75,
    );
    await page.getByTestId("studio-binding-chart").click();
    await expect(page.getByTestId("studio-palette-announcement")).toContainText(
      "Inserted chart",
    );
    await page.getByRole("button", { name: "Save", exact: true }).click();

    const file = path.join(copy, "pages", "ticket-detail.json");
    try {
      await expect
        .poll(async () => JSON.parse(await readFile(file, "utf8")).widgets[1])
        .toEqual({
          type: "chart",
          dataSource: "OpenTicketsByCategory",
          kind: "bar",
          category: "category",
          measures: [{ name: "openCount", label: { textKey: "" } }],
          title: { textKey: "" },
        });
    } finally {
      // The saved chart has empty text keys and does not compile. The page metadata route answers no page while any
      // file has an error, so the next journeys, which share this folder, would find no canvas.
      await cp(path.join(serviceDesk, "pages", "ticket-detail.json"), file);
    }
  });

  test("an author drags the Dashboard's table onto the chart in the second column, saves, and finds it there", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 1000 });
    await useSession(page, "hoa");
    await page.goto(
      `${server.url}/studio?app=ServiceDesk&resource=${dashboardId}`,
    );
    await page.getByRole("tab", { name: "Design", exact: true }).click();

    const canvas = page.getByTestId("studio-page-canvas");
    const frame = (pointer: string) =>
      canvas.locator(
        `[data-testid="studio-canvas-widget"][data-path="${pointer}"]`,
      );
    const table = frame("/widgets/1");
    const chart = frame("/widgets/0/columns/1/widgets/0");
    await expect(table).toBeVisible();
    await expect(chart).toBeVisible();
    await dragFrom(page, table, chart, 0.75);

    await expect(page.getByTestId("studio-palette-announcement")).toContainText(
      "Moved table",
    );
    await page.getByRole("button", { name: "Save", exact: true }).click();

    const file = path.join(copy, "pages", "dashboard.json");
    try {
      await expect
        .poll(async () => {
          const { widgets } = JSON.parse(await readFile(file, "utf8"));
          return [widgets.length, widgets[0].columns[1].widgets[1]];
        })
        .toEqual([1, { type: "table", dataSource: "AllTicketList" }]);
    } finally {
      await cp(path.join(serviceDesk, "pages", "dashboard.json"), file);
    }
  });

  test("an author drops a section, saves, undoes the drop and saves again", async ({
    page,
  }) => {
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
    await dragType(page, "studio-palette-section", detail, 0.75);
    const save = page.getByRole("button", { name: "Save", exact: true });
    await save.click();
    await expect(page.getByTestId("studio-saved")).toBeVisible();

    const file = path.join(copy, "pages", "ticket-detail.json");
    const original = JSON.parse(
      await readFile(
        path.join(serviceDesk, "pages", "ticket-detail.json"),
        "utf8",
      ),
    );
    try {
      await expect
        .poll(async () => JSON.parse(await readFile(file, "utf8")).widgets)
        .toHaveLength(original.widgets.length + 1);

      await page.getByRole("button", { name: "Undo", exact: true }).click();
      await expect(save).toBeEnabled();
      await save.click();

      await expect
        .poll(async () => JSON.parse(await readFile(file, "utf8")))
        .toEqual(original);
    } finally {
      // The saved empty section does not compile, so the file goes back even when the test fails half way.
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

  test("an author drags the Resolution field from the palette onto a section of the triage form's outline, and the JSON tab shows it there", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 1000 });
    await useSession(page, "hoa");
    await page.goto(
      `${server.url}/studio?app=ServiceDesk&resource=${ticketTriageId}`,
    );
    await page.getByRole("tab", { name: "Design", exact: true }).click();

    // The ticket entity has a Resolution field that the triage form leaves out.
    await expect(
      page.getByTestId("studio-form-palette-resolution"),
    ).toBeVisible();
    // The outline's section rows hold their field rows, so the section's own middle is a row of a field. The first
    // line of the row is the section itself.
    const section = page.locator(
      '[data-testid="studio-outline-section"][data-path="/sections/1"]',
    );
    await expect(section).toBeVisible();
    await dragField(page, "resolution", section.locator("> span").first());

    await expect(
      page.getByTestId("studio-form-palette-announcement"),
    ).toContainText("Inserted resolution");
    await expect(
      page.getByTestId("studio-form-palette-resolution"),
    ).toHaveCount(0);

    await page.getByRole("tab", { name: "JSON", exact: true }).click();
    const json = page.getByRole("textbox", { name: "JSON", exact: true });
    const draft = JSON.parse(await json.inputValue());
    expect(draft.sections[1].fields.at(-1)).toEqual({ field: "resolution" });
  });

  test("an author drags the triage form's number field onto the category field by pointer, and one undo restores both sections", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 1000 });
    await useSession(page, "hoa");
    await page.goto(
      `${server.url}/studio?app=ServiceDesk&resource=${ticketTriageId}`,
    );
    await page.getByRole("tab", { name: "Design", exact: true }).click();

    const row = (path: string) =>
      page.locator(`[data-testid="studio-outline-field"][data-path="${path}"]`);
    await expect(row("/sections/1/fields/0")).toBeVisible();
    await dragPointer(
      page,
      row("/sections/0/fields/0"),
      row("/sections/1/fields/0"),
    );

    await expect(row("/sections/1/fields/0")).toHaveAttribute(
      "aria-current",
      "true",
    );
    await expect(
      page.getByTestId("studio-form-palette-announcement"),
    ).toContainText("Moved number · Section 2 · position 1");

    const fieldsOf = async () => {
      await page.getByRole("tab", { name: "JSON", exact: true }).click();
      const json = page.getByRole("textbox", { name: "JSON", exact: true });
      const draft = JSON.parse(await json.inputValue());
      return draft.sections.map((section: { fields: { field: string }[] }) =>
        section.fields.map((field) => field.field),
      );
    };
    const original = [
      ["number", "title", "description", "requester", "status"],
      ["category", "priority", "assignedAgent"],
    ];
    expect(await fieldsOf()).toEqual([
      ["title", "description", "requester", "status"],
      ["number", "category", "priority", "assignedAgent"],
    ]);
    const json = page.getByRole("textbox", { name: "JSON", exact: true });
    expect(JSON.parse(await json.inputValue()).sections[1].fields[0]).toEqual({
      field: "number",
      readOnly: true,
    });

    await page.getByRole("button", { name: "Undo", exact: true }).click();
    expect(await fieldsOf()).toEqual(original);
  });

  test("an author drags the ticket edit form's problem section onto the second step, and one undo restores the draft", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 1000 });
    await useSession(page, "hoa");
    await page.goto(
      `${server.url}/studio?app=ServiceDesk&resource=${ticketEditId}`,
    );
    await page.getByRole("tab", { name: "Design", exact: true }).click();

    const section = page.locator(
      '[data-testid="studio-outline-section"][data-path="/sections/0"]',
    );
    const step = page.locator(
      '[data-testid="studio-outline-step"][data-path="/steps/1"]',
    );
    await expect(step).toBeVisible();
    await dragPointer(page, section.locator("> span").first(), step);

    await expect(
      page.getByTestId("studio-form-palette-announcement"),
    ).toContainText("Moved problem");

    const layout = async () => {
      await page.getByRole("tab", { name: "JSON", exact: true }).click();
      const json = page.getByRole("textbox", { name: "JSON", exact: true });
      const draft = JSON.parse(await json.inputValue());
      return {
        sections: draft.sections.map((item: { name: string }) => item.name),
        steps: draft.steps.map((item: { sections: string[] }) => item.sections),
      };
    };
    expect(await layout()).toEqual({
      sections: ["details", "problem"],
      steps: [[], ["details", "problem"]],
    });

    await page.getByRole("button", { name: "Undo", exact: true }).click();
    expect(await layout()).toEqual({
      sections: ["problem", "details"],
      steps: [["problem"], ["details"]],
    });
  });

  test("an author renames the ticket edit form's problem section, and the first step follows", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 1000 });
    await useSession(page, "hoa");
    await page.goto(
      `${server.url}/studio?app=ServiceDesk&resource=${ticketEditId}`,
    );
    await page.getByRole("tab", { name: "Design", exact: true }).click();

    await page
      .locator('[data-testid="studio-outline-section"][data-path="/sections/0"]')
      .locator("> span")
      .first()
      .click();
    const name = page.getByTestId("studio-design-section-name");
    await name.fill("issue");
    await name.press("Enter");

    await page.getByRole("tab", { name: "JSON", exact: true }).click();
    const json = page.getByRole("textbox", { name: "JSON", exact: true });
    const draft = JSON.parse(await json.inputValue());
    expect(draft.sections[0].name).toBe("issue");
    expect(draft.steps[0].sections).toEqual(["issue"]);
  });

  test("an author moves the triage form's status field to the second section with the keyboard, and the live region says where it landed", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 1000 });
    await useSession(page, "hoa");
    await page.goto(
      `${server.url}/studio?app=ServiceDesk&resource=${ticketTriageId}`,
    );
    await page.getByRole("tab", { name: "Design", exact: true }).click();

    const status = page.locator(
      '[data-testid="studio-outline-field"][data-path="/sections/0/fields/4"]',
    );
    await expect(status).toContainText("status");
    await status.focus();
    await page.keyboard.press("Space");
    await page.keyboard.press("ArrowDown");
    // The first field of the second section, not the section row, is the target: it shows the drop outline.
    await expect(
      page.locator(
        '[data-testid="studio-outline-field"][data-path="/sections/1/fields/0"]',
      ),
    ).toHaveCSS("outline-width", "2px");
    await page.keyboard.press("Space");

    await expect(
      page.getByTestId("studio-form-palette-announcement"),
    ).toContainText("Moved status · Section 2 · position 1");

    await page.getByRole("tab", { name: "JSON", exact: true }).click();
    const json = page.getByRole("textbox", { name: "JSON", exact: true });
    const draft = JSON.parse(await json.inputValue());
    expect(draft.sections[1].fields[0]).toEqual({
      field: "status",
      readOnly: true,
    });
    expect(draft.sections[0].fields).toHaveLength(4);
  });

  test("an author removes the triage form's priority field, sees it back in the palette, and one undo restores it", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 1000 });
    await useSession(page, "hoa");
    await page.goto(
      `${server.url}/studio?app=ServiceDesk&resource=${ticketTriageId}`,
    );
    await page.getByRole("tab", { name: "Design", exact: true }).click();

    await page
      .locator(
        '[data-testid="studio-outline-field"][data-path="/sections/1/fields/1"]',
      )
      .click();
    await page
      .getByRole("button", { name: "Remove field", exact: true })
      .click();

    await expect(
      page.getByTestId("studio-form-palette-priority"),
    ).toBeVisible();
    await expect(
      page.getByTestId("studio-form-palette-announcement"),
    ).toContainText("Removed priority · Section 2");
    await expect(
      page.locator(
        '[data-testid="studio-outline-section"][data-path="/sections/1"]',
      ),
    ).toHaveAttribute("aria-current", "true");

    await page.getByRole("button", { name: "Undo", exact: true }).click();
    await expect(page.getByTestId("studio-form-palette-priority")).toHaveCount(
      0,
    );

    await page.getByRole("tab", { name: "JSON", exact: true }).click();
    const json = page.getByRole("textbox", { name: "JSON", exact: true });
    const draft = JSON.parse(await json.inputValue());
    expect(
      draft.sections[1].fields.map((field: { field: string }) => field.field),
    ).toEqual(["category", "priority", "assignedAgent"]);
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

  test("an author adds a section to a one-section form, fills both from the palette, saves, and finds both in the file", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 1000 });
    await useSession(page, "hoa");
    const file = path.join(copy, "forms", "ticket-resolution.json");
    const original = await readFile(file, "utf8");
    try {
      const form = JSON.parse(original);
      form.sections = [{ ...form.sections[0], fields: [{ field: "number" }] }];
      await writeFile(file, JSON.stringify(form, null, 2));

      await page.goto(
        `${server.url}/studio?app=ServiceDesk&resource=${ticketResolutionId}`,
      );
      await page.getByRole("tab", { name: "Design", exact: true }).click();

      await page.getByTestId("studio-form-palette-title").click();
      await page.getByTestId("studio-form-add-section").click();
      await expect(
        page.getByTestId("studio-outline-empty-section"),
      ).toBeVisible();
      await page.getByTestId("studio-form-palette-category").click();
      await page.getByTestId("studio-form-palette-priority").click();
      await page.getByRole("button", { name: "Save", exact: true }).click();
      await expect(page.getByTestId("studio-saved")).toBeVisible();

      await expect
        .poll(async () => JSON.parse(await readFile(file, "utf8")).sections)
        .toEqual([
          {
            ...form.sections[0],
            fields: [{ field: "number" }, { field: "title" }],
          },
          {
            name: "section1",
            title: { textKey: "forms.ticketResolution.sections.1.title" },
            fields: [{ field: "category" }, { field: "priority" }],
          },
        ]);
    } finally {
      await writeFile(file, original);
    }
  });
});
