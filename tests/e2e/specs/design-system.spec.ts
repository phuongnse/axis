import { expect, test } from "../test";
import type { FrameLocator, Locator, Page } from "@playwright/test";
import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { waitForDialog } from "../dialogs";
import { cellValue, expectShown, fieldLabel, fieldValue } from "../fields";
import { closeEntityField, openEntityField } from "../studio-resources";
import { cardGroups, cardName } from "../../../web/src/designSystem/cardOrder";
import {
  bundleFile,
  bundleUrl,
  figure,
  harnessUrl,
  openHarness,
  origin,
  pickState,
  repositoryRoot,
} from "../design-system";
import { contrast } from "../../../web/src/test/contrast";
import { designTokensFor } from "../../../web/src/platform/designTokens";

const productionBuild = path.join(
  repositoryRoot,
  "src",
  "Axis.Server",
  "wwwroot",
);
// The name of the fixture application in web/src/designSystem/fixtures.ts.
const fixtureMarker = "DesignSystemSample";

const modes = ["light", "dark"] as const;

// Each preview and a locator for its widget. The table and the kpi also draw an empty state, so they are looked up in
// the "With data" figure. A field preview's first state is its editable one.
const previews: Record<string, (page: Page | FrameLocator) => Locator> = {
  table: (page) =>
    figure(page, "With data").getByRole("columnheader", {
      name: "Title",
      exact: true,
    }),
  form: (page) =>
    page.getByRole("button", { name: "Next", exact: true }).first(),
  taskInbox: (page) =>
    page.getByRole("link", { name: "Review the request", exact: true }).first(),
  processInspector: (page) =>
    page.getByRole("button", { name: "Filter", exact: true }).first(),
  detail: (page) =>
    page.getByText("Standing desks for the new office", { exact: true }),
  "chart-bar": (page) =>
    page
      .getByRole("img", { name: "Requests by status", exact: true })
      .locator("svg"),
  "chart-line": (page) =>
    page
      .getByRole("img", { name: "Requests by status", exact: true })
      .locator("svg"),
  "chart-pie": (page) =>
    page
      .getByRole("img", { name: "Requests by status", exact: true })
      .locator("svg"),
  kpi: (page) => figure(page, "With data").getByTestId("kpi-value"),
  columns: (page) => page.getByRole("region", { name: "Tasks", exact: true }),
  tabs: (page) => page.getByRole("tab", { name: "Requests", exact: true }),
  section: (page) => page.getByRole("region", { name: "Tasks", exact: true }),
  states: (page) =>
    page
      .getByRole("alert")
      .filter({ hasText: "The records could not be loaded." }),
  statusTag: (page) =>
    page
      .getByTestId("status-tag-surface")
      .getByText("neutral", { exact: true }),
  "field-text": (page) =>
    figure(page, "Editable").getByLabel(fieldLabel("Title")),
  "field-integer": (page) =>
    figure(page, "Editable").getByLabel(fieldLabel("Quantity")),
  "field-decimal": (page) =>
    figure(page, "Editable").getByLabel(fieldLabel("Amount")),
  "field-boolean": (page) =>
    figure(page, "Editable").getByLabel(fieldLabel("Urgent")),
  "field-date": (page) =>
    figure(page, "Editable").getByLabel(fieldLabel("Needed on")),
  "field-date-time": (page) =>
    figure(page, "Editable").getByLabel(fieldLabel("Submitted at")),
  "field-enum": (page) =>
    figure(page, "Editable").getByLabel(fieldLabel("Status")),
  "field-reference": (page) =>
    figure(page, "Editable").getByLabel(fieldLabel("Category")),
  "field-child-collection": (page) =>
    figure(page, "Reference and computed cells").getByRole("columnheader", {
      name: "Description",
      exact: true,
    }),
  shell: (page) => figure(page, "Application site").getByRole("menu"),
  signIn: (page) =>
    figure(page, "Sign-in refused").getByRole("button", {
      name: "Sign in",
      exact: true,
    }),
  identitySignIn: (page) =>
    figure(page, "Sign in").getByLabel("Email or username", { exact: true }),
  studio: (page) =>
    figure(page, "Typical").getByRole("heading", {
      name: "Purchase requests",
      exact: true,
      level: 1,
    }),
  // The page canvas draws the table's columns from the page metadata that the fixtures answer.
  studioPageDesign: (page) =>
    figure(page, "Typical")
      .getByTestId("studio-page-canvas")
      .getByRole("columnheader", { name: "Title", exact: true }),
  studioFormDesign: (page) =>
    figure(page, "Typical")
      .getByTestId("studio-form-canvas")
      .getByLabel(fieldLabel("Title")),
  studioProcessDesign: (page) =>
    figure(page, "Typical")
      .getByTestId("studio-process-graph")
      .getByRole("button", { name: "review, task", exact: true }),
  signUp: (page) =>
    figure(page, "Typical").getByRole("textbox", {
      name: "Full name",
      exact: true,
    }),
};

