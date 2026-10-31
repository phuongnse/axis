import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import path from "node:path";
import { test } from "node:test";

const specs = path.join(import.meta.dirname, "specs");

// The one spec whose serial group reactivates the purchase request sample on purpose.
const reactivates = "purchase-request-release.spec.ts";

test("no spec but purchase-request-release starts a server that reactivates the purchase request sample", async () => {
  const files = (await readdir(specs)).filter((file) => file.endsWith(".ts"));
  let calls = 0;
  for (const file of files) {
    const source = await readFile(path.join(specs, file), "utf8");
    for (const [call] of source.matchAll(/startServer\(\s*\{[^}]*\}/g)) {
      calls++;
      if (file === reactivates) continue;
      assert.match(
        call,
        /ActivateOnStartup__0/,
        `${file} starts a server without ActivateOnStartup__0. The server would reactivate the purchase request sample in every tenant while purchase-request-release runs. Set it to e2eApp from ../server-settings.`,
      );
    }
  }
  assert.ok(calls > 0, "the scan found no startServer call, so it checks nothing");
});
