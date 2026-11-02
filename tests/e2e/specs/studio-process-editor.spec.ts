import { cp, mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { expect, test } from "../test";
import type { Page } from "@playwright/test";
import { startServer } from "../activate";
import { dragTo } from "../design-drag";
import { pickAction, pickOption } from "../select";
import { e2eApp, purchaseRequests } from "../server-settings";
import { useSession } from "../sessions";
import { openFormTab } from "../studio-resources";

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000);

// The journeys change the process. They run on a second server over a folder of its own, copied from the committed
// sample, so no other spec's writes reach it.
test.describe("Studio edits a process in the process editor", () => {
  test.describe.configure({ mode: "serial" });

  const file = "processes/purchase-request-approval.json";
  const folder = "/api/studio/apps/PurchaseRequests";
  let copy: string;
  let server: Awaited<ReturnType<typeof startServer>>;

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), "axis-studio-process-editor-"));
    await cp(purchaseRequests, copy, { recursive: true });
    // The sample's only event is raised by its own process, so the wait for event journey adds one of its own about the subject entity.
    await mkdir(path.join(copy, "events"), { recursive: true });
    await writeFile(
      path.join(copy, "events", "payment-received.json"),
      JSON.stringify({
        id: "c3b7d1a4-5e2f-4a68-9d01-7b8c9e0f1a23",
        kind: "event",
        name: "PaymentReceived",
        formatVersion: 1,
        entity: "PurchaseRequest",
        fields: [{ name: "amount", type: "decimal" }],
      }),
    );
    // No sample connector declares an object or a list input, and the list journey needs one about the line items.
    const connectorFile = path.join(
      copy,
      "connectors",
      "purchasing-system.json",
    );
    const connector = JSON.parse(await readFile(connectorFile, "utf8")) as {
      operations: { name: string; input: unknown[] }[];
    };
    connector.operations
      .find((operation) => operation.name === "createPurchaseOrder")!
      .input.push({
        name: "lines",
        type: "list",
        fields: [
          { name: "description", type: "text", required: true },
          { name: "quantity", type: "integer", required: true },
        ],
      });
    await writeFile(connectorFile, JSON.stringify(connector));
    // Index 0 of ActivateOnStartup is the purchase request sample, so it stays out of this server and the live
    // release of the main E2E server is not activated again.
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

  async function processId(page: Page): Promise<string> {
    const list = await page.request.get(`${server.url}${folder}/resources`);
    const { items } = (await list.json()) as {
      items: { id: string; file: string }[];
    };
    return items.find((item) => item.file === file)!.id;
  }

  async function diagnostics(page: Page): Promise<unknown> {
    return (
      await page.request.get(`${server.url}${folder}/diagnostics`)
    ).json();
  }

  async function pick(page: Page, field: string, name: string) {
    await pickOption(
      page.getByTestId(`studio-field-${field}`).getByRole("combobox"),
      name,
    );
  }

  test("an author adds a decision step, makes submit lead to it and saves without a new diagnostic", async ({
    page,
  }) => {
    await useSession(page, "author");
    const id = await processId(page);
    const before = await diagnostics(page);

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${id}`);
    await openFormTab(page);
    await pickAction(
      page.getByRole("combobox", { name: "Add step", exact: true }),
      "decision",
      page.getByTestId("studio-step-11"),
    );
    const card = page.getByTestId("studio-step-11");
    await card
      .getByRole("textbox", { name: "Step name", exact: true })
      .fill("checkTotal");
    await card.getByRole("button", { name: "Add branch", exact: true }).click();
    const when = page.getByTestId("studio-field-/steps/11/branches/0/when");
    await expect(when.locator(".cm-editor")).toBeVisible();
    await when.getByRole("textbox").fill("totalAmount > 0");
    await pick(page, "/steps/11/branches/0/next", "managerApproval");
    await pick(page, "/steps/11/otherwise", "markRejected");
    await pick(page, "/steps/0/next", "checkTotal");
    await page.getByRole("button", { name: "Save", exact: true }).click();

    await expect(page.getByTestId("studio-saved")).toBeVisible();
    expect(await diagnostics(page)).toEqual(before);
    const saved = JSON.parse(await readFile(path.join(copy, file), "utf8")) as {
      steps: Record<string, unknown>[];
    };
    expect(saved.steps[0].next).toBe("checkTotal");
    expect(saved.steps[11]).toEqual({
      name: "checkTotal",
      type: "decision",
      branches: [{ name: "branch1", when: "totalAmount > 0", next: "managerApproval" }],
      otherwise: "markRejected",
    });
  });

  test("an author edits the set expression of markRejected and the file changes only there", async ({
    page,
  }) => {
    await useSession(page, "author");
    const id = await processId(page);
    const before = await diagnostics(page);
    const original = await readFile(path.join(copy, file), "utf8");

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${id}`);
    await openFormTab(page);
    await expect(page.getByTestId("studio-step-9")).toContainText(
      "markRejected",
    );
    const status = page.getByTestId("studio-field-/steps/9/set/status");
    await expect(status.locator(".cm-editor")).toBeVisible();
    await status
      .getByRole("textbox", { name: "Expression", exact: true })
      .fill("'returned'");
    await page.getByRole("button", { name: "Save", exact: true }).click();

    await expect(page.getByTestId("studio-saved")).toBeVisible();
    expect(await diagnostics(page)).toEqual(before);
    const expected = JSON.parse(original) as {
      steps: { set: Record<string, string> }[];
    };
    expected.steps[9].set.status = "'returned'";
    expect(JSON.parse(await readFile(path.join(copy, file), "utf8"))).toEqual(
      expected,
    );
  });

  test("an author changes the due time and an outcome target of a task and saves only those changes", async ({
    page,
  }) => {
    await useSession(page, "author");
    const id = await processId(page);
    const before = JSON.parse(
      await readFile(path.join(copy, file), "utf8"),
    ) as {
      steps: { dueIn?: string; outcomes: { next: string }[] }[];
    };
    const beforeDiagnostics = await diagnostics(page);

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${id}`);
    await openFormTab(page);
    await expect(page.getByTestId("studio-step-3")).toContainText(
      "financeApproval",
    );
    await page
      .getByTestId("studio-field-/steps/3/dueIn")
      .getByRole("textbox")
      .fill("P5D");
    await pick(page, "/steps/3/outcomes/1/next", "markRejected");
    await page.getByRole("button", { name: "Save", exact: true }).click();

    await expect(page.getByTestId("studio-saved")).toBeVisible();
    const expected = structuredClone(before);
    expected.steps[3].dueIn = "P5D";
    expected.steps[3].outcomes[1].next = "markRejected";
    expect(JSON.parse(await readFile(path.join(copy, file), "utf8"))).toEqual(
      expected,
    );
    expect(await diagnostics(page)).toEqual(beforeDiagnostics);
  });

  test("an author removes a step, and the diagnostic of the transition that names it opens its picker", async ({
    page,
  }) => {
    await useSession(page, "author");
    const id = await processId(page);

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${id}`);
    await openFormTab(page);
    const card = page.getByTestId("studio-step-4");
    await expect(card).toContainText("markApproved");
    await card
      .getByRole("button", { name: "Remove step", exact: true })
      .click();
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByTestId("studio-saved")).toBeVisible();

    const diagnostic = page
      .getByTestId("studio-diagnostics")
      .getByRole("button")
      .filter({ hasText: "Path: /steps/2/otherwise" });
    await expect(diagnostic).toContainText("AXC0070");
    await diagnostic.click();

    await expect(page).toHaveURL(/path=%2Fsteps%2F2%2Fotherwise/);
    const otherwise = page.getByTestId("studio-field-/steps/2/otherwise");
    await expect(otherwise).toBeInViewport();
    await expect(
      otherwise.getByTestId("studio-reference-unknown"),
    ).toBeVisible();
  });

  test("a step whose transition names a missing step is marked on the graph, and the transition draws no edge", async ({
    page,
  }) => {
    await useSession(page, "author");
    const id = await processId(page);
    const target = path.join(copy, file);
    const original = await readFile(target, "utf8");

    try {
      const content = JSON.parse(original) as { steps: { name: string }[] };
      const i = content.steps.findIndex((step) => step.name === "markRejected");
      await page.goto(
        `${server.url}/studio?app=PurchaseRequests&resource=${id}`,
      );
      await expect(page.getByTestId(`rf__edge-/steps/${i}/next`)).toHaveCount(
        1,
      );
      await expect(
        page.getByTestId(`studio-graph-node-${i}`),
      ).not.toHaveAttribute("data-marked");

      await page.getByRole("tab", { name: "JSON", exact: true }).click();
      const textarea = page.getByRole("textbox", { name: "JSON", exact: true });
      const draft = JSON.parse(await textarea.inputValue()) as {
        steps: { next?: string }[];
      };
      draft.steps[i].next = "noSuchStep";
      await textarea.fill(JSON.stringify(draft, null, 2));
      await page.getByRole("button", { name: "Save", exact: true }).click();

      await expect(page.getByTestId("studio-saved")).toBeVisible();
      await page.getByRole("tab", { name: "Design", exact: true }).click();
      const node = page.getByTestId(`studio-graph-node-${i}`);
      await expect(node).toHaveAttribute("data-marked", "true");
      await expect(node).toHaveAccessibleName(
        "markRejected · operation · has diagnostics",
      );
      await expect(page.getByTestId(`rf__edge-/steps/${i}/next`)).toHaveCount(
        0,
      );
    } finally {
      await writeFile(target, original);
    }
  });

  test("an author moves an edge in the panel, removes a step by keyboard, sees the diagnostic after a save and undoes it", async ({
    page,
  }) => {
    await useSession(page, "author");
    const id = await processId(page);
    const target = path.join(copy, file);
    const original = await readFile(target, "utf8");

    try {
      // The earlier journeys changed the copy, so the steps are found by name.
      const content = JSON.parse(original) as { steps: { name: string }[] };
      const indexOf = (name: string) =>
        content.steps.findIndex((step) => step.name === name);
      const first = 0;
      const removed = indexOf("markReturned");
      expect(removed).toBeGreaterThan(0);
      await page.goto(
        `${server.url}/studio?app=PurchaseRequests&resource=${id}`,
      );

      const edge = page.getByTestId(`rf__edge-/steps/${first}/next`);
      await expect(edge).toHaveAccessibleName(/^submit to \w+ · next$/);
      await page.getByTestId(`studio-graph-node-${first}`).click();
      await pick(page, `/steps/${first}/next`, "done");
      await expect(edge).toHaveAccessibleName("submit to done · next");

      const node = page.getByTestId(`studio-graph-node-${removed}`);
      await node.focus();
      await page.keyboard.press("Enter");
      await expect(page.getByTestId(`studio-step-${removed}`)).toContainText(
        "markReturned",
      );
      await page.keyboard.press("Delete");
      await expect(page.getByTestId("studio-process-announcement")).toHaveText(
        "Removed step markReturned",
      );
      await expect(
        page.getByTestId(`studio-graph-node-${content.steps.length - 1}`),
      ).toHaveCount(0);

      await page.getByRole("button", { name: "Save", exact: true }).click();
      await expect(page.getByTestId("studio-saved")).toBeVisible();
      await expect(page.getByTestId("studio-diagnostics")).toContainText(
        "AXC0070",
      );

      await page.getByRole("tab", { name: "Design", exact: true }).click();
      await page.getByRole("button", { name: "Undo", exact: true }).click();
      // The save marked steps by the saved file's numbers, so the restored node may carry another node's mark.
      await expect(
        page.getByTestId(`studio-graph-node-${removed}`),
      ).toHaveAccessibleName(/^markReturned · operation/);
    } finally {
      await writeFile(target, original);
    }
  });

  test("an author moves a step up on the Design tab, hears where it went and undoes the move", async ({
    page,
  }) => {
    await useSession(page, "author");
    const id = await processId(page);
    const target = path.join(copy, file);
    const original = await readFile(target, "utf8");

    try {
      const content = JSON.parse(original) as { steps: { name: string }[] };
      const moved = content.steps.findIndex(
        (step) => step.name === "financeCheck",
      );
      expect(moved).toBeGreaterThan(0);
      const before =
        moved === 1
          ? "moved to the start"
          : `moved after ${content.steps[moved - 2].name}`;
      await page.goto(
        `${server.url}/studio?app=PurchaseRequests&resource=${id}`,
      );

      await page.getByTestId(`studio-graph-node-${moved}`).click();
      await page
        .getByTestId(`studio-step-${moved}`)
        .getByRole("button", { name: "Move up", exact: true })
        .click();
      await expect(page.getByTestId("studio-process-announcement")).toHaveText(
        `Step financeCheck ${before}`,
      );
      await expect(page.getByTestId(`studio-step-${moved - 1}`)).toContainText(
        "financeCheck",
      );

      await page.getByRole("button", { name: "Undo", exact: true }).click();
      await expect(
        page.getByTestId(`studio-graph-node-${moved}`),
      ).toHaveAccessibleName(/^financeCheck · /);
    } finally {
      await writeFile(target, original);
    }
  });

  test("an author drags a type onto an edge, inserts another after it by keyboard and finds both wired in the saved file", async ({
    page,
  }) => {
    await useSession(page, "author");
    const id = await processId(page);
    const target = path.join(copy, file);
    const original = await readFile(target, "utf8");

    try {
      // The earlier journeys changed the copy, so the steps are found by name.
      const content = JSON.parse(original) as { steps: { name: string }[] };
      const indexOf = (name: string) =>
        content.steps.findIndex((step) => step.name === name);
      const rejected = indexOf("markRejected");
      expect(rejected).toBeGreaterThan(0);
      const announcement = page.getByTestId("studio-process-announcement");
      await page.goto(
        `${server.url}/studio?app=PurchaseRequests&resource=${id}`,
      );

      await expect(
        page.getByTestId(`rf__edge-/steps/${rejected}/next`),
      ).toHaveAccessibleName("markRejected to done · next");
      await dragTo(
        page,
        "studio-process-design",
        page.getByTestId("studio-process-palette-updateRecord"),
        page.getByTestId(`studio-process-edge-drop-/steps/${rejected}/next`),
      );
      await expect(announcement).toHaveText(
        "Step updateRecord1 added after markRejected",
      );

      await page.getByTestId("studio-process-palette-raiseEvent").focus();
      await page.keyboard.press("Enter");
      await expect(announcement).toHaveText(
        "Step raiseEvent1 added after updateRecord1",
      );

      await page.getByRole("button", { name: "Save", exact: true }).click();
      await expect(page.getByTestId("studio-saved")).toBeVisible();
      const saved = JSON.parse(await readFile(target, "utf8")) as {
        steps: Record<string, unknown>[];
      };
      const named = (name: string) =>
        saved.steps.find((step) => step.name === name);
      expect(named("markRejected")?.next).toBe("updateRecord1");
      expect(named("updateRecord1")).toMatchObject({
        type: "operation",
        operation: "updateRecord",
        next: "raiseEvent1",
      });
      expect(named("raiseEvent1")).toMatchObject({
        type: "operation",
        operation: "raiseEvent",
        next: "done",
      });
    } finally {
      await writeFile(target, original);
    }
  });

  test("an author drags a task handle to a step, picks an outcome, undoes it and saves it after a redo", async ({
    page,
  }) => {
    await useSession(page, "author");
    const id = await processId(page);
    const target = path.join(copy, file);
    const original = await readFile(target, "utf8");

    try {
      // The earlier journeys changed the copy, so the steps and the outcome are found by name.
      const content = JSON.parse(original) as {
        steps: { name: string; outcomes?: { name: string; next: string }[] }[];
      };
      const task = content.steps.findIndex(
        (step) => step.name === "managerApproval",
      );
      const done = content.steps.findIndex((step) => step.name === "done");
      const outcome =
        content.steps[task].outcomes?.findIndex(
          (candidate) => candidate.name === "approve",
        ) ?? -1;
      expect(task).toBeGreaterThanOrEqual(0);
      expect(done).toBeGreaterThanOrEqual(0);
      expect(outcome).toBeGreaterThanOrEqual(0);
      await page.goto(
        `${server.url}/studio?app=PurchaseRequests&resource=${id}`,
      );

      const edge = page.getByTestId(
        `rf__edge-/steps/${task}/outcomes/${outcome}`,
      );
      const before = await edge.getAttribute("aria-label");
      expect(before).not.toBeNull();
      expect(before).not.toBe("managerApproval to done · approve");

      // The mouse works in viewport coordinates, and the graph starts below the release table.
      await page
        .getByTestId("studio-process-graph")
        .evaluate((element) => element.scrollIntoView({ block: "start" }));
      const handle = (await page
        .getByTestId(`studio-graph-handle-${task}`)
        .boundingBox())!;
      const node = (await page
        .getByTestId(`studio-graph-node-${done}`)
        .boundingBox())!;
      await page.mouse.move(
        handle.x + handle.width / 2,
        handle.y + handle.height / 2,
      );
      await page.mouse.down();
      await page.mouse.move(node.x + node.width / 2, node.y + node.height / 2, {
        steps: 10,
      });
      await page.mouse.up();

      await expect(
        page.getByTestId("studio-process-connect-menu"),
      ).toBeVisible();
      await page
        .getByRole("menuitem", { name: "approve", exact: true })
        .click();
      await expect(edge).toHaveAccessibleName(
        "managerApproval to done · approve",
      );
      await expect(
        page.getByTestId("studio-process-announcement"),
      ).toContainText("now goes to done");

      await page.getByRole("button", { name: "Undo", exact: true }).click();
      await expect(edge).toHaveAccessibleName(before!);
      await page.getByRole("button", { name: "Redo", exact: true }).click();
      await expect(edge).toHaveAccessibleName(
        "managerApproval to done · approve",
      );

      await page.getByRole("button", { name: "Save", exact: true }).click();
      await expect(page.getByTestId("studio-saved")).toBeVisible();
      const saved = JSON.parse(
        await readFile(target, "utf8"),
      ) as typeof content;
      expect(saved.steps[task].outcomes?.[outcome].next).toBe("done");
    } finally {
      await writeFile(target, original);
    }
  });

  test("an author adds a timer on the Form tab without the JSON view, wires it and saves without a new diagnostic", async ({
    page,
  }) => {
    await useSession(page, "author");
    const id = await processId(page);
    const target = path.join(copy, file);
    const original = await readFile(target, "utf8");

    try {
      // The earlier journeys changed the copy, so the steps are found by name.
      const content = JSON.parse(original) as { steps: { name: string }[] };
      const added = content.steps.length;
      const rejected = content.steps.findIndex(
        (step) => step.name === "markRejected",
      );
      expect(rejected).toBeGreaterThanOrEqual(0);
      const before = await diagnostics(page);

      await page.goto(
        `${server.url}/studio?app=PurchaseRequests&resource=${id}`,
      );
      await openFormTab(page);
      await pickAction(
        page.getByRole("combobox", { name: "Add step", exact: true }),
        "timer",
        page.getByTestId(`studio-step-${added}`),
      );
      const card = page.getByTestId(`studio-step-${added}`);
      await card
        .getByRole("textbox", { name: "Step name", exact: true })
        .fill("coolDown");
      await expect(
        card.getByRole("radio", { name: "Wait", exact: true }),
      ).toBeChecked();
      await card
        .getByRole("textbox", { name: "Duration", exact: true })
        .fill("PT4H");
      await pick(page, `/steps/${added}/next`, "done");
      await pick(page, `/steps/${rejected}/next`, "coolDown");
      await page.getByRole("button", { name: "Save", exact: true }).click();

      await expect(page.getByTestId("studio-saved")).toBeVisible();
      expect(await diagnostics(page)).toEqual(before);
      const saved = JSON.parse(await readFile(target, "utf8")) as {
        steps: Record<string, unknown>[];
      };
      expect(
        saved.steps.find((step) => step.name === "markRejected")?.next,
      ).toBe("coolDown");
      expect(saved.steps.find((step) => step.name === "coolDown")).toEqual({
        name: "coolDown",
        type: "timer",
        wait: "PT4H",
        next: "done",
      });
    } finally {
      await writeFile(target, original);
    }
  });

  test("an author adds a timer and a wait for event with a timeout on the Form tab without the JSON view and saves without a new diagnostic", async ({
    page,
  }) => {
    await useSession(page, "author");
    const id = await processId(page);
    const target = path.join(copy, file);
    const original = await readFile(target, "utf8");

    try {
      // The earlier journeys changed the copy, so the steps are found by name.
      const content = JSON.parse(original) as { steps: { name: string }[] };
      const timer = content.steps.length;
      const wait = timer + 1;
      const rejected = content.steps.findIndex(
        (step) => step.name === "markRejected",
      );
      expect(rejected).toBeGreaterThanOrEqual(0);
      const before = await diagnostics(page);

      await page.goto(
        `${server.url}/studio?app=PurchaseRequests&resource=${id}`,
      );
      await openFormTab(page);
      await pickAction(
        page.getByRole("combobox", { name: "Add step", exact: true }),
        "timer",
        page.getByTestId(`studio-step-${timer}`),
      );
      const timerCard = page.getByTestId(`studio-step-${timer}`);
      await timerCard
        .getByRole("textbox", { name: "Step name", exact: true })
        .fill("chase");
      await timerCard
        .getByRole("textbox", { name: "Duration", exact: true })
        .fill("PT1H");
      await pick(page, `/steps/${timer}/next`, "done");

      await pickAction(
        page.getByRole("combobox", { name: "Add step", exact: true }),
        "waitEvent",
        page.getByTestId(`studio-step-${wait}`),
      );
      const card = page.getByTestId(`studio-step-${wait}`);
      await card
        .getByRole("textbox", { name: "Step name", exact: true })
        .fill("awaitPayment");
      await pick(page, `/steps/${wait}/event`, "PaymentReceived");
      await card
        .getByRole("button", { name: "Add timeout", exact: true })
        .click();
      await card
        .getByRole("textbox", { name: "After", exact: true })
        .fill("P2D");
      await pick(page, `/steps/${wait}/timeout/next`, "chase");
      await pick(page, `/steps/${wait}/next`, "done");
      await pick(page, `/steps/${rejected}/next`, "awaitPayment");
      await page.getByRole("button", { name: "Save", exact: true }).click();

      await expect(page.getByTestId("studio-saved")).toBeVisible();
      expect(await diagnostics(page)).toEqual(before);
      const saved = JSON.parse(await readFile(target, "utf8")) as {
        steps: Record<string, unknown>[];
      };
      expect(
        saved.steps.find((step) => step.name === "markRejected")?.next,
      ).toBe("awaitPayment");
      expect(saved.steps.find((step) => step.name === "awaitPayment")).toEqual({
        name: "awaitPayment",
        type: "waitEvent",
        event: "PaymentReceived",
        timeout: { after: "P2D", next: "chase" },
        next: "done",
      });
    } finally {
      await writeFile(target, original);
    }
  });

  test("an author adds a notify step on the Form tab without the JSON view, writes its texts and saves without a new diagnostic", async ({
    page,
  }) => {
    await useSession(page, "author");
    const id = await processId(page);
    const target = path.join(copy, file);
    const texts = ["en", "vi"].map((locale) =>
      path.join(copy, "texts", `${locale}.json`),
    );
    const originals = await Promise.all(
      [target, ...texts].map((name) => readFile(name, "utf8")),
    );

    // The label editor writes the texts files at once, outside the save, so the journey restores them too.
    async function writeTexts(field: string, en: string, vi: string) {
      const label = page.getByTestId(field);
      await label.getByRole("textbox", { name: "en", exact: true }).fill(en);
      await label.getByRole("textbox", { name: "vi", exact: true }).fill(vi);
      await label
        .getByRole("button", { name: "Write texts", exact: true })
        .click();
      await expect(label.getByTestId("studio-label-written")).toBeVisible();
    }

    try {
      const content = JSON.parse(originals[0]) as { steps: { name: string }[] };
      const added = content.steps.length;
      const rejected = content.steps.findIndex(
        (step) => step.name === "markRejected",
      );
      expect(rejected).toBeGreaterThanOrEqual(0);
      const before = await diagnostics(page);

      await page.goto(
        `${server.url}/studio?app=PurchaseRequests&resource=${id}`,
      );
      await openFormTab(page);
      await pickAction(
        page.getByRole("combobox", { name: "Add step", exact: true }),
        "operation",
        page.getByTestId(`studio-step-${added}`),
      );
      const card = page.getByTestId(`studio-step-${added}`);
      await card
        .getByRole("textbox", { name: "Step name", exact: true })
        .fill("tellRequester");
      await pick(page, `/steps/${added}/operation`, "notify");

      const to = page.getByTestId(`studio-field-/steps/${added}/to`);
      await to
        .getByRole("button", { name: "Add recipient", exact: true })
        .click();
      const user = page.getByTestId(`studio-field-/steps/${added}/to/0/user`);
      await expect(user.locator(".cm-editor")).toBeVisible();
      await user.getByRole("textbox").fill("requester");
      await page
        .getByTestId(`studio-field-/steps/${added}/channels`)
        .getByRole("checkbox", { name: "In-app", exact: true })
        .check();

      await writeTexts(
        `studio-field-/steps/${added}/message/subject`,
        "Request {number} was rejected",
        "Yêu cầu {number} bị từ chối",
      );
      await writeTexts(
        `studio-field-/steps/${added}/message/body`,
        "Request {number} was rejected by a manager",
        "Yêu cầu {number} bị quản lý từ chối",
      );
      const values = page.getByTestId(
        `studio-field-/steps/${added}/message/values`,
      );
      await values
        .getByRole("button", { name: "Add mapping", exact: true })
        .click();
      await values
        .getByRole("textbox", { name: "Placeholder", exact: true })
        .fill("number");
      const value = page.getByTestId(
        `studio-field-/steps/${added}/message/values/number`,
      );
      await expect(value.locator(".cm-editor")).toBeVisible();
      await value
        .getByRole("textbox", { name: "Expression", exact: true })
        .fill("number");

      await pick(page, `/steps/${added}/next`, "done");
      await pick(page, `/steps/${rejected}/next`, "tellRequester");
      await page.getByRole("button", { name: "Save", exact: true }).click();

      await expect(page.getByTestId("studio-saved")).toBeVisible();
      expect(await diagnostics(page)).toEqual(before);
      const saved = JSON.parse(await readFile(target, "utf8")) as {
        steps: {
          name: string;
          next?: string;
          to?: unknown;
          channels?: unknown;
          message?: {
            subject: { textKey: string };
            body: { textKey: string };
            values: unknown;
          };
        }[];
      };
      expect(
        saved.steps.find((step) => step.name === "markRejected")?.next,
      ).toBe("tellRequester");
      const notify = saved.steps.find((step) => step.name === "tellRequester")!;
      expect(notify).toMatchObject({
        type: "operation",
        operation: "notify",
        to: [{ user: "requester" }],
        channels: ["inApp"],
        message: { values: { number: "number" } },
        next: "done",
      });
      expect(notify.message?.subject.textKey).not.toBe("");
      expect(notify.message?.body.textKey).not.toBe("");
    } finally {
      await Promise.all(
        [target, ...texts].map((name, index) =>
          writeFile(name, originals[index]),
        ),
      );
    }
  });

  test("an author gives a task a deadline and a notify escalation to a role without the JSON view and saves without a new diagnostic", async ({
    page,
  }) => {
    await useSession(page, "author");
    const id = await processId(page);
    const target = path.join(copy, file);
    const texts = ["en", "vi"].map((locale) =>
      path.join(copy, "texts", `${locale}.json`),
    );
    const originals = await Promise.all(
      [target, ...texts].map((name) => readFile(name, "utf8")),
    );

    // The label editor writes the texts files at once, outside the save, so the journey restores them too.
    async function writeTexts(field: string, en: string, vi: string) {
      const label = page.getByTestId(field);
      await label.getByRole("textbox", { name: "en", exact: true }).fill(en);
      await label.getByRole("textbox", { name: "vi", exact: true }).fill(vi);
      await label
        .getByRole("button", { name: "Write texts", exact: true })
        .click();
      await expect(label.getByTestId("studio-label-written")).toBeVisible();
    }

    try {
      // The earlier journeys changed the copy, so the task is found by name.
      const before = JSON.parse(originals[0]) as {
        steps: Record<string, unknown>[];
      };
      const index = before.steps.findIndex(
        (step) => step.name === "financeApproval",
      );
      expect(index).toBeGreaterThanOrEqual(0);
      const beforeDiagnostics = await diagnostics(page);

      await page.goto(
        `${server.url}/studio?app=PurchaseRequests&resource=${id}`,
      );
      await openFormTab(page);
      await expect(page.getByTestId(`studio-step-${index}`)).toContainText(
        "financeApproval",
      );

      const deadline = page.getByTestId(
        `studio-field-/steps/${index}/deadline`,
      );
      const after = deadline.getByRole("textbox", {
        name: "Deadline after",
        exact: true,
      });
      // A click on the button can be lost while the form re-renders, so it is repeated until the field shows.
      await expect(async () => {
        if (!(await after.isVisible())) {
          await deadline
            .getByRole("button", { name: "Add deadline", exact: true })
            .click({ timeout: 2_000 });
        }
        await expect(after).toBeVisible({ timeout: 2_000 });
      }).toPass({ timeout: 20_000 });
      await after.fill("P5D");
      await pick(page, `/steps/${index}/deadline/next`, "markRejected");

      const row = `/steps/${index}/escalations/0`;
      await page
        .getByTestId(`studio-field-/steps/${index}/escalations`)
        .getByRole("button", { name: "Add escalation", exact: true })
        .click();
      const escalation = page.getByTestId(`studio-field-${row}`);
      await escalation
        .getByRole("textbox", { name: "Escalate after", exact: true })
        .fill("P1D");
      await escalation
        .getByRole("switch", { name: "Notify", exact: true })
        .click();

      const notify = `${row}/notify`;
      await page
        .getByTestId(`studio-field-${notify}/to`)
        .getByRole("button", { name: "Add recipient", exact: true })
        .click();
      // The radio input of a button group is not visible, so the click goes to its label.
      await page
        .getByTestId(`studio-field-${notify}/to/0`)
        .locator("label")
        .filter({ has: page.getByRole("radio", { name: "Role", exact: true }) })
        .click();
      await pick(page, `${notify}/to/0/role`, "finance-reviewer");
      await page
        .getByTestId(`studio-field-${notify}/channels`)
        .getByRole("checkbox", { name: "In-app", exact: true })
        .check();
      await writeTexts(
        `studio-field-${notify}/message/subject`,
        "A request waits for finance",
        "Một yêu cầu đang chờ tài chính",
      );
      await writeTexts(
        `studio-field-${notify}/message/body`,
        "A request has waited a day for the finance review",
        "Một yêu cầu đã chờ tài chính một ngày",
      );
      await page.getByRole("button", { name: "Save", exact: true }).click();

      await expect(page.getByTestId("studio-saved")).toBeVisible();
      expect(await diagnostics(page)).toEqual(beforeDiagnostics);
      const saved = JSON.parse(await readFile(target, "utf8")) as {
        steps: {
          deadline?: unknown;
          escalations?: {
            after: string;
            notify: Record<string, unknown> & {
              message: Record<string, { textKey: string }>;
            };
          }[];
        }[];
      };
      const { escalations, ...step } = saved.steps[index];
      const { deadline: savedDeadline, ...unchanged } = step;
      expect(unchanged).toEqual(before.steps[index]);
      expect(savedDeadline).toEqual({ after: "P5D", next: "markRejected" });
      expect(escalations).toHaveLength(1);
      expect(escalations![0]).toMatchObject({
        after: "P1D",
        notify: { to: [{ role: "finance-reviewer" }], channels: ["inApp"] },
      });
      expect(escalations![0].notify).not.toHaveProperty("next");
      expect(escalations![0].notify.message.subject.textKey).not.toBe("");
      expect(escalations![0].notify.message.body.textKey).not.toBe("");
    } finally {
      await Promise.all(
        [target, ...texts].map((name, i) => writeFile(name, originals[i])),
      );
    }
  });

  test("an author maps a list input from a child collection on the Form tab without the JSON view and saves without a new diagnostic", async ({
    page,
  }) => {
    await useSession(page, "author");
    const id = await processId(page);
    const target = path.join(copy, file);
    const original = await readFile(target, "utf8");

    try {
      const content = JSON.parse(original) as {
        steps: { name: string; input?: Record<string, unknown> }[];
      };
      const index = content.steps.findIndex(
        (step) => step.name === "createPurchaseOrder",
      );
      expect(index).toBeGreaterThanOrEqual(0);
      const before = await diagnostics(page);

      // The sample already maps the lines. The author starts without that mapping and adds it.
      const mapped = content.steps[index].input?.lines;
      expect(mapped).toBeDefined();
      delete content.steps[index].input?.lines;
      await writeFile(target, JSON.stringify(content, null, 2));

      await page.goto(
        `${server.url}/studio?app=PurchaseRequests&resource=${id}`,
      );
      await openFormTab(page);
      const input = page.getByTestId(`studio-field-/steps/${index}/input`);
      await input
        .getByRole("button", { name: "Add mapping", exact: true })
        .click();
      await page
        .getByTestId(`studio-field-/steps/${index}/input/`)
        .getByRole("textbox", { name: "Input name", exact: true })
        .fill("lines");

      const row = page.getByTestId(`studio-field-/steps/${index}/input/lines`);
      await row
        .getByRole("radio", { name: "List", exact: true })
        .locator("xpath=ancestor::label")
        .click();
      await pick(page, `/steps/${index}/input/lines/from`, "lineItems");

      const nested = page.getByTestId(
        `studio-field-/steps/${index}/input/lines/fields`,
      );
      for (const name of ["description", "quantity", "unitPrice", "amount"]) {
        await nested
          .getByRole("button", { name: "Add mapping", exact: true })
          .click();
        await page
          .getByTestId(`studio-field-/steps/${index}/input/lines/fields/`)
          .getByRole("textbox", { name: "Nested field", exact: true })
          .fill(name);
        const field = page.getByTestId(
          `studio-field-/steps/${index}/input/lines/fields/${name}`,
        );
        await expect(field.locator(".cm-editor")).toBeVisible();
        await field
          .getByRole("textbox", { name: "Expression", exact: true })
          .fill(name);
      }
      await expect(row.getByTestId("studio-json-only")).toHaveCount(0);
      await page.getByRole("button", { name: "Save", exact: true }).click();

      await expect(page.getByTestId("studio-saved")).toBeVisible();
      expect(await diagnostics(page)).toEqual(before);
      const saved = JSON.parse(await readFile(target, "utf8")) as {
        steps: { input: Record<string, unknown> }[];
      };
      expect(saved.steps[index].input.lines).toEqual(mapped);
    } finally {
      await writeFile(target, original);
    }
  });
});
