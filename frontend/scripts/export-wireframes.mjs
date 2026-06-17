#!/usr/bin/env node
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import esbuild from "esbuild";
import { JSDOM } from "jsdom";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(scriptDir, "..");
const repoRoot = path.resolve(frontendRoot, "..");
const docsRoot = path.join(repoRoot, "docs");

function repoPath(filePath) {
  return path.relative(repoRoot, filePath).replace(/\\/g, "/");
}

function normalizeRepoPath(filePath) {
  const normalized = filePath.replace(/\\/g, "/");
  return normalized.startsWith("./") ? normalized.slice(2) : normalized;
}

function readDirRecursive(root, extension) {
  const files = [];
  if (!fs.existsSync(root)) {
    return files;
  }

  for (const entry of fs.readdirSync(root, { withFileTypes: true })) {
    const fullPath = path.join(root, entry.name);
    if (entry.isDirectory()) {
      files.push(...readDirRecursive(fullPath, extension));
    } else if (entry.isFile() && entry.name.endsWith(extension)) {
      files.push(fullPath);
    }
  }
  return files;
}

function wireframeFiles() {
  const roots = [path.join(docsRoot, "wireframes"), path.join(docsRoot, "use-cases")];
  return roots
    .flatMap((root) => readDirRecursive(root, ".excalidraw"))
    .filter((filePath) => !repoPath(filePath).includes("architecture/diagrams"))
    .sort((left, right) => repoPath(left).localeCompare(repoPath(right)));
}

function changedRepoPaths() {
  const output = execFileSync("git", ["-C", repoRoot, "status", "--porcelain", "--", "docs"], {
    encoding: "utf8",
  });
  return output
    .split(/\r?\n/)
    .map((line) => line.trimEnd())
    .filter(Boolean)
    .map((line) => {
      const filePath = line.slice(3);
      return normalizeRepoPath(filePath.includes(" -> ") ? filePath.split(" -> ").pop() : filePath);
    });
}

