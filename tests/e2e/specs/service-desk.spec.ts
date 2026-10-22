import {
  expect,
  test,
  type APIRequest,
  type APIRequestContext,
  type Page,
} from "@playwright/test";
import { csrfHeaders } from "../csrf";
import { expectChosen } from "../select";
import { sessionFile, useSession, type SampleUser } from "../sessions";

// The service desk sample runs on the same platform as the other samples, with no platform code of its
// own. Specs run in parallel against one database, so each test looks only for the tickets it creates.
// Every ticket gets its own title, which is its display field, so the title finds its task. anna and lan
// are requesters, linh leads the service desk, and khoa and minh are agents in the scenarios. Only khoa
// is an agent in the development realm.
const appPath = "/api/apps/ServiceDesk";
const recordsPath = `${appPath}/entities/Ticket/records`;
const tasksPath = `${appPath}/tasks`;

/** A unique part for the titles of this run, so retries and parallel specs never share a title. */
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
  await row.getByRole("link", { name: label }).click();
}

test("anna submits a ticket, linh triages it to khoa, khoa resolves it, and anna confirms it closed", async ({
  page,
  playwright,
  baseURL,
}) => {
  const title = `Zz Close ${runId()}`;
  const id = await asUser(playwright.request, baseURL, "anna", (anna) =>
    createTicket(anna, title),
  );

  await useSession(page, "anna");
  await page.goto(`/service-desk/ticketform/${id}`);
  await expect(page.getByLabel("Title", { exact: true })).toHaveValue(title);
  await page.getByRole("button", { name: "Submit" }).click();
  await expect(page.getByTestId("form-started")).toBeVisible();

  await useSession(page, "linh");
  await openTask(page, title, "Triage ticket");
  await expect(page.getByLabel("Title", { exact: true })).toHaveValue(title);
  await page.getByLabel("Assigned agent", { exact: true }).fill("khoa");
  await page.getByRole("button", { name: "Assign" }).click();
  await expect(page).toHaveURL(/\/service-desk\/mytasks$/);

  await useSession(page, "khoa");
  await openTask(page, title, "Resolve ticket");
  await page.getByLabel("Resolution", { exact: true }).fill("Replaced the battery.");
  await page.getByRole("button", { name: "Resolve" }).click();
  await expect(page).toHaveURL(/\/service-desk\/mytasks$/);

  await useSession(page, "anna");
  await openTask(page, title, "Confirm resolution");
  await expect(page.getByLabel("Resolution", { exact: true })).toHaveValue(
    "Replaced the battery.",
  );
  await page.getByRole("button", { name: "Confirm" }).click();
  await expect(page).toHaveURL(/\/service-desk\/mytasks$/);

  // The worker ends the process some time after the decision.
  await expect(async () => {
    await page.goto(`/service-desk/ticketform/${id}`);
    await expectChosen(page.getByRole('combobox', { name: 'Status', exact: true }), "closed", { timeout: 1_000 });
  }).toPass({ timeout: 30_000 });
});

test("an agent cannot open a ticket that is assigned to another agent, by its form or the API", async ({
  page,
  playwright,
  baseURL,
}) => {
  const id = await asUser(playwright.request, baseURL, "anna", async (anna) => {
    const created = await createTicket(anna, `Zz Other agent ${runId()}`);
    await start(anna, created);
    return created;
  });
  const triage = await taskId(playwright.request, baseURL, "linh", id);
  await asUser(playwright.request, baseURL, "linh", async (linh) => {
    const read = await linh.get(`${recordsPath}/${id}`);
    expect(read.status()).toBe(200);
    const { version } = (await read.json()) as { version: number };
    const completed = await linh.post(`${tasksPath}/${triage}/complete`, {
      data: { outcome: "assign", values: { assignedAgent: "minh" }, version },
      headers: csrfHeaders,
    });
    expect(completed.status()).toBe(200);
  });
  // The worker moves the ticket to assigned some time after the decision.
  await asUser(playwright.request, baseURL, "linh", async (linh) => {
    await expect(async () => {
      const read = await linh.get(`${recordsPath}/${id}`);
      const { values } = (await read.json()) as {
        values: { status: string };
      };
      expect(values.status).toBe("assigned");
    }).toPass({ timeout: 30_000 });
  });
  await useSession(page, "khoa");

  await page.goto(`/service-desk/ticketform/${id}`);
  await expect(page.getByTestId("not-found")).toBeVisible();
  await expect(page.getByLabel("Title", { exact: true })).toHaveCount(0);

  expect((await page.request.get(`${recordsPath}/${id}`)).status()).toBe(404);
});
