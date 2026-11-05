import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, test } from "node:test";
import { fileURLToPath } from "node:url";

// Reads web/package-lock.json, so it needs no network, and fails when the SPA resolves a package to a
// version that one of the high advisories below lists as vulnerable. The ranges are the advisories' own:
// [first vulnerable, first patched) for each release line. The dependency scan (scripts/scan.mjs) finds
// new advisories, this test keeps these ones fixed.
const lock = JSON.parse(
  readFileSync(
    fileURLToPath(new URL("../web/package-lock.json", import.meta.url)),
    "utf8",
  ),
);

const advisories = {
  undici: [
    // GHSA-xjj6-f33q-929c, GHSA-4w7w-5xrc-9h26, GHSA-2m5v-pch6-74hx and GHSA-qrxm-fqhp-jwvm
    {
      advisories: [
        "GHSA-xjj6-f33q-929c",
        "GHSA-4w7w-5xrc-9h26",
        "GHSA-2m5v-pch6-74hx",
        "GHSA-qrxm-fqhp-jwvm",
      ],
      from: "4.0.0",
      patched: "6.29.1",
    },
    {
      advisories: [
        "GHSA-xjj6-f33q-929c",
        "GHSA-4w7w-5xrc-9h26",
        "GHSA-2m5v-pch6-74hx",
        "GHSA-qrxm-fqhp-jwvm",
      ],
      from: "7.0.0",
      patched: "7.30.1",
    },
    {
      advisories: [
        "GHSA-xjj6-f33q-929c",
        "GHSA-4w7w-5xrc-9h26",
        "GHSA-2m5v-pch6-74hx",
        "GHSA-qrxm-fqhp-jwvm",
      ],
      from: "8.0.0",
      patched: "8.12.2",
    },
  ],
  vite: [
    { advisories: ["GHSA-587h-7wmj-h5pm"], from: "0.0.0", patched: "6.4.4" },
    { advisories: ["GHSA-587h-7wmj-h5pm"], from: "7.0.0", patched: "7.3.7" },
    { advisories: ["GHSA-587h-7wmj-h5pm"], from: "8.0.0", patched: "8.1.6" },
    { advisories: ["GHSA-587h-7wmj-h5pm"], from: "8.2.0", patched: "8.2.4" },
    { advisories: ["GHSA-587h-7wmj-h5pm"], from: "8.3.0", patched: "8.3.3" },
  ],
};

function parts(version) {
  return version.split("-")[0].split(".").map(Number);
}

function compare(a, b) {
  const [x, y] = [parts(a), parts(b)];
  for (let i = 0; i < 3; i++) if (x[i] !== y[i]) return x[i] - y[i];
  return 0;
}

function vulnerableTo(name, version) {
  return advisories[name]
    .filter(
      (range) =>
        compare(version, range.from) >= 0 &&
        compare(version, range.patched) < 0,
    )
    .flatMap((range) => range.advisories);
}

function resolved(name) {
  return Object.entries(lock.packages)
    .filter(
      ([location]) =>
        location === `node_modules/${name}` ||
        location.endsWith(`/node_modules/${name}`),
    )
    .map(([location, entry]) => ({ location, version: entry.version }));
}

describe("web/package-lock.json", () => {
  test("recognises the versions that the dependency scan failed on", () => {
    assert.deepEqual(
      vulnerableTo("undici", "8.11.2").sort(),
      advisories.undici[2].advisories.slice().sort(),
    );
    assert.deepEqual(vulnerableTo("vite", "8.3.2"), ["GHSA-587h-7wmj-h5pm"]);
    assert.deepEqual(vulnerableTo("undici", "8.12.2"), []);
    assert.deepEqual(vulnerableTo("vite", "8.3.3"), []);
  });

  test("resolves undici to a version patched for its four high advisories", () => {
    const found = resolved("undici");
    assert.notEqual(found.length, 0, "undici is not in the lock file");
    for (const { location, version } of found) {
      assert.deepEqual(
        vulnerableTo("undici", version),
        [],
        `${location} ${version}`,
      );
    }
  });

  test("resolves vite to a version patched for GHSA-587h-7wmj-h5pm", () => {
    const found = resolved("vite");
    assert.notEqual(found.length, 0, "vite is not in the lock file");
    for (const { location, version } of found) {
      assert.deepEqual(
        vulnerableTo("vite", version),
        [],
        `${location} ${version}`,
      );
    }
  });
});
