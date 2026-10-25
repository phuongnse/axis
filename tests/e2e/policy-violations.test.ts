import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import path from "node:path";
import { after, before, test } from "node:test";
import { chromium, type Browser, type BrowserContext } from "@playwright/test";
import { watchPolicyViolations } from "./policy-violations.ts";

const specs = path.join(import.meta.dirname, "specs");

let browser: Browser;

before(async () => {
  browser = await chromium.launch();
});

after(async () => {
  await browser.close();
});

async function watched(): Promise<{ context: BrowserContext; violations: string[] }> {
  const context = await browser.newContext();
  const violations: string[] = [];
  await watchPolicyViolations(context, violations);
  return { context, violations };
}

/**
 * Opens a page at a made-up address that the browser never reaches. `setContent` would not do: it rewrites the open
 * document, which drops the listener that the watcher added, and a real page does not do that.
 */
async function openPage(context: BrowserContext, body: string, policy?: string) {
  await context.route("http://page.test/", (route) =>
    route.fulfill({
      contentType: "text/html",
      headers: policy ? { "Content-Security-Policy": policy } : {},
      body,
    }),
  );
  const page = await context.newPage();
  await page.goto("http://page.test/");
  return page;
}

async function eventually(check: () => void) {
  const deadline = Date.now() + 5000;
  for (;;) {
    try {
      check();
      return;
    } catch (error) {
      if (Date.now() > deadline) throw error;
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
  }
}

test("a page whose policy blocks an inline script records one violation", async () => {
  const { context, violations } = await watched();
  try {
    await openPage(context, "<script>1</script>", "script-src 'self'");
    await eventually(() => assert.equal(violations.length, 1));
    assert.match(violations[0], /^script-src/);
  } finally {
    await context.close();
  }
});

test("a page without a policy records no violation", async () => {
  const { context, violations } = await watched();
  try {
    const page = await openPage(context, "<script>1</script>");
    await page.evaluate(() => new Promise((resolve) => setTimeout(resolve, 200)));
    assert.deepEqual(violations, []);
  } finally {
    await context.close();
  }
});

test("a context that is watched twice records a violation once", async () => {
  const { context, violations } = await watched();
  try {
    await watchPolicyViolations(context, violations);
    await openPage(context, "<script>1</script>", "script-src 'self'");
    await eventually(() => assert.equal(violations.length, 1));
  } finally {
    await context.close();
  }
});

test("no spec imports test from @playwright/test, so every spec runs through the shared fixture", async () => {
  const importsTest = /import\s*\{[^}]*\btest\b[^}]*\}\s*from\s*['"]@playwright\/test['"]/;
  const files = (await readdir(specs)).filter((file) => file.endsWith(".ts"));
  assert.ok(files.length > 0);
  for (const file of files) {
    const source = await readFile(path.join(specs, file), "utf8");
    assert.doesNotMatch(source, importsTest, `${file} imports test from @playwright/test. Import it from ../test.`);
  }
});
