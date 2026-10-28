import { expect, test } from "../test";
import type { APIRequest, APIRequestContext, Page } from "@playwright/test";
import { csrfHeaders } from "../csrf";
import { sessionFile, useSession, type SampleUser } from "../sessions";
import { expectShown, fieldValue } from "../fields";
import { logInAtKeycloak } from "../keycloak";

// Each journey checks what a user sees in the browser, then sends the same request straight to the
// API, so a hidden button never stands in for the server's check. No session is set for the whole
// file, because the signed-out journeys need none.
//
// Specs run in parallel against one database, so each test looks only for the requests it creates.
// The manager and employee journeys create drafts, and the task and finance journeys use Operations
// requests, so that the per-department test keeps the Engineering total fixed.
const appPath = "/api/apps/PurchaseRequests";
const recordsPath = `${appPath}/entities/PurchaseRequest/records`;
const tasksPath = `${appPath}/tasks`;
const listAddress = "/purchasing/purchaserequests?pageSize=100&sort=-title";

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

/** The id of the seeded department named `name`, read from the record API. */
async function departmentId(request: APIRequestContext, name: string) {
  const response = await request.get(
    `${appPath}/entities/Department/records?pageSize=100`,
  );
  expect(response.status()).toBe(200);
  const body = (await response.json()) as {
    items: { id: string; values: { name: string } }[];
  };
  const department = body.items.find((item) => item.values.name === name);
  expect(department).toBeDefined();
  return department!.id;
}

/** Creates a draft request in `department` with one line of `unitPrice`, and returns its id. */
async function createRequest(
  request: APIRequestContext,
  title: string,
  department: string,
  unitPrice = 500,
) {
  const response = await request.post(recordsPath, {
    data: {
      values: {
        title,
        status: "draft",
        department: await departmentId(request, department),
        lineItems: [{ description: "Item", quantity: 1, unitPrice }],
      },
    },
    headers: csrfHeaders,
  });
  expect(response.status()).toBe(201);
  return ((await response.json()) as { id: string }).id;
}

/** Starts the approval of the request `id`. The worker submits it some time later. */
async function start(request: APIRequestContext, id: string) {
  const response = await request.post(
    `${appPath}/processes/PurchaseRequestApproval/instances`,
    {
      data: { subjectId: id },
      headers: csrfHeaders,
    },
  );
  expect(response.status()).toBe(201);
}

/** The ids of the rows the request list reads for the signed-in user. */
async function listedIds(request: APIRequestContext) {
  const response = await request.get(
    `${appPath}/data-sources/PurchaseRequestList/rows?pageSize=100&sort=-title`,
  );
  expect(response.status()).toBe(200);
  const body = (await response.json()) as { items: { id: string }[] };
  return body.items.map((item) => item.id);
}

/** The table row that shows `title`. */
function row(page: Page, title: string) {
  return page.getByRole("row", { name: new RegExp(`${title} `) });
}

/** Creates and starts an Operations request as requester, and returns its id. */
async function startOperationsRequest(
  apiRequest: APIRequest,
  baseURL: string | undefined,
  title: string,
) {
  return asUser(apiRequest, baseURL, "requester", async (requester) => {
    const created = await createRequest(requester, title, "Operations");
    await start(requester, created);
    return created;
  });
}

/**
 * The id of the manager approval task for the Operations request `id`. manager-2 manages Operations, so
 * the worker assigns the task to manager-2. It waits until the worker has created the task.
 */
