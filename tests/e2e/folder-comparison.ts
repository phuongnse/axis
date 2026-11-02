import fs from "node:fs";
import path from "node:path";

export interface CompareOptions {
  files?: string[];
  /** Ignores the `name` of page widgets, actions, columns and tabs, which Studio does not write yet. */
  ignorePageElementNames?: boolean;
}

type Seeds = Map<string, string>;

/** Throws at the first difference. Top-level ids and key order are ignored; seed ids pair by position. */
export function compareApplicationFolders(expected: string, actual: string, options: CompareOptions = {}): void {
  const expectedFiles = listFiles(expected);
  const actualFiles = listFiles(actual);
  const expectedSet = new Set(expectedFiles);
  const actualSet = new Set(actualFiles);

  let files: string[];
  if (options.files) {
    for (const file of options.files) {
      if (!expectedSet.has(file)) throw new Error(`${file}: missing in expected folder`);
      if (!actualSet.has(file)) throw new Error(`${file}: missing in actual folder`);
    }
    files = [...options.files].sort();
  } else {
    for (const file of expectedFiles) {
      if (!actualSet.has(file)) throw new Error(`${file}: missing`);
    }
    for (const file of actualFiles) {
      if (!expectedSet.has(file)) throw new Error(`${file}: extra file`);
    }
    files = expectedFiles;
  }

  const seeds = pairSeedRecordIds(expected, actual, expectedFiles.filter((file) => actualSet.has(file)));

  for (const file of files) {
    if (file.endsWith(".json")) {
      const expectedJson = readJson(path.join(expected, file));
      const actualJson = readJson(path.join(actual, file));
      if (isObject(expectedJson)) delete expectedJson.id;
      if (isObject(actualJson)) delete actualJson.id;
      if (options.ignorePageElementNames) {
        dropPageElementNames(expectedJson);
        dropPageElementNames(actualJson);
      }
      compareValue(file, "", expectedJson, actualJson, seeds);
    } else {
      const expectedText = fs.readFileSync(path.join(expected, file), "utf8");
      const actualText = fs.readFileSync(path.join(actual, file), "utf8");
      if (expectedText !== actualText) fail(file, "", JSON.stringify(expectedText), JSON.stringify(actualText));
    }
  }
}

function listFiles(root: string): string[] {
  const files: string[] = [];
  const visit = (dir: string, prefix: string) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const relative = prefix ? `${prefix}/${entry.name}` : entry.name;
      if (entry.isDirectory()) visit(path.join(dir, entry.name), relative);
      else if (entry.isFile()) files.push(relative);
    }
  };
  visit(root, "");
  return files.sort();
}

function pairSeedRecordIds(expected: string, actual: string, sharedFiles: string[]): Seeds {
  const seeds: Seeds = new Map();
  for (const file of sharedFiles) {
    if (!file.endsWith(".json")) continue;
    const expectedJson = readJson(path.join(expected, file));
    const actualJson = readJson(path.join(actual, file));
    if (!isObject(expectedJson) || expectedJson.kind !== "seed" || !isObject(actualJson)) continue;
    const expectedRecords = expectedJson.records;
    const actualRecords = actualJson.records;
    if (!Array.isArray(expectedRecords) || !Array.isArray(actualRecords)) continue;
    const count = Math.min(expectedRecords.length, actualRecords.length);
    for (let index = 0; index < count; index++) {
      const expectedId = recordId(expectedRecords[index]);
      const actualId = recordId(actualRecords[index]);
      if (expectedId !== undefined && actualId !== undefined) seeds.set(expectedId, actualId);
    }
  }
  return seeds;
}

function recordId(record: unknown): string | undefined {
  return isObject(record) && typeof record.id === "string" ? record.id : undefined;
}

function compareValue(file: string, pointer: string, expected: unknown, actual: unknown, seeds: Seeds): void {
  if (kindOf(expected) !== kindOf(actual)) {
    fail(file, pointer, JSON.stringify(expected), JSON.stringify(actual));
  }

  if (Array.isArray(expected) && Array.isArray(actual)) {
    const shared = Math.min(expected.length, actual.length);
    for (let index = 0; index < shared; index++) {
      compareValue(file, `${pointer}/${index}`, expected[index], actual[index], seeds);
    }
    if (expected.length !== actual.length) {
      fail(file, pointer, `${expected.length} items`, `${actual.length} items`);
    }
    return;
  }

  if (isObject(expected) && isObject(actual)) {
    for (const key of Object.keys(expected).sort()) {
      const child = `${pointer}/${escapePointer(key)}`;
      if (!Object.hasOwn(actual, key)) fail(file, child, JSON.stringify(expected[key]), "missing");
      compareValue(file, child, expected[key], actual[key], seeds);
    }
    for (const key of Object.keys(actual).sort()) {
      if (!Object.hasOwn(expected, key)) {
        fail(file, `${pointer}/${escapePointer(key)}`, "missing", JSON.stringify(actual[key]));
      }
    }
    return;
  }

  if (typeof expected === "string" && typeof actual === "string") {
    if (actual !== (seeds.get(expected) ?? expected)) {
      fail(file, pointer, JSON.stringify(expected), JSON.stringify(actual));
    }
    return;
  }

  if (expected !== actual) fail(file, pointer, JSON.stringify(expected), JSON.stringify(actual));
}

function fail(file: string, pointer: string, expected: string, actual: string): never {
  throw new Error(`${file} at ${pointer || "(root)"}: expected ${expected}, got ${actual}`);
}

function readJson(file: string): unknown {
  return JSON.parse(fs.readFileSync(file, "utf8"));
}

function kindOf(value: unknown): string {
  if (value === null) return "null";
  if (Array.isArray(value)) return "array";
  return typeof value;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function escapePointer(key: string): string {
  return key.replaceAll("~", "~0").replaceAll("/", "~1");
}

function dropPageElementNames(page: unknown): void {
  if (isObject(page) && page.kind === "page") dropWidgetNames(page.widgets);
}

function dropWidgetNames(widgets: unknown): void {
  for (const widget of objectsIn(widgets)) {
    delete widget.name;
    for (const key of ["actions", "rowActions", "toolbarActions"]) {
      for (const action of objectsIn(widget[key])) delete action.name;
    }
    dropWidgetNames(widget.widgets);
    for (const column of [...objectsIn(widget.columns), ...objectsIn(widget.tabs)]) {
      delete column.name;
      dropWidgetNames(column.widgets);
    }
  }
}

function objectsIn(value: unknown): Record<string, unknown>[] {
  return Array.isArray(value) ? value.filter(isObject) : [];
}
