/**
 * generate-diagrams.mjs
 * Generates Excalidraw JSON for all architecture diagrams.
 * Run:  node docs/diagrams/generate-diagrams.mjs
 * Then: docs/scripts/generate-diagrams.ps1  (to produce .svg files)
 *
 * Platform architecture (docs/diagrams/):
 *   system-context  — who uses Axis, what external systems
 *   container       — runtime containers, brokers, and per-module databases
 *   module-overview — 6 modules + Kafka/RabbitMQ/gRPC communication flows
 *
 * Use-case-level (docs/use-cases/{domain}/{use-case}/):
 *   tenant-provisioning — org registration & async schema provisioning  (platform-foundation)
 *   auth-flow           — JWT + refresh token authentication flow         (identity-access)
 *   data-model          — DataModeling entity relationships               (data-modeling)
 *   workflow-model      — WorkflowBuilder entity relationships            (workflow-builder)
 *   form-model          — FormBuilder entity relationships                (form-builder)
 *   execution-flow      — WorkflowEngine step execution loop             (workflow-engine)
 */

import { writeFileSync, mkdirSync } from "fs";
import { fileURLToPath } from "url";
import { dirname, join } from "path";

const __dir = dirname(fileURLToPath(import.meta.url));
const architectureDir = __dir;
const useCaseDir = (domain, useCase) => join(__dir, "..", "use-cases", domain, useCase);

// ─── Excalidraw primitives ────────────────────────────────────────────────────

let _id = 1;
// uid() increments _id; after the call _id-1 is the number just assigned.
// We expose a seedOf() helper so base() can derive a deterministic seed
// from the element's sequential number — same element always gets the same
// seed across runs, so git diff is meaningful (only real changes show up).
const uid   = () => `diag-${_id++}`;
const seedOf = () => _id - 1;   // called immediately after uid()

const C = {
  bg:        "#f8fafc",
  border:    "#94a3b8",
  text:      "#1e293b",
  muted:     "#64748b",
  sysBg:     "#dbeafe",
  sysBdr:    "#3b82f6",
  modBg:     "#dcfce7",
  modBdr:    "#16a34a",
  extBg:     "#f3e8ff",
  extBdr:    "#9333ea",
  infraBg:   "#ffedd5",
  infraBdr:  "#ea580c",
  evtBg:     "#fef9c3",
  evtBdr:    "#ca8a04",
  arrow:     "#475569",
};

const base = (extra = {}) => {
  // Call uid() first so seedOf() captures the just-assigned number.
  const id   = uid();
  const seed = seedOf();
  return {
    id,
    x: 0, y: 0,
    angle: 0,
    strokeColor: C.border,
    backgroundColor: "transparent",
    fillStyle: "solid",
    strokeWidth: 1.5,
    strokeStyle: "solid",
    roughness: 1,
    opacity: 100,
    groupIds: [],
    frameId: null,
    roundness: { type: 2 },
    seed,
    version: 1,
    versionNonce: 0,
    isDeleted: false,
    boundElements: [],
    updated: 1,
    link: null,
    locked: false,
    ...extra,
  };
};

function rect({ x, y, w, h, bg = "transparent", stroke = C.border, label, labelSize = 13, labelColor = C.text, labelBold = false, sub, rx = 6 }) {
  const el = {
    ...base(),
    type: "rectangle",
    x, y, width: w, height: h,
    backgroundColor: bg,
    strokeColor: stroke,
    roundness: { type: 3, value: rx },
  };
  const els = [el];
  if (label) {
    els.push(text({
      x: x + w / 2, y: sub ? y + h / 2 - 10 : y + h / 2,
      value: label, size: labelSize, color: labelColor,
      bold: labelBold, anchor: "center",
    }));
  }
  if (sub) {
    els.push(text({
      x: x + w / 2, y: y + h / 2 + 8,
      value: sub, size: 10, color: C.muted, anchor: "center",
    }));
  }
  return els;
}

function text({ x, y, value, size = 13, color = C.text, bold = false, anchor = "left" }) {
  return {
    ...base(),
    type: "text",
    x: anchor === "center" ? x - estimateWidth(value, size) / 2 : x,
    y: y - size / 2,
    width: estimateWidth(value, size),
    height: size * 1.4,
    text: value,
    fontSize: size,
    fontFamily: 1,
    textAlign: "center",
    verticalAlign: "middle",
    strokeColor: color,
    backgroundColor: "transparent",
    fillStyle: "solid",
    roughness: 1,
    strokeWidth: 1,
    strokeStyle: "solid",
  };
}

function estimateWidth(str, size) {
  return Math.max(str.length * size * 0.55, 40);
}

function arrow({ x1, y1, x2, y2, label, color = C.arrow, dashed = false }) {
  const el = {
    ...base(),
    type: "arrow",
    x: x1, y: y1,
    width: Math.abs(x2 - x1),
    height: Math.abs(y2 - y1),
    points: [[0, 0], [x2 - x1, y2 - y1]],
    strokeColor: color,
    backgroundColor: "transparent",
    fillStyle: "solid",
    strokeWidth: 1.5,
    strokeStyle: dashed ? "dashed" : "solid",
    roughness: 1,
    startArrowhead: null,
    endArrowhead: "arrow",
    roundness: { type: 2 },
  };
  const els = [el];
  if (label) {
    const mx = (x1 + x2) / 2;
    const my = (y1 + y2) / 2;
    els.push(text({ x: mx, y: my - 14, value: label, size: 10, color: C.muted, anchor: "center" }));
  }
  return els;
}

function routedArrow({ waypoints, label, color = C.arrow, dashed = false }) {
  const [ox, oy] = waypoints[0];
  const points = waypoints.map(([x, y]) => [x - ox, y - oy]);
  const xs = waypoints.map(([x]) => x);
  const ys = waypoints.map(([, y]) => y);
  const el = {
    ...base(),
    type: "arrow",
    x: ox, y: oy,
    width: Math.max(...xs) - Math.min(...xs),
    height: Math.max(...ys) - Math.min(...ys),
    points,
    strokeColor: color,
    backgroundColor: "transparent",
    fillStyle: "solid",
    strokeWidth: 1.5,
    strokeStyle: dashed ? "dashed" : "solid",
    roughness: 1,
    startArrowhead: null,
    endArrowhead: "arrow",
    roundness: { type: 2 },
  };
  const els = [el];
  if (label) {
    const mid = waypoints[Math.floor(waypoints.length / 2)];
    els.push(text({ x: mid[0], y: mid[1] - 14, value: label, size: 10, color: C.muted, anchor: "center" }));
  }
  return els;
}

