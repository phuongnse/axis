import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import { test } from "node:test";
import { failAfter, guardStep, machineState } from "./hang-guard.ts";

const never = () => new Promise<never>(() => {});

test("a step that finishes in time returns its result and asks for no evidence", async () => {
  let asked = false;
  const result = await failAfter("A step", 1_000, async () => 42, async () => {
    asked = true;
    return "";
  });
  assert.equal(result, 42);
  assert.equal(asked, false);
});

test("a step that throws in time throws the same error", async () => {
  await assert.rejects(
    failAfter("A step", 1_000, async () => { throw new Error("boom"); }, async () => ""),
    { message: "boom" },
  );
});

test("a step that hangs fails with its name, the limit and the evidence", async () => {
  await assert.rejects(
    failAfter("The Studio edit", 50, never, async () => "evidence line"),
    (error: Error) =>
      error.message.startsWith("The Studio edit did not finish within 0.05 seconds") &&
      error.message.endsWith("evidence line"),
  );
});

test("a step that fails after the limit does not leave an unhandled rejection", async () => {
  const unhandled: unknown[] = [];
  const record = (reason: unknown) => unhandled.push(reason);
  process.on("unhandledRejection", record);
  try {
    const late = () => new Promise<never>((_, reject) => setTimeout(() => reject(new Error("late")), 100));
    await assert.rejects(failAfter("A step", 20, late, async () => ""));
    await new Promise((resolve) => setTimeout(resolve, 200));
    assert.deepEqual(unhandled, []);
  } finally {
    process.off("unhandledRejection", record);
  }
});

test("the machine state names the load and the busiest processes", async () => {
  const state = await machineState();
  assert.match(state, /^load average/);
  assert.match(state, /busiest processes:/);
});

test("a hung step attaches the screenshot and the events of the page to the test", async () => {
  const page = Object.assign(new EventEmitter(), {
    isClosed: () => false,
    url: () => "http://127.0.0.1/studio",
    screenshot: async () => Buffer.from("png"),
  });
  const attachments: { name: string; contentType: string }[] = [];
  const testInfo = { attach: async (name: string, options: { contentType: string }) => { attachments.push({ name, contentType: options.contentType }); } };

  await assert.rejects(
    guardStep(page as never, testInfo as never, "The Studio edit of texts/en.json", 50, async () => {
      page.emit("console", { type: () => "error", text: () => "Maximum update depth exceeded" });
      return never();
    }),
    (error: Error) =>
      error.message.includes("did not finish within 0.05 seconds") &&
      error.message.includes("console error: Maximum update depth exceeded") &&
      error.message.includes("page: http://127.0.0.1/studio") &&
      error.message.includes("screenshot: attached"),
  );
  assert.deepEqual(attachments, [
    { name: "hang-the-studio-edit-of-texts-en-json.png", contentType: "image/png" },
    { name: "hang-the-studio-edit-of-texts-en-json.txt", contentType: "text/plain" },
  ]);
  assert.equal(page.listenerCount("console"), 0);
});

test("a hung page that never answers the screenshot still ends the step", async () => {
  const page = Object.assign(new EventEmitter(), {
    isClosed: () => false,
    url: () => "about:blank",
    screenshot: never,
  });
  const testInfo = { attach: async () => {} };
  const started = Date.now();
  await assert.rejects(
    guardStep(page as never, testInfo as never, "A step", 20, never),
    (error: Error) => error.message.includes("screenshot: none"),
  );
  assert.ok(Date.now() - started < 15_000);
});
