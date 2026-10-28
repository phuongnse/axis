import { expect, test } from "../test";
import type { APIRequest, APIRequestContext, Page } from "@playwright/test";
import { csrfHeaders } from "../csrf";
import { waitForDialog } from "../dialogs";
import { expectShown, fieldLabel, fieldValue } from "../fields";
import { expectChosen, pickOption } from "../select";
import { sessionFile, useSession, type SampleUser } from "../sessions";

// The service desk sample runs on the same platform as the other samples, with no platform code of its
// own. Specs run in parallel against one database, so each test looks only for the tickets it creates.
// Every ticket gets its own title, which is its display field, so the title finds its task. requester and employee
// are requesters, lead and lead-2 lead the service desk, and agent and approver are agents in the scenarios. Only
// agent is an agent in the development realm.
const appPath = "/api/apps/ServiceDesk";
const recordsPath = `${appPath}/entities/Ticket/records`;
const tasksPath = `${appPath}/tasks`;

/** A unique part for the titles of this run, so runs and parallel specs never share a title. */
function runId() {
  return `${Date.now()}-${test.info().retry}`;
}

/** Runs `body` with an API context that holds `user`'s saved session, and disposes the context after. */
async function asUser<T>(
  apiRequest: APIRequest,
  baseURL: string | undefined,
  user: SampleUser,
  body: (request: APIRequestContext) => Promise<T>,
) {
  // The context shares no cookies with the browser or the request fixture.
  const request = await apiRequest.newContext({
    baseURL,
    storageState: sessionFile(user),
  });
  try {
    return await body(request);
  } finally {
    await request.dispose();
  }
}

/** Creates a draft ticket and returns its id. */
async function createTicket(request: APIRequestContext, title: string) {
  const response = await request.post(recordsPath, {
    data: {
      values: {
        title,
        description: "It shows a black screen.",
        status: "draft",
      },
    },
    headers: csrfHeaders,
  });
  expect(response.status()).toBe(201);
  return ((await response.json()) as { id: string }).id;
}

/** Starts the handling of the ticket `id`. */
async function start(request: APIRequestContext, id: string) {
  const response = await request.post(
    `${appPath}/processes/TicketHandling/instances`,
    { data: { subjectId: id }, headers: csrfHeaders },
  );
  expect(response.status()).toBe(201);
}

/** The id of the task for the ticket `id` in `user`'s task list. It waits until the worker creates it. */
async function taskId(
  apiRequest: APIRequest,
  baseURL: string | undefined,
  user: SampleUser,
  id: string,
) {
  return asUser(apiRequest, baseURL, user, async (request) => {
    let found = "";
    await expect(async () => {
      const response = await request.get(`${tasksPath}?pageSize=100`);
      expect(response.status()).toBe(200);
      const { items } = (await response.json()) as {
        items: { id: string; subject: { id: string } }[];
      };
      const task = items.find((item) => item.subject.id === id);
      expect(task).toBeDefined();
      found = task!.id;
    }).toPass({ timeout: 30_000 });
    return found;
  });
}


/** Opens the task `label` of the ticket `title` in My tasks. It waits until the worker creates the task. */
async function openTask(page: Page, title: string, label: string) {
  const row = page.getByRole("row", { name: new RegExp(title) });
  await expect(async () => {
    await page.goto("/service-desk/mytasks?pageSize=100");
    await expect(row).toBeVisible({ timeout: 1_000 });
  }).toPass({ timeout: 30_000 });
  await row.getByRole("link", { name: label, exact: true }).click();
}

