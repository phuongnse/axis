import fs from "node:fs";
import path from "node:path";

export interface CompareOptions {
  files?: string[];
  /** Ignores the `name` of page widgets, actions, columns and tabs. Studio's generated names differ from the samples' names. */
  ignorePageElementNames?: boolean;
  /**
   * Accepts a generated name, such as `table1` or `navigate2`, that the actual page has and the expected page lacks.
   * Studio names every widget, column, tab and action it adds.
   */
  generatedNames?: boolean;
}

type Seeds = Map<string, string>;

interface LabelKeys {
  /** The sample keys used at the same place as each built key. */
  pairs: Map<string, Set<string>>;
  /** The keys that a label of the built folder uses. */
  built: Set<string>;
  /** The keys that a label of the sample uses. */
  sample: Set<string>;
}

interface Pairings {
  seeds: Seeds;
  labels: LabelKeys;
}

type TextResource = Record<string, unknown> & { texts: Record<string, string> };

/**
 * Throws at the first difference. Top-level ids and key order are ignored. Seed ids pair by position. A label's text key
 * may differ from the sample's when the sample's key is used at the same place, and the texts are compared through that
 * pairing.
 */
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

  const shared = expectedFiles.filter((file) => actualSet.has(file));
  const pairings: Pairings = {
    seeds: pairSeedRecordIds(expected, actual, shared),
    labels: pairLabelKeys(expected, actual, shared),
  };

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
      if (isTextResource(expectedJson) && isTextResource(actualJson)) {
        const { texts: expectedTexts, ...expectedRest } = expectedJson;
        const { texts: actualTexts, ...actualRest } = actualJson;
        compareValue(file, "", expectedRest, actualRest, pairings, false);
        compareTexts(file, expectedTexts, actualTexts, pairings.labels);
      } else {
        compareValue(file, "", expectedJson, actualJson, pairings, options.generatedNames === true && file.startsWith("pages/"));
      }
    } else {
      const expectedText = fs.readFileSync(path.join(expected, file), "utf8");
      const actualText = fs.readFileSync(path.join(actual, file), "utf8");
      if (expectedText !== actualText) fail(file, "", JSON.stringify(expectedText), JSON.stringify(actualText));
    }
  }
}

/**
 * The keys that a journey's text step adds to the texts. These are the keys that a journey picks in a label, and the keys
 * that no label uses. Every other label is typed into its label, which makes the key it uses.
 */
export function textEditorKeys(sample: string): Set<string> {
  const picked = new Set<string>();
  const used = new Set<string>();
  const keys = new Set<string>();
  for (const file of listFiles(sample)) {
    if (!file.endsWith(".json")) continue;
    const json = readJson(path.join(sample, file));
    if (isTextResource(json)) {
      for (const key of Object.keys(json.texts)) keys.add(key);
      continue;
    }
    for (const [pointer, key] of labelsIn(json)) {
      used.add(key);
      if (isPickedLabel(file, pointer)) picked.add(key);
    }
  }
  return new Set([...keys].filter((key) => picked.has(key) || !used.has(key)));
}