/** A key element of each state of the shell, sign-in and identity sign-in previews, relative to the state's figure. */
const stateKeys: Record<string, Record<string, (state: Locator) => Locator>> = {
  shell: {
    "Application site": (state) => state.getByRole("menu"),
    "Navigation collapsed": (state) =>
      state.locator(
        'button[aria-controls="shell-sidebar"][aria-expanded="false"]',
      ),
    "Form page": (state) =>
      state.getByRole("heading", { name: "Request", exact: true, level: 1 }),
    "Saved record": (state) =>
      state.getByRole("button", { name: "Submit for approval", exact: true }),
    "Saved record with a change": (state) => state.getByTestId("form-save-bar"),
    "Record not found": (state) => state.getByTestId("record-not-found"),
    Task: (state) =>
      state.getByRole("group", { name: "Decision", exact: true }),
    "Task not found": (state) => state.getByTestId("record-not-found"),
    "Platform site": (state) =>
      state.getByText(
        "Runs the company's purchases, from the request to approval and ordering.",
        { exact: true },
      ),
    "App switcher": (state) =>
      state
        .getByRole("menu", { name: "Switch to", exact: true })
        .getByRole("link", { name: "Purchase requests", exact: true })
        .and(state.locator('[aria-current="page"]')),
    "Language and theme": (state) =>
      state.getByRole("menuitemradio", { name: "System", exact: true }),
    "Account page": (state) =>
      state.getByRole("button", {
        name: "End all other sessions",
        exact: true,
      }),
    "Account notifications": (state) =>
      state.getByRole("switch", {
        name: "Tasks: In the application",
        exact: true,
      }),
  },
  signIn: {
    "Sign-in refused": (state) => state.getByTestId("sign-in-failed"),
    "Signed out": (state) =>
      state.getByRole("button", { name: "Sign in again", exact: true }),
    "Session ended": (state) => state.getByTestId("sign-in-prompt"),
  },
  identitySignIn: {
    "Sign in": (state) =>
      state.getByLabel("Email or username", { exact: true }),
    "Wrong password": (state) =>
      state.getByText("Invalid email, username or password.", { exact: true }),
    "Fields left empty": (state) =>
      state.getByText("Enter your password.", { exact: true }),
    "Organization sign-in only": (state) =>
      state.getByRole("button", {
        name: "Continue with Contoso ID",
        exact: true,
      }),
    "Two-step verification": (state) =>
      state.getByText("Verification code", { exact: true }),
    "Forgot password": (state) =>
      state.getByRole("button", { name: "Send link", exact: true }),
    "Set password": (state) =>
      state.getByLabel("Confirm password", { exact: true }),
    "Add an authenticator": (state) =>
      state.getByText("Device name", { exact: true }),
    "Choose how to sign in": (state) =>
      state.getByText("Authenticator app", { exact: true }),
    "Update your profile": (state) =>
      state.getByLabel("First name", { exact: true }),
    "Terms and privacy": (state) =>
      state.getByRole("link", { name: "Privacy policy", exact: true }),
    "Terms not accepted": (state) =>
      state.getByText("Accept the terms to continue.", { exact: true }),
    "Sign out": (state) =>
      state.getByRole("button", { name: "Sign out", exact: true }),
    Notice: (state) =>
      state.getByRole("link", { name: "Back to application", exact: true }),
    Error: (state) =>
      state.getByText("The link is no longer valid.", { exact: true }),
    "Language and theme": (state) =>
      state.getByRole("menuitemradio", { name: "System", exact: true }),
  },
};

/** The text a preview's empty state shows, or none for a preview that draws no empty state. */
function emptyTextOf(name: string): string | undefined {
  if (name === "table") {
    return "No records yet.";
  }
  if (name === "taskInbox") {
    return "No open tasks.";
  }
  if (name === "detail") {
    // The shared table's empty text for the empty Lines table.
    return "No records yet.";
  }
  if (name === "kpi") {
    // The dash is what the eye sees. The words are visually hidden for assistive technology.
    return "—";
  }
  if (name.startsWith("chart-")) {
    return "No data to show";
  }
  return undefined;
}

/** Files of a folder with their paths relative to it, or none when the folder does not exist. */
function filesUnder(folder: string): string[] {
  try {
    return readdirSync(folder, { recursive: true, withFileTypes: true })
      .filter((entry) => entry.isFile())
      .map((entry) =>
        path.relative(folder, path.join(entry.parentPath, entry.name)),
      );
  } catch {
    return [];
  }
}

for (const [name, widget] of Object.entries(previews)) {
  for (const mode of modes) {
    test(`the ${name} preview mounts its widget in a 992px frame sandboxed without allow-forms in ${mode} mode with no error`, async ({
      page,
    }) => {
      const query = `?preview=${name}&mode=${mode}`;
      const recording = await openHarness(page, query, {
        sandboxed: true,
        frameWidth: 992,
      });
      const frame = page.frameLocator("iframe");

      await expect(widget(frame)).toBeVisible();
      if (name === "states") {
        // The loading state's request never finishes, so its table stays busy.
        await expect(
          figure(frame, "Loading").locator('[aria-busy="true"]').first(),
        ).toBeVisible();
      }
      const emptyText = emptyTextOf(name);
      if (emptyText !== undefined) {
        await expect(
          figure(frame, "Empty")
            .getByText(emptyText, { exact: true })
            .filter({ visible: true })
            .first(),
        ).toBeVisible();
      }
      const withData = figure(frame, "With data");
      if (name === "table") {
        await expect(withData.getByRole("link").first()).toBeVisible();
        await expect(
          withData.getByRole("button", { name: "New request", exact: true }),
        ).toBeVisible();
        // The table folds the row actions into the "…" menu, so the delete action is not a button of the row.
        const many = figure(frame, "Many row actions");
        await expect(
          many.getByRole("button", { name: "Actions", exact: true }).first(),
        ).toBeVisible();
        await expect(
          many.getByRole("button", { name: "Delete", exact: true }),
        ).toHaveCount(0);
      }
      if (name === "taskInbox") {
        await expect(
          withData.getByText("Overdue", { exact: true }),
        ).toBeVisible();
        await expect(
          withData.getByText("Claimed by you", { exact: true }),
        ).toBeVisible();
      }
      if (name === "processInspector") {
        await expect(
          frame.getByRole("cell", { name: "Failed", exact: true }).first(),
        ).toBeVisible();
        await expect(
          frame.getByRole("button", { name: "Redeliver", exact: true }),
        ).toBeVisible();
        await expect(
          frame.getByRole("button", { name: "Discard", exact: true }),
        ).toBeVisible();
      }
      await page.waitForLoadState("networkidle");

      if (name.startsWith("field-")) {
        await expect(
          frame.getByRole("button", { name: "Save", exact: true }),
        ).toHaveCount(0);
        await expect(
          frame.getByRole("button", {
            name: "Submit for approval",
            exact: true,
          }),
        ).toHaveCount(0);
        // The history card's title is not an ARIA heading, so the test id and the text are checked.
        await expect(frame.getByTestId("record-history")).toHaveCount(0);
        await expect(frame.getByText("History", { exact: true })).toHaveCount(
          0,
        );
      }

      // A card never scrolls sideways at 992px.
      const preview = page.frame({
        url: (url) => url.pathname === "/design-system.html",
      })!;
      expect(
        await preview.evaluate(
          () =>
            document.documentElement.scrollWidth <=
            document.documentElement.clientWidth,
        ),
      ).toBe(true);

      expect(recording.errors).toEqual([]);
      // The fixtures answer every API call, so nothing but the page, the harness and the bundle is requested.
      expect(
        recording.requests.filter((url) => !url.startsWith("data:")),
      ).toEqual([
        `${origin}/sandboxed.html`,
        `${harnessUrl}${query}`,
        bundleUrl,
      ]);
    });
  }
}