test("requester submits a ticket, lead triages it to agent, agent resolves it, and requester confirms it closed", async ({
  page,
  playwright,
  baseURL,
}) => {
  const title = `Zz Close ${runId()}`;
  const id = await asUser(playwright.request, baseURL, "requester", (requester) =>
    createTicket(requester, title),
  );

  await useSession(page, "requester");
  await page.goto(`/service-desk/ticketform/${id}`);
  await expect(page.getByLabel(fieldLabel("Title"))).toHaveValue(title);
  // Submit shows on the last step only.
  await page.getByRole("button", { name: "Next", exact: true }).click();
  await page.getByRole("button", { name: "Submit", exact: true }).click();
  await expect(page.getByTestId("form-started")).toBeVisible();

  await useSession(page, "lead");
  await openTask(page, title, "Triage ticket");
  await expectShown(fieldValue(page, "title"), title);
  await page.getByLabel(fieldLabel("Assigned agent")).fill("agent");
  await page.getByRole("button", { name: "Assign", exact: true }).click();
  await expect(page).toHaveURL(/\/service-desk\/mytasks$/);

  await useSession(page, "agent");
  await openTask(page, title, "Resolve ticket");
  await page.getByLabel(fieldLabel("Resolution")).fill("Replaced the battery.");
  await page.getByRole("button", { name: "Resolve", exact: true }).click();
  await expect(page).toHaveURL(/\/service-desk\/mytasks$/);

  await useSession(page, "requester");
  await openTask(page, title, "Confirm resolution");
  await expectShown(fieldValue(page, "resolution"), "Replaced the battery.");
  await page.getByRole("button", { name: "Confirm", exact: true }).click();
  await expect(page).toHaveURL(/\/service-desk\/mytasks$/);

  // The worker ends the process some time after the decision.
  await expect(async () => {
    await page.goto(`/service-desk/ticketform/${id}`);
    await page.getByRole("button", { name: "Next", exact: true }).click();
    await expectChosen(page.getByRole('combobox', { name: 'Status', exact: true }), "closed", { timeout: 1_000 });
  }).toPass({ timeout: 30_000 });
});

test("lead claims a triage task so lead-2 no longer lists it, then reassigns it to agent by name", async ({
  page,
  playwright,
  baseURL,
}) => {
  const title = `Zz Reassign ${runId()}`;
  const id = await asUser(playwright.request, baseURL, "requester", async (requester) => {
    const created = await createTicket(requester, title);
    await start(requester, created);
    return created;
  });
  // Both leads list the triage task until one of them claims it.
  await taskId(playwright.request, baseURL, "lead-2", id);

  await useSession(page, "lead");
  await openTask(page, title, "Triage ticket");
  await page.getByRole("button", { name: "Claim", exact: true }).click();
  await expect(page.getByRole("button", { name: "Release", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Reassign", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Claim", exact: true })).toHaveCount(0);

  // The claim hides the task from the other lead.
  await useSession(page, "lead-2");
  const listed = page.waitForResponse(
    (response) =>
      response.url().includes(tasksPath) && response.request().method() === "GET",
  );
  await page.goto("/service-desk/mytasks?pageSize=100");
  expect((await listed).status()).toBe(200);
  await expect(page.getByRole("row", { name: new RegExp(title) })).toHaveCount(0);
  await asUser(playwright.request, baseURL, "lead-2", async (lead2) => {
    const response = await lead2.get(`${tasksPath}?pageSize=100`);
    expect(response.status()).toBe(200);
    const { items } = (await response.json()) as {
      items: { subject: { id: string } }[];
    };
    expect(items.map((item) => item.subject.id)).not.toContain(id);
  });

  await useSession(page, "lead");
  const row = page.getByRole("row", { name: new RegExp(title) });
  await expect(async () => {
    await page.goto("/service-desk/mytasks?pageSize=100");
    await expect(row).toBeVisible({ timeout: 1_000 });
  }).toPass({ timeout: 30_000 });
  await expect(row.getByText("Claimed by you", { exact: true })).toBeVisible();
  await row.getByRole("link", { name: "Triage ticket", exact: true }).click();
  await page.getByRole("button", { name: "Reassign", exact: true }).click();

  const dialog = await waitForDialog(page);
  await dialog.getByRole("radio", { name: "Person", exact: true }).check();
  await pickOption(dialog.getByRole("combobox"), "Agent User", "Agent");
  await dialog.getByLabel(fieldLabel("Reason")).fill("The agent knows this printer.");
  // The page has a button with the same name, so the click stays in the dialog.
  await dialog.getByRole("button", { name: "Reassign", exact: true }).click();
  await expect(page).toHaveURL(/\/service-desk\/mytasks$/);

  // The triage form does not load for agent, whose ticket grant hides a ticket with no agent. So agent only finds the task.
  await useSession(page, "agent");
  await expect(async () => {
    await page.goto("/service-desk/mytasks?pageSize=100");
    await expect(
      page
        .getByRole("row", { name: new RegExp(title) })
        .getByRole("link", { name: "Triage ticket", exact: true }),
    ).toBeVisible({ timeout: 1_000 });
  }).toPass({ timeout: 30_000 });
});

