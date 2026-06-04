#!/usr/bin/env node
/**
 * Ensures every ```mermaid block in docs/ starts with MERMAID_INIT.
 * Run from repo root: node docs/scripts/sync-mermaid-theme.mjs
 */
import { readFileSync, writeFileSync, readdirSync, statSync } from "fs";
import { join, relative } from "path";
import { MERMAID_INIT } from "../diagrams/mermaid-theme.mjs";

const docsRoot = join(import.meta.dirname, "..");
const skipFiles = new Set(["playbooks/mermaid.md"]);
const initLine = MERMAID_INIT;
const initRegex = /^%%\{init:[\s\S]*?\}%%[ \t]*(?:\r?\n[ \t]*)*/;

function walk(dir, files = []) {
  for (const name of readdirSync(dir)) {
    if (name.startsWith(".")) continue;
    const p = join(dir, name);
    const st = statSync(p);
    if (st.isDirectory()) walk(p, files);
    else if (name.endsWith(".md")) files.push(p);
  }
  return files;
}

function syncMermaidBlocks(content) {
  const fence = /```mermaid\n([\s\S]*?)```/g;
  let changed = false;
  const out = content.replace(fence, (_match, body) => {
    let next = body;
    if (initRegex.test(next.trimStart())) {
      next = next.replace(initRegex, `${initLine}\n`);
    } else {
      next = `${initLine}\n${next.replace(/^\n+/, "")}`;
    }
    if (next !== body) changed = true;
    return `\`\`\`mermaid\n${next}\`\`\``;
  });
  return { content: out, changed };
}

let updated = 0;
for (const file of walk(docsRoot)) {
  const normalizedRel = relative(docsRoot, file).replace(/\\/g, "/");
  if (skipFiles.has(normalizedRel)) continue;
  const raw = readFileSync(file, "utf8");
  const { content, changed } = syncMermaidBlocks(raw);
  if (changed) {
    writeFileSync(file, content, "utf8");
    console.log(`updated docs/${normalizedRel}`);
    updated++;
  }
}
console.log(updated ? `Done. ${updated} file(s).` : "All mermaid blocks already synced.");