function badge({ x, y, label }) {
  const w = estimateWidth(label, 10) + 16;
  return [
    { ...base(), type: "rectangle", x, y, width: w, height: 22,
      backgroundColor: C.evtBg, strokeColor: C.evtBdr,
      roundness: { type: 3, value: 11 }, strokeWidth: 1.5, roughness: 1 },
    text({ x: x + w / 2, y: y + 11, value: label, size: 10, color: "#92400e", anchor: "center" }),
  ];
}

// ─── Line helpers (no arrowhead) ─────────────────────────────────────────────

function hline({ x, y, w, color = C.border, dashed = false }) {
  return [{
    ...base({ roundness: { type: 2 } }),
    type: "arrow",
    x, y, width: w, height: 0,
    points: [[0, 0], [w, 0]],
    strokeColor: color,
    strokeStyle: dashed ? "dashed" : "solid",
    strokeWidth: 1,
    roughness: 1,
    startArrowhead: null,
    endArrowhead: null,
  }];
}

function vline({ x, y, h, color = C.border, dashed = false }) {
  return [{
    ...base({ roundness: { type: 2 } }),
    type: "arrow",
    x, y, width: 0, height: h,
    points: [[0, 0], [0, h]],
    strokeColor: color,
    strokeStyle: dashed ? "dashed" : "solid",
    strokeWidth: 1,
    roughness: 1,
    startArrowhead: null,
    endArrowhead: null,
  }];
}

// ─── Entity box (UML-style with header + attribute body) ─────────────────────

function entityBox({ x, y, name, attrs = [], isEnum = false, isValueObject = false, planned = false,
                     headerBg = null, stroke = null }) {
  const effectiveBg     = headerBg ?? (isValueObject ? "#e2e8f0" : C.sysBg);
  const effectiveStroke = stroke   ?? (isValueObject ? "#64748b" : C.sysBdr);
  const W = 190, LH = 17;
  const hdrH = (isEnum || isValueObject) ? 36 : 28;
  const bodyH = attrs.length > 0 ? attrs.length * LH + 8 : 0;
  const H = hdrH + bodyH;
  const els = [];

  // Full outer box (white body background + visible border)
  els.push({
    ...base(), type: "rectangle", x, y, width: W, height: H,
    backgroundColor: "#ffffff", strokeColor: effectiveStroke,
    roundness: { type: 3, value: 4 }, strokeWidth: 1.5,
    strokeStyle: planned ? "dashed" : "solid",
  });

  // Header colored overlay (strokeColor = headerBg so border blends into fill)
  els.push({
    ...base(), type: "rectangle", x, y, width: W, height: hdrH,
    backgroundColor: effectiveBg, strokeColor: effectiveBg,
    roundness: { type: 3, value: 4 }, strokeWidth: 1,
  });

  // Separator between header and body
  if (attrs.length > 0) {
    els.push(...hline({ x, y: y + hdrH, w: W, color: effectiveStroke }));
  }

  // Name
  if (isEnum) {
    els.push(text({ x: x + W / 2, y: y + 11, value: "«enum»", size: 8.5, color: C.muted, anchor: "center" }));
    els.push(text({ x: x + W / 2, y: y + 26, value: name, size: 11, bold: true, color: C.text, anchor: "center" }));
  } else if (isValueObject) {
    els.push(text({ x: x + W / 2, y: y + 11, value: "«value object»", size: 8.5, color: C.muted, anchor: "center" }));
    els.push(text({ x: x + W / 2, y: y + 26, value: name, size: 11, bold: true, color: C.text, anchor: "center" }));
  } else {
    els.push(text({ x: x + W / 2, y: y + hdrH / 2, value: name, size: 11, bold: true, color: C.text, anchor: "center" }));
  }

  // Attributes
  for (let i = 0; i < attrs.length; i++) {
    els.push(text({ x: x + 7, y: y + hdrH + 4 + i * LH + LH / 2, value: attrs[i], size: 9.5, color: C.text }));
  }

  const [cx, cy] = [x + W / 2, y + H / 2];
  return {
    els, x, y, w: W, h: H, cx, cy,
    midLeft:   { x,       y: cy },
    midRight:  { x: x + W, y: cy },
    midTop:    { x: cx,    y },
    midBottom: { x: cx,    y: y + H },
  };
}

function excalidraw(elements) {
  return JSON.stringify({
    type: "excalidraw",
    version: 2,
    source: "generated",
    elements,
    appState: { gridSize: null, viewBackgroundColor: C.bg },
    files: {},
  }, null, 2);
}

// ─── Diagram 1 — System Context ───────────────────────────────────────────────