function isPickedLabel(file: string, pointer: string): boolean {
  if (file.startsWith("entities/")) return pointer.startsWith("/fields/");
  if (file.startsWith("processes/")) return /^\/steps\/\d+(\/outcomes\/\d+)?\/label\/textKey$/.test(pointer);
  return false;
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

function pairLabelKeys(expected: string, actual: string, sharedFiles: string[]): LabelKeys {
  const labels: LabelKeys = { pairs: new Map(), built: new Set(), sample: new Set() };
  for (const file of sharedFiles) {
    if (!file.endsWith(".json")) continue;
    const expectedLabels = labelsIn(readJson(path.join(expected, file)));
    const actualLabels = labelsIn(readJson(path.join(actual, file)));
    for (const [pointer, sampleKey] of expectedLabels) {
      labels.sample.add(sampleKey);
      const builtKey = actualLabels.get(pointer);
      if (builtKey === undefined) continue;
      const pair = labels.pairs.get(builtKey) ?? new Set<string>();
      pair.add(sampleKey);
      labels.pairs.set(builtKey, pair);
    }
    for (const builtKey of actualLabels.values()) labels.built.add(builtKey);
  }
  return labels;
}

/** The text key of each label in a file, by the pointer of the key, such as `/steps/0/label/textKey`. */
function labelsIn(value: unknown, pointer = "", found = new Map<string, string>()): Map<string, string> {
  if (Array.isArray(value)) {
    value.forEach((child, index) => labelsIn(child, `${pointer}/${index}`, found));
  } else if (isObject(value)) {
    for (const [key, child] of Object.entries(value)) {
      const childPointer = `${pointer}/${escapePointer(key)}`;
      if (key === "textKey" && typeof child === "string") found.set(childPointer, child);
      else labelsIn(child, childPointer, found);
    }
  }
  return found;
}

function recordId(record: unknown): string | undefined {
  return isObject(record) && typeof record.id === "string" ? record.id : undefined;
}

function compareValue(
  file: string,
  pointer: string,
  expected: unknown,
  actual: unknown,
  pairings: Pairings,
  generatedNames: boolean,
): void {
  if (kindOf(expected) !== kindOf(actual)) {
    fail(file, pointer, JSON.stringify(expected), JSON.stringify(actual));
  }

  if (Array.isArray(expected) && Array.isArray(actual)) {
    const shared = Math.min(expected.length, actual.length);
    for (let index = 0; index < shared; index++) {
      compareValue(file, `${pointer}/${index}`, expected[index], actual[index], pairings, generatedNames);
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
      compareValue(file, child, expected[key], actual[key], pairings, generatedNames);
    }
    for (const key of Object.keys(actual).sort()) {
      if (!Object.hasOwn(expected, key)) {
        if (generatedNames && pointer !== "" && key === "name" && /^[A-Za-z]+\d+$/.test(String(actual[key]))) continue;
        fail(file, `${pointer}/${escapePointer(key)}`, "missing", JSON.stringify(actual[key]));
      }
    }
    return;
  }

  if (typeof expected === "string" && typeof actual === "string") {
    if (pointer.endsWith("/textKey") && pairings.labels.pairs.get(actual)?.has(expected)) return;
    if (actual !== (pairings.seeds.get(expected) ?? expected)) {
      fail(file, pointer, JSON.stringify(expected), JSON.stringify(actual));
    }
    return;
  }

  if (expected !== actual) fail(file, pointer, JSON.stringify(expected), JSON.stringify(actual));
}

function compareTexts(
  file: string,
  expected: Record<string, string>,
  actual: Record<string, string>,
  labels: LabelKeys,
): void {
  const covered = new Set<string>();
  const builtKeys = new Set([...Object.keys(actual), ...labels.built]);
  for (const key of [...builtKeys].sort()) {
    const pointer = `/texts/${escapePointer(key)}`;
    if (!labels.built.has(key) && labels.sample.has(key)) {
      fail(file, pointer, "a key that a label uses", "an unused key");
    }
    for (const sampleKey of labels.pairs.get(key) ?? [key]) {
      covered.add(sampleKey);
      if (!Object.hasOwn(expected, sampleKey)) {
        fail(file, pointer, `a text for ${sampleKey} in the sample`, describe(actual[key]));
      }
      if (actual[key] !== expected[sampleKey]) {
        fail(file, pointer, JSON.stringify(expected[sampleKey]), describe(actual[key]));
      }
    }
  }
  for (const sampleKey of Object.keys(expected).sort()) {
    if (!covered.has(sampleKey)) {
      fail(file, `/texts/${escapePointer(sampleKey)}`, JSON.stringify(expected[sampleKey]), "missing");
    }
  }
}

function describe(value: unknown): string {
  return value === undefined ? "missing" : JSON.stringify(value);
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

function isTextResource(value: unknown): value is TextResource {
  return isObject(value) && value.kind === "text" && isObject(value.texts);
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
