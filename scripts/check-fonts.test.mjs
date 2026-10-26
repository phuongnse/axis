import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { afterEach, beforeEach, describe, test } from "node:test";
import { fileURLToPath } from "node:url";
import { checkFonts } from "./check-fonts.mjs";

const script = fileURLToPath(new URL("./check-fonts.mjs", import.meta.url));

let root;

function write(file, text = "font") {
  mkdirSync(path.dirname(path.join(root, file)), { recursive: true });
  writeFileSync(path.join(root, file), text);
}

const license = "SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007\n";

const fontNames = [
  "ibm-plex-sans-latin-wght-normal-AAA.woff2",
  "ibm-plex-sans-latin-ext-wght-normal-BBB.woff2",
  "ibm-plex-sans-vietnamese-wght-normal-CCC.woff2",
  "ibm-plex-mono-latin-400-normal-DDD.woff2",
  "ibm-plex-mono-latin-ext-400-normal-EEE.woff2",
  "ibm-plex-mono-vietnamese-400-normal-FFF.woff2",
];

/** A SPA folder with the three subsets of both families and both license texts, less what a test leaves out. */
function spa({ without = [] } = {}) {
  for (const name of fontNames.filter((candidate) => !without.includes(candidate))) {
    write(`assets/${name}`);
  }
  if (!without.includes("OFL-IBM-Plex-Sans.txt")) write("fonts/OFL-IBM-Plex-Sans.txt", license);
  if (!without.includes("OFL-IBM-Plex-Mono.txt")) write("fonts/OFL-IBM-Plex-Mono.txt", license);
}

beforeEach(() => {
  root = mkdtempSync(path.join(tmpdir(), "check-fonts-"));
});

afterEach(() => rmSync(root, { recursive: true, force: true }));

describe("checkFonts", () => {
  test("passes for the three subsets of both families with the license texts", () => {
    spa();

    const { files, problems } = checkFonts(root);

    assert.deepEqual(problems, []);
    assert.deepEqual(files, [...fontNames].sort());
  });

  test("fails when the Vietnamese subset of IBM Plex Sans is missing", () => {
    spa({ without: ["ibm-plex-sans-vietnamese-wght-normal-CCC.woff2"] });

    const { problems } = checkFonts(root);

    assert.equal(problems.length, 1);
    assert.match(problems[0], /no IBM Plex Sans vietnamese font file/);
  });

  test("fails when a font file is not a .woff2 file", () => {
    spa({ without: ["ibm-plex-mono-latin-ext-400-normal-EEE.woff2"] });
    write("assets/ibm-plex-mono-latin-ext-400-normal-EEE.woff");

    const { problems } = checkFonts(root);

    assert.equal(problems.length, 1);
    assert.match(problems[0], /no IBM Plex Mono latin-ext font file/);
  });

  test("fails when there are no font files at all", () => {
    write("fonts/OFL-IBM-Plex-Sans.txt", license);
    write("fonts/OFL-IBM-Plex-Mono.txt", license);

    const { files, problems } = checkFonts(root);

    assert.deepEqual(files, []);
    assert.equal(problems.length, 6);
  });

  test("fails when another subset ships", () => {
    spa();
    write("assets/ibm-plex-sans-cyrillic-wght-normal-XXX.woff2");

    const { problems } = checkFonts(root);

    assert.equal(problems.length, 1);
    assert.match(problems[0], /ibm-plex-sans-cyrillic-wght-normal-XXX\.woff2 is not IBM Plex/);
  });

  test("fails when another font family ships", () => {
    spa();
    write("assets/inter-latin-wght-normal-XXX.woff2");

    const { problems } = checkFonts(root);

    assert.equal(problems.length, 1);
    assert.match(problems[0], /inter-latin-wght-normal-XXX\.woff2 is not IBM Plex/);
  });

  test("fails when a license text is missing", () => {
    spa({ without: ["OFL-IBM-Plex-Mono.txt"] });

    const { problems } = checkFonts(root);

    assert.equal(problems.length, 1);
    assert.match(problems[0], /IBM Plex Mono license text fonts\/OFL-IBM-Plex-Mono\.txt is missing/);
  });

  test("fails when a license file does not hold the license text", () => {
    spa();
    write("fonts/OFL-IBM-Plex-Sans.txt", "Some other text");

    const { problems } = checkFonts(root);

    assert.equal(problems.length, 1);
    assert.match(problems[0], /OFL-IBM-Plex-Sans\.txt does not hold the text/);
  });
});

describe("check-fonts.mjs", () => {
  const run = (...args) => spawnSync(process.execPath, [script, ...args], { encoding: "utf8" });

  test("prints the font files and exits 0 for a complete SPA", () => {
    spa();

    const result = run(root);

    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /Font files: 6\./);
    assert.match(result.stdout, /assets\/ibm-plex-sans-latin-wght-normal-AAA\.woff2/);
  });

  test("exits 1 and names the problem when a subset is missing", () => {
    spa({ without: ["ibm-plex-sans-latin-ext-wght-normal-BBB.woff2"] });

    const result = run(root);

    assert.equal(result.status, 1);
    assert.match(result.stderr, /no IBM Plex Sans latin-ext font file/);
  });
});