function linkedWireframePaths(markdownRepoPath) {
  const markdownPath = path.join(repoRoot, markdownRepoPath);
  if (!fs.existsSync(markdownPath)) {
    return [];
  }

  const content = fs.readFileSync(markdownPath, "utf8");
  const paths = [];
  const linkPattern = /\]\(([^)]+\.excalidraw(?:#[^)]+)?)\)/g;
  let match;
  while ((match = linkPattern.exec(content)) !== null) {
    const href = match[1].split("#", 1)[0].trim();
    const resolved = href.startsWith("docs/")
      ? path.join(repoRoot, href)
      : path.join(path.dirname(markdownPath), href.replace(/^\.\//, ""));
    paths.push(repoPath(path.resolve(resolved)));
  }
  return paths;
}

function changedWireframeSet() {
  const changed = new Set();
  for (const changedPath of changedRepoPaths()) {
    if (changedPath.endsWith(".excalidraw")) {
      changed.add(changedPath);
    } else if (changedPath.endsWith(".svg")) {
      changed.add(`${changedPath.slice(0, -4)}.excalidraw`);
    } else if (changedPath.endsWith(".md")) {
      for (const linkedPath of linkedWireframePaths(changedPath)) {
        changed.add(linkedPath);
      }
    }
  }
  return changed;
}

function parseArgs(argv) {
  const args = { filter: "", changed: false };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === "--changed") {
      args.changed = true;
    } else if (arg === "-f" || arg === "--filter") {
      index += 1;
      if (!argv[index]) {
        throw new Error(`${arg} requires a value`);
      }
      args.filter = argv[index];
    } else {
      throw new Error(`unknown argument: ${arg}`);
    }
  }
  return args;
}

function selectedWireframes(args) {
  const changed = args.changed ? changedWireframeSet() : null;
  return wireframeFiles().filter((filePath) => {
    const rel = repoPath(filePath);
    if (args.filter && !rel.includes(args.filter)) {
      return false;
    }
    return !changed || changed.has(rel);
  });
}

function installBrowserApiShims(window) {
  const context2d = {
    filter: "none",
    measureText: (text) => ({ width: String(text).length * 8 }),
    save() {},
    restore() {},
    scale() {},
    translate() {},
    rotate() {},
    clearRect() {},
    fillRect() {},
    strokeRect() {},
    beginPath() {},
    moveTo() {},
    lineTo() {},
    bezierCurveTo() {},
    quadraticCurveTo() {},
    arc() {},
    ellipse() {},
    rect() {},
    closePath() {},
    fill() {},
    stroke() {},
    clip() {},
    drawImage() {},
    setLineDash() {},
    fillText() {},
    strokeText() {},
    createLinearGradient() {
      return { addColorStop() {} };
    },
    createPattern() {
      return null;
    },
    getImageData() {
      return { data: new Uint8ClampedArray(4) };
    },
    putImageData() {},
  };
  window.HTMLCanvasElement.prototype.getContext = () => context2d;
  window.FontFace = class FontFace {
    constructor(family, source, descriptors = {}) {
      this.family = family;
      this.source = source;
      this.descriptors = descriptors;
      this.status = "loaded";
      this.display = descriptors.display || "swap";
      this.style = descriptors.style || "normal";
      this.weight = descriptors.weight || "400";
      this.unicodeRange = descriptors.unicodeRange || "U+0000-10FFFF";
    }

    load() {
      return Promise.resolve(this);
    }
  };
  Object.defineProperty(window.document, "fonts", {
    value: {
      add() {},
      load: () => Promise.resolve([]),
      ready: Promise.resolve(),
    },
  });
  window.Response = globalThis.Response;
  window.fetch = async (resource) => {
    const url = String(resource);
    const match = url.match(/@excalidraw\/excalidraw@[^/]+\/dist\/(prod|dev)\/(.+)$/);
    if (!match) {
      throw new Error(`unhandled Excalidraw asset fetch: ${url}`);
    }
    const localPath = path.join(
      frontendRoot,
      "node_modules",
      "@excalidraw",
      "excalidraw",
      "dist",
      match[1],
      ...match[2].split("/"),
    );
    return new Response(fs.readFileSync(localPath));
  };
}

async function loadExcalidrawExporter() {
  const bundle = await esbuild.build({
    stdin: {
      contents: "import { exportToSvg } from '@excalidraw/excalidraw'; globalThis.__axisExportToSvg = exportToSvg;",
      resolveDir: frontendRoot,
      sourcefile: path.join(frontendRoot, "scripts", "excalidraw-export-entry.mjs"),
      loader: "js",
    },
    absWorkingDir: frontendRoot,
    nodePaths: [path.join(frontendRoot, "node_modules")],
    bundle: true,
    platform: "browser",
    format: "iife",
    write: false,
    define: {
      "process.env.NODE_ENV": '"production"',
      "import.meta.env.PROD": "true",
      "import.meta.env.DEV": "false",
      "import.meta.env.MODE": '"production"',
    },
  });

  const dom = new JSDOM("<!doctype html><html><body></body></html>", {
    pretendToBeVisual: true,
    runScripts: "outside-only",
    url: "http://localhost/",
  });
  installBrowserApiShims(dom.window);
  dom.window.eval(bundle.outputFiles[0].text);
  return dom.window.__axisExportToSvg;
}

async function exportWireframe(exportToSvg, excalidrawPath) {
  const scene = JSON.parse(fs.readFileSync(excalidrawPath, "utf8"));
  const svg = await exportToSvg({
    elements: (scene.elements || []).filter((element) => !element.isDeleted),
    appState: {
      ...(scene.appState || {}),
      exportBackground: true,
      exportWithDarkMode: false,
      viewBackgroundColor: scene.appState?.viewBackgroundColor || "#ffffff",
    },
    files: scene.files || null,
    exportPadding: 0,
  });
  const svgPath = excalidrawPath.replace(/\.excalidraw$/, ".svg");
  let output = svg.outerHTML.replace(/\r\n/g, "\n").replace(/\r/g, "\n");
  if (!output.endsWith("\n")) {
    output += "\n";
  }
  fs.writeFileSync(svgPath, output, "utf8");
  return svgPath;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const files = selectedWireframes(args);
  console.log(`Generating ${files.length} wireframe SVG preview(s) with Excalidraw...`);

  const exportToSvg = await loadExcalidrawExporter();
  for (const filePath of files) {
    const svgPath = await exportWireframe(exportToSvg, filePath);
    const sizeKb = fs.statSync(svgPath).size / 1024;
    console.log(`OK ${repoPath(svgPath)} (${sizeKb.toFixed(1)} KB)`);
  }
  console.log("Done.");
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : error);
  process.exit(1);
});
