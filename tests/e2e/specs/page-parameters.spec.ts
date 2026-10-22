import { expect, test, type APIRequestContext } from "@playwright/test";
import { csrfHeaders } from "../csrf";
import { sessionFile } from "../sessions";

// The record API needs a session with a grant, so these journeys start signed in as the employee anna.
test.use({ storageState: sessionFile("anna") });

async function createRecord(
  request: APIRequestContext,
  entity: string,
  values: Record<string, unknown>,
) {
  const response = await request.post(
    `/api/apps/E2eApp/entities/${entity}/records`,
    {
      data: { values },
      headers: csrfHeaders,
    },
  );
  expect(response.status()).toBe(201);
  return (await response.json()) as { id: string };
}

test("a page without its required parameter, or with a malformed one, shows not-found inside the site shell", async ({
  page,
}) => {
  for (const address of [
    "/e2e/categorynotes",
    "/e2e/categorynotes?category=nope",
  ]) {
    await page.goto(address);

    await expect(page.getByTestId("not-found")).toBeVisible();
    await expect(page.getByRole("menuitem", { name: "Notes" })).toBeVisible();
  }
});

test("a table shows the rows of the category named in the address", async ({
  page,
  request,
}) => {
  const run = `${Date.now()}-${test.info().retry}`;
  const category = await createRecord(request, "Category", {
    name: `Bound ${run}`,
  });
  const other = await createRecord(request, "Category", {
    name: `Other ${run}`,
  });
  await createRecord(request, "Note", {
    title: `In category ${run}`,
    category: category.id,
  });
  await createRecord(request, "Note", {
    title: `Elsewhere ${run}`,
    category: other.id,
  });

  await page.goto(`/e2e/categorynotes?category=${category.id}`);

  await expect(
    page.getByRole("row", { name: new RegExp(`In category ${run}`) }),
  ).toBeVisible();
  await expect(
    page.getByRole("row", { name: new RegExp(`Elsewhere ${run}`) }),
  ).toHaveCount(0);
  await expect(page.getByTestId("filter-bar")).toHaveCount(0);
});