for (const name of Object.keys(previews).filter(
  (key) => key.startsWith("field-") && key !== "field-child-collection",
)) {
  for (const mode of modes) {
    test(`the ${name} preview draws its read-only and required states in ${mode} mode`, async ({
      page,
    }) => {
      const recording = await openHarness(
        page,
        `?preview=${name}&mode=${mode}`,
      );

      await expect(
        figure(page, "Read-only").getByTestId(/^field-value-/),
      ).toBeVisible();
      await expect(figure(page, "Read-only").getByRole("textbox")).toHaveCount(
        0,
      );
      const required = figure(page, "Required, help text and error");
      // The required mark is a hidden element after the label text, so screen readers skip it.
      await expect(
        required
          .locator("label")
          .first()
          .locator('span[aria-hidden="true"]')
          .last(),
      ).toHaveText("*");
      await expect(
        required.getByText("A short note that explains the field.", {
          exact: true,
        }),
      ).toBeVisible();
      await expect(required.getByRole("alert")).toHaveText(
        "This value is not valid.",
      );
      expect(recording.errors).toEqual([]);
    });
  }
}

for (const mode of modes) {
  test(`the field-child-collection preview draws its states in ${mode} mode`, async ({
    page,
  }) => {
    const recording = await openHarness(
      page,
      `?preview=field-child-collection&mode=${mode}`,
    );

    const reference = figure(page, "Reference and computed cells");
    await expectShown(cellValue(reference, "lines", 0, "amount"), "1,700.00");
    await expect(
      reference
        .getByTestId("reference-lines-0-category")
        .getByRole("button", { name: "Choose… Category 1", exact: true }),
    ).toBeVisible();
    await expect(
      figure(page, "Cell error").getByTestId("field-error-lines-1-unitPrice"),
    ).toHaveText("Unit price cannot be negative.");
    const readOnly = figure(page, "Read-only");
    await expectShown(
      cellValue(readOnly, "lines", 0, "description"),
      "Standing desk",
    );
    await expect(readOnly.getByRole("textbox")).toHaveCount(0);
    await expect(
      readOnly.getByRole("button", { name: "Add row", exact: true }),
    ).toHaveCount(0);
    const empty = figure(page, "Empty");
    await expect(
      empty.getByRole("button", { name: "Add row", exact: true }),
    ).toBeVisible();
    await expect(empty.getByLabel(fieldLabel("Description 1"))).toHaveCount(0);
    expect(recording.errors).toEqual([]);
  });
}

for (const mode of modes) {
  test(`the form preview draws its validation, read-only, wizard, columns and many-steps states in ${mode} mode`, async ({
    page,
  }) => {
    const recording = await openHarness(page, `?preview=form&mode=${mode}`);

    await expect(
      figure(page, "Validation errors").getByTestId("field-error-title"),
    ).toHaveText("Enter a title.");
    const locked = figure(page, "Read-only by a condition");
    await expect(fieldValue(locked, "amount")).toBeVisible();
    await expect(locked.locator("input#amount")).toHaveCount(0);
    const wizard = figure(page, "Wizard steps");
    await expect(wizard.getByTestId("steps-bar")).toBeVisible();
    await wizard.getByRole("button", { name: "Next", exact: true }).click();
    await expect(wizard.locator("#quantity")).toBeVisible();
    // Fields of several widths share rows, and some of them show messages.
    const columns = figure(page, "Fields in columns, with errors");
    await expect(columns.getByTestId("field-error-title")).toHaveText(
      "Enter a title.",
    );
    await expect(columns.getByTestId("field-error-amount")).toHaveText(
      "This value is not valid.",
    );
    const top = async (field: string) =>
      (await columns.locator(`label[for="${field}"]`).boundingBox())!.y;
    expect(await top("title")).toBe(await top("status"));
    // A wizard with seven steps shows the number of each step.
    const many = figure(page, "Many steps");
    const steps = many.getByTestId("steps-bar");
    await expect(steps).toBeVisible();
    await expect(steps.locator("li")).toHaveCount(7);
    await expect(steps.getByText("2", { exact: true })).toBeVisible();
    expect(recording.errors).toEqual([]);
  });
}

test("the form preview validates and saves inside a frame sandboxed without allow-forms", async ({
  page,
}) => {
  const messages: string[] = [];
  page.on("console", (message) => messages.push(message.text()));
  const recording = await openHarness(page, "?preview=form&mode=light", {
    sandboxed: true,
  });
  const frame = page.frameLocator("iframe");

  const validation = figure(frame, "Validation errors");
  await expect(
    validation.getByRole("alert").filter({ hasText: "Enter a title." }),
  ).toBeVisible();
  await expect(
    validation
      .getByRole("alert")
      .filter({ hasText: "This value is not valid." }),
  ).toBeVisible();

  // The fixtures answer inside the bundle, so no request reaches the network. The test wraps fetch to see the save.
  const preview = page.frame({
    url: (url) => url.pathname === "/design-system.html",
  })!;
  await preview.evaluate(() => {
    const saves: string[] = [];
    const holder = window as unknown as { saves: string[] };
    holder.saves = saves;
    const original = window.fetch;
    window.fetch = (input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === "PATCH") {
        saves.push(String(input));
      }
      return original.call(window, input, init);
    };
  });
  // A saved record shows its save bar only once something changes, so Title is edited first.
  const locked = figure(frame, "Read-only by a condition");
  await locked.locator("input#title").fill("Standing desks, revised");
  await locked.getByRole("button", { name: "Save", exact: true }).click();

  await expect
    .poll(() =>
      preview.evaluate(() => (window as unknown as { saves: string[] }).saves),
    )
    .toEqual([expect.stringContaining("?form=LockedRequestForm")]);
  expect(
    messages.filter((text) => text.includes("Blocked form submission")),
  ).toEqual([]);
  expect(recording.errors).toEqual([]);
});

for (const [name, keys] of Object.entries(stateKeys)) {
  for (const mode of modes) {
    test(`the ${name} preview draws every state in a 992px frame sandboxed without allow-forms in ${mode} mode`, async ({
      page,
    }) => {
      const recording = await openHarness(
        page,
        `?preview=${name}&mode=${mode}`,
        {
          sandboxed: true,
          frameWidth: 992,
        },
      );
      const frame = page.frameLocator("iframe");

      for (const [caption, key] of Object.entries(keys)) {
        await pickState(frame, caption);
        await expect(key(figure(frame, caption))).toBeVisible();
      }
      expect(recording.errors).toEqual([]);
    });
  }
}