test("an agent cannot open a ticket that is assigned to another agent, by its form or the API", async ({
  page,
  playwright,
  baseURL,
}) => {
  const id = await asUser(playwright.request, baseURL, "requester", async (requester) => {
    const created = await createTicket(requester, `Zz Other agent ${runId()}`);
    await start(requester, created);
    return created;
  });
  const triage = await taskId(playwright.request, baseURL, "lead", id);
  await asUser(playwright.request, baseURL, "lead", async (lead) => {
    const read = await lead.get(`${recordsPath}/${id}`);
    expect(read.status()).toBe(200);
    const { version } = (await read.json()) as { version: number };
    const completed = await lead.post(`${tasksPath}/${triage}/complete`, {
      data: { outcome: "assign", values: { assignedAgent: "approver" }, version },
      headers: csrfHeaders,
    });
    expect(completed.status()).toBe(200);
  });
  // The worker moves the ticket to assigned some time after the decision.
  await asUser(playwright.request, baseURL, "lead", async (lead) => {
    await expect(async () => {
      const read = await lead.get(`${recordsPath}/${id}`);
      const { values } = (await read.json()) as {
        values: { status: string };
      };
      expect(values.status).toBe("assigned");
    }).toPass({ timeout: 30_000 });
  });
  await useSession(page, "agent");

  await page.goto(`/service-desk/ticketform/${id}`);
  await expect(page.getByTestId("not-found")).toBeVisible();
  await expect(page.getByLabel(/Title/i)).toHaveCount(0);

  expect((await page.request.get(`${recordsPath}/${id}`)).status()).toBe(404);
});

test("the ticket form guides the requester through two steps", async ({ page }) => {
  await useSession(page, "requester");
  await page.goto("/service-desk/ticketform/new");
  await expect(page.getByLabel(fieldLabel("Title"))).toBeVisible();
  await expect(page.getByLabel(fieldLabel("Description"))).toBeVisible();
  await expect(page.getByRole("combobox", { name: "Status", exact: true })).toHaveCount(0);
  const steps = page.getByTestId("form-steps");
  await expect(steps).toContainText("Describe the problem");
  await expect(steps).toContainText("Add details");

  await page.getByLabel(fieldLabel("Title")).fill(`Zz Steps ${runId()}`);
  await page.getByRole("button", { name: "Next", exact: true }).click();
  for (const name of ["Category", "Priority", "Status"]) {
    const control = page.getByLabel(fieldLabel(name));
    await expect(control).toBeVisible();
    await expect(control).toBeEnabled();
  }
  await expect(page.getByLabel(fieldLabel("Title"))).toHaveCount(0);
});

test("the ticket form shows the resolution, marked required, only while the status is resolved", async ({ page }) => {
  await useSession(page, "requester");
  await page.goto("/service-desk/ticketform/new");
  await page.getByLabel(fieldLabel("Title")).fill(`Zz Resolution ${runId()}`);
  await page.getByRole("button", { name: "Next", exact: true }).click();

  const status = page.getByRole("combobox", { name: "Status", exact: true });
  // The requester cannot write resolution, so the form shows it as plain text. Its label has no input to point to.
  const resolution = fieldValue(page, "resolution");
  const requiredMark = page.locator("label", { hasText: "Resolution" }).locator('span[aria-hidden="true"]').last();
  await pickOption(status, "draft");
  await expect(resolution).toHaveCount(0);
  await pickOption(status, "resolved");
  await expect(resolution).toBeVisible();
  await expect(requiredMark).toHaveText("*");
  await pickOption(status, "draft");
  await expect(resolution).toHaveCount(0);
});

