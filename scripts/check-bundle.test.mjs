import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { randomBytes } from "node:crypto";
import { afterEach, beforeEach, describe, test } from "node:test";
import { fileURLToPath } from "node:url";
import { gzipSync } from "node:zlib";
import { checkBundle } from "./check-bundle.mjs";

const script = fileURLToPath(new URL("./check-bundle.mjs", import.meta.url));
const budgetFile = fileURLToPath(
  new URL("../web/bundle-budget.json", import.meta.url),
);

let root;

/** Random bytes do not compress, so the gzipped size of a file is about its length. */
function write(file, bytes) {
  mkdirSync(path.dirname(path.join(root, file)), { recursive: true });
  writeFileSync(
    path.join(root, file),
    typeof bytes === "number" ? randomBytes(bytes) : bytes,
  );
}

function index({ preload = [] } = {}) {
  return [
    "<!doctype html><html><head>",
    '<script type="module" crossorigin src="/assets/index-AAA.js"></script>',
    ...preload.map(
      (file) => `<link rel="modulepreload" crossorigin href="/assets/${file}">`,
    ),
    '</head><body><div id="root"></div></body></html>',
  ].join("\n");
}

const budget = {
  mainBaselineBytes: 10_000,
  allowedGrowthBytes: 2_000,
  chartChunk: "ChartDrawing-",
  designChunk: "StudioPageDesign-",
};

beforeEach(() => {
  root = mkdtempSync(path.join(tmpdir(), "check-bundle-"));
});

afterEach(() => rmSync(root, { recursive: true, force: true }));

describe("checkBundle", () => {
  test("passes when the main bundle is within budget and the chart has its own chunk", () => {
    write("index.html", index({ preload: ["vendor-BBB.js"] }));
    write("assets/index-AAA.js", 7_000);
    write("assets/vendor-BBB.js", 4_000);
    write("assets/ChartDrawing-CCC.js", 20_000);
    write("assets/StudioPageDesign-EEE.js", 30_000);

    const { mainBytes, chartBytes, problems } = checkBundle(root, budget);

    assert.deepEqual(problems, []);
    // The entry script and the preloaded script count, the lazy chart chunk does not.
    assert.ok(
      mainBytes > 11_000 && mainBytes < 11_200,
      `main bundle ${mainBytes}`,
    );
    assert.ok(
      chartBytes > 20_000 && chartBytes < 20_200,
      `chart chunk ${chartBytes}`,
    );
  });

  test("measures the gzipped size, not the file size", () => {
    write("index.html", index());
    write("assets/index-AAA.js", "axis ".repeat(100_000));
    write("assets/ChartDrawing-CCC.js", "chart");
    write("assets/StudioPageDesign-EEE.js", "design");

    const { mainBytes, problems } = checkBundle(root, budget);

    assert.deepEqual(problems, []);
    assert.equal(mainBytes, gzipSync("axis ".repeat(100_000)).length);
  });

  test("fails when the main bundle grows past its baseline plus the allowed growth", () => {
    write("index.html", index({ preload: ["vendor-BBB.js"] }));
    write("assets/index-AAA.js", 7_000);
    write("assets/vendor-BBB.js", 6_500);
    write("assets/ChartDrawing-CCC.js", 20_000);
    write("assets/StudioPageDesign-EEE.js", 30_000);

    const { problems } = checkBundle(root, budget);

    assert.equal(problems.length, 1);
    assert.match(problems[0], /main bundle is .* over its budget/);
  });

  test("fails when there is no separate chart chunk", () => {
    write("index.html", index());
    write("assets/index-AAA.js", 7_000);
    write("assets/other-DDD.js", 20_000);
    write("assets/StudioPageDesign-EEE.js", 30_000);

    const { chartBytes, problems } = checkBundle(root, budget);

    assert.equal(chartBytes, 0);
    assert.equal(problems.length, 1);
    assert.match(problems[0], /no chart chunk/);
  });

  test("fails when the chart code is among the main bundle files", () => {
    write("index.html", index({ preload: ["ChartDrawing-CCC.js"] }));
    write("assets/index-AAA.js", 3_000);
    write("assets/ChartDrawing-CCC.js", 4_000);
    write("assets/StudioPageDesign-EEE.js", 30_000);

    const { problems } = checkBundle(root, budget);

    assert.equal(problems.length, 1);
    assert.match(
      problems[0],
      /ChartDrawing-CCC\.js is part of the main bundle/,
    );
  });

  test("fails when there is no separate page design chunk", () => {
    write("index.html", index());
    write("assets/index-AAA.js", 7_000);
    write("assets/ChartDrawing-CCC.js", 20_000);
    write("assets/other-DDD.js", 30_000);

    const { designBytes, problems } = checkBundle(root, budget);

    assert.equal(designBytes, 0);
    assert.equal(problems.length, 1);
    assert.match(problems[0], /no page design chunk/);
  });

  test("fails when the page designer is among the main bundle files", () => {
    write("index.html", index({ preload: ["StudioPageDesign-EEE.js"] }));
    write("assets/index-AAA.js", 3_000);
    write("assets/ChartDrawing-CCC.js", 20_000);
    write("assets/StudioPageDesign-EEE.js", 4_000);

    const { problems } = checkBundle(root, budget);

    assert.equal(problems.length, 1);
    assert.match(
      problems[0],
      /StudioPageDesign-EEE\.js is part of the main bundle/,
    );
  });
});

describe("check-bundle.mjs", () => {
  const run = (...args) =>
    spawnSync(process.execPath, [script, ...args], { encoding: "utf8" });

  test("prints both sizes and exits 0 for a bundle within budget", () => {
    write("index.html", index());
    write("assets/index-AAA.js", 7_000);
    write("assets/ChartDrawing-CCC.js", 20_000);
    write("assets/StudioPageDesign-EEE.js", 30_000);
    write("budget.json", JSON.stringify(budget));

    const result = run(root, "--budget", path.join(root, "budget.json"));

    assert.equal(result.status, 0, result.stderr);
    assert.match(
      result.stdout,
      /Main bundle: \d+\.\d KB gzipped, budget 11\.7 KB\./,
    );
    assert.match(result.stdout, /Chart chunk: \d+\.\d KB gzipped\./);
    assert.match(result.stdout, /Page design chunk: \d+\.\d KB gzipped\./);
  });

  test("exits 1 and names the problem when the main bundle is over budget", () => {
    write("index.html", index());
    write("assets/index-AAA.js", 13_000);
    write("assets/ChartDrawing-CCC.js", 20_000);
    write("assets/StudioPageDesign-EEE.js", 30_000);
    write("budget.json", JSON.stringify(budget));

    const result = run(root, "--budget", path.join(root, "budget.json"));

    assert.equal(result.status, 1);
    assert.match(result.stderr, /main bundle is .* over its budget/);
  });

  test("the committed budget file names the lazy chart and page design modules", () => {
    const committed = JSON.parse(readFileSync(budgetFile, "utf8"));

    assert.equal(committed.chartChunk, "ChartDrawing-");
    assert.equal(committed.designChunk, "StudioPageDesign-");
    assert.ok(committed.mainBaselineBytes > 0);
    assert.equal(committed.allowedGrowthBytes, 50 * 1024);
  });
});