for (const [name, keys] of Object.entries(stateKeys)) {
  for (const mode of modes) {
    test(`the ${name} preview shows every state's caption above its frame at 992px in ${mode} mode`, async ({
      page,
    }) => {
      const recording = await openHarness(
        page,
        `?preview=${name}&mode=${mode}`,
        {
          sandboxed: true,
          frameWidth: 992,
        },
      );
      const frame = page.frameLocator("iframe");
      const captions = Object.keys(keys);

      for (const [caption, key] of Object.entries(keys)) {
        await pickState(frame, caption);
        await expect(key(figure(frame, caption))).toBeVisible();
      }
      const boxOf = async (locator: Locator) => {
        const box = await locator.boundingBox();
        if (!box) {
          throw new Error("A caption, frame or drawn page has no box");
        }
        return box;
      };
      const bottom = (box: { y: number; height: number }) => box.y + box.height;

      for (const caption of captions) {
        await pickState(frame, caption);
        await expect(frame.getByRole("figure")).toHaveCount(1);
        const state = figure(frame, caption);
        await expect(state.locator("figcaption")).toBeVisible();
        const captionBox = await boxOf(state.locator("figcaption"));
        const figureBox = await boxOf(state);
        const frameBox = await boxOf(state.locator(":scope > div"));
        const pageBox = await boxOf(
          state.locator(":scope > div > :first-child"),
        );

        expect(frameBox.y).toBeGreaterThanOrEqual(bottom(captionBox));
        expect(bottom(frameBox)).toBeLessThanOrEqual(bottom(figureBox));
        expect(pageBox.y).toBeGreaterThanOrEqual(frameBox.y);
        expect(bottom(pageBox)).toBeLessThanOrEqual(bottom(frameBox));
      }
      expect(recording.errors).toEqual([]);
    });
  }
}

for (const mode of modes) {
  test(`the identitySignIn preview's Sign in state shows the organization's heading in ${mode} mode`, async ({
    page,
  }) => {
    const recording = await openHarness(
      page,
      `?preview=identitySignIn&mode=${mode}`,
      {
        sandboxed: true,
        frameWidth: 992,
      },
    );
    const state = figure(page.frameLocator("iframe"), "Sign in");

    await expect(state.getByTestId("organization-heading")).toContainText(
      "Contoso",
    );
    expect(recording.errors).toEqual([]);
  });

  test(`the identitySignIn preview's Choose how to sign in state starts each method's name at the row's start in ${mode} mode`, async ({
    page,
  }) => {
    const recording = await openHarness(
      page,
      `?preview=identitySignIn&mode=${mode}`,
      {
        sandboxed: true,
        frameWidth: 992,
      },
    );
    await pickState(page.frameLocator("iframe"), "Choose how to sign in");
    const state = figure(page.frameLocator("iframe"), "Choose how to sign in");
    const insets: number[] = [];

    for (const label of ["Authenticator app", "Password"]) {
      const row = state.getByRole("button", { name: new RegExp(label) });
      const name = row.getByText(label, { exact: true });
      const rowBox = await row.boundingBox();
      const nameBox = await name.boundingBox();
      if (!rowBox || !nameBox) {
        throw new Error(`The ${label} row or its name has no box`);
      }
      insets.push(nameBox.x - rowBox.x);
    }

    const [authenticatorInset, passwordInset] = insets;
    expect(Math.abs(passwordInset - authenticatorInset)).toBeLessThanOrEqual(1);
    expect(recording.errors).toEqual([]);
  });

  test(`the identitySignIn preview's sign-in and terms states draw their messages and links in ${mode} mode`, async ({
    page,
  }) => {
    const query = `?preview=identitySignIn&mode=${mode}`;
    const recording = await openHarness(page, query, {
      sandboxed: true,
      frameWidth: 992,
    });
    const frame = page.frameLocator("iframe");

    await pickState(frame, "Fields left empty");
    const empty = figure(frame, "Fields left empty");
    await expect(
      empty.getByText("Enter your email or username.", { exact: true }),
    ).toBeVisible();
    await expect(
      empty.getByText("Enter your password.", { exact: true }),
    ).toBeVisible();

    await pickState(frame, "Terms and privacy");
    const terms = figure(frame, "Terms and privacy");
    await expect(
      terms.getByRole("link", { name: "Terms of service", exact: true }),
    ).toHaveAttribute("href", "#terms-of-service");
    await expect(
      terms.getByRole("link", { name: "Privacy policy", exact: true }),
    ).toHaveAttribute("href", "#privacy-policy");
    await expect(
      terms.getByText("Accept the terms to continue.", { exact: true }),
    ).toHaveCount(0);

    await pickState(frame, "Terms not accepted");
    await expect(
      figure(frame, "Terms not accepted").getByText(
        "Accept the terms to continue.",
        { exact: true },
      ),
    ).toBeVisible();
    expect(recording.errors).toEqual([]);
    // The fixtures answer every API call, so nothing but the page, the harness and the bundle is requested.
    expect(
      recording.requests.filter((url) => !url.startsWith("data:")),
    ).toEqual([`${origin}/sandboxed.html`, `${harnessUrl}${query}`, bundleUrl]);
  });

  test(`the signIn preview draws the Axis heading above the sign-in card in ${mode} mode`, async ({
    page,
  }) => {
    const recording = await openHarness(page, `?preview=signIn&mode=${mode}`, {
      sandboxed: true,
      frameWidth: 992,
    });
    const frame = page.frameLocator("iframe");

    for (const caption of ["Sign-in refused", "Signed out"]) {
      await pickState(frame, caption);
      const state = figure(frame, caption);
      const heading = state.getByTestId("organization-heading");
      await expect(heading).toContainText("Axis");
      const headingBox = await heading.boundingBox();
      const cardBox = await state.getByTestId("card-page-card").boundingBox();
      if (!headingBox || !cardBox) {
        throw new Error("The heading or the sign-in card has no box");
      }
      expect(headingBox.y + headingBox.height).toBeLessThanOrEqual(cardBox.y);
    }
    expect(recording.errors).toEqual([]);
  });
}

for (const mode of modes) {
  test(`the shell preview's Form page marks Requests in the sidebar in ${mode} mode`, async ({
    page,
  }) => {
    const recording = await openHarness(page, `?preview=shell&mode=${mode}`, {
      sandboxed: true,
      frameWidth: 992,
    });
    await pickState(page.frameLocator("iframe"), "Form page");
    const formPage = figure(page.frameLocator("iframe"), "Form page");

    await expect(
      formPage.getByRole("link", { name: "Requests", exact: true }),
    ).toHaveAttribute("aria-current", "page");
    await expect(
      formPage.getByRole("navigation", { name: "Breadcrumb", exact: true }),
    ).toHaveCount(0);
    expect(recording.errors).toEqual([]);
  });
}

