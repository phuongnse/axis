/**
 * generate-diagrams.mjs
 * Generates Excalidraw JSON for all architecture diagrams.
 * Run:  node docs/diagrams/generate-diagrams.mjs
 * Then: docs/scripts/generate-diagrams.ps1  (to produce .svg files)
 *
 * Top-level (docs/diagrams/):
 *   system-context  — who uses Axis, what external systems
 *   container       — what runs inside Axis, per-module databases, Wolverine
 *   module-overview — 6 modules + event-driven communication flows
 *
 * Epic-level (docs/epics/E0{N}-name/diagrams/):
 *   tenant-provisioning — org registration & async schema provisioning  (E01)
 *   auth-flow           — JWT + refresh token authentication flow         (E02)
 *   data-model          — DataModeling entity relationships               (E03)
 *   workflow-model      — WorkflowBuilder entity relationships            (E04)
 *   form-model          — FormBuilder entity relationships                (E05)
 *   execution-flow      — WorkflowEngine step execution loop             (E06)
 */

import { writeFileSync, mkdirSync } from "fs";
import { fileURLToPath } from "url";
import { dirname, join } from "path";

const __dir = dirname(fileURLToPath(import.meta.url));
const epicDir = (folder) => join(__dir, "..", "epics", folder, "diagrams");

// ─── Excalidraw primitives ────────────────────────────────────────────────────

let _id = 1;
const uid = () => `diag-${_id++}`;

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

const base = (extra = {}) => ({
  id: uid(),
  x: 0, y: 0,
  angle: 0,
  strokeColor: C.border,
  backgroundColor: "transparent",
  fillStyle: "solid",
  strokeWidth: 1.5,
  strokeStyle: "solid",
  roughness: 0,
  opacity: 100,
  groupIds: [],
  frameId: null,
  roundness: { type: 2 },
  seed: Math.floor(Math.random() * 99999),
  version: 1,
  versionNonce: 0,
  isDeleted: false,
  boundElements: [],
  updated: 1,
  link: null,
  locked: false,
  ...extra,
});

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
    fontFamily: 4,
    textAlign: "center",
    verticalAlign: "middle",
    strokeColor: color,
    backgroundColor: "transparent",
    fillStyle: "solid",
    roughness: 0,
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
    roughness: 0,
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

function badge({ x, y, label }) {
  const w = estimateWidth(label, 10) + 16;
  return [
    { ...base(), type: "rectangle", x, y, width: w, height: 22,
      backgroundColor: C.evtBg, strokeColor: C.evtBdr,
      roundness: { type: 3, value: 11 }, strokeWidth: 1.5, roughness: 0 },
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
    roughness: 0,
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
    roughness: 0,
    startArrowhead: null,
    endArrowhead: null,
  }];
}

// ─── Entity box (UML-style with header + attribute body) ─────────────────────