function systemContext() {
  _id = 1;
  const els = [];

  els.push(text({ x: 540, y: 28, value: "Axis Platform — System Context", size: 18, bold: true, color: C.text, anchor: "center" }));

  function actor(x, y, label, sub) {
    els.push(...rect({ x: x - 22, y, w: 44, h: 44, bg: "#e0f2fe", stroke: "#0284c7", rx: 22, label: "👤", labelSize: 18 }));
    els.push(text({ x, y: y + 56, value: label, size: 12, bold: true, anchor: "center" }));
    if (sub) els.push(text({ x, y: y + 72, value: sub, size: 10, color: C.muted, anchor: "center" }));
  }

  actor(105, 195, "Org Admin", "[builds workflows & forms]");
  actor(105, 460, "End User", "[submits forms, views pages]");

  // Platform boundary
  els.push(...rect({ x: 230, y: 65, w: 600, h: 650, bg: "#f0f9ff", stroke: C.sysBdr, label: "Axis Platform", labelSize: 14, labelBold: true, labelColor: C.sysBdr }));

  // Inside platform — col 1 (left)
  els.push(...rect({ x: 268, y: 120, w: 240, h: 70, bg: C.sysBg, stroke: C.sysBdr, label: "Web Application", sub: "React 19 + TypeScript" }));
  els.push(...rect({ x: 268, y: 250, w: 240, h: 70, bg: C.sysBg, stroke: C.sysBdr, label: "API Server", sub: "ASP.NET Core 8 · Modulith (strict boundaries)" }));
  els.push(...rect({ x: 268, y: 395, w: 240, h: 70, bg: C.infraBg, stroke: C.infraBdr, label: "PostgreSQL 16", sub: "Per-module databases" }));
  els.push(...rect({ x: 268, y: 505, w: 240, h: 70, bg: C.infraBg, stroke: C.infraBdr, label: "Redis 7", sub: "Cache · Session" }));

  // Inside platform — col 2 (right)
  els.push(...rect({ x: 558, y: 210, w: 220, h: 50, bg: C.evtBg, stroke: C.evtBdr, label: "Kafka + Schema Registry", sub: "Events/Snapshots · Avro + CloudEvents" }));
  els.push(...rect({ x: 558, y: 270, w: 220, h: 50, bg: C.evtBg, stroke: C.evtBdr, label: "RabbitMQ", sub: "Commands/Jobs/Saga steps" }));
  els.push(...rect({ x: 558, y: 330, w: 220, h: 50, bg: C.sysBg, stroke: C.sysBdr, label: "gRPC Contracts", sub: "Sync RPC escape hatch" }));
  els.push(...rect({ x: 558, y: 395, w: 220, h: 70, bg: C.infraBg, stroke: C.infraBdr, label: "AWS S3", sub: "File storage" }));

  // External services
  els.push(...rect({ x: 890, y: 200, w: 170, h: 70, bg: C.extBg, stroke: C.extBdr, label: "Email Service", sub: "SMTP / MailKit" }));
  els.push(...rect({ x: 890, y: 395, w: 170, h: 70, bg: C.extBg, stroke: C.extBdr, label: "External APIs", sub: "HTTP Request steps" }));

  // Org Admin → Web App
  els.push(...routedArrow({ waypoints: [[149, 217], [248, 217], [248, 155], [268, 155]], label: "HTTPS" }));
  // End User → API Server (direct)
  els.push(...routedArrow({ waypoints: [[149, 482], [248, 482], [248, 285], [268, 285]], label: "HTTPS" }));
  // Web App → API Server
  els.push(...arrow({ x1: 388, y1: 190, x2: 388, y2: 250, label: "REST / WS" }));
  // API Server → PostgreSQL (straight down)
  els.push(...arrow({ x1: 388, y1: 320, x2: 388, y2: 395 }));
  // API Server → Redis (route around left to avoid crossing PostgreSQL)
  els.push(...routedArrow({ waypoints: [[268, 285], [248, 285], [248, 540], [268, 540]] }));
  // API Server → messaging / contracts
  els.push(...arrow({ x1: 508, y1: 275, x2: 558, y2: 235 }));
  els.push(...arrow({ x1: 508, y1: 285, x2: 558, y2: 295 }));
  els.push(...arrow({ x1: 508, y1: 295, x2: 558, y2: 355 }));
  // API Server → S3 (via app integration)
  els.push(...routedArrow({ waypoints: [[508, 305], [540, 305], [540, 430], [558, 430]] }));
  // Messaging / handlers → Email + External APIs
  els.push(...routedArrow({ waypoints: [[778, 235], [840, 235], [840, 235], [890, 235]] }));
  els.push(...routedArrow({ waypoints: [[778, 295], [840, 295], [840, 430], [890, 430]] }));

  return excalidraw(els);
}

// ─── Diagram 2 — Container ────────────────────────────────────────────────────

