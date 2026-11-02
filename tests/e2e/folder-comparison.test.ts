import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { after, test } from "node:test";
import { compareApplicationFolders } from "./folder-comparison.ts";

const sample = path.resolve(import.meta.dirname, "../../samples/apps/service-desk");
const scenario = "scenarios/agent-cannot-act-on-another-agents-ticket.json";
const seedFile = "seeds/categories.json";

const tempRoots: string[] = [];
after(() => {
  for (const root of tempRoots) fs.rmSync(root, { recursive: true, force: true });
});

type Json = Record<string, any>;

function copySample(): string {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "folder-comparison-"));
  tempRoots.push(root);
  const folder = path.join(root, "service-desk");
  fs.cpSync(sample, folder, { recursive: true });
  return folder;
}

function readJson(folder: string, file: string): Json {
  return JSON.parse(fs.readFileSync(path.join(folder, file), "utf8"));
}

function writeJson(folder: string, file: string, value: unknown): void {
  fs.writeFileSync(path.join(folder, file), `${JSON.stringify(value, null, 2)}\n`);
}

function rewriteJson(folder: string, file: string, change: (value: Json) => void): void {
  const value = readJson(folder, file);
  change(value);
  writeJson(folder, file, value);
}

function rewriteEveryJson(folder: string, change: (value: Json) => unknown): void {
  for (const file of fs.readdirSync(folder, { recursive: true }).map(String)) {
    if (file.endsWith(".json")) writeJson(folder, file, change(readJson(folder, file)));
  }
}

function filesIn(folder: string, dir: string): string[] {
  return fs.readdirSync(path.join(folder, dir)).map((name) => `${dir}/${name}`);
}

function seedRecordIds(folder: string): string[] {
  return readJson(folder, seedFile).records.map((record: Json) => record.id);
}

function replaceIds(folder: string, files: string[], replacements: Map<string, string>): void {
  for (const file of files) {
    let text = fs.readFileSync(path.join(folder, file), "utf8");
    for (const [from, to] of replacements) text = text.replaceAll(from, to);
    fs.writeFileSync(path.join(folder, file), text);
  }
}

function reverseKeys(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(reverseKeys);
  if (value !== null && typeof value === "object") {
    return Object.fromEntries(
      Object.entries(value)
        .reverse()
        .map(([key, child]) => [key, reverseKeys(child)]),
    );
  }
  return value;
}

test("new resource ids and reversed keys match the sample", () => {
  const copy = copySample();
  rewriteEveryJson(copy, (value) => reverseKeys("id" in value ? { ...value, id: crypto.randomUUID() } : value));
  assert.doesNotThrow(() => compareApplicationFolders(sample, copy));
});

test("new seed record ids replaced in the seed and every scenario match the sample", () => {
  const copy = copySample();
  const replacements = new Map<string, string>(
    seedRecordIds(copy).map((id): [string, string] => [id, crypto.randomUUID()]),
  );
  replaceIds(copy, [seedFile, ...filesIn(copy, "scenarios")], replacements);
  assert.doesNotThrow(() => compareApplicationFolders(sample, copy));
});

test("a seed record id replaced only in the seed fails at the scenario that uses it", () => {
  const copy = copySample();
  rewriteJson(copy, seedFile, (seed) => {
    seed.records[0].id = crypto.randomUUID();
  });
  assert.throws(
    () => compareApplicationFolders(sample, copy),
    /scenarios\/agent-cannot-act-on-another-agents-ticket\.json at \/steps\/2\/values\/category/,
  );
});

test("a changed value fails naming the file and the JSON pointer", () => {
  const copy = copySample();
  rewriteJson(copy, "entities/category.json", (entity) => {
    entity.fields[0].maxLength = 200;
  });
  assert.throws(
    () => compareApplicationFolders(sample, copy),
    /entities\/category\.json at \/fields\/0\/maxLength: expected 100, got 200/,
  );
});

test("a missing file fails naming that file", () => {
  const copy = copySample();
  fs.rmSync(path.join(copy, "texts/vi.json"));
  assert.throws(() => compareApplicationFolders(sample, copy), { message: "texts/vi.json: missing" });
});

test("an extra file fails naming that file", () => {
  const copy = copySample();
  fs.writeFileSync(path.join(copy, "extra.json"), "{}\n");
  assert.throws(() => compareApplicationFolders(sample, copy), { message: "extra.json: extra file" });
});

test("a subset ignores a changed file outside it", () => {
  const copy = copySample();
  rewriteJson(copy, "entities/category.json", (entity) => {
    entity.fields[0].maxLength = 200;
  });
  assert.doesNotThrow(() => compareApplicationFolders(sample, copy, { files: ["application.json"] }));
});

test("a subset fails when a named file is missing", () => {
  const copy = copySample();
  fs.rmSync(path.join(copy, "texts/vi.json"));
  assert.throws(
    () => compareApplicationFolders(sample, copy, { files: ["texts/vi.json"] }),
    { message: "texts/vi.json: missing in actual folder" },
  );
  assert.throws(
    () => compareApplicationFolders(sample, copy, { files: ["missing.json"] }),
    { message: "missing.json: missing in expected folder" },
  );
});

test("a subset pairs seed ids from a seed file outside it", () => {
  const seedOnly = copySample();
  rewriteJson(seedOnly, seedFile, (seed) => {
    seed.records[0].id = crypto.randomUUID();
  });
  assert.throws(
    () => compareApplicationFolders(sample, seedOnly, { files: [scenario] }),
    /scenarios\/agent-cannot-act-on-another-agents-ticket\.json at \/steps\/2\/values\/category/,
  );

  const both = copySample();
  const [oldId] = seedRecordIds(both);
  const newId = crypto.randomUUID();
  replaceIds(both, [seedFile, scenario], new Map([[oldId, newId]]));
  assert.doesNotThrow(() => compareApplicationFolders(sample, both, { files: [scenario] }));
});

test("a generated name on a page is accepted only when asked, and a different name never is", () => {
  const copy = copySample();
  rewriteJson(copy, "pages/all-tickets.json", (page) => {
    page.widgets[0].name = "table1";
  });
  assert.throws(() => compareApplicationFolders(sample, copy), /pages\/all-tickets\.json at \/widgets\/0\/name/);
  assert.doesNotThrow(() => compareApplicationFolders(sample, copy, { generatedNames: true }));

  rewriteJson(copy, "pages/all-tickets.json", (page) => {
    page.widgets[0].name = "Tickets";
  });
  assert.throws(
    () => compareApplicationFolders(sample, copy, { generatedNames: true }),
    /pages\/all-tickets\.json at \/widgets\/0\/name/,
  );
});

test("a generated name outside a page is not accepted", () => {
  const copy = copySample();
  rewriteJson(copy, scenario, (value) => {
    value.steps[0].name = "step1";
  });
  assert.throws(
    () => compareApplicationFolders(sample, copy, { generatedNames: true }),
    /scenarios\/agent-cannot-act-on-another-agents-ticket\.json at \/steps\/0\/name/,
  );
});