function entityBox({ x, y, name, attrs = [], isEnum = false,
                     headerBg = C.sysBg, stroke = C.sysBdr }) {
  const W = 190, LH = 17;
  const hdrH = isEnum ? 36 : 28;
  const bodyH = attrs.length > 0 ? attrs.length * LH + 8 : 0;
  const H = hdrH + bodyH;
  const els = [];

  // Full outer box (white body background + visible border)
  els.push({
    ...base(), type: "rectangle", x, y, width: W, height: H,
    backgroundColor: "#ffffff", strokeColor: stroke,
    roundness: { type: 3, value: 4 }, strokeWidth: 1.5,
  });

  // Header colored overlay (strokeColor = headerBg so border blends into fill)
  els.push({
    ...base(), type: "rectangle", x, y, width: W, height: hdrH,
    backgroundColor: headerBg, strokeColor: headerBg,
    roundness: { type: 3, value: 4 }, strokeWidth: 1,
  });

  // Separator between header and body
  if (attrs.length > 0) {
    els.push(...hline({ x, y: y + hdrH, w: W, color: stroke }));
  }

  // Name
  if (isEnum) {
    els.push(text({ x: x + W / 2, y: y + 11, value: "«enum»", size: 8.5, color: C.muted, anchor: "center" }));
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

  els.push(text({ x: 520, y: 30, value: "Axis Platform — System Context", size: 18, bold: true, color: C.text, anchor: "center" }));

  function actor(x, y, label, sub) {
    els.push(...rect({ x: x - 20, y, w: 40, h: 40, bg: "#e0f2fe", stroke: "#0284c7", rx: 20, label: "👤", labelSize: 18 }));
    els.push(text({ x, y: y + 52, value: label, size: 12, bold: true, anchor: "center" }));
    if (sub) els.push(text({ x, y: y + 68, value: sub, size: 10, color: C.muted, anchor: "center" }));
  }

  actor(120, 200, "Org Admin", "[builds workflows & forms]");
  actor(120, 420, "End User", "[submits forms, views pages]");

  els.push(...rect({ x: 260, y: 80, w: 520, h: 560, bg: "#f0f9ff", stroke: C.sysBdr, label: "Axis Platform", labelSize: 14, labelBold: true, labelColor: C.sysBdr }));
  els.push(...rect({ x: 300, y: 130, w: 200, h: 60, bg: C.sysBg, stroke: C.sysBdr, label: "Web Application", sub: "React 18 + TypeScript" }));
  els.push(...rect({ x: 300, y: 240, w: 200, h: 60, bg: C.sysBg, stroke: C.sysBdr, label: "API Server", sub: "ASP.NET Core 8 · Modular Monolith" }));
  els.push(...rect({ x: 300, y: 370, w: 200, h: 60, bg: C.infraBg, stroke: C.infraBdr, label: "PostgreSQL 16", sub: "Per-module databases" }));
  els.push(...rect({ x: 300, y: 460, w: 200, h: 60, bg: C.infraBg, stroke: C.infraBdr, label: "Redis 7", sub: "Cache · Session" }));
  els.push(...rect({ x: 540, y: 240, w: 200, h: 60, bg: C.modBg, stroke: C.modBdr, label: "Wolverine", sub: "Event bus · Durable outbox" }));
  els.push(...rect({ x: 540, y: 370, w: 200, h: 60, bg: C.infraBg, stroke: C.infraBdr, label: "AWS S3", sub: "File storage" }));
  els.push(...rect({ x: 830, y: 200, w: 160, h: 60, bg: C.extBg, stroke: C.extBdr, label: "Email Service", sub: "SMTP / MailKit" }));
  els.push(...rect({ x: 830, y: 340, w: 160, h: 60, bg: C.extBg, stroke: C.extBdr, label: "External APIs", sub: "HTTP Request steps" }));

  els.push(...arrow({ x1: 160, y1: 220, x2: 260, y2: 220, label: "HTTPS" }));
  els.push(...arrow({ x1: 160, y1: 440, x2: 260, y2: 300 }));
  els.push(...arrow({ x1: 400, y1: 190, x2: 400, y2: 240, label: "REST / WS" }));
  els.push(...arrow({ x1: 400, y1: 300, x2: 400, y2: 370 }));
  els.push(...arrow({ x1: 400, y1: 300, x2: 400, y2: 460 }));
  els.push(...arrow({ x1: 500, y1: 270, x2: 540, y2: 270 }));
  els.push(...arrow({ x1: 640, y1: 300, x2: 640, y2: 370 }));
  els.push(...arrow({ x1: 740, y1: 255, x2: 830, y2: 230 }));
  els.push(...arrow({ x1: 740, y1: 280, x2: 830, y2: 360 }));

  return excalidraw(els);
}

// ─── Diagram 2 — Container ────────────────────────────────────────────────────

function containerDiagram() {
  _id = 1;
  const els = [];

  els.push(text({ x: 560, y: 25, value: "Axis Platform — Container Diagram", size: 18, bold: true, color: C.text, anchor: "center" }));

  els.push(...rect({ x: 200, y: 60, w: 660, h: 700, bg: "#f0f9ff", stroke: C.sysBdr }));
  els.push(text({ x: 530, y: 85, value: "API Server — ASP.NET Core 8 Modular Monolith", size: 14, bold: true, color: C.sysBdr, anchor: "center" }));

  const modules = [
    { label: "Identity", x: 220, y: 110 },
    { label: "DataModeling", x: 380, y: 110 },
    { label: "WorkflowBuilder", x: 560, y: 110 },
    { label: "FormBuilder", x: 220, y: 210 },
    { label: "WorkflowEngine", x: 400, y: 210 },
    { label: "PageBuilder", x: 620, y: 210 },
  ];
  for (const m of modules) {
    els.push(...rect({ x: m.x, y: m.y, w: 140, h: 70, bg: C.modBg, stroke: C.modBdr, label: m.label, labelSize: 12 }));
  }

  els.push(...rect({ x: 220, y: 320, w: 620, h: 80, bg: C.evtBg, stroke: C.evtBdr, label: "Wolverine — Event Bus + Durable Outbox (per-module)", sub: "In-process relay · At-least-once delivery · Per-module outbox tables in each module DB", labelSize: 12 }));
  els.push(...rect({ x: 220, y: 430, w: 280, h: 60, bg: C.sysBg, stroke: C.sysBdr, label: "OpenIddict 5.x", sub: "OAuth2/OIDC · Auth Code + PKCE · Client Credentials" }));
  els.push(...rect({ x: 540, y: 430, w: 300, h: 60, bg: C.sysBg, stroke: C.sysBdr, label: "SignalR", sub: "Real-time execution status" }));
  els.push(...rect({ x: 200, y: 520, w: 660, h: 60, bg: C.sysBg, stroke: C.sysBdr, label: "Web Application", sub: "React 18 + TypeScript + Vite · shadcn/ui · React Flow · dnd-kit · TanStack Query · Zustand" }));

  els.push(text({ x: 960, y: 70, value: "Per-Module Databases (PostgreSQL 16)", size: 13, bold: true, color: C.infraBdr, anchor: "center" }));
  const dbs = [
    { label: "axis_identity", sub: "public schema", y: 100 },
    { label: "axis_wb", sub: "wolverine schema (outbox)", y: 170 },
    { label: "axis_we", sub: "wolverine schema (outbox)", y: 240 },
    { label: "axis_fb", sub: "wolverine schema (outbox)", y: 310 },
    { label: "axis_dm", sub: "tenant schema", y: 380 },
  ];
  for (const db of dbs) {
    els.push(...rect({ x: 880, y: db.y, w: 180, h: 55, bg: C.infraBg, stroke: C.infraBdr, label: db.label, sub: db.sub, labelSize: 11 }));
    els.push(...arrow({ x1: 860, y1: db.y + 28, x2: 870, y2: db.y + 28, color: C.infraBdr }));
  }

  els.push(...rect({ x: 880, y: 460, w: 180, h: 55, bg: C.infraBg, stroke: C.infraBdr, label: "Redis 7", sub: "Cache · Session · Schema name" }));
  els.push(...rect({ x: 880, y: 540, w: 180, h: 55, bg: C.extBg, stroke: C.extBdr, label: "AWS S3", sub: "File storage" }));
  els.push(...rect({ x: 880, y: 620, w: 180, h: 55, bg: C.extBg, stroke: C.extBdr, label: "Email Service", sub: "SMTP · MailKit" }));
  els.push(...arrow({ x1: 860, y1: 270, x2: 880, y2: 270 }));
  els.push(...arrow({ x1: 860, y1: 487, x2: 880, y2: 487 }));

  return excalidraw(els);
}

// ─── Diagram 3 — Module Overview (event-driven) ───────────────────────────────

function moduleOverview() {
  _id = 1;
  const els = [];

  els.push(text({ x: 560, y: 25, value: "Axis — Module Communication (Event-Driven)", size: 18, bold: true, color: C.text, anchor: "center" }));
  els.push(text({ x: 560, y: 50, value: "Modules are data-sovereign. Cross-module communication via Wolverine domain events only — no shared DB access.", size: 11, color: C.muted, anchor: "center" }));

  els.push(...rect({ x: 320, y: 75, w: 480, h: 55, bg: "#e2e8f0", stroke: C.border, label: "Shared Kernel  —  Domain Primitives · CQRS Abstractions · Multi-Tenancy · Event Bus", labelSize: 12 }));

  function module(x, y, label, sub, color = { bg: C.modBg, stroke: C.modBdr }) {
    return rect({ x, y, w: 200, h: 80, bg: color.bg, stroke: color.stroke, label, sub, labelSize: 13, labelBold: true });
  }

  els.push(...module(60, 180, "Identity", "Auth · Users · Roles · RBAC", { bg: "#ede9fe", stroke: "#7c3aed" }));
  els.push(...module(300, 180, "DataModeling", "Models · Records · Data Classes"));
  els.push(...module(540, 180, "WorkflowBuilder", "Definitions · Steps · Triggers"));
  els.push(...module(780, 180, "FormBuilder", "Forms · Fields · Submissions"));
  els.push(...module(300, 380, "WorkflowEngine", "Executions · Step Handlers"));
  els.push(...module(540, 380, "PageBuilder", "Pages · Widgets · Bindings", { bg: "#fce7f3", stroke: "#be185d" }));

  els.push(...rect({ x: 60, y: 320, w: 960, h: 30, bg: C.evtBg, stroke: C.evtBdr }));
  els.push(text({ x: 540, y: 335, value: "Wolverine Domain Event Bus  (durable outbox per module DB — at-least-once delivery)", size: 11, color: "#92400e", anchor: "center" }));

  const events = [
    { label: "WorkflowPublished", x: 80 },
    { label: "WorkflowArchived", x: 260 },
    { label: "WorkflowUnarchived", x: 430 },
    { label: "FormCreated", x: 620 },
    { label: "FormTaskCreated", x: 770 },
    { label: "ExecutionStarted", x: 900 },
  ];
  for (const e of events) {
    els.push(...badge({ x: e.x, y: 358, label: e.label }));
  }

  els.push(...arrow({ x1: 640, y1: 260, x2: 640, y2: 320, color: C.evtBdr, label: "publishes" }));
  els.push(...arrow({ x1: 400, y1: 350, x2: 400, y2: 380, color: C.evtBdr, label: "consumes" }));
  els.push(...arrow({ x1: 880, y1: 350, x2: 880, y2: 380, color: C.evtBdr, label: "consumes" }));
  els.push(...arrow({ x1: 400, y1: 460, x2: 400, y2: 500, color: C.muted, dashed: true, label: "reads own copy" }));

  els.push(...rect({ x: 60, y: 480, w: 220, h: 100, bg: "transparent", stroke: C.border }));
  els.push(text({ x: 170, y: 497, value: "Legend", size: 11, bold: true, anchor: "center" }));
  els.push(...rect({ x: 75, y: 510, w: 16, h: 16, bg: C.modBg, stroke: C.modBdr }));
  els.push(text({ x: 100, y: 518, value: "Module (owns its DB)", size: 10 }));
  els.push(...rect({ x: 75, y: 534, w: 16, h: 16, bg: C.evtBg, stroke: C.evtBdr }));
  els.push(text({ x: 100, y: 542, value: "Domain Event", size: 10 }));
  els.push(...arrow({ x1: 75, y1: 558, x2: 107, y2: 558, color: C.evtBdr }));
  els.push(text({ x: 112, y: 558, value: "Event-driven", size: 10 }));
  els.push(...arrow({ x1: 75, y1: 572, x2: 107, y2: 572, color: C.muted, dashed: true }));
  els.push(text({ x: 112, y: 572, value: "Local copy (denormalized)", size: 10 }));

  return excalidraw(els);
}

// ─── Sequence diagram shared helpers ─────────────────────────────────────────

function seqBuild({ title, participants, gap = 165, messages, sections = [], notes = [] }) {
  const els = [];
  const BOX_W = 125, BOX_H = 42, BOX_Y = 48;
  const LIFE_TOP = BOX_Y + BOX_H;
  const LIFE_BOT = messages.reduce((max, m) => Math.max(max, m.y), 0) + 50;
  const CANVAS_W = participants[participants.length - 1].cx + BOX_W / 2 + 30;

  // Title
  els.push(text({ x: CANVAS_W / 2, y: 20, value: title, size: 15, bold: true, color: C.text, anchor: "center" }));

  // Compute cx for each participant
  const ps = participants.map((p, i) => ({ ...p, cx: 60 + i * gap }));

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

// ─── E01 — Tenant Provisioning ───────────────────────────────────────────────

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

// ─── E02 — Auth Flow ─────────────────────────────────────────────────────────

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

// ─── E06 — Execution Flow ─────────────────────────────────────────────────────

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

// ─── E03 — Data Model ────────────────────────────────────────────────────────

function dataModelDiagram() {
  _id = 1;
  const els = [];

  els.push(text({ x: 500, y: 20, value: "Data Modeling — Core Entity Relationships", size: 16, bold: true, color: C.text, anchor: "center" }));

  const mdl = entityBox({ x: 40, y: 60, name: "ModelDefinition", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+name: string", "+slug: string", "+description: string", "+icon: string", "+color: string", "+createdAt: DateTime", "+updatedAt: DateTime"] });

  const fld = entityBox({ x: 280, y: 60, name: "FieldDefinition", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+modelId: UUID", "+name: string", "+label: string", "+type: FieldType", "+required: boolean", "+displayOrder: int", "+config: JSONB", "+createdAt: DateTime"] });

  const ftype = entityBox({ x: 520, y: 60, name: "FieldType", isEnum: true, headerBg: "#ede9fe", stroke: "#7c3aed",
    attrs: ["Text", "Number", "Boolean", "Date", "Enum", "Relation", "DataClass", "File", "JSON"] });

  const dcd = entityBox({ x: 40, y: 350, name: "DataClassDefinition", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+name: string", "+description: string", "+createdAt: DateTime", "+updatedAt: DateTime"] });

  const dcf = entityBox({ x: 280, y: 350, name: "DataClassField", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+dataClassId: UUID", "+name: string", "+label: string", "+type: FieldType", "+required: boolean", "+displayOrder: int", "+config: JSONB"] });

  const rec = entityBox({ x: 40, y: 620, name: "Record", headerBg: C.modBg, stroke: C.modBdr,
    attrs: ["+id: UUID", "+modelId: UUID", "+data: JSONB", "+createdAt: DateTime", "+updatedAt: DateTime", "+createdBy: UUID"] });

  for (const b of [mdl, fld, ftype, dcd, dcf, rec]) els.push(...b.els);

  // Relationships
  els.push(...arrow({ x1: mdl.midRight.x, y1: mdl.midRight.y, x2: fld.midLeft.x, y2: fld.midLeft.y, label: "1 *--" }));
  els.push(...arrow({ x1: fld.midRight.x, y1: fld.midRight.y, x2: ftype.midLeft.x, y2: ftype.midLeft.y, label: "has type" }));
  els.push(...arrow({ x1: dcd.midRight.x, y1: dcd.midRight.y, x2: dcf.midLeft.x, y2: dcf.midLeft.y, label: "1 *--" }));
  els.push(...arrow({ x1: dcf.midRight.x, y1: dcf.midRight.y + 20, x2: ftype.midLeft.x, y2: ftype.midBottom.y - 20, label: "has type", dashed: true }));
  els.push(...arrow({ x1: fld.midBottom.x - 20, y1: fld.bottom, x2: dcd.midTop.x + 20, y2: dcd.top, label: "→ DataClass ref", dashed: true }));
  els.push(...arrow({ x1: rec.midTop.x, y1: rec.top, x2: mdl.midBottom.x, y2: mdl.bottom, label: "instance of", dashed: true }));

  return excalidraw(els);
}

// ─── E04 — Workflow Model ────────────────────────────────────────────────────

function workflowModelDiagram() {
  _id = 1;
  const els = [];

  els.push(text({ x: 600, y: 20, value: "Workflow Builder — Core Entity Relationships", size: 16, bold: true, color: C.text, anchor: "center" }));

  const wfd = entityBox({ x: 40, y: 60, name: "WorkflowDefinition", headerBg: C.modBg, stroke: C.modBdr,
    attrs: ["+id: UUID", "+name: string", "+slug: string", "+description: string", "+status: WorkflowStatus", "+version: int", "+createdAt: DateTime", "+updatedAt: DateTime"] });

  const wfs = entityBox({ x: 40, y: 360, name: "WorkflowStatus", isEnum: true, headerBg: "#ede9fe", stroke: "#7c3aed",
    attrs: ["Draft", "Active", "Archived"] });

  const trig = entityBox({ x: 290, y: 60, name: "TriggerConfig", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+workflowId: UUID", "+type: TriggerType", "+config: JSONB"] });

  const trigType = entityBox({ x: 290, y: 290, name: "TriggerType", isEnum: true, headerBg: "#ede9fe", stroke: "#7c3aed",
    attrs: ["Manual", "Schedule", "Webhook", "Event"] });

  const step = entityBox({ x: 540, y: 60, name: "StepDefinition", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+workflowId: UUID", "+name: string", "+type: StepType", "+config: JSONB", "+positionX: float", "+positionY: float", "+displayOrder: int"] });

  const stepType = entityBox({ x: 540, y: 360, name: "StepType", isEnum: true, headerBg: "#ede9fe", stroke: "#7c3aed",
    attrs: ["Form", "HttpRequest", "Condition", "Script", "Notification"] });

  const trans = entityBox({ x: 790, y: 60, name: "Transition", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+workflowId: UUID", "+fromStepId: UUID", "+toStepId: UUID", "+label: string", "+condition: string", "+displayOrder: int"] });

  const pg = entityBox({ x: 790, y: 340, name: "ParallelGroup", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+workflowId: UUID", "+name: string", "+joinType: JoinType"] });

  const jt = entityBox({ x: 790, y: 520, name: "JoinType", isEnum: true, headerBg: "#ede9fe", stroke: "#7c3aed",
    attrs: ["WaitAll", "WaitAny"] });

  for (const b of [wfd, wfs, trig, trigType, step, stepType, trans, pg, jt]) els.push(...b.els);

  // Relationships
  els.push(...arrow({ x1: wfd.midBottom.x, y1: wfd.bottom, x2: wfs.midTop.x, y2: wfs.top, label: "status" }));
  els.push(...arrow({ x1: wfd.midRight.x, y1: wfd.midRight.y - 10, x2: trig.midLeft.x, y2: trig.midLeft.y, label: "1 -- 1" }));
  els.push(...arrow({ x1: wfd.midRight.x, y1: wfd.midRight.y + 10, x2: step.midLeft.x, y2: step.midLeft.y, label: "1 *--" }));
  els.push(...arrow({ x1: wfd.midRight.x, y1: wfd.midRight.y + 30, x2: trans.midLeft.x, y2: trans.midLeft.y, label: "1 *--" }));
  els.push(...arrow({ x1: trig.midBottom.x, y1: trig.bottom, x2: trigType.midTop.x, y2: trigType.top, label: "type" }));
  els.push(...arrow({ x1: step.midBottom.x, y1: step.bottom, x2: stepType.midTop.x, y2: stepType.top, label: "type" }));
  els.push(...arrow({ x1: pg.midBottom.x, y1: pg.bottom, x2: jt.midTop.x, y2: jt.top, label: "joinType" }));
  els.push(...arrow({ x1: step.midRight.x, y1: step.midRight.y + 20, x2: pg.midLeft.x, y2: pg.midLeft.y, label: "0..1", dashed: true }));

  return excalidraw(els);
}

// ─── E05 — Form Model ────────────────────────────────────────────────────────

function formModelDiagram() {
  _id = 1;
  const els = [];

  els.push(text({ x: 520, y: 20, value: "Form Builder — Core Entity Relationships", size: 16, bold: true, color: C.text, anchor: "center" }));

  const fdef = entityBox({ x: 40, y: 60, name: "FormDefinition", headerBg: C.infraBg, stroke: C.infraBdr,
    attrs: ["+id: UUID", "+name: string", "+description: string", "+organizationId: UUID", "+createdAt: DateTime", "+updatedAt: DateTime"] });

  const fsec = entityBox({ x: 290, y: 60, name: "FormSection", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+formId: UUID", "+title: string", "+description: string", "+displayOrder: int"] });

  const fft = entityBox({ x: 540, y: 60, name: "FormFieldType", isEnum: true, headerBg: "#ede9fe", stroke: "#7c3aed",
    attrs: ["Text", "Number", "Boolean", "Date", "Dropdown", "MultiSelect", "Checkbox", "FileUpload", "RelationPicker"] });

  const ffield = entityBox({ x: 290, y: 300, name: "FormFieldDefinition", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+formId: UUID", "+key: string", "+label: string", "+type: FormFieldType", "+required: boolean", "+displayOrder: int", "+config: JSONB"] });

  const ftask = entityBox({ x: 40, y: 440, name: "FormTask", headerBg: C.modBg, stroke: C.modBdr,
    attrs: ["+id: UUID", "+formId: UUID", "+executionId: UUID", "+stepId: UUID", "+assigneeId: UUID", "+status: FormTaskStatus", "+token: string", "+expiresAt: DateTime", "+createdAt: DateTime"] });

  const fts = entityBox({ x: 40, y: 730, name: "FormTaskStatus", isEnum: true, headerBg: "#ede9fe", stroke: "#7c3aed",
    attrs: ["Pending", "Completed", "Expired", "Cancelled"] });

  const fsub = entityBox({ x: 290, y: 590, name: "FormSubmission", headerBg: C.sysBg, stroke: C.sysBdr,
    attrs: ["+id: UUID", "+formTaskId: UUID", "+submittedBy: UUID", "+submittedAt: DateTime", "+data: JSONB"] });

  for (const b of [fdef, fsec, fft, ffield, ftask, fts, fsub]) els.push(...b.els);

  // Relationships
  els.push(...arrow({ x1: fdef.midRight.x, y1: fdef.midRight.y - 8, x2: fsec.midLeft.x, y2: fsec.midLeft.y, label: "1 *--" }));
  els.push(...arrow({ x1: fdef.midRight.x, y1: fdef.midRight.y + 8, x2: ffield.midLeft.x, y2: ffield.midLeft.y, label: "1 *--" }));
  els.push(...arrow({ x1: ffield.midRight.x, y1: ffield.midRight.y, x2: fft.midLeft.x, y2: fft.midLeft.y, label: "type" }));
  els.push(...arrow({ x1: fdef.midBottom.x, y1: fdef.bottom, x2: ftask.midTop.x, y2: ftask.top, label: "→ runtime" }));
  els.push(...arrow({ x1: ftask.midBottom.x, y1: ftask.bottom, x2: fts.midTop.x, y2: fts.top, label: "status" }));
  els.push(...arrow({ x1: ftask.midRight.x, y1: ftask.midRight.y + 30, x2: fsub.midLeft.x, y2: fsub.midLeft.y, label: "1 -- 1" }));

  return excalidraw(els);
}

// ─── Write all files ──────────────────────────────────────────────────────────

const diagrams = [
  // Top-level architecture diagrams
  { name: "system-context",       fn: systemContext,           dir: __dir },
  { name: "container",            fn: containerDiagram,        dir: __dir },
  { name: "module-overview",      fn: moduleOverview,          dir: __dir },
  // Epic-level diagrams
  { name: "tenant-provisioning",  fn: tenantProvisioningDiagram, dir: epicDir("E01-platform-foundation") },
  { name: "auth-flow",            fn: authFlowDiagram,           dir: epicDir("E02-identity-access") },
  { name: "data-model",           fn: dataModelDiagram,          dir: epicDir("E03-data-modeling") },
  { name: "workflow-model",       fn: workflowModelDiagram,      dir: epicDir("E04-workflow-builder") },
  { name: "form-model",           fn: formModelDiagram,          dir: epicDir("E05-form-builder") },
  { name: "execution-flow",       fn: executionFlowDiagram,      dir: epicDir("E06-workflow-engine") },
];

for (const { name, fn, dir } of diagrams) {
  mkdirSync(dir, { recursive: true });
  const path = join(dir, `${name}.excalidraw`);
  writeFileSync(path, fn(), "utf8");
  console.log(`✓ ${path.replace(join(__dir, ".."), "docs")}`);
}

console.log("\nNext: docs/scripts/generate-diagrams.ps1  (generates .svg from all .excalidraw files)");