test("the service desk lead sees the open and urgent tiles and the chart by category on the Dashboard", async ({
  page,
  playwright,
  baseURL,
}) => {
  // The lead's navigation has four entries, which the default window would fold into the overflow menu.
  await page.setViewportSize({ width: 1440, height: 900 });
  // One open ticket, so the tile and the chart have something to count.
  await asUser(playwright.request, baseURL, "requester", async (requester) => {
    await start(requester, await createTicket(requester, `Zz Dashboard ${runId()}`));
  });

  await useSession(page, "lead");
  await page.goto("/service-desk/dashboard");

  await expect(page.getByRole("menuitem", { name: "Dashboard", exact: true })).toBeVisible();
  // Specs create tickets in parallel, so the numbers change from run to run. The test checks that they load.
  const tiles = page.getByTestId("kpi-tile");
  await expect(tiles).toHaveCount(2);
  await expect(tiles.nth(0)).toContainText("Open tickets");
  await expect(tiles.nth(1)).toContainText("Urgent tickets");
  await expect(page.getByTestId("kpi-value")).toHaveCount(2);
  await expect(page.getByTestId("kpi-error")).toHaveCount(0);
  await expect(page.getByTestId("chart-widget")).toContainText("Open tickets by category");
  await expect(page.getByTestId("chart-error")).toHaveCount(0);

  // The link wraps the tile.
  await page.getByRole("link", { name: /Show all tickets/ }).click();
  await expect(page).toHaveURL(/\/service-desk\/alltickets$/);
});

test("a requester has no Dashboard entry, and the page endpoint answers 403", async ({ page }) => {
  await useSession(page, "requester");
  await page.goto("/service-desk/mytickets");
  await expect(page.getByRole("menuitem", { name: "My tickets", exact: true })).toBeVisible();
  await expect(page.getByRole("menuitem", { name: "Dashboard", exact: true })).toHaveCount(0);

  const site = await page.request.get("/api/sites/service-desk");
  expect(site.status()).toBe(200);
  const { navigation } = (await site.json()) as { navigation: { page: string }[] };
  expect(navigation.map((entry) => entry.page)).not.toContain("Dashboard");

  expect((await page.request.get("/api/sites/service-desk/pages/Dashboard")).status()).toBe(403);
});

test("requester presses View on their ticket in My tickets and sees it read-only on the detail page", async ({
  page,
  playwright,
  baseURL,
}) => {
  const title = `Zz View ${runId()}`;
  const id = await asUser(playwright.request, baseURL, "requester", (requester) =>
    createTicket(requester, title),
  );

  await useSession(page, "requester");
  await page.goto("/service-desk/mytickets?pageSize=100");
  await page
    .getByRole("row", { name: new RegExp(title) })
    .getByRole("button", { name: "View", exact: true })
    .click();

  await expect(page).toHaveURL(`/service-desk/ticketdetail?ticket=${id}`);
  await expect(page.getByTestId("detail")).toContainText(title);
  await expect(page.getByRole("textbox")).toHaveCount(0);
});

test("an agent opening the detail page of a ticket that is not assigned to them gets the not-found page", async ({
  page,
  playwright,
  baseURL,
}) => {
  // The draft ticket is never started, so no agent is assigned to it.
  const id = await asUser(playwright.request, baseURL, "requester", (requester) =>
    createTicket(requester, `Zz Unassigned ${runId()}`),
  );

  await useSession(page, "agent");
  await page.goto(`/service-desk/ticketdetail?ticket=${id}`);

  await expect(page.getByTestId("not-found")).toBeVisible();
  await expect(page.getByTestId("detail")).toHaveCount(0);
});