for (const mode of modes) {
  test(`the shell preview's Platform site lays the home grid out in columns at 992px in ${mode} mode`, async ({
    page,
  }) => {
    const recording = await openHarness(page, `?preview=shell&mode=${mode}`, {
      sandboxed: true,
      frameWidth: 992,
    });
    await pickState(page.frameLocator("iframe"), "Platform site");
    const state = figure(page.frameLocator("iframe"), "Platform site");
    const boxOf = async (locator: Locator) => {
      const box = await locator.boundingBox();
      if (!box) {
        throw new Error("A home page tile or grid has no box");
      }
      return box;
    };

    const tools = ["Studio", "Operator console", "Users"];
    const boxes = [];
    for (const tool of tools) {
      const link = state
        .getByRole("main")
        .getByRole("link", { name: new RegExp(`^${tool}`) });
      await expect(link).toBeVisible();
      boxes.push(await boxOf(link));
    }
    expect(new Set(boxes.map((box) => box.y)).size).toBe(1);
    expect(new Set(boxes.map((box) => box.x)).size).toBe(3);

    const site = state.getByRole("link", { name: /^Service desk/ });
    const siteBox = await boxOf(site);
    const gridBox = await boxOf(site.locator("xpath=.."));
    expect(siteBox.width).toBeGreaterThan(gridBox.width - 2);

    const tile = state.getByRole("link", { name: /^Purchase requests/ });
    // The click that picked the state left the browser in pointer mode, where a focus shows no outline. A key press
    // first puts it back in keyboard mode, as a keyboard user's focus is.
    await page.keyboard.press("Shift");
    await tile.focus();
    const outline = await tile.evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        width: style.outlineWidth,
        offset: style.outlineOffset,
        style: style.outlineStyle,
      };
    });
    expect(outline).toEqual({ width: "2px", offset: "2px", style: "solid" });
    expect(recording.errors).toEqual([]);
  });
}

for (const mode of modes) {
  test(`the shell preview shows each state's footer inside its figure at 992px in ${mode} mode`, async ({
    page,
  }) => {
    const recording = await openHarness(page, `?preview=shell&mode=${mode}`, {
      sandboxed: true,
      frameWidth: 992,
    });
    const frame = page.frameLocator("iframe");

    for (const caption of Object.keys(stateKeys.shell)) {
      await pickState(frame, caption);
      // The harness wraps the previews in a main landmark, which takes the footer out of the contentinfo role.
      const footer = figure(frame, caption).locator("footer");
      await expect(footer).toBeVisible();
      // The page scrolls inside its frame, so the footer can lie below the frame's visible part.
      await footer.scrollIntoViewIfNeeded();
      const figureBox = await figure(frame, caption).boundingBox();
      const footerBox = await footer.boundingBox();
      if (!figureBox || !footerBox) {
        throw new Error(`The ${caption} figure or its footer has no box`);
      }
      expect(footerBox.y).toBeGreaterThanOrEqual(figureBox.y);
      expect(footerBox.y + footerBox.height).toBeLessThanOrEqual(
        figureBox.y + figureBox.height,
      );
    }
    expect(recording.errors).toEqual([]);
  });
}

// The captions of each framed preview's states. A preview with a State control draws one state at a time, so its
// states are picked there.
const framedPreviews: Record<
  string,
  { captions: readonly string[]; pickedInState: boolean }
> = {
  shell: { captions: Object.keys(stateKeys.shell), pickedInState: true },
  signIn: { captions: Object.keys(stateKeys.signIn), pickedInState: true },
  identitySignIn: {
    captions: Object.keys(stateKeys.identitySignIn),
    pickedInState: true,
  },
  signUp: { captions: ["Typical"], pickedInState: false },
  studio: {
    captions: [
      "Typical",
      "Empty",
      "Loading",
      "Failed",
      "Many rows",
      "Long names",
      "Access denied",
      "Overview with errors and changes not live",
      "Overview of a new application",
      "Entity",
      "Entity with expressions",
      "Policy matrix",
      "Releases",
      "History",
      "Revision",
      "Release page",
    ],
    pickedInState: true,
  },
  studioPageDesign: { captions: ["Typical"], pickedInState: true },
  studioFormDesign: { captions: ["Typical"], pickedInState: true },
  studioProcessDesign: { captions: ["Typical"], pickedInState: true },
};

for (const [name, { captions, pickedInState }] of Object.entries(
  framedPreviews,
)) {
  test(`the ${name} preview draws each state in a frame exactly as tall as its height, with the page scrolling inside it, in light mode`, async ({
    page,
  }) => {
    const recording = await openHarness(page, `?preview=${name}&mode=light`, {
      sandboxed: true,
      frameWidth: 992,
    });
    const frame = page.frameLocator("iframe");
    const stateControl = frame.getByRole("radiogroup", {
      name: "State",
      exact: true,
    });

    for (const caption of captions) {
      if (pickedInState) {
        await stateControl.getByText(caption, { exact: true }).click();
      }
      const state = figure(frame, caption);
      const screen = state.locator(":scope > div > :first-child");
      await expect(screen).toBeVisible();
      // The frame sets its height in --shell-min-height, so the spec reads it rather than copying the heights.
      const height = await state
        .locator(":scope > div")
        .evaluate((element) =>
          parseFloat(
            getComputedStyle(element).getPropertyValue("--shell-min-height"),
          ),
        );
      const box = await screen.boundingBox();
      if (!box) {
        throw new Error(`The ${caption} state has no screen box`);
      }
      expect(Math.abs(box.height - height)).toBeLessThanOrEqual(0.5);
      expect(
        await screen.evaluate((element) => getComputedStyle(element).overflowY),
      ).toBe("auto");
    }
    expect(recording.errors).toEqual([]);
  });
}