function containerDiagram() {
  _id = 1;
  const els = [];

  els.push(text({ x: 565, y: 25, value: "Axis Platform — Container Diagram", size: 18, bold: true, color: C.text, anchor: "center" }));

  // Platform boundary (right edge x=810)
  els.push(...rect({ x: 50, y: 55, w: 760, h: 620, bg: "#f0f9ff", stroke: C.sysBdr }));
  els.push(text({ x: 430, y: 80, value: "Axis.Api Gateway + Module Services (Modulith, strict boundaries)", size: 14, bold: true, color: C.sysBdr, anchor: "center" }));

  // Modules — row 1 (y=105) and row 2 (y=245), 65px gap between rows
  const MW = 165, MH = 75;
  const modules = [
    { label: "Identity",        x: 70,  y: 105 },
    { label: "DataModeling",    x: 250, y: 105 },
    { label: "WorkflowBuilder", x: 430, y: 105 },
    { label: "FormBuilder",     x: 70,  y: 245 },
    { label: "WorkflowEngine",  x: 250, y: 245 },
    { label: "PageBuilder",     x: 430, y: 245 },
  ];
  for (const m of modules) {
    els.push(...rect({
      x: m.x,
      y: m.y,
      w: MW,
      h: MH,
      bg: m.bg ?? C.modBg,
      stroke: m.stroke ?? C.modBdr,
      label: m.label,
      sub: m.sub,
      labelSize: 12,
    }));
  }

  // Messaging and contract lanes (55px gap below row 2 bottom at y=320)
  els.push(...rect({ x: 70, y: 360, w: 550, h: 42, bg: C.evtBg, stroke: C.evtBdr,
    label: "Kafka + Schema Registry", sub: "Events/Snapshots · Avro + CloudEvents", labelSize: 12 }));
  els.push(...rect({ x: 70, y: 408, w: 550, h: 42, bg: C.evtBg, stroke: C.evtBdr,
    label: "RabbitMQ", sub: "Commands/Jobs/Saga steps", labelSize: 12 }));
  els.push(...rect({ x: 70, y: 456, w: 550, h: 42, bg: C.sysBg, stroke: C.sysBdr,
    label: "gRPC Contracts", sub: "Sync RPC escape hatch", labelSize: 12 }));

  // OpenIddict + SignalR
  els.push(...rect({ x: 70,  y: 505, w: 250, h: 55, bg: C.sysBg, stroke: C.sysBdr,
    label: "OpenIddict 5.x", sub: "OAuth2/OIDC · Auth Code + PKCE" }));
  els.push(...rect({ x: 335, y: 505, w: 285, h: 55, bg: C.sysBg, stroke: C.sysBdr,
    label: "SignalR", sub: "Real-time execution status" }));

  // Web Application band (inset from platform border for readability)
  els.push(...rect({ x: 70, y: 585, w: 720, h: 55, bg: C.sysBg, stroke: C.sysBdr,
    label: "Web Application",
    sub: "React 19 + TypeScript + Vite · shadcn/ui · React Flow · dnd-kit · TanStack Query · Zustand" }));

  // DB column (right side; arrows from platform right edge x=810 → DB left edge x=870, 60px each)
  const DBX = 870, DBW = 190, DBH = 55, DBGap = 10;
  els.push(text({ x: DBX + DBW / 2, y: 65, value: "Per-Module Databases", size: 12, bold: true, color: C.infraBdr, anchor: "center" }));

  const dbs = [
    { label: "Identity DB",        y: 100 },
    { label: "DataModeling DB",    y: 100 + (DBH + DBGap) },
    { label: "WorkflowBuilder DB", y: 100 + (DBH + DBGap) * 2 },
    { label: "WorkflowEngine DB",  y: 100 + (DBH + DBGap) * 3 },
    { label: "FormBuilder DB",     y: 100 + (DBH + DBGap) * 4 },
    { label: "PageBuilder DB",     y: 100 + (DBH + DBGap) * 5 },
  ];
  for (const db of dbs) {
    els.push(...rect({ x: DBX, y: db.y, w: DBW, h: DBH, bg: C.infraBg, stroke: C.infraBdr, label: db.label, labelSize: 11 }));
    els.push(...arrow({ x1: 810, y1: db.y + DBH / 2, x2: DBX, y2: db.y + DBH / 2, color: C.arrow }));
  }

  // Other infrastructure (right side, below DB column) - distinct color from DB blocks
  els.push(...rect({ x: DBX, y: 495, w: DBW, h: DBH, bg: C.sysBg, stroke: C.sysBdr, label: "Redis", sub: "Cache · Session" }));
  els.push(...arrow({ x1: 810, y1: 522, x2: DBX, y2: 522, color: C.arrow }));

  // Production operations containers (right side, straight arrows from platform edge)
  els.push(...rect({ x: 870, y: 565, w: 220, h: 55, bg: C.sysBg, stroke: C.sysBdr,
    label: "HashiCorp Vault", sub: "Secrets management" }));
  els.push(...rect({ x: 870, y: 635, w: 220, h: 55, bg: C.sysBg, stroke: C.sysBdr,
    label: "Grafana Tempo/Loki/Mimir", sub: "Tracing · Logs · Metrics" }));
  els.push(...arrow({ x1: 810, y1: 592, x2: 870, y2: 592, color: C.arrow }));
  els.push(...arrow({ x1: 810, y1: 662, x2: 870, y2: 662, color: C.arrow }));

  // External managed services aligned with operations rows
  els.push(...rect({ x: 1110, y: 565, w: 200, h: 55, bg: C.extBg, stroke: C.extBdr, label: "AWS S3", sub: "File storage" }));
  els.push(...rect({ x: 1110, y: 635, w: 200, h: 55, bg: C.extBg, stroke: C.extBdr, label: "Email Service", sub: "SMTP · MailKit" }));
  // Connections avoid crossing ops blocks while preserving clear source/target.
  els.push(...routedArrow({ waypoints: [[810, 560], [1100, 560], [1100, 592], [1110, 592]], color: C.arrow }));
  els.push(...routedArrow({ waypoints: [[810, 662], [845, 662], [845, 705], [1100, 705], [1100, 662], [1110, 662]], color: C.arrow }));

  // Compact legend
  els.push(...rect({ x: 628, y: 88, w: 176, h: 142, bg: "#ffffff", stroke: C.border }));
  els.push(text({ x: 716, y: 102, value: "Legend", size: 10, bold: true, color: C.text, anchor: "center" }));
  els.push(...rect({ x: 640, y: 114, w: 12, h: 12, bg: C.modBg, stroke: C.modBdr }));
  els.push(text({ x: 658, y: 120, value: "Module service", size: 9, color: C.text }));
  els.push(...rect({ x: 640, y: 132, w: 12, h: 12, bg: C.evtBg, stroke: C.evtBdr }));
  els.push(text({ x: 658, y: 138, value: "Messaging lanes", size: 9, color: C.text }));
  els.push(...rect({ x: 640, y: 150, w: 12, h: 12, bg: C.infraBg, stroke: C.infraBdr }));
  els.push(text({ x: 658, y: 156, value: "Database", size: 9, color: C.text }));
  els.push(...rect({ x: 640, y: 168, w: 12, h: 12, bg: C.sysBg, stroke: C.sysBdr }));
  els.push(text({ x: 658, y: 174, value: "Platform ops", size: 9, color: C.text }));
  els.push(...rect({ x: 640, y: 186, w: 12, h: 12, bg: C.extBg, stroke: C.extBdr }));
  els.push(text({ x: 658, y: 192, value: "External service", size: 9, color: C.text }));
  els.push(...arrow({ x1: 640, y1: 210, x2: 670, y2: 210, color: C.arrow }));
  els.push(text({ x: 676, y: 210, value: "Connection", size: 9, color: C.text }));

  return excalidraw(els);
}

// ─── Diagram 3 — Module Overview (event-driven) ───────────────────────────────