async function managerTaskId(
  apiRequest: APIRequest,
  baseURL: string | undefined,
  id: string,
) {
  return asUser(apiRequest, baseURL, "manager-2", async (manager2) => {
    let found = "";
    await expect(async () => {
      const response = await manager2.get(`${tasksPath}?pageSize=100`);
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

/** Signs in as `user` on the Keycloak login page. */
async function logIn(page: Page, user: SampleUser) {
  await expect(page.locator("#username")).toBeVisible();
  await logInAtKeycloak(page, user);
}

test("an employee does not see another employee's request in the list or by its address", async ({
  page,
  playwright,
  baseURL,
}) => {
  const run = runId();
  const employeeTitle = `Zz Deny ${run} employee`;
  const annaTitle = `Zz Deny ${run} requester`;
  const employeeId = await asUser(playwright.request, baseURL, "employee", (employee) =>
    createRequest(employee, employeeTitle, "Operations"),
  );
  const annaId = await asUser(playwright.request, baseURL, "requester", (requester) =>
    createRequest(requester, annaTitle, "Operations"),
  );
  await useSession(page, "requester");

  // With sort=-title, requester's request sorts right after employee's, so a missing row is not on another page.
  await page.goto(listAddress);
  await expect(row(page, annaTitle)).toBeVisible();
  await expect(row(page, employeeTitle)).toHaveCount(0);
  await page.goto(`/purchasing/purchaserequestform/${employeeId}`);
  await expect(page.getByTestId("not-found")).toBeVisible();
  await expect(page.getByRole("textbox", { name: /Title/ })).toHaveCount(0);

  expect((await page.request.get(`${recordsPath}/${employeeId}`)).status()).toBe(
    404,
  );
  const ids = await listedIds(page.request);
  expect(ids).toContain(annaId);
  expect(ids).not.toContain(employeeId);
});

test("a department manager does not see another department's request in the list or by its address", async ({
  page,
  playwright,
  baseURL,
}) => {
  const run = runId();
  const engineeringTitle = `Zz Scope ${run} Engineering`;
  const operationsTitle = `Zz Scope ${run} Operations`;
  const { engineeringId, operationsId } = await asUser(
    playwright.request,
    baseURL,
    "requester",
    async (requester) => ({
      engineeringId: await createRequest(requester, engineeringTitle, "Engineering"),
      operationsId: await createRequest(requester, operationsTitle, "Operations"),
    }),
  );
  // manager manages Engineering. manager-2 manages Operations.
  await useSession(page, "manager");

  await page.goto(listAddress);
  await expect(row(page, engineeringTitle)).toBeVisible();
  await expect(row(page, operationsTitle)).toHaveCount(0);
  await page.goto(`/purchasing/purchaserequestform/${operationsId}`);
  await expect(page.getByTestId("not-found")).toBeVisible();

  expect(
    (await page.request.get(`${recordsPath}/${engineeringId}`)).status(),
  ).toBe(200);
  expect(
    (await page.request.get(`${recordsPath}/${operationsId}`)).status(),
  ).toBe(404);
  const ids = await listedIds(page.request);
  expect(ids).toContain(engineeringId);
  expect(ids).not.toContain(operationsId);
});

test("an employee who opens a manager's task cannot see or complete it, and the task stays open", async ({
  page,
  playwright,
  baseURL,
}) => {
  const title = `Zz Task ${runId()}`;
  const id = await startOperationsRequest(playwright.request, baseURL, title);
  const taskId = await managerTaskId(playwright.request, baseURL, id);
  await useSession(page, "requester");

  await page.goto(`/purchasing/mytasks/${taskId}`);
  await expect(
    page.getByText("This task is assigned to someone else.", { exact: true }),
  ).toBeVisible();
  await expect(page.getByRole("button", { name: /Approve/ })).toHaveCount(0);
  const inboxLoad = page.waitForResponse(
    (response) => new URL(response.url()).pathname === tasksPath,
  );
  await page.goto("/purchasing/mytasks?pageSize=100");
  expect((await inboxLoad).status()).toBe(200);
  // The inbox has rendered its answer once the table stops loading.
  await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
  await expect(page.getByText(new RegExp(title))).toHaveCount(0);

  expect((await page.request.get(`${tasksPath}/${taskId}`)).status()).toBe(403);
  const completed = await page.request.post(`${tasksPath}/${taskId}/complete`, {
    data: { outcome: "approve" },
    headers: csrfHeaders,
  });
  expect(completed.status()).toBe(403);
  await asUser(playwright.request, baseURL, "manager-2", async (manager2) => {
    const read = await manager2.get(`${tasksPath}/${taskId}`);
    expect(read.status()).toBe(200);
    expect(((await read.json()) as { state: string }).state).toBe("open");
  });
});

test("a finance reviewer sees a submitted request read-only, and an update through the API is refused", async ({
  page,
  playwright,
  baseURL,
}) => {
  const title = `Zz Finance ${runId()}`;
  const id = await asUser(playwright.request, baseURL, "requester", async (requester) => {
    const created = await createRequest(requester, title, "Operations");
    await start(requester, created);
    // The worker submits the request some time after the start. finance-reviewer reads only submitted requests.
    await expect
      .poll(
        async () => {
          const read = await requester.get(`${recordsPath}/${created}`);
          return ((await read.json()) as { values: { status: string } }).values
            .status;
        },
        { timeout: 30_000 },
      )
      .toBe("submitted");
    return created;
  });
  await useSession(page, "finance-reviewer");

  await page.goto(`/purchasing/purchaserequestform/${id}`);
  await expectShown(fieldValue(page, "title"), title);
  await expect(page.getByRole("textbox", { name: "Title", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Cancel", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: /Save/ })).toHaveCount(0);
  await expect(page.getByRole("button", { name: /Submit/ })).toHaveCount(0);

  const read = await page.request.get(`${recordsPath}/${id}`);
  expect(read.status()).toBe(200);
  const { version } = (await read.json()) as { version: number };
  const patched = await page.request.patch(`${recordsPath}/${id}`, {
    data: { version, values: { title: "Changed" } },
    headers: csrfHeaders,
  });
  expect(patched.status()).toBe(403);
  const after = await page.request.get(`${recordsPath}/${id}`);
  expect(
    ((await after.json()) as { values: { title: string } }).values.title,
  ).toBe(title);
});

/**
 * The sample pages that read records. Each opens with no session, and `user` signs in. The request
 * id is that of an Engineering draft by requester, which requester and its manager may read.
 */
const signedOutPages: {
  name: string;
  address: (id: string) => string;
  user: SampleUser;
  api: (id: string) => string;
  expectation: (page: Page, title: string) => Promise<void>;
}[] = [
  {
    name: "the request list",
    address: () => listAddress,
    user: "requester",
    api: () =>
      `${appPath}/data-sources/PurchaseRequestList/rows?pageSize=100&sort=-title`,
    expectation: (page, title) => expect(row(page, title)).toBeVisible(),
  },
  {
    name: "the request form",
    address: (id) => `/purchasing/purchaserequestform/${id}`,
    user: "requester",
    api: (id) => `${recordsPath}/${id}`,
    expectation: (page, title) =>
      expect(page.getByRole("textbox", { name: "Title", exact: true })).toHaveValue(title),
  },
  {
    // An employee may not read this data source, so its department manager signs in.
    name: "Requests by department",
    address: () => "/purchasing/requestsbydepartment",
    user: "manager",
    api: () => `${appPath}/data-sources/RequestsByDepartment/rows?pageSize=100`,
    expectation: (page) =>
      expect(page.getByRole("row", { name: /^Engineering / })).toBeVisible(),
  },
  {
    name: "Departments",
    address: () => "/purchasing/departments",
    user: "requester",
    api: () => `${appPath}/entities/Department/records?pageSize=100`,
    expectation: (page) =>
      expect(page.getByRole("row", { name: /Engineering/ })).toBeVisible(),
  },
  {
    name: "Suppliers",
    address: () => "/purchasing/suppliers",
    user: "requester",
    api: () => `${appPath}/entities/Supplier/records?pageSize=100`,
    expectation: (page) =>
      expect(page.getByRole("row", { name: /Acme Supplies/ })).toBeVisible(),
  },
];

/**
 * Opens `address` while signed out and checks that the sign-in prompt shows and no sign-in request
 * goes out. Then it clicks **Sign in** on the prompt and logs in as `user` on the Keycloak login page.
 */
async function signInFromPrompt(page: Page, address: string, user: SampleUser) {
  const signInRequests: string[] = [];
  page.on("request", (request) => {
    const { pathname } = new URL(request.url());
    if (pathname === "/api/auth/sign-in") {
      signInRequests.push(request.url());
    }
  });

  await page.goto(address);
  const prompt = page.getByTestId("sign-in-prompt");
  await expect(prompt).toBeVisible();
  expect(signInRequests).toEqual([]);

  await prompt.getByRole("button", { name: "Sign in", exact: true }).click();
  await logIn(page, user);
}

for (const entry of signedOutPages) {
  // Each test has its own browser context, so no Keycloak session carries over from another sign-in.
  test(`a signed-out person who opens ${entry.name} signs in from the prompt on the Keycloak login page and lands back on it`, async ({
    page,
    request,
    playwright,
    baseURL,
  }) => {
    const title = `Zz Signed out ${runId()}`;
    const id = await asUser(playwright.request, baseURL, "requester", (requester) =>
      createRequest(requester, title, "Engineering"),
    );
    const address = entry.address(id);

    // The request fixture holds no session either.
    expect((await request.get(entry.api(id))).status()).toBe(401);

    await signInFromPrompt(page, address, entry.user);

    await page.waitForURL(`${baseURL}${address}`);
    await entry.expectation(page, title);
  });
}

test("a signed-out person who opens My tasks signs in from the prompt on the Keycloak login page and lands back on it", async ({
  page,
  request,
  baseURL,
}) => {
  const address = "/purchasing/mytasks?pageSize=100";

  // The request fixture holds no session either.
  expect((await request.get(`${tasksPath}?pageSize=100`)).status()).toBe(401);

  await signInFromPrompt(page, address, "manager-2");

  await page.waitForURL(`${baseURL}${address}`);
  await expect(page.getByRole("columnheader", { name: "Task", exact: true })).toBeVisible();
});

test("a signed-out person who opens a task's address signs in from the prompt on the Keycloak login page and lands back on it", async ({
  page,
  request,
  playwright,
  baseURL,
}) => {
  const title = `Zz Signed out task ${runId()}`;
  const id = await startOperationsRequest(playwright.request, baseURL, title);
  const taskId = await managerTaskId(playwright.request, baseURL, id);
  const address = `/purchasing/mytasks/${taskId}`;

  expect((await request.get(`${tasksPath}/${taskId}`)).status()).toBe(401);

  await signInFromPrompt(page, address, "manager-2");

  await page.waitForURL(`${baseURL}${address}`);
  await expect(page.getByRole("button", { name: "Approve", exact: true })).toBeVisible();
});