test("a dialog opened in the scrolled entity state of studio shows inside the frame", async ({
  page,
}) => {
  const recording = await openHarness(
    page,
    "?preview=studio&mode=light&state=entity",
  );
  const screen = figure(page, "Entity").locator(":scope > div > :first-child");

  await expect(page.getByTestId("studio-entity-designer")).toBeVisible();
  await page
    .getByTestId("studio-field-/label")
    .getByRole("textbox", { name: "English", exact: true })
    .pressSequentially("!");
  await expect(page.getByTestId("studio-save-bar")).toBeVisible();

  expect(
    await screen.evaluate(
      (element) => element.scrollHeight > element.clientHeight,
    ),
  ).toBe(true);
  await screen.evaluate((element) => {
    element.scrollTop = element.scrollHeight;
  });
  await expect
    .poll(() => screen.evaluate((element) => element.scrollTop))
    .toBeGreaterThan(0);

  await page
    .getByTestId("studio-save-bar")
    .getByRole("button", { name: "Discard", exact: true })
    .click();
  const dialog = await waitForDialog(page, "Discard all unsaved changes?");

  const dialogBox = await dialog.boundingBox();
  const screenBox = await screen.boundingBox();
  if (!dialogBox || !screenBox) {
    throw new Error("The dialog or the screen has no box");
  }
  expect(dialogBox.x).toBeGreaterThanOrEqual(screenBox.x);
  expect(dialogBox.y).toBeGreaterThanOrEqual(screenBox.y);
  expect(dialogBox.x + dialogBox.width).toBeLessThanOrEqual(
    screenBox.x + screenBox.width,
  );
  expect(dialogBox.y + dialogBox.height).toBeLessThanOrEqual(
    screenBox.y + screenBox.height,
  );
  expect(await screen.evaluate((element) => element.scrollTop)).toBeGreaterThan(
    0,
  );
  expect(recording.errors).toEqual([]);
});

/**
 * What a Studio editor draws, read in the page: each element that has the panel's style, a surface background with a
 * 1px border of borderSubtle on all four sides, and each part whose box is not bare, which is a background, a border,
 * a padding or a radius. The panel's colours come from a probe element, so the browser's own form of each colour is
 * compared. `testIds` lists the parts in order.
 */
function panelReport(
  root: HTMLElement,
  colour: { surface: string; borderSubtle: string },
) {
  const document = root.ownerDocument;
  const probe = document.createElement("div");
  probe.style.background = colour.surface;
  probe.style.border = `1px solid ${colour.borderSubtle}`;
  document.body.append(probe);
  const expected = getComputedStyle(probe);
  const background = expected.backgroundColor;
  const sides = ["top", "right", "bottom", "left"];
  const borders = sides.map((side) =>
    expected.getPropertyValue(`border-${side}-color`),
  );
  probe.remove();

  const panels = [root, ...root.querySelectorAll<HTMLElement>("*")]
    .filter((element) => {
      const style = getComputedStyle(element);
      return (
        style.backgroundColor === background &&
        sides.every(
          (side, index) =>
            style.getPropertyValue(`border-${side}-width`) === "1px" &&
            style.getPropertyValue(`border-${side}-style`) === "solid" &&
            style.getPropertyValue(`border-${side}-color`) === borders[index],
        )
      );
    })
    .map((element) => element.dataset.testid ?? element.tagName);

  const parts = [
    ...root.querySelectorAll<HTMLElement>('[data-testid^="studio-section-"]'),
  ];
  const styled = parts
    .filter((part) => {
      const style = getComputedStyle(part);
      return (
        style.backgroundColor !== "rgba(0, 0, 0, 0)" ||
        style.borderRadius !== "0px" ||
        sides.some(
          (side) =>
            style.getPropertyValue(`border-${side}-width`) !== "0px" ||
            style.getPropertyValue(`padding-${side}`) !== "0px",
        )
      );
    })
    .map((part) => part.dataset.testid!);

  return {
    panels,
    styled,
    testIds: parts.map((part) => part.dataset.testid!),
  };
}

test("the studio preview draws no panel on its Settings page or its Entity state in light mode", async ({
  page,
}) => {
  const { surface, borderSubtle } = designTokensFor("light").colour;
  const colour = { surface, borderSubtle };

  const settingsQuery = "?preview=studio&mode=light";
  const settings = await openHarness(page, settingsQuery, {
    sandboxed: true,
    frameWidth: 992,
  });
  const state = figure(page.frameLocator("iframe"), "Typical");
  await state
    .getByRole("menu")
    .getByRole("menuitem", { name: "Settings", exact: true })
    .click();
  await expect(
    state.getByRole("heading", {
      name: fixtureMarker,
      exact: true,
      level: 1,
    }),
  ).toBeVisible();
  const settingsEditor = state.getByTestId("studio-editor");
  await expect(settingsEditor).toBeVisible();
  const settingsReport = await settingsEditor.evaluate(panelReport, colour);
  expect(settingsReport.panels).toEqual([]);
  expect(settingsReport.styled).toEqual([]);
  expect(settingsReport.testIds.at(-1)).toBe("studio-section-autoAccept");
  expect(settings.errors).toEqual([]);
  expect(settings.requests.filter((url) => !url.startsWith("data:"))).toEqual([
    `${origin}/sandboxed.html`,
    `${harnessUrl}${settingsQuery}`,
    bundleUrl,
  ]);

  const entityQuery = "?preview=studio&mode=light&state=entity";
  const entity = await openHarness(page, entityQuery, {
    sandboxed: true,
    frameWidth: 992,
  });
  const entityFrame = page.frameLocator("iframe");
  await expect(entityFrame.getByTestId("studio-entity-designer")).toBeVisible();
  const entityEditor = entityFrame.getByTestId("studio-editor");
  await expect(entityEditor).toBeVisible();
  const entityReport = await entityEditor.evaluate(panelReport, colour);
  expect(entityReport.panels).toEqual([]);
  expect(entityReport.styled).toEqual([]);
  expect(entity.errors).toEqual([]);
  expect(entity.requests.filter((url) => !url.startsWith("data:"))).toEqual([
    `${origin}/sandboxed.html`,
    `${harnessUrl}${entityQuery}`,
    bundleUrl,
  ]);
});

for (const mode of modes) {
  test(`the studio preview moves between Overview, Settings, a kind list and Releases from the sidebar in ${mode} mode`, async ({
    page,
  }) => {
    const query = `?preview=studio&mode=${mode}`;
    const recording = await openHarness(page, query, {
      sandboxed: true,
      frameWidth: 992,
    });
    const state = figure(page.frameLocator("iframe"), "Typical");
    const heading = (name: string) =>
      state.getByRole("heading", { name, exact: true, level: 1 });
    const sidebar = state.getByRole("menu");

    await expect(heading("Purchase requests")).toBeVisible();
    await expect(state.getByTestId("studio-releases")).toHaveCount(0);
    await sidebar
      .getByRole("menuitem", { name: "Settings", exact: true })
      .click();
    await expect(heading("DesignSystemSample")).toBeVisible();
    await sidebar
      .getByRole("menuitem", { name: "Entities", exact: true })
      .click();
    await expect(heading("Entities")).toBeVisible();
    await sidebar
      .getByRole("menuitem", { name: "Releases", exact: true })
      .click();
    await expect(heading("Releases")).toBeVisible();
    await expect(state.getByTestId("studio-releases")).toBeVisible();
    await sidebar
      .getByRole("menuitem", { name: "Overview", exact: true })
      .click();
    await expect(heading("Purchase requests")).toBeVisible();

    expect(recording.errors).toEqual([]);
    expect(
      recording.requests.filter((url) => !url.startsWith("data:")),
    ).toEqual([`${origin}/sandboxed.html`, `${harnessUrl}${query}`, bundleUrl]);
  });
}