function moduleOverview() {
  _id = 1;
  const els = [];

  els.push(text({ x: 530, y: 25, value: "Axis — Module Communication", size: 18, bold: true, color: C.text, anchor: "center" }));
  els.push(text({ x: 530, y: 50, value: "Modules are data-sovereign. Cross-module via Kafka (events), RabbitMQ (commands/jobs/saga), gRPC (sync).", size: 11, color: C.muted, anchor: "center" }));

  // Shared Kernel spans all 4 row-1 modules (x=60 to x=935)
  els.push(...rect({ x: 60, y: 75, w: 875, h: 50, bg: "#e2e8f0", stroke: C.border,
    label: "Shared Kernel  —  Domain Primitives · CQRS Abstractions · Multi-Tenancy Interfaces", labelSize: 12 }));

  function module(x, y, label, sub, color = { bg: C.modBg, stroke: C.modBdr }) {
    return rect({ x, y, w: 200, h: 80, bg: color.bg, stroke: color.stroke, label, sub, labelSize: 13, labelBold: true });
  }

  // Row 1 (y=155): 4 modules with 25px gaps
  els.push(...module(60,  155, "Identity",        "Auth · Users · Roles · RBAC"));
  els.push(...module(285, 155, "DataModeling",    "Models · Records · Data Classes"));
  els.push(...module(510, 155, "WorkflowBuilder", "Definitions · Steps · Triggers"));
  els.push(...module(735, 155, "FormBuilder",     "Forms · Fields · Submissions"));

  // Communication lanes between rows
  els.push(...rect({ x: 60, y: 286, w: 875, h: 28, bg: C.evtBg, stroke: C.evtBdr }));
  els.push(text({ x: 497, y: 300, value: "Kafka + Schema Registry — Events/Snapshots", size: 11, color: "#92400e", anchor: "center" }));
  els.push(...rect({ x: 60, y: 318, w: 875, h: 28, bg: C.evtBg, stroke: C.evtBdr }));
  els.push(text({ x: 497, y: 332, value: "RabbitMQ — Commands/Jobs/Saga steps", size: 11, color: "#92400e", anchor: "center" }));
  els.push(...rect({ x: 60, y: 350, w: 875, h: 28, bg: C.sysBg, stroke: C.sysBdr }));
  els.push(text({ x: 497, y: 364, value: "gRPC Contracts — Sync RPC escape hatch", size: 11, color: C.muted, anchor: "center" }));

  // Example event badges
  const events = [
    { label: "WorkflowPublished",  x: 95 },
    { label: "WorkflowArchived",   x: 240 },
    { label: "WorkflowUnarchived", x: 385 },
    { label: "FormCreated",        x: 530 },
    { label: "FormTaskCreated",    x: 675 },
    { label: "ExecutionStarted",   x: 820 },
  ];
  for (const e of events) {
    els.push(...badge({ x: e.x, y: 386, label: e.label }));
  }

  // Row 2
  els.push(...module(285, 470, "WorkflowEngine", "Executions · Step Handlers"));
  els.push(...module(510, 470, "PageBuilder",    "Pages · Widgets · Bindings"));

  // Arrows
  // Cross-module connection examples (single connector style)
  els.push(...arrow({ x1: 610, y1: 235, x2: 610, y2: 286, color: C.arrow }));
  // Drop to WorkflowEngine through the gap between event badges.
  els.push(...arrow({ x1: 340, y1: 378, x2: 340, y2: 470, color: C.arrow }));
  // FormBuilder consumes from messaging lanes via right-side routed path (no lane cut-through).
  els.push(...routedArrow({ waypoints: [[935, 332], [970, 332], [970, 230], [860, 230], [860, 235]], color: C.arrow }));
  // WorkflowEngine (bottom y=550) → reads own local copy
  els.push(...arrow({ x1: 385, y1: 550, x2: 385, y2: 595, color: C.muted, dashed: true, label: "reads own copy" }));

  // Legend
  els.push(...rect({ x: 650, y: 560, w: 285, h: 110, bg: "transparent", stroke: C.border }));
  els.push(text({ x: 792, y: 577, value: "Legend", size: 11, bold: true, anchor: "center" }));
  els.push(...rect({ x: 665, y: 590, w: 16, h: 16, bg: C.modBg, stroke: C.modBdr }));
  els.push(text({ x: 690, y: 598, value: "Module (owns its DB)", size: 10 }));
  els.push(...rect({ x: 665, y: 612, w: 16, h: 16, bg: C.evtBg, stroke: C.evtBdr }));
  els.push(text({ x: 690, y: 620, value: "Kafka/RabbitMQ lanes", size: 10 }));
  els.push(...arrow({ x1: 665, y1: 638, x2: 697, y2: 638, color: C.arrow }));
  els.push(text({ x: 702, y: 638, value: "Connection", size: 10 }));
  els.push(...arrow({ x1: 665, y1: 655, x2: 697, y2: 655, color: C.muted, dashed: true }));
  els.push(text({ x: 702, y: 655, value: "Local copy (denormalized)", size: 10 }));

  return excalidraw(els);
}

// ─── Sequence diagram shared helpers ─────────────────────────────────────────

function seqBuild({ title, participants, gap = 165, messages, sections = [], notes = [] }) {
  const els = [];
  const BOX_W = 125, BOX_H = 42, BOX_Y = 48;
  const LIFE_TOP = BOX_Y + BOX_H;
  const LIFE_BOT = messages.reduce((max, m) => Math.max(max, m.y), 0) + 50;

  // Compute cx first — CANVAS_W depends on it
  const ps = participants.map((p, i) => ({ ...p, cx: 60 + i * gap }));
  const CANVAS_W = ps[ps.length - 1].cx + BOX_W / 2 + 30;

  // Title
  els.push(text({ x: CANVAS_W / 2, y: 20, value: title, size: 15, bold: true, color: C.text, anchor: "center" }));

  // Participant boxes + lifelines
  for (const p of ps) {
    const boxBg = p.db ? C.infraBg : p.external ? C.extBg : C.sysBg;
    const boxStroke = p.db ? C.infraBdr : p.external ? C.extBdr : C.sysBdr;
    els.push(...rect({ x: p.cx - BOX_W / 2, y: BOX_Y, w: BOX_W, h: BOX_H, bg: boxBg, stroke: boxStroke, label: p.label, sub: p.sub, labelSize: 10, labelBold: false }));
    els.push(...vline({ x: p.cx, y: LIFE_TOP, h: LIFE_BOT - LIFE_TOP, color: "#94a3b8", dashed: true }));
  }

  // Section dividers
  for (const s of sections) {
    els.push(...hline({ x: 20, y: s.y, w: CANVAS_W - 40, color: "#94a3b8", dashed: true }));
    els.push(text({ x: CANVAS_W / 2, y: s.y + 12, value: `== ${s.label} ==`, size: 10, bold: true, color: C.muted, anchor: "center" }));
  }

  // Messages
  for (const m of messages) {
    const from = ps[m.from];
    const to = ps[m.to];
    els.push(...arrow({ x1: from.cx, y1: m.y, x2: to.cx, y2: m.y, label: m.label, dashed: m.dashed }));
  }

  // Notes (info boxes at bottom)
  for (const n of notes) {
    els.push(...rect({ x: n.x, y: n.y, w: n.w || 400, h: n.h || 32, bg: C.evtBg, stroke: C.evtBdr, label: n.label, labelSize: 10 }));
  }

  return excalidraw(els);
}

// ─── platform-foundation — Tenant Provisioning ───────────────────────────────────────────────

