import { expect, test } from "../test";
import { csrfHeaders } from "../csrf";
import { sessionFile } from "../sessions";

// The record API needs a session with a grant, so this journey starts signed in as the employee anna.
test.use({
  storageState: sessionFile("anna"),
  viewport: { width: 390, height: 844 },
});

test("at phone width, the title link opens the row and the Actions menu opens by keyboard and runs an action", async ({
  page,
  request,
}) => {
  // Specs run in parallel against one database, so the note has a title of its own. It sorts first.
  const title = `Aaa phone ${Date.now()}-${test.info().retry}`;
  const created = await request.post("/api/apps/E2eApp/entities/Note/records", {
    data: { values: { title } },
    headers: csrfHeaders,
  });
  expect(created.status()).toBe(201);
  const { id } = (await created.json()) as { id: string };

  await page.goto("/e2e/noterows");
  const row = page.getByRole("row", { name: new RegExp(title) });
  await expect(row.getByRole("button")).toHaveCount(1);

  // The title link opens the row, so focus it and let the next Tab reach the Actions button.
  await row.getByRole("link", { name: title, exact: true }).focus();
  await page.keyboard.press("Tab");
  await expect(row.getByRole("button", { name: "Actions", exact: true })).toBeFocused();

  await page.keyboard.press("Enter");
  // The menu moves focus to its first entry once it has opened.
  const edit = page.getByRole("menuitem", { name: "Edit", exact: true });
  await expect(edit).toBeFocused();
  await page.keyboard.press("ArrowDown");
  await page.keyboard.press("Enter");

  await expect(page).toHaveURL(new RegExp(`/e2e/noteform/${id}$`));
});