for (const [slug, testId] of [
  ["empty", "studio-no-applications"],
  ["failed", "studio-load-error"],
  ["access-denied", "studio-forbidden"],
]) {
  test(`the studio preview opens its ${slug} state from the address`, async ({
    page,
  }) => {
    const recording = await openHarness(
      page,
      `?preview=studio&mode=light&state=${slug}`,
    );

    await expect(page.getByTestId(testId)).toBeVisible();
    expect(recording.errors).toEqual([]);
  });
}

for (const [slug, expectKeyElement] of [
  [
    "overview-with-errors-and-changes-not-live",
    async (state: FrameLocator) => {
      await expect(state.getByTestId("studio-problems")).toHaveText(
        "3 errors, 1 warning",
      );
      await expect(state.getByTestId("studio-folder-differs")).toBeVisible();
    },
  ],
  [
    "overview-of-a-new-application",
    async (state: FrameLocator) => {
      await expect(
        state.getByTestId("studio-section-get-started"),
      ).toBeVisible();
      await expect(state.getByTestId("studio-section-more")).toBeVisible();
      await expect(state.getByTestId("studio-section-live")).toHaveCount(0);
    },
  ],
  [
    "entity",
    async (state: FrameLocator) => {
      await expect(state.getByTestId("studio-entity-designer")).toBeVisible();
    },
  ],
  [
    "policy-matrix",
    async (state: FrameLocator) => {
      await expect(state.getByTestId("studio-matrix")).toBeVisible();
      await expect(
        state.getByTestId("studio-matrix-cell-Requester-entity-Request"),
      ).toBeVisible();
    },
  ],
  [
    "releases",
    async (state: FrameLocator) => {
      await expect(state.getByTestId("studio-releases")).toBeVisible();
      await expect(state.getByTestId("studio-release-row")).toHaveCount(2);
    },
  ],
  [
    "history",
    async (state: FrameLocator) => {
      await expect(state.getByTestId("studio-history")).toBeVisible();
      await expect(state.getByTestId("studio-revision-row")).toHaveCount(3);
    },
  ],
  [
    "revision",
    async (state: FrameLocator) => {
      await expect(state.getByTestId("studio-revision")).toBeVisible();
      await expect(state.getByTestId("studio-changes-file")).toHaveCount(3);
    },
  ],
  [
    "release-page",
    async (state: FrameLocator) => {
      await expect(state.getByTestId("studio-release")).toBeVisible();
    },
  ],
] as const) {
  test(`the studio preview opens its ${slug} state from the address`, async ({
    page,
  }) => {
    const query = `?preview=studio&mode=light&state=${slug}`;
    const recording = await openHarness(page, query, {
      sandboxed: true,
      frameWidth: 992,
    });

    await expectKeyElement(page.frameLocator("iframe"));
    expect(recording.errors).toEqual([]);
    expect(
      recording.requests.filter((url) => !url.startsWith("data:")),
    ).toEqual([`${origin}/sandboxed.html`, `${harnessUrl}${query}`, bundleUrl]);
  });
}

test("the studio preview's entity state loads each field's default editor with no missing fixture", async ({
  page,
}) => {
  const recording = await openHarness(
    page,
    "?preview=studio&mode=light&state=entity",
  );

  await openEntityField(page, 0);
  await expect(
    page.getByTestId("studio-field-/fields/0/default").locator(".cm-editor"),
  ).toBeVisible();
  await closeEntityField(page);
  expect(recording.errors).toEqual([]);
});

test("the studio preview replaces the entity designer with the policy matrix picked in the State control", async ({
  page,
}) => {
  const recording = await openHarness(page, "?preview=studio&mode=light");
  const stateControl = page.getByRole("radiogroup", {
    name: "State",
    exact: true,
  });

  await stateControl.getByText("Entity", { exact: true }).click();
  await expect(page.getByTestId("studio-entity-designer")).toBeVisible();
  await stateControl.getByText("Policy matrix", { exact: true }).click();
  await expect(page.getByTestId("studio-matrix")).toBeVisible();
  await expect(page.getByTestId("studio-entity-designer")).toHaveCount(0);
  expect(recording.errors).toEqual([]);
});

test("the studio preview replaces one state's page with the next one picked in the State control", async ({
  page,
}) => {
  const recording = await openHarness(page, "?preview=studio&mode=light");
  const stateControl = page.getByRole("radiogroup", {
    name: "State",
    exact: true,
  });
  await expect(page.getByTestId("studio-apps")).toHaveCount(0);

  await stateControl.getByText("Failed", { exact: true }).click();
  await expect(page.getByTestId("studio-load-error")).toBeVisible();
  await stateControl.getByText("Access denied", { exact: true }).click();
  await expect(page.getByTestId("studio-forbidden")).toBeVisible();
  await expect(page.getByTestId("studio-load-error")).toHaveCount(0);
  expect(recording.errors).toEqual([]);
});

test("the State control wraps its captions onto more lines in a narrow frame", async ({
  page,
}) => {
  const recording = await openHarness(page, "?preview=studio&mode=light", {
    sandboxed: true,
    frameWidth: 360,
  });
  const frame = page.frameLocator("iframe");
  const stateControl = frame.getByRole("radiogroup", {
    name: "State",
    exact: true,
  });
  const first = (await stateControl
    .getByText("Typical", { exact: true })
    .boundingBox())!;
  const last = (await stateControl
    .getByText("Policy matrix", { exact: true })
    .boundingBox())!;
  const group = (await stateControl.boundingBox())!;
  const framed = (await page.locator("iframe").boundingBox())!;
  expect(last.y).toBeGreaterThan(first.y);
  expect(group.x + group.width).toBeLessThanOrEqual(framed.x + framed.width);
  expect(recording.errors).toEqual([]);
});

test("the field-date-time preview opens its date picker", async ({ page }) => {
  const recording = await openHarness(
    page,
    "?preview=field-date-time&mode=light",
  );
  await figure(page, "Editable").getByLabel(fieldLabel("Submitted at")).click();

  await expect(
    page.getByRole("button", { name: "Next month", exact: true }),
  ).toBeVisible();
  expect(recording.errors).toEqual([]);
});