function tenantProvisioningDiagram() {
  _id = 1;
  const GAP = 165;
  return seqBuild({
    title: "Tenant Registration & Provisioning Flow",
    gap: GAP,
    participants: [
      { label: "New Admin" },
      { label: "Web App" },
      { label: "API Server" },
      { label: "Email", sub: "Service", external: true },
      { label: "Wolverine", sub: "(Jobs)" },
      { label: "PostgreSQL", sub: "(public)", db: true },
      { label: "PostgreSQL", sub: "(tenant)", db: true },
    ],
    sections: [
      { y: 342, label: "Email Verification" },
      { y: 570, label: "Async Provisioning" },
    ],
    messages: [
      { from: 0, to: 1, y: 160, label: "Fill registration form" },
      { from: 1, to: 2, y: 200, label: "POST /auth/register" },
      { from: 2, to: 5, y: 240, label: "Save org (PENDING) + user" },
      { from: 2, to: 3, y: 280, label: "Send verification email" },
      { from: 2, to: 1, y: 312, label: "202 Accepted", dashed: true },

      { from: 0, to: 1, y: 368, label: "Click verification link" },
      { from: 1, to: 2, y: 408, label: "POST /auth/verify-email" },
      { from: 2, to: 5, y: 448, label: "Mark VERIFIED + PROVISIONING" },
      { from: 2, to: 4, y: 488, label: "Enqueue ProvisionTenantJob" },
      { from: 2, to: 1, y: 528, label: "200 OK → Redirect", dashed: true },

      { from: 4, to: 5, y: 600, label: "Generate schema name" },
      { from: 4, to: 6, y: 640, label: "CREATE SCHEMA tenant_{slug}" },
      { from: 4, to: 6, y: 680, label: "Run EF Core migrations" },
      { from: 4, to: 5, y: 720, label: "Assign Admin role + ACTIVE" },
      { from: 4, to: 3, y: 760, label: "Send Welcome email" },
    ],
    notes: [
      { x: 20, y: 800, w: 560, h: 30, label: "UI polls org.status = ACTIVE then shows dashboard" },
    ],
  });
}

// ─── identity-access — Auth Flow ─────────────────────────────────────────────────────────

function authFlowDiagram() {
  _id = 1;
  return seqBuild({
    title: "Authentication Flow — JWT + Refresh Token",
    gap: 175,
    participants: [
      { label: "User" },
      { label: "Web App", sub: "(React SPA)" },
      { label: "API Server", sub: "(OpenIddict)" },
      { label: "Redis", sub: "(token blacklist)", db: true },
      { label: "PostgreSQL", db: true },
    ],
    sections: [
      { y: 140, label: "Sign In" },
      { y: 320, label: "Authenticated Request" },
      { y: 490, label: "Silent Token Refresh" },
      { y: 640, label: "Sign Out" },
    ],
    messages: [
      { from: 0, to: 1, y: 168, label: "Enter email + password" },
      { from: 1, to: 2, y: 208, label: "POST /auth/token" },
      { from: 2, to: 4, y: 248, label: "Validate credentials" },
      { from: 2, to: 1, y: 288, label: "{access_token, refresh_token}", dashed: true },

      { from: 0, to: 1, y: 348, label: "Navigate / perform action" },
      { from: 1, to: 2, y: 388, label: "GET /resource (Bearer token)" },
      { from: 2, to: 2, y: 428, label: "Validate JWT + set tenant" },
      { from: 2, to: 1, y: 460, label: "200 OK + data", dashed: true },

      { from: 1, to: 2, y: 518, label: "POST /auth/refresh (httpOnly cookie)" },
      { from: 2, to: 4, y: 558, label: "Validate + rotate refresh token" },
      { from: 2, to: 1, y: 598, label: "{new access_token, new refresh_token}", dashed: true },

      { from: 0, to: 1, y: 668, label: "Click Sign Out" },
      { from: 1, to: 2, y: 708, label: "POST /auth/signout" },
      { from: 2, to: 3, y: 748, label: "Blacklist access_token JTI" },
      { from: 2, to: 4, y: 788, label: "Revoke refresh token" },
      { from: 2, to: 1, y: 828, label: "200 OK", dashed: true },
    ],
  });
}

// ─── workflow-engine — Execution Flow ─────────────────────────────────────────────────────

function executionFlowDiagram() {
  _id = 1;
  return seqBuild({
    title: "Workflow Execution Flow",
    gap: 160,
    participants: [
      { label: "Trigger", sub: "(Manual/Webhook)" },
      { label: "Execution", sub: "Orchestrator" },
      { label: "Step Handler", sub: "(per type)" },
      { label: "Wolverine", sub: "(Jobs)" },
      { label: "PostgreSQL", db: true },
      { label: "SignalR Hub" },
      { label: "Browser", external: true },
    ],
    sections: [
      { y: 140, label: "Execution Start" },
      { y: 390, label: "Step Execution Loop" },
      { y: 750, label: "Execution Complete" },
    ],
    messages: [
      { from: 0, to: 1, y: 168, label: "Start(workflowId, inputPayload)" },
      { from: 1, to: 4, y: 208, label: "Create Execution (PENDING)" },
      { from: 1, to: 4, y: 248, label: "Create StepExecution records" },
      { from: 1, to: 3, y: 288, label: "Enqueue ExecuteNextStep" },
      { from: 1, to: 5, y: 328, label: "ExecutionStarted event" },
      { from: 5, to: 6, y: 360, label: "Push status update" },

      { from: 3, to: 1, y: 418, label: "ExecuteNextStep(executionId)" },
      { from: 1, to: 4, y: 458, label: "Update step (RUNNING)" },
      { from: 1, to: 5, y: 490, label: "StepStarted event" },
      { from: 1, to: 2, y: 530, label: "Execute(stepDefinition, context)" },
      { from: 2, to: 4, y: 570, label: "[Form] Create FormTask (PENDING)" },
      { from: 2, to: 3, y: 610, label: "[Form] Enqueue notification" },
      { from: 2, to: 1, y: 650, label: "[Sync] StepResult(success, output)", dashed: true },
      { from: 1, to: 4, y: 688, label: "Update step (COMPLETED)" },
      { from: 1, to: 3, y: 720, label: "Enqueue ExecuteNextStep" },

      { from: 1, to: 4, y: 778, label: "Update execution (COMPLETED)" },
      { from: 1, to: 5, y: 818, label: "ExecutionCompleted event" },
      { from: 5, to: 6, y: 858, label: "Push status update" },
    ],
  });
}

// ─── Entity diagram shared helpers ───────────────────────────────────────────

function entityRelArrow(a, b, label, opts = {}) {
  const { fromSide = "right", toSide = "left", dashed = false } = opts;
  const p1 = a[`mid${fromSide.charAt(0).toUpperCase() + fromSide.slice(1)}`];
  const p2 = b[`mid${toSide.charAt(0).toUpperCase() + toSide.slice(1)}`];
  return arrow({ x1: p1.x, y1: p1.y, x2: p2.x, y2: p2.y, label, dashed });
}

