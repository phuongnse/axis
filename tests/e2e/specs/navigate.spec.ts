import { expect, test } from "../test";
import { csrfHeaders } from "../csrf";
import { sessionFile } from "../sessions";

// The record API needs a session with a grant, so this journey starts signed in as the employee requester.
test.use({ storageState: sessionFile("requester") });

test("the title link opens a note, and saving returns to the table with the edited row", async ({
  page,
  request,
}) => {
  // Specs run in parallel against one database, so the note has a title of its own. It sorts first.
  const title = `Aaa navigate ${Date.now()}-${test.info().retry}`;
  const created = await request.post("/api/apps/E2eApp/entities/Note/records", {
    data: { values: { title } },
    headers: csrfHeaders,
  });
  expect(created.status()).toBe(201);
  const { id } = (await created.json()) as { id: string };

  await page.goto("/e2e/noterows");
  await page
    .getByRole("row", { name: new RegExp(title) })
    .getByRole("link", { name: title, exact: true })
    .click();

  await expect(page).toHaveURL(new RegExp(`/e2e/noteform/${id}$`));
  await page.getByRole("textbox", { name: "Title", exact: true }).fill(`${title} edited`);
  await page.getByRole("button", { name: "Save", exact: true }).click();

  await expect(page).toHaveURL(/\/e2e\/noterows$/);
  await expect(
    page.getByRole("row", { name: new RegExp(`${title} edited`) }),
  ).toBeVisible();
});