test("the field-reference preview lists the fixture categories in its lookup", async ({
  page,
}) => {
  const recording = await openHarness(
    page,
    "?preview=field-reference&mode=light",
  );
  await figure(page, "Editable")
    .getByTestId("reference-category")
    .getByRole("button", { name: "Choose…", exact: true })
    .click();

  const dialog = await waitForDialog(page, "Category");
  await expect(dialog).toContainText("Office furniture");
  expect(recording.errors).toEqual([]);
});

test("the field-child-collection preview adds a row", async ({ page }) => {
  const recording = await openHarness(
    page,
    "?preview=field-child-collection&mode=light",
  );
  const reference = figure(page, "Reference and computed cells");
  await expect(reference.getByLabel(fieldLabel("Description 2"))).toBeVisible();
  await reference.getByRole("button", { name: "Add row", exact: true }).click();

  await expect(reference.getByLabel(fieldLabel("Description 3"))).toBeVisible();
  expect(recording.errors).toEqual([]);
});

test("a mounted table redraws in the other mode when the page changes data-theme", async ({
  page,
}) => {
  const recording = await openHarness(page, "?preview=table&mode=light");
  const header = page
    .getByRole("figure", { name: "With data", exact: true })
    .getByRole("columnheader", { name: "Title", exact: true });
  await expect(header).toBeVisible();
  const background = () =>
    header.evaluate((cell) => getComputedStyle(cell).backgroundColor);
  const light = await background();

  await page.evaluate(() => {
    document.documentElement.dataset.theme = "dark";
  });

  await expect.poll(background).not.toBe(light);
  expect(recording.errors).toEqual([]);
});

test("a mounted form uses the page's --primary and redraws when it changes", async ({
  page,
}) => {
  const recording = await openHarness(page, "?preview=table&mode=light");
  await expect(previews.table(page)).toBeVisible();

  // The harness mounts its previews at load, so the test sets the property and then mounts the form itself.
  await page.evaluate(() => {
    document.documentElement.style.setProperty("--primary", "#13c2c2");
    const element = document.createElement("div");
    document.getElementById("previews")!.appendChild(element);
    (
      window as unknown as { Axis: { form: (element: HTMLElement) => void } }
    ).Axis.form(element);
  });

  const save = page.getByRole("button", { name: "Next", exact: true }).first();
  const background = () =>
    save.evaluate((button) => getComputedStyle(button).backgroundColor);
  await expect.poll(background).toBe("rgb(19, 194, 194)");

  await page.evaluate(() =>
    document.documentElement.style.setProperty("--primary", "#eb2f96"),
  );

  await expect.poll(background).toBe("rgb(235, 47, 150)");
  expect(recording.errors).toEqual([]);
});

for (const mode of modes) {
  test(`a form validation message follows the page's --dangerText in ${mode} mode`, async ({
    page,
  }) => {
    const recording = await openHarness(page, `?preview=form&mode=${mode}`);
    const message = figure(page, "Validation errors").getByText(
      "Enter a title.",
      { exact: true },
    );
    await expect(message).toBeVisible();
    await page.evaluate(() =>
      document.documentElement.style.setProperty("--dangerText", "#13c2c2"),
    );

    await expect
      .poll(() =>
        message.evaluate((element) => getComputedStyle(element).color),
      )
      .toBe("rgb(19, 194, 194)");
    expect(recording.errors).toEqual([]);
  });
}

test("the dark table draws the Delete icon and the current page at 4.5:1 or more against their surface", async ({
  page,
}) => {
  const recording = await openHarness(page, "?preview=table&mode=dark");
  const withData = figure(page, "With data");
  const deleteIcon = withData
    .getByRole("button", { name: "Delete", exact: true })
    .first();
  const currentPage = withData.locator('[aria-current="page"]');
  await expect(deleteIcon).toBeVisible();
  await expect(currentPage).toBeVisible();

  // The text colour and the first ancestor with an opaque background, which is the surface the text sits on.
  const colours = (locator: Locator) =>
    locator.evaluate((element) => {
      const alpha = (colour: string) =>
        Number(colour.match(/[\d.]+/g)?.[3] ?? 1);
      let surface: Element | null = element;
      while (surface && alpha(getComputedStyle(surface).backgroundColor) < 1) {
        surface = surface.parentElement;
      }
      return {
        foreground: getComputedStyle(element).color,
        background: surface ? getComputedStyle(surface).backgroundColor : "",
      };
    });

  for (const text of [deleteIcon, currentPage]) {
    const { foreground, background } = await colours(text);
    expect(contrast(foreground, background)).toBeGreaterThanOrEqual(4.5);
  }
  expect(recording.errors).toEqual([]);
});

test("the bundle sets window.Axis with one mount for each preview", async ({
  page,
}) => {
  const recording = await openHarness(page, "");

  const mounts = await page.evaluate(() =>
    Object.keys((window as unknown as { Axis: object }).Axis),
  );
  expect(mounts.sort()).toEqual(Object.keys(previews).sort());
  // The bundle's first line names the card of each window.Axis preview, in the order of cardOrder.ts.
  const firstLine = readFileSync(bundleFile, "utf8").split("\n", 1)[0];
  const header = JSON.parse(
    /^\/\* @ds-bundle: (\{.*\}) \*\/$/.exec(firstLine)![1],
  ) as {
    format: number;
    namespace: string;
    components: { name: string }[];
  };
  const cards = cardGroups.flatMap((group) => group.previews).map(cardName);
  expect(header).toEqual({
    format: 4,
    namespace: "Axis",
    components: cards.map((name) => ({ name })),
  });
  expect(mounts.map(cardName).sort()).toEqual(
    header.components.map(({ name }) => name).sort(),
  );
  await expect(page.locator("[data-preview]")).toHaveCount(
    Object.keys(previews).length,
  );
  await page.waitForLoadState("networkidle");
  expect(recording.errors).toEqual([]);
});

test("only the bundle holds the fixtures, and the production SPA does not", () => {
  expect(readFileSync(bundleFile, "utf8")).toContain(fixtureMarker);

  const productionFiles = filesUnder(productionBuild);
  // An empty web root would pass the checks below for no reason.
  expect(productionFiles).toContain("index.html");
  expect(
    productionFiles.filter(
      (file) => path.basename(file) === "design-system.html",
    ),
  ).toEqual([]);
  expect(
    productionFiles.filter((file) =>
      readFileSync(path.join(productionBuild, file), "utf8").includes(
        fixtureMarker,
      ),
    ),
  ).toEqual([]);
});