// ─── data-modeling — Data Model ────────────────────────────────────────────────────────

function dataModelDiagram() {
  _id = 1;
  const els = [];

  els.push(text({ x: 500, y: 20, value: "Data Modeling — Core Entity Relationships", size: 16, bold: true, color: C.text, anchor: "center" }));

  // Row 1: ModelDefinition | FieldDefinition | FieldType
  const mdl = entityBox({ x: 40, y: 60, name: "ModelDefinition", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+name: string", "+slug: string", "+description: string", "+icon: string", "+color: string", "+createdAt: DateTime", "+updatedAt: DateTime"] });

  const fld = entityBox({ x: 280, y: 60, name: "FieldDefinition", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+modelId: UUID", "+name: string", "+label: string", "+type: FieldType", "+required: boolean", "+displayOrder: int", "+config: JSONB", "+createdAt: DateTime"] });

  const ftype = entityBox({ x: 520, y: 60, name: "FieldType", isEnum: true, headerBg: "#ede9fe", stroke: "#7c3aed",
    attrs: ["Text", "Number", "Boolean", "Date", "Enum", "Relation", "DataClass", "File", "JSON"] });

  // Row 2: DataRecord | DataClassDefinition
  // DataClassDefinition reuses FieldDefinition directly — no separate DataClassField entity
  const rec = entityBox({ x: 40, y: 350, name: "DataRecord", headerBg: C.modBg, stroke: C.modBdr,
    attrs: ["+id: UUID", "+modelId: UUID", "+data: JSONB", "+createdAt: DateTime", "+updatedAt: DateTime", "+createdBy: UUID"] });

  const dcd = entityBox({ x: 280, y: 350, name: "DataClassDefinition", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+name: string", "+description: string", "+createdAt: DateTime", "+updatedAt: DateTime"] });

  for (const b of [mdl, fld, ftype, rec, dcd]) els.push(...b.els);

  // Relationships
  // mdl → fld: 1 owns many fields
  els.push(...arrow({ x1: mdl.midRight.x, y1: mdl.midRight.y, x2: fld.midLeft.x, y2: fld.midLeft.y, label: "1 *--" }));
  // fld → ftype: each field has a type
  els.push(...arrow({ x1: fld.midRight.x, y1: fld.midRight.y, x2: ftype.midLeft.x, y2: ftype.midLeft.y, label: "has type" }));
  // dcd → fld: data class reuses FieldDefinition (right channel, +15 offset, going up)
  els.push(...arrow({ x1: dcd.midTop.x + 15, y1: dcd.midTop.y, x2: fld.midBottom.x + 15, y2: fld.midBottom.y, label: "1 *--" }));
  // fld → dcd: FieldDefinition may reference a DataClass (left channel, -15 offset, going down, dashed)
  els.push(...arrow({ x1: fld.midBottom.x - 15, y1: fld.midBottom.y, x2: dcd.midTop.x - 15, y2: dcd.midTop.y, label: "→ DataClass ref", dashed: true }));
  // rec → mdl: record is an instance of a model (straight up)
  els.push(...arrow({ x1: rec.midTop.x, y1: rec.midTop.y, x2: mdl.midBottom.x, y2: mdl.midBottom.y, label: "instance of", dashed: true }));

  return excalidraw(els);
}

// ─── workflow-builder — Workflow Model ────────────────────────────────────────────────────

function workflowModelDiagram() {
  _id = 1;
  const els = [];

  els.push(text({ x: 600, y: 20, value: "Workflow Builder — Core Entity Relationships", size: 16, bold: true, color: C.text, anchor: "center" }));

  const wfd = entityBox({ x: 40, y: 60, name: "WorkflowDefinition", headerBg: C.modBg, stroke: C.modBdr,
    attrs: ["+id: UUID", "+name: string", "+slug: string", "+description: string", "+status: WorkflowStatus", "+version: int", "+createdAt: DateTime", "+updatedAt: DateTime"] });

  const wfs = entityBox({ x: 40, y: 360, name: "WorkflowStatus", isEnum: true, headerBg: "#ede9fe", stroke: "#7c3aed",
    attrs: ["Draft", "Active", "Archived"] });

  // TriggerConfig is a value object (no identity, owned by WorkflowDefinition)
  const trig = entityBox({ x: 290, y: 60, name: "TriggerConfig", isValueObject: true,
    attrs: ["+workflowId: UUID", "+type: TriggerType", "+config: JSONB"] });

  const trigType = entityBox({ x: 290, y: 290, name: "TriggerType", isEnum: true, headerBg: "#ede9fe", stroke: "#7c3aed",
    attrs: ["Manual", "Schedule", "Webhook", "Event"] });

  const step = entityBox({ x: 540, y: 60, name: "StepDefinition", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+workflowId: UUID", "+name: string", "+type: StepType", "+config: JSONB", "+positionX: float", "+positionY: float", "+displayOrder: int"] });

  const stepType = entityBox({ x: 540, y: 360, name: "StepType", isEnum: true, headerBg: "#ede9fe", stroke: "#7c3aed",
    attrs: ["Start", "End", "Form", "HttpRequest", "Condition", "Script", "Notification"] });

  // Transition is a value object (no identity, owned by WorkflowDefinition via OwnsMany)
  const trans = entityBox({ x: 790, y: 60, name: "Transition", isValueObject: true,
    attrs: ["+fromStepId: UUID", "+toStepId: UUID", "+condition: string"] });

  // ParallelGroup and JoinType are planned (Phase 2) — dashed border
  const pg = entityBox({ x: 790, y: 340, name: "ParallelGroup", planned: true, headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+workflowId: UUID", "+name: string", "+joinType: JoinType"] });

  const jt = entityBox({ x: 790, y: 520, name: "JoinType", isEnum: true, planned: true, headerBg: "#ede9fe", stroke: "#7c3aed",
    attrs: ["WaitAll", "WaitAny"] });

  for (const b of [wfd, wfs, trig, trigType, step, stepType, trans, pg, jt]) els.push(...b.els);

  // Relationships
  els.push(...arrow({ x1: wfd.midBottom.x, y1: wfd.midBottom.y, x2: wfs.midTop.x, y2: wfs.midTop.y, label: "status" }));
  els.push(...arrow({ x1: wfd.midRight.x, y1: wfd.midRight.y - 10, x2: trig.midLeft.x, y2: trig.midLeft.y, label: "1 -- 1" }));
  els.push(...arrow({ x1: wfd.midRight.x, y1: wfd.midRight.y + 10, x2: step.midLeft.x, y2: step.midLeft.y, label: "1 *--" }));
  // wfd → trans: route above all boxes to avoid crossing step
  els.push(...routedArrow({ waypoints: [[wfd.midRight.x, wfd.midRight.y + 30], [wfd.midRight.x, 30], [trans.midTop.x, 30], [trans.midTop.x, trans.midTop.y]], label: "1 *--" }));
  els.push(...arrow({ x1: trig.midBottom.x, y1: trig.midBottom.y, x2: trigType.midTop.x, y2: trigType.midTop.y, label: "type" }));
  els.push(...arrow({ x1: step.midBottom.x, y1: step.midBottom.y, x2: stepType.midTop.x, y2: stepType.midTop.y, label: "type" }));
  els.push(...arrow({ x1: pg.midBottom.x, y1: pg.midBottom.y, x2: jt.midTop.x, y2: jt.midTop.y, label: "joinType" }));
  // step → pg: route right then down (planned relationship)
  els.push(...routedArrow({ waypoints: [[step.midRight.x, step.midRight.y + 20], [760, step.midRight.y + 20], [760, pg.midLeft.y], [pg.midLeft.x, pg.midLeft.y]], label: "0..1", dashed: true }));

  return excalidraw(els);
}

// ─── form-builder — Form Model ────────────────────────────────────────────────────────

function formModelDiagram() {
  _id = 1;
  const els = [];

  els.push(text({ x: 260, y: 20, value: "Form Builder — Core Entity Relationships", size: 16, bold: true, color: C.text, anchor: "center" }));

  // Left column: FormDefinition → FormSubmission (runtime) → FormSubmissionStatus
  const fdef = entityBox({ x: 40, y: 60, name: "FormDefinition", headerBg: C.infraBg, stroke: C.infraBdr,
    attrs: ["+id: UUID", "+name: string", "+description: string", "+organizationId: UUID", "+createdAt: DateTime", "+updatedAt: DateTime"] });

  // Right column: FormFieldType enum | FormFieldDefinition
  // Section is a field type — no separate FormSection structural entity
  const fft = entityBox({ x: 290, y: 60, name: "FormFieldType", isEnum: true, headerBg: "#ede9fe", stroke: "#7c3aed",
    attrs: ["Text", "Number", "Boolean", "Date", "Dropdown", "MultiSelect", "RelationPicker", "FileUpload", "Section"] });

  const ffield = entityBox({ x: 290, y: 330, name: "FormFieldDefinition", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+formId: UUID", "+key: string", "+label: string", "+type: FormFieldType", "+required: boolean", "+displayOrder: int", "+config: JSONB"] });

  // FormSubmission: single aggregate combining assignment + captured data (1:1 relationship)
  const fsub = entityBox({ x: 40, y: 440, name: "FormSubmission", headerBg: C.modBg, stroke: C.modBdr,
    attrs: ["+id: UUID", "+formDefinitionId: UUID", "+executionId: UUID", "+executionStepId: UUID", "+assigneeUserId: UUID?", "+status: FormSubmissionStatus", "+accessToken: UUID", "+expiresAt: DateTime?", "+submittedData: JSONB", "+submittedAt: DateTime?"] });

  const fss = entityBox({ x: 40, y: 705, name: "FormSubmissionStatus", isEnum: true, headerBg: "#ede9fe", stroke: "#7c3aed",
    attrs: ["Pending", "Submitted", "Expired", "Cancelled"] });

  for (const b of [fdef, fft, ffield, fsub, fss]) els.push(...b.els);

  // Relationships
  // fdef → ffield: definition owns fields (route right then down)
  els.push(...routedArrow({ waypoints: [[fdef.midRight.x, fdef.midRight.y], [260, fdef.midRight.y], [260, ffield.midLeft.y], [ffield.midLeft.x, ffield.midLeft.y]], label: "1 *--" }));
  // ffield → fft: field has a type (straight up to enum above)
  els.push(...arrow({ x1: ffield.midTop.x, y1: ffield.midTop.y, x2: fft.midBottom.x, y2: fft.midBottom.y, label: "type" }));
  // fdef → fsub: definition instantiated at runtime
  els.push(...arrow({ x1: fdef.midBottom.x, y1: fdef.midBottom.y, x2: fsub.midTop.x, y2: fsub.midTop.y, label: "→ runtime" }));
  // fsub → fss: submission has a status
  els.push(...arrow({ x1: fsub.midBottom.x, y1: fsub.midBottom.y, x2: fss.midTop.x, y2: fss.midTop.y, label: "status" }));

  return excalidraw(els);
}

// ─── Write all files ──────────────────────────────────────────────────────────

const diagrams = [
  // Top-level architecture diagrams
  { name: "system-context",       fn: systemContext,           dir: architectureDir },
  { name: "container",            fn: containerDiagram,        dir: architectureDir },
  { name: "module-overview",      fn: moduleOverview,          dir: architectureDir },
  // Domain-level diagrams
  { name: "tenant-provisioning",  fn: tenantProvisioningDiagram, dir: useCaseDir("platform-foundation", "provision-tenant") },
  { name: "auth-flow",            fn: authFlowDiagram,           dir: useCaseDir("identity-access", "sign-in") },
  { name: "data-model",           fn: dataModelDiagram,          dir: useCaseDir("data-modeling", "create-model") },
  { name: "workflow-model",       fn: workflowModelDiagram,      dir: useCaseDir("workflow-builder", "create-workflow") },
  { name: "form-model",           fn: formModelDiagram,          dir: useCaseDir("form-builder", "create-form") },
  { name: "execution-flow",       fn: executionFlowDiagram,      dir: useCaseDir("workflow-engine", "start-execution") },
];

for (const { name, fn, dir } of diagrams) {
  mkdirSync(dir, { recursive: true });
  const path = join(dir, `${name}.excalidraw`);
  writeFileSync(path, fn(), "utf8");
  console.log(`✓ ${path.replace(join(__dir, ".."), "docs")}`);
}

console.log("\nNext: docs/scripts/generate-diagrams.ps1  (generates .svg from all .excalidraw files)");
