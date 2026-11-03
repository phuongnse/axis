import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { afterEach, beforeEach, describe, test } from "node:test";
import { fileURLToPath } from "node:url";
import { npmPackages, nuspecLicence, writeLicences } from "./write-licences.mjs";

const script = fileURLToPath(new URL("./write-licences.mjs", import.meta.url));

let root;

function write(file, text) {
  mkdirSync(path.dirname(path.join(root, file)), { recursive: true });
  writeFileSync(path.join(root, file), text);
}

const lock = {
  packages: {
    "": { name: "axis-web", version: "0.0.0" },
    "node_modules/react": { version: "19.0.0", license: "MIT" },
    "node_modules/@ant-design/icons": { version: "6.0.0", license: "MIT" },
    "node_modules/typescript": { version: "5.9.0", license: "Apache-2.0", dev: true },
    "node_modules/axis-local": { resolved: "local", link: true },
    "node_modules/no-licence": { version: "1.0.0" },
    "node_modules/a/node_modules/react": { version: "18.0.0", license: "MIT" },
    "node_modules/alias": { name: "real-name", version: "2.0.0", license: "ISC" },
  },
};

const nuspec = (license) => `<?xml version="1.0"?><package><metadata><id>x</id>${license}</metadata></package>`;

/** A NuGet cache with the packages Alpha (an expression), Beta (a licenseUrl) and Gamma (nothing), and the assets file of a project. */
function project(name, ids) {
  const cache = path.join(root, "nuget") + path.sep;
  write("nuget/alpha/1.0.0/alpha.nuspec", nuspec('<license type="expression">Apache-2.0</license><licenseUrl>https://aka.ms/deprecateLicenseUrl</licenseUrl>'));
  write("nuget/beta/2.0.0/beta.nuspec", nuspec("<licenseUrl>https://example.com/beta-licence</licenseUrl>"));
  write("nuget/gamma/3.0.0/gamma.nuspec", nuspec(""));
  const libraries = {};
  for (const id of ids) {
    const version = { Alpha: "1.0.0", Beta: "2.0.0", Gamma: "3.0.0" }[id];
    libraries[`${id}/${version}`] = { type: "package", path: `${id.toLowerCase()}/${version}` };
  }
  libraries["Axis.Core/1.0.0"] = { type: "project", path: "../Axis.Core/Axis.Core.csproj" };
  write(`src/${name}/obj/project.assets.json`, JSON.stringify({ packageFolders: { [cache]: {} }, libraries }));
}

function sources({ nuget = true } = {}) {
  write("web/package-lock.json", JSON.stringify(lock));
  project("Axis.Server", nuget ? ["Alpha", "Beta"] : []);
  project("Axis.Worker", nuget ? ["Alpha", "Gamma"] : []);
  project("Axis.Admin", nuget ? ["Beta"] : []);
}

const readJson = (file) => JSON.parse(readFileSync(path.join(root, file), "utf8"));

beforeEach(() => {
  root = mkdtempSync(path.join(tmpdir(), "write-licences-"));
});

afterEach(() => rmSync(root, { recursive: true, force: true }));

describe("npmPackages", () => {
  test("leaves out the root, links and dev packages and names a nested or aliased package", () => {
    const names = npmPackages(lock).map(({ name, version }) => `${name}@${version}`);

    assert.deepEqual(names, ["react@19.0.0", "@ant-design/icons@6.0.0", "no-licence@1.0.0", "react@18.0.0", "real-name@2.0.0"]);
  });

  test("shows a package without a license field as UNKNOWN", () => {
    assert.equal(npmPackages(lock).find(({ name }) => name === "no-licence").licence, "UNKNOWN");
  });
});

describe("nuspecLicence", () => {
  test("prefers the expression, then the file name, then the URL, then UNKNOWN", () => {
    assert.equal(nuspecLicence(nuspec('<license type="expression">MIT</license><licenseUrl>https://x</licenseUrl>')), "MIT");
    assert.equal(nuspecLicence(nuspec('<license type="file">LICENSE.txt</license><licenseUrl>https://x</licenseUrl>')), "LICENSE.txt");
    assert.equal(nuspecLicence(nuspec("<licenseUrl>https://x/a?b=1&amp;c=2</licenseUrl>")), "https://x/a?b=1&c=2");
    assert.equal(nuspecLicence(nuspec("")), "UNKNOWN");
  });
});

describe("writeLicences", () => {
  const outputs = { outPublic: () => path.join(root, "out", "wwwroot", "licences.json"), outPrivate: () => path.join(root, "out", "licences.json") };
  const run = () => writeLicences({ root, outPublic: outputs.outPublic(), outPrivate: outputs.outPrivate() });

  test("leaves a dev package out of both files", () => {
    sources();

    const { problems } = run();

    assert.deepEqual(problems, []);
    for (const file of ["out/wwwroot/licences.json", "out/licences.json"]) {
      assert.ok(!readJson(file).npm.some(({ name }) => name === "typescript"), file);
      assert.ok(readJson(file).npm.some(({ name }) => name === "react"), file);
    }
  });

  test("takes each NuGet licence from its nuspec and lists a package once, sorted by name", () => {
    sources();

    run();

    assert.deepEqual(readJson("out/licences.json").nuget, [
      { name: "Alpha", version: "1.0.0", licence: "Apache-2.0" },
      { name: "Beta", version: "2.0.0", licence: "https://example.com/beta-licence" },
      { name: "Gamma", version: "3.0.0", licence: "UNKNOWN" },
    ]);
  });

  test("gives a version to every private entry and to no public entry", () => {
    sources();

    run();

    const publicFile = readJson("out/wwwroot/licences.json");
    const privateFile = readJson("out/licences.json");
    for (const list of ["npm", "nuget"]) {
      assert.ok(publicFile[list].length > 0);
      assert.ok(publicFile[list].every((entry) => !("version" in entry) && typeof entry.licence === "string"));
      assert.ok(privateFile[list].every((entry) => typeof entry.version === "string" && entry.version !== ""));
      assert.deepEqual(publicFile[list].map(({ name }) => name), privateFile[list].map(({ name }) => name));
    }
  });

  test("reports a missing source file", () => {
    write("web/package-lock.json", JSON.stringify(lock));

    const { problems } = run();

    assert.equal(problems.length, 3);
    assert.match(problems[0], /project\.assets\.json is missing/);
  });
});

describe("the script", () => {
  const exec = () =>
    spawnSync(process.execPath, [script, path.join(root, "out", "public.json"), path.join(root, "out", "private.json")], {
      encoding: "utf8",
      env: { ...process.env, AXIS_LICENCES_ROOT: root },
    });

  test("exits 0 and prints the counts when both lists have entries", () => {
    sources();

    const result = exec();

    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /5 npm packages, 3 NuGet packages/);
  });

  test("exits 1 when the NuGet list is empty", () => {
    sources({ nuget: false });

    const result = exec();

    assert.equal(result.status, 1);
    assert.match(result.stderr, /no NuGet packages/);
  });
});
