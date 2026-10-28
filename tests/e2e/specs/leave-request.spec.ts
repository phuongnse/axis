import { expect, test } from "../test";
import type { APIRequest, APIRequestContext } from "@playwright/test";
import { csrfHeaders } from "../csrf";
import { expectShown, fieldLabel, fieldValue } from "../fields";
import { expectChosen } from "../select";
import { sessionFile, useSession, type SampleUser } from "../sessions";

// The leave request sample runs on the same platform as the purchase request sample, with no platform
// code of its own. Each journey checks what a user sees in the browser, then sends the same request
// straight to the API, so a hidden button never stands in for the server's check.
//
// Specs run in parallel against one database, so each test looks only for the requests it creates. Every
// request gets its own reason, which is its display field, so the reason finds its task. requester and lan are employees. chau manages
// Operations and binh manages Engineering.
const appPath = "/api/apps/LeaveRequests";
const recordsPath = `${appPath}/entities/LeaveRequest/records`;
const tasksPath = `${appPath}/tasks`;

/** A unique part for the reasons of this run, so runs and parallel specs never share a reason. */
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

/** The id of the seeded department named `name`, read from the record API of the departments module. */
async function departmentId(request: APIRequestContext, name: string) {
  const response = await request.get(
    `${appPath}/entities/departments/Department/records?pageSize=100`,
  );
  expect(response.status()).toBe(200);
  const body = (await response.json()) as {
    items: { id: string; values: { name: string } }[];
  };
  const department = body.items.find((item) => item.values.name === name);
  expect(department).toBeDefined();
  return department!.id;
}

/** Creates a draft Operations request with the given days, and returns its id. */
async function createRequest(
  request: APIRequestContext,
  reason: string,
  firstDay = "2026-11-02",
  lastDay = "2026-11-04",
) {
  const response = await request.post(recordsPath, {
    data: {
      values: {
        department: await departmentId(request, "Operations"),
        leaveType: "annual",
        firstDay,
        lastDay,
        reason,
        status: "draft",
      },
    },
    headers: csrfHeaders,
  });
  expect(response.status()).toBe(201);
  return ((await response.json()) as { id: string }).id;
}

/** Starts the approval of the request `id`. */
async function start(request: APIRequestContext, id: string) {
  const response = await request.post(
    `${appPath}/processes/LeaveRequestApproval/instances`,
    { data: { subjectId: id }, headers: csrfHeaders },
  );
  expect(response.status()).toBe(201);
}

/** The id of the task for the request `id` in `user`'s task list. It waits until the worker creates it. */
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

test("requester submits a leave request, chau approves it in My tasks, and requester sees it approved with its days", async ({
  page,
  playwright,
  baseURL,
}) => {
  const reason = `Zz Approve ${runId()}`;
  const id = await asUser(playwright.request, baseURL, "requester", (requester) =>
    createRequest(requester, reason),
  );

  await useSession(page, "requester");
  await page.goto(`/leave/leaverequestform/${id}`);
  await expect(page.getByLabel(fieldLabel("Reason"))).toHaveValue(reason);
  await page.getByRole("button", { name: "Submit", exact: true }).click();
  await expect(page.getByTestId("form-started")).toBeVisible();

  await useSession(page, "chau");
  const row = page.getByRole("row", { name: new RegExp(reason) });
  // The worker creates the task some time after the start.
  await expect(async () => {
    await page.goto("/leave/mytasks?pageSize=100");
    await expect(row).toBeVisible({ timeout: 1_000 });
  }).toPass({ timeout: 30_000 });
  await row.getByRole("link", { name: "Manager approval", exact: true }).click();
  await expectShown(fieldValue(page, "reason"), reason);
  await page.getByRole("button", { name: "Approve", exact: true }).click();
  await expect(page).toHaveURL(/\/leave\/mytasks$/);

  await useSession(page, "requester");
  // The worker ends the process some time after the decision.
  await expect(async () => {
    await page.goto(`/leave/leaverequestform/${id}`);
    await expectChosen(page.getByRole('combobox', { name: 'Status', exact: true }), "approved", { timeout: 1_000 });
  }).toPass({ timeout: 30_000 });
  await expectShown(fieldValue(page, "days"), "3");
});

test("an employee cannot open another employee's leave request, by its form or the API", async ({
  page,
  playwright,
  baseURL,
}) => {
  const id = await asUser(playwright.request, baseURL, "requester", (requester) =>
    createRequest(requester, `Zz Deny ${runId()}`),
  );
  await useSession(page, "lan");

  await page.goto(`/leave/leaverequestform/${id}`);
  await expect(page.getByTestId("not-found")).toBeVisible();
  await expect(page.getByLabel(/Reason/i)).toHaveCount(0);

  expect((await page.request.get(`${recordsPath}/${id}`)).status()).toBe(404);
});

test("a manager of another department gets no task for the request, and cannot complete another manager's task", async ({
  page,
  playwright,
  baseURL,
}) => {
  const id = await asUser(playwright.request, baseURL, "requester", async (requester) => {
    const created = await createRequest(requester, `Zz Scope ${runId()}`);
    await start(requester, created);
    return created;
  });
  // chau manages Operations, so the task is chau's. It exists once the worker has created it.
  const chauTask = await taskId(playwright.request, baseURL, "chau", id);
  await useSession(page, "binh");

  const tasks = await page.request.get(`${tasksPath}?pageSize=100`);
  expect(tasks.status()).toBe(200);
  const { items } = (await tasks.json()) as {
    items: { subject: { id: string } }[];
  };
  expect(items.filter((item) => item.subject.id === id)).toEqual([]);

  const completed = await page.request.post(
    `${tasksPath}/${chauTask}/complete`,
    { data: { outcome: "approve" }, headers: csrfHeaders },
  );
  expect(completed.status()).toBe(403);
  await asUser(playwright.request, baseURL, "chau", async (chau) => {
    const read = await chau.get(`${tasksPath}/${chauTask}`);
    expect(read.status()).toBe(200);
    expect(((await read.json()) as { state: string }).state).toBe("open");
  });
});

test("a request whose last day is before its first day cannot be submitted, and shows the start condition's message", async ({
  page,
  playwright,
  baseURL,
}) => {
  const id = await asUser(playwright.request, baseURL, "requester", (requester) =>
    createRequest(requester, `Zz Dates ${runId()}`, "2026-11-04", "2026-11-02"),
  );
  await useSession(page, "requester");

  await page.goto(`/leave/leaverequestform/${id}`);
  await expectChosen(page.getByRole('combobox', { name: 'Status', exact: true }), "draft");
  const started = page.waitForResponse((response) =>
    response.url().endsWith("/processes/LeaveRequestApproval/instances"),
  );
  await page.getByRole("button", { name: "Submit", exact: true }).click();

  expect((await started).status()).toBe(400);
  await expect(page.getByTestId("form-error")).toHaveText(
    "Only a draft or returned request whose last day is not before its first day can be submitted.",
  );
  await expect(page.getByTestId("form-started")).toHaveCount(0);
});
