/**
 * Axis Screen Wireframes Generator
 * Run: node docs/wireframes/generate-screens.mjs
 *
 * All component helper dimensions match generate-template.mjs exactly.
 * See docs/playbooks/wireframes.md §"Component dimensions" for the canonical table.
 *
 * Files produced:
 *   _shared/app-shell.excalidraw
 *   E02-identity-access/settings-users.excalidraw
 *   E02-identity-access/settings-roles.excalidraw
 *   E02-identity-access/settings-security.excalidraw
 *   E02-identity-access/accept-invitation.excalidraw
 *   E03-data-modeling/data-models.excalidraw
 *   E03-data-modeling/data-classes.excalidraw
 *   E03-data-modeling/records.excalidraw
 *   E04-workflow-builder/workflows.excalidraw
 *   E04-workflow-builder/workflow-editor.excalidraw
 *   E05-form-builder/forms.excalidraw
 *   E05-form-builder/form-editor.excalidraw
 *   E05-form-builder/form-submission.excalidraw
 *   E06-workflow-engine/executions.excalidraw
 *   E06-workflow-engine/execution-detail.excalidraw
 */

import { writeFileSync, mkdirSync } from 'fs';
import { dirname } from 'path';

// ─── Primitive builders (identical signatures to generate-template.mjs) ───────

let _seed = 2001;
const nextSeed = () => (_seed += 3);
const BASE = {
  angle: 0, opacity: 100, groupIds: [], isDeleted: false,
  boundElements: null, updated: 1700000000000, link: null, locked: false, version: 1,
};

function rect(id, x, y, w, h, stroke, bg, sw = 1, rounded = false, extra = {}) {
  const s = nextSeed();
  return {
    ...BASE, id, type: 'rectangle', x, y, width: w, height: h,
    strokeColor: stroke, backgroundColor: bg, fillStyle: 'solid',
    strokeWidth: sw, strokeStyle: 'solid', roughness: 1,
    roundness: rounded ? { type: 3 } : null, seed: s, versionNonce: s + 1, ...extra,
  };
}

function txt(id, x, y, w, h, str, fontSize, color, align = 'left', extra = {}) {
  const s = nextSeed();
  return {
    ...BASE, id, type: 'text', x, y, width: w, height: h,
    strokeColor: color, backgroundColor: 'transparent', fillStyle: 'solid',
    strokeWidth: 1, strokeStyle: 'solid', roughness: 1, roundness: null,
    seed: s, versionNonce: s + 1, text: str, fontSize, fontFamily: 1,
    textAlign: align, verticalAlign: 'top', containerId: null,
    originalText: str, lineHeight: 1.25, ...extra,
  };
}

function hline(id, x, y, w, stroke = '#D9D7D1', sw = 1) {
  const s = nextSeed();
  return {
    ...BASE, id, type: 'line', x, y, width: w, height: 0,
    strokeColor: stroke, backgroundColor: 'transparent', fillStyle: 'solid',
    strokeWidth: sw, strokeStyle: 'solid', roughness: 0, roundness: null,
    seed: s, versionNonce: s + 1, points: [[0, 0], [w, 0]],
    lastCommittedPoint: null, startBinding: null, endBinding: null,
    startArrowhead: null, endArrowhead: null,
  };
}

function vline(id, x, y, h, stroke = '#D9D7D1', sw = 1) {
  const s = nextSeed();
  return {
    ...BASE, id, type: 'line', x, y, width: 0, height: h,
    strokeColor: stroke, backgroundColor: 'transparent', fillStyle: 'solid',
    strokeWidth: sw, strokeStyle: 'solid', roughness: 0, roundness: null,
    seed: s, versionNonce: s + 1, points: [[0, 0], [0, h]],
    lastCommittedPoint: null, startBinding: null, endBinding: null,
    startArrowhead: null, endArrowhead: null,
  };
}

function ellipse(id, x, y, w, h, stroke, bg, sw = 1) {
  const s = nextSeed();
  return {
    ...BASE, id, type: 'ellipse', x, y, width: w, height: h,
    strokeColor: stroke, backgroundColor: bg, fillStyle: 'solid',
    strokeWidth: sw, strokeStyle: 'solid', roughness: 1,
    roundness: { type: 3 }, seed: s, versionNonce: s + 1,
  };
}

// ─── Colors (identical to generate-template.mjs) ─────────────────────────────

const C = {
  primary:       '#667A6E',
  primaryDark:   '#4F5F57',
  accent:        '#C58B55',
  accentDark:    '#A8743E',
  danger:        '#9E4A44',
  dangerDark:    '#7D3A35',
  success:       '#4D6B44',
  warning:       '#B5763D',
  gray900:       '#2B2F33',
  gray700:       '#4A5058',
  gray500:       '#8A9099',
  gray300:       '#D9D7D1',
  gray100:       '#F4F3EF',
  gray50:        '#F9F8F5',
  white:         '#FFFFFF',
  infoBg:        '#EDF0EE',
  infoBorder:    '#A8BAB1',
  successBg:     '#EBF0E9',
  successBorder: '#7FA676',
  warningBg:     '#F6EEE4',
  warningBorder: '#C9975E',
  dangerBg:      '#F3ECEA',
  dangerBorder:  '#C08078',
};

// ─── Layout constants (from S18 App Shell in generate-template.mjs) ───────────

const SB  = 230;      // sidebar width  — matches shell_sidebar w=230 in S18
const HDR = 60;       // header height  — matches shell_header h=60 in S18
const OX  = 20;       // canvas left margin
const OY  = 20;       // canvas top margin
const CX  = OX + SB; // content area x = 250
const CY  = OY + HDR; // content area y = 80

// ─── Component helpers — dimensions must match generate-template.mjs ──────────

// Button — h=36 (or 40 for primary full-width), sw=2 for primary/danger, sw=1 for ghost
// Text at y+10, 13px, centered. Source: S03 Buttons.
function btn(id, x, y, label, variant = 'primary', w = 120) {
  const configs = {
    primary: { bg: C.accent,   stroke: C.accentDark, color: C.white, sw: 2 },
    danger:  { bg: C.danger,   stroke: C.dangerDark, color: C.white, sw: 2 },
    ghost:   { bg: C.white,    stroke: C.gray300,    color: C.gray700, sw: 1 },
    secondary:{ bg: C.infoBg,  stroke: C.primary,    color: C.primary, sw: 1 },
  };
  const { bg, stroke, color, sw } = configs[variant] || configs.ghost;
  return [
    rect(`${id}_bg`, x, y, w, 36, stroke, bg, sw, true),
    txt(`${id}_lbl`, x, y + 10, w, 16, label, 13, color, 'center'),
  ];
}

// Input field — h=40, placeholder at y+11, 13px. Source: S04 Form Controls.
function inputField(id, x, y, placeholder, w = 280) {
  return [
    rect(`${id}_box`, x, y, w, 40, C.gray300, C.white, 1, true),
    txt(`${id}_ph`, x + 12, y + 11, w - 24, 18, placeholder, 13, C.gray500),
  ];
}

// Select — same h=40 as input, arrow at x+w-22. Source: S04 Form Controls.
function selectField(id, x, y, placeholder, w = 280) {
  return [
    rect(`${id}_box`, x, y, w, 40, C.gray300, C.white, 1, true),
    txt(`${id}_ph`, x + 12, y + 11, w - 36, 18, placeholder, 13, C.gray500),
    txt(`${id}_arr`, x + w - 22, y + 11, 16, 18, '▾', 13, C.gray700),
  ];
}

// Badge — h=28, width=label.length×8+24, text at y+6, 12px. Source: S09 Badges.
function badge(id, x, y, label, color, bg) {
  const w = label.length * 8 + 24;
  return [
    rect(`${id}_bg`, x, y, w, 28, color, bg, 1, true),
    txt(`${id}_lbl`, x, y + 6, w, 16, label, 12, color, 'center'),
  ];
}

// Search bar — h=40, icon at y+11. Source: S04 Form Controls (search variant).
function searchBar(id, x, y, w = 280) {
  return [
    rect(`${id}_box`, x, y, w, 40, C.gray300, C.white, 1, true),
    txt(`${id}_icon`, x + 10, y + 11, 18, 18, '⌕', 14, C.gray500),
    txt(`${id}_ph`, x + 34, y + 11, w - 46, 18, 'Search…', 13, C.gray500),
  ];
}

// Page header — title 18px + optional subtitle 12px.
function pageHeader(id, x, y, title, subtitle = '') {
  const els = [txt(`${id}_title`, x, y, 500, 26, title, 18, C.gray900)];
  if (subtitle) els.push(txt(`${id}_sub`, x, y + 30, 500, 18, subtitle, 12, C.gray500));
  return els;
}

// Table header — h=44, text at yH+12, 13px. Source: S10 Table.
function tableHeader(id, x, y, w, cols) {
  const els = [
    rect(`${id}_bg`, x, y, w, 44, C.gray300, C.gray100, 1),
    hline(`${id}_div`, x, y + 44, w, C.gray300),
  ];
  let cx = x + 12;
  cols.forEach(([label, cw], i) => {
    els.push(txt(`${id}_h${i}`, cx, y + 12, cw - 16, 20, label, 13, C.gray900));
    cx += cw;
  });
  return els;
}

// Table row — h=50, text at yR+15, 13px. Source: S10 Table.
function tableRow(id, x, y, w, cells, shade = false) {
  const bg = shade ? C.gray50 : C.white;
  const els = [
    rect(`${id}_bg`, x, y, w, 50, 'transparent', bg, 0),
    hline(`${id}_div`, x, y + 50, w, C.gray300),
  ];
  let cx = x + 12;
  cells.forEach(([label, cw, color], i) => {
    els.push(txt(`${id}_c${i}`, cx, y + 15, cw - 16, 20, label, 13, color || C.gray900));
    cx += cw;
  });
  return els;
}

// Table outer border — wraps header + rows.
function tableOuter(id, x, y, w, h) {
  return rect(id, x, y, w, h, C.gray300, 'transparent', 1);
}

// ─── App shell — matches S18 App Shell in generate-template.mjs exactly ──────

// navItems: array of [label, active]
function appShell(prefix, W, H, activeNav) {
  const els = [];
  const totalH = H - OY;
  const totalW = W - OX;

  // Page background (warm off-white)
  els.push(rect(`${prefix}_bg`, OX, OY, totalW, totalH, C.gray300, C.gray100, 1));

  // Sidebar (white surface, sage accents) — matches shell_sidebar w=230
  els.push(rect(`${prefix}_sb`, OX, OY, SB, totalH, C.gray300, C.white, 1));

  // Logo area — h=60, gray50 bg, matches shell_logo in S18
  els.push(rect(`${prefix}_logo_area`, OX, OY, SB, 60, C.gray300, C.gray50, 1));
  els.push(txt(`${prefix}_logo_t`, OX + 28, OY + 18, 160, 26, '⬡  Axis', 18, C.primary));

  // Nav items — 214×36px, matches shell_ni pattern in S18
  const navItems = ['Data Models', 'Workflows', 'Forms', 'Executions', 'Settings'];
  navItems.forEach((item, i) => {
    const ny = OY + 72 + i * 44;
    const active = item === activeNav;
    const bg = active ? C.infoBg : 'transparent';
    const stroke = active ? C.infoBorder : 'transparent';
    const tc = active ? C.primary : C.gray700;
    els.push(rect(`${prefix}_ni_${i}`, OX + 8, ny, 214, 36, stroke, bg, 1));
    // Active left accent bar — 3px, matches shell_acc in S18
    if (active) els.push(rect(`${prefix}_nacc_${i}`, OX + 8, ny, 3, 36, C.primary, C.primary, 1));
    els.push(txt(`${prefix}_nl_${i}`, OX + 28, ny + 9, 170, 18, item, 13, tc));
  });

  // User area at sidebar bottom — ellipse avatar matches shell_uav in S18
  els.push(hline(`${prefix}_user_div`, OX + 8, OY + totalH - 52, 214, C.gray300));
  els.push(ellipse(`${prefix}_uav`, OX + 16, OY + totalH - 44, 32, 32, C.infoBorder, C.infoBg, 1));
  els.push(txt(`${prefix}_un`, OX + 56, OY + totalH - 36, 140, 18, 'Alex Brown', 12, C.gray900));

  // Header bar — h=60, white surface, matches shell_header in S18
  els.push(rect(`${prefix}_hdr`, CX, OY, totalW - SB, HDR, C.gray300, C.white, 1));
  els.push(hline(`${prefix}_hdr_div`, CX, OY + HDR, totalW - SB, C.gray300));

  // Header: notification + avatar ellipses — matches S18
  els.push(ellipse(`${prefix}_notif`, OX + totalW - 76, OY + 12, 36, 36, C.gray300, C.gray100, 1));
  els.push(txt(`${prefix}_notif_t`, OX + totalW - 76, OY + 21, 36, 18, '🔔', 12, C.gray700, 'center'));
  els.push(ellipse(`${prefix}_av`, OX + totalW - 32, OY + 12, 36, 36, C.infoBorder, C.infoBg, 1));
  els.push(txt(`${prefix}_av_t`, OX + totalW - 32, OY + 21, 36, 18, 'AB', 12, C.primary, 'center'));

  return els;
}

// ─── File writer ──────────────────────────────────────────────────────────────

function writeExcalidraw(filePath, elements) {
  const doc = {
    type: 'excalidraw',
    version: 2,
    source: 'generate-screens.mjs',
    elements,
    appState: { gridSize: null, viewBackgroundColor: C.gray100 },
    files: {},
  };
  mkdirSync(dirname(filePath), { recursive: true });
  writeFileSync(filePath, JSON.stringify(doc, null, 2));
  console.log(`  ✓  ${filePath}`);
}

const BASE_DIR = 'docs/wireframes';

// ═══════════════════════════════════════════════════════════════════════════════
// _shared / app-shell
// ═══════════════════════════════════════════════════════════════════════════════

function genAppShell() {
  const W = 1100, H = 640;
  const els = [];

  // ── Section 1: Full app shell layout reference ─────────────────────────────
  els.push(txt('as_title', OX, OY - 16, 700, 20, 'App Shell — Authenticated Layout (SB=230px  HDR=60px  CX=250  CY=80)', 12, C.gray500));

  els.push(...appShell('as', W, H, 'Data Models'));

  // Breadcrumb in header (matches shell_breadcrumb in S18)
  els.push(txt('as_bc', CX + 16, OY + 18, 300, 22, 'Data Models', 18, C.gray900));

  // Content area annotation
  els.push(txt('as_ann_cx', CX + 20, CY + 20, 300, 18, `Content starts at x=${CX}  y=${CY}`, 11, C.gray500));
  els.push(txt('as_ann_w', CX + 20, CY + 40, 400, 18, `Content width = W − OX − SB − 40 (20px pad each side)`, 11, C.gray500));

  // ── Section 2: Settings sub-navigation pattern ─────────────────────────────
  const s2Y = H + 20;
  els.push(txt('as2_title', OX, s2Y, 600, 18, 'Settings area — sub-navigation pattern (used by E02 settings screens)', 12, C.gray500));
  els.push(hline('as2_div', OX, s2Y + 22, W - OX * 2, C.gray300));

  const s2C = s2Y + 36;
  const s2H = 280;

  els.push(rect('as2_bg', OX, s2C, W - OX * 2, s2H, C.gray300, C.gray100));
  // main sidebar
  els.push(rect('as2_sb', OX, s2C, SB, s2H, C.gray300, C.white));
  els.push(txt('as2_sb_nav', OX + 28, s2C + 18, 160, 18, '⬡  Axis', 18, C.primary));
  // settings sub-nav panel
  const subW = 180;
  els.push(rect('as2_sub', OX + SB, s2C, subW, s2H, C.gray300, C.white));
  els.push(txt('as2_sub_hdr', OX + SB + 16, s2C + 16, 150, 20, 'Settings', 14, C.gray900));
  els.push(hline('as2_sub_sep', OX + SB, s2C + 44, subW, C.gray300));
  const subItems = [['Users & Invites', true], ['Roles & Permissions', false], ['Security', false]];
  subItems.forEach(([label, active], i) => {
    const iy = s2C + 52 + i * 40;
    if (active) {
      els.push(rect(`as2_sub_act_${i}`, OX + SB + 4, iy - 2, subW - 8, 32, C.infoBorder, C.infoBg, 1, true));
      els.push(rect(`as2_sub_bar_${i}`, OX + SB + 4, iy - 2, 3, 32, C.primary, C.primary, 1));
    }
    els.push(txt(`as2_sub_lbl_${i}`, OX + SB + 20, iy + 6, 150, 18, label, 12, active ? C.primary : C.gray700));
  });
  // content area
  els.push(rect('as2_content', OX + SB + subW, s2C, W - OX * 2 - SB - subW, s2H, C.gray300, C.gray50));
  els.push(txt('as2_content_lbl', OX + SB + subW + 20, s2C + 40, 300, 20, 'Settings Content Area', 13, C.gray500));

  writeExcalidraw(`${BASE_DIR}/_shared/app-shell.excalidraw`, els);
}

// ═══════════════════════════════════════════════════════════════════════════════
// E02 — Identity Access
// ═══════════════════════════════════════════════════════════════════════════════

function genSettingsUsers() {
  const W = 1100, H = 800;
  const PAD = 20;                          // content padding left/right
  const tW = W - OX - SB - PAD * 2;       // table width = 810
  const tX = CX + PAD;                     // table x = 270
  const els = [...appShell('su', W, H, 'Settings')];

  // Header breadcrumb
  els.push(txt('su_bc', CX + 16, OY + 20, 340, 22, 'Settings / Users & Invites', 14, C.gray700));

  // Page header + primary action
  els.push(...pageHeader('su_ph', tX, CY + PAD, 'Users & Invites'));
  els.push(...btn('su_invite', tX + tW - 120, CY + PAD, '+ Invite User', 'primary', 120));

  // Search + filters row
  const filterY = CY + 78;
  els.push(...searchBar('su_search', tX, filterY, 260));
  els.push(...selectField('su_status', tX + 276, filterY, 'Status: All', 160));
  els.push(...selectField('su_role', tX + 452, filterY, 'Role: All', 150));

  // Tabs
  const tabY = filterY + 56;
  els.push(rect('su_tab1_bg', tX, tabY, 110, 36, C.primary, C.white, 2, true));
  els.push(txt('su_tab1', tX, tabY + 9, 110, 18, 'Users', 13, C.primary, 'center'));
  els.push(rect('su_tab2_bg', tX + 118, tabY, 180, 36, C.gray300, C.white, 1, true));
  els.push(txt('su_tab2', tX + 118, tabY + 9, 180, 18, 'Pending Invitations', 13, C.gray500, 'center'));
  els.push(hline('su_tab_line', tX, tabY + 36, tW, C.gray300));

  // Table
  const tY = tabY + 52;
  const cols = [['Name / Email', 260], ['Role', 140], ['Status', 100], ['Last Active', 160], ['Actions', 150]];
  els.push(tableOuter('su_outer', tX, tY, tW, 44 + 4 * 50));
  els.push(...tableHeader('su_th', tX, tY, tW, cols));

  const users = [
    ['John Doe  ·  john@acme.com', 'Admin', 'Active', '2 hours ago', false],
    ['Jane Smith  ·  jane@acme.com', 'Editor', 'Active', 'Yesterday', true],
    ['Bob Wilson  ·  bob@acme.com', 'Viewer', 'Inactive', '3 weeks ago', false],
    ['Alice Chen  ·  alice@acme.com', 'Editor', 'Active', '1 hour ago', true],
  ];
  users.forEach(([name, role, status, lastActive, shade], i) => {
    const rY = tY + 44 + i * 50;
    const sColor = status === 'Active' ? C.success : C.gray500;
    els.push(...tableRow(`su_r${i}`, tX, rY, tW, [
      [name, 260], [role, 140], [status, 100, sColor], [lastActive, 160, C.gray500], ['Edit  ·  Remove', 150, C.primary],
    ], shade));
  });

  // Pagination
  const pgY = tY + 44 + users.length * 50 + 16;
  els.push(txt('su_pg_info', tX, pgY + 8, 280, 20, 'Showing 1–4 of 4 users', 12, C.gray500));
  els.push(...btn('su_pg_prev', tX + tW - 188, pgY, '← Prev', 'ghost', 88));
  els.push(...btn('su_pg_next', tX + tW - 92, pgY, 'Next →', 'ghost', 88));

  // ── Invite User modal ──────────────────────────────────────────────────────
  const mW = 460, mH = 340;
  const mX = CX + Math.round((W - OX - SB - mW) / 2);
  const mY = CY + 60;
  els.push(rect('su_ov', OX, OY, W - OX, H - OY, 'transparent', C.gray900, 0, false, { opacity: 30 }));
  els.push(rect('su_modal', mX, mY, mW, mH, C.gray300, C.white, 1, true));
  els.push(txt('su_modal_title', mX + 20, mY + 20, mW - 40, 24, 'Invite User', 16, C.gray900));
  els.push(hline('su_modal_sep', mX + 20, mY + 52, mW - 40));

  els.push(txt('su_email_lbl', mX + 20, mY + 68, 140, 18, 'Email address', 12, C.gray700));
  els.push(...inputField('su_email', mX + 20, mY + 88, 'colleague@company.com', mW - 40));

  els.push(txt('su_role_lbl', mX + 20, mY + 144, 60, 18, 'Role', 12, C.gray700));
  els.push(...selectField('su_role_sel', mX + 20, mY + 164, 'Select role…', mW - 40));
  els.push(txt('su_role_hint', mX + 20, mY + 212, mW - 40, 18, 'Viewer · Editor · Admin', 11, C.gray500));

  els.push(...btn('su_cancel', mX + mW - 240, mY + mH - 52, 'Cancel', 'ghost', 100));
  els.push(...btn('su_send', mX + mW - 132, mY + mH - 52, 'Send Invite', 'primary', 120));

  writeExcalidraw(`${BASE_DIR}/E02-identity-access/settings-users.excalidraw`, els);
}

function genSettingsRoles() {
  const W = 1100, H = 820;
  const PAD = 20;
  const els = [...appShell('sr', W, H, 'Settings')];

  els.push(txt('sr_bc', CX + 16, OY + 20, 360, 22, 'Settings / Roles & Permissions', 14, C.gray700));
  els.push(...pageHeader('sr_ph', CX + PAD, CY + PAD, 'Roles & Permissions'));
  els.push(...btn('sr_add', W - OX - PAD - 120, CY + PAD, '+ New Role', 'primary', 120));

  // Two-panel layout
  const panelY = CY + 76;
  const panelH = H - OY - HDR - 96;
  const listW = 220;
  const matX = CX + PAD + listW + 12;
  const matW = W - OX - PAD - matX;

  // Role list panel
  els.push(rect('sr_list', CX + PAD, panelY, listW, panelH, C.gray300, C.white));
  els.push(txt('sr_list_hdr', CX + PAD + 16, panelY + 14, 150, 20, 'Roles', 14, C.gray900));
  els.push(hline('sr_list_sep', CX + PAD, panelY + 44, listW));

  const roles = [['Admin', '3 users', true], ['Editor', '5 users', false], ['Viewer', '8 users', false], ['Custom Role', '1 user', false]];
  roles.forEach(([name, count, active], i) => {
    const ry = panelY + 52 + i * 52;
    els.push(rect(`sr_ri_${i}`, CX + PAD, ry, listW, 44,
      active ? C.infoBorder : C.gray100, active ? C.infoBg : C.white));
    if (active) els.push(rect(`sr_rb_${i}`, CX + PAD, ry, 3, 44, C.primary, C.primary, 1));
    els.push(txt(`sr_rn_${i}`, CX + PAD + 16, ry + 8, 150, 18, name, 13, active ? C.primary : C.gray900));
    els.push(txt(`sr_rc_${i}`, CX + PAD + 16, ry + 26, 150, 16, count, 11, C.gray500));
  });

  // Permission matrix panel
  els.push(rect('sr_mat', matX, panelY, matW, panelH, C.gray300, C.white));
  els.push(txt('sr_mat_hdr', matX + 16, panelY + 14, 300, 20, 'Admin — Permissions', 14, C.gray900));
  els.push(hline('sr_mat_sep', matX, panelY + 44, matW));

  const resources = ['Data Models', 'Records', 'Workflows', 'Forms', 'Executions', 'Users', 'Roles'];
  const actions = ['View', 'Create', 'Edit', 'Delete'];
  const col0W = 180;
  const actionW = 80;

  els.push(txt('sr_mx_res_h', matX + 16, panelY + 56, col0W, 18, 'Resource', 12, C.gray700));
  actions.forEach((a, i) => {
    els.push(txt(`sr_mx_act_h${i}`, matX + col0W + i * actionW, panelY + 56, actionW, 18, a, 12, C.gray700, 'center'));
  });
  els.push(hline('sr_mx_col_sep', matX + 16, panelY + 76, matW - 32));

  resources.forEach((res, ri) => {
    const ry = panelY + 84 + ri * 36;
    els.push(txt(`sr_res_${ri}`, matX + 16, ry + 9, col0W - 8, 18, res, 13, C.gray900));
    actions.forEach((_, ai) => {
      const cx = matX + col0W + ai * actionW + 28;
      els.push(rect(`sr_chk_${ri}_${ai}`, cx, ry + 8, 20, 20, C.successBorder, C.successBg, 1, true));
      els.push(txt(`sr_chkm_${ri}_${ai}`, cx, ry + 10, 20, 16, '✓', 11, C.success, 'center'));
    });
    if (ri < resources.length - 1) els.push(hline(`sr_rdiv_${ri}`, matX + 16, ry + 36, matW - 32, C.gray100));
  });

  els.push(...btn('sr_save', matX + matW - 140, panelY + panelH - 52, 'Save Changes', 'primary', 130));

  writeExcalidraw(`${BASE_DIR}/E02-identity-access/settings-roles.excalidraw`, els);
}

function genSettingsSecurity() {
  const W = 1100, H = 780;
  const PAD = 20;
  const cardX = CX + PAD;
  const cardW = W - OX - SB - PAD * 2;
  const els = [...appShell('ss', W, H, 'Settings')];

  els.push(txt('ss_bc', CX + 16, OY + 20, 280, 22, 'Settings / Security', 14, C.gray700));
  els.push(...pageHeader('ss_ph', cardX, CY + PAD, 'Security'));

  // Change Password card
  let cY = CY + 72;
  els.push(rect('ss_pwd_card', cardX, cY, cardW, 236, C.gray300, C.white, 1, true));
  els.push(txt('ss_pwd_title', cardX + 20, cY + 16, 300, 22, 'Change Password', 15, C.gray900));
  els.push(hline('ss_pwd_sep', cardX + 20, cY + 48, cardW - 40));

  const fields = [['Current password', 'ss_cur'], ['New password', 'ss_new'], ['Confirm password', 'ss_conf']];
  fields.forEach(([label, fid], i) => {
    const fy = cY + 64 + i * 52;
    els.push(txt(`${fid}_lbl`, cardX + 20, fy, 160, 18, label, 12, C.gray700));
    els.push(...inputField(fid, cardX + 200, fy - 2, '••••••••', cardW - 220));
  });
  els.push(...btn('ss_pwd_save', cardX + cardW - 160, cY + 196, 'Update Password', 'primary', 150));

  // Active Sessions card
  cY += 256;
  els.push(rect('ss_sess_card', cardX, cY, cardW, 260, C.gray300, C.white, 1, true));
  els.push(txt('ss_sess_title', cardX + 20, cY + 16, 300, 22, 'Active Sessions', 15, C.gray900));
  els.push(hline('ss_sess_sep', cardX + 20, cY + 48, cardW - 40));

  const sessions = [
    ['Chrome · macOS · 192.168.1.1', 'Current session', true],
    ['Firefox · Windows · 10.0.0.5', 'Last seen 2 hours ago', false],
    ['Mobile Safari · iOS · 172.16.0.8', 'Last seen yesterday', false],
  ];
  sessions.forEach(([device, meta, isCurrent], i) => {
    const sy = cY + 60 + i * 60;
    els.push(txt(`ss_dev_${i}`, cardX + 20, sy, 400, 20, device, 13, C.gray900));
    els.push(txt(`ss_meta_${i}`, cardX + 20, sy + 22, 400, 18, meta, 12, C.gray500));
    if (isCurrent) {
      els.push(...badge(`ss_cur_badge_${i}`, cardX + cardW - 100, sy + 8, 'Current', C.success, C.successBg));
    } else {
      els.push(...btn(`ss_revoke_${i}`, cardX + cardW - 110, sy + 4, 'Revoke', 'danger', 100));
    }
    if (i < sessions.length - 1) els.push(hline(`ss_sdiv_${i}`, cardX + 20, sy + 56, cardW - 40, C.gray100));
  });
  els.push(...btn('ss_revoke_all', cardX + cardW - 180, cY + 220, 'Revoke All Others', 'danger', 168));

  writeExcalidraw(`${BASE_DIR}/E02-identity-access/settings-security.excalidraw`, els);
}

function genAcceptInvitation() {
  // Public page — no app shell
  const W = 760, H = 520;
  const cW = 440, cH = 400;
  const cX = (W - cW) / 2, cY = (H - cH) / 2;
  const els = [];

  els.push(rect('ai_bg', 0, 0, W, H, C.gray300, C.gray100));
  els.push(rect('ai_card', cX, cY, cW, cH, C.gray300, C.white, 1, true));

  // Logo — matches S18 style: gray50 area + primary text
  els.push(rect('ai_logo_area', cX + cW / 2 - 52, cY + 24, 104, 36, C.gray300, C.gray50, 1, true));
  els.push(txt('ai_logo_t', cX + cW / 2 - 52, cY + 32, 104, 22, '⬡  Axis', 15, C.primary, 'center'));

  els.push(txt('ai_title', cX + 24, cY + 76, cW - 48, 28, 'Accept Invitation', 20, C.gray900, 'center'));
  els.push(txt('ai_sub', cX + 24, cY + 108, cW - 48, 20, "You've been invited to join Acme Corp.", 12, C.gray500, 'center'));
  els.push(hline('ai_sep', cX + 24, cY + 136, cW - 48));

  els.push(txt('ai_name_lbl', cX + 24, cY + 152, 120, 18, 'Full name', 12, C.gray700));
  els.push(...inputField('ai_name', cX + 24, cY + 172, 'Your full name', cW - 48));

  els.push(txt('ai_email_lbl', cX + 24, cY + 228, 60, 18, 'Email', 12, C.gray700));
  els.push(rect('ai_email_box', cX + 24, cY + 248, cW - 48, 40, C.gray300, C.gray100, 1, true));
  els.push(txt('ai_email_val', cX + 36, cY + 259, cW - 72, 18, 'invited@company.com', 13, C.gray700));

  els.push(txt('ai_pwd_lbl', cX + 24, cY + 304, 140, 18, 'Create password', 12, C.gray700));
  els.push(...inputField('ai_pwd', cX + 24, cY + 324, '••••••••', cW - 48));

  els.push(...btn('ai_accept', cX + 24, cY + cH - 56, 'Accept & Create Account', 'primary', cW - 48));

  writeExcalidraw(`${BASE_DIR}/E02-identity-access/accept-invitation.excalidraw`, els);
}

// ═══════════════════════════════════════════════════════════════════════════════
// E03 — Data Modeling
// ═══════════════════════════════════════════════════════════════════════════════

function genDataModels() {
  const W = 1100, H = 820;
  const PAD = 20;
  const tW = W - OX - SB - PAD * 2;
  const tX = CX + PAD;
  const els = [...appShell('dm', W, H, 'Data Models')];

  els.push(txt('dm_bc', CX + 16, OY + 20, 240, 22, 'Data Models', 14, C.gray700));
  els.push(...pageHeader('dm_ph', tX, CY + PAD, 'Data Models', 'Define custom data structures for your workspace'));
  els.push(...btn('dm_create', tX + tW - 120, CY + PAD, '+ New Model', 'primary', 120));

  els.push(...searchBar('dm_search', tX, CY + 82, 260));

  const tY = CY + 138;
  const cols = [['Name (slug)', 200], ['Display Name', 180], ['Fields', 80], ['Records', 90], ['Updated', 140], ['Actions', 120]];
  els.push(tableOuter('dm_outer', tX, tY, tW, 44 + 4 * 50));
  els.push(...tableHeader('dm_th', tX, tY, tW, cols));

  const models = [
    ['customer', 'Customer', '12', '1,240', '2 hours ago'],
    ['order', 'Order', '8', '4,891', 'Yesterday'],
    ['product', 'Product', '15', '342', '3 days ago'],
    ['invoice', 'Invoice', '10', '2,104', 'Last week'],
  ];
  models.forEach(([slug, display, fields, records, updated], i) => {
    els.push(...tableRow(`dm_r${i}`, tX, tY + 44 + i * 50, tW, [
      [slug, 200, C.primary], [display, 180], [fields, 80, C.gray700],
      [records, 90, C.gray700], [updated, 140, C.gray500], ['Fields  Edit  ···', 120, C.primary],
    ], i % 2 === 1));
  });

  // Field editor side sheet
  const shW = 380;
  const shX = W - OX - shW;
  const shH = H - OY;
  els.push(rect('dm_sh_ov', CX, CY, shX - CX, shH - CY, 'transparent', C.gray900, 0, false, { opacity: 20 }));
  els.push(rect('dm_sh', shX, OY, shW, shH, C.gray300, C.white, 2));
  els.push(txt('dm_sh_title', shX + 16, OY + 16, shW - 60, 22, 'customer — Fields', 15, C.gray900));
  els.push(txt('dm_sh_close', shX + shW - 36, OY + 14, 24, 24, '✕', 14, C.gray500));
  els.push(hline('dm_sh_sep', shX, OY + 48, shW));
  els.push(...btn('dm_sh_add', shX + shW - 128, OY + 60, '+ Add Field', 'primary', 116));

  const shFields = [['id', 'UUID', 'System'], ['name', 'Text', ''], ['email', 'Email', ''], ['phone', 'Text', 'Optional'], ['created_at', 'DateTime', 'System']];
  shFields.forEach(([fname, ftype, note], i) => {
    const fy = OY + 108 + i * 48;
    els.push(rect(`dm_sf_${i}`, shX + 12, fy, shW - 24, 40, C.gray100, C.gray50, 1));
    els.push(txt(`dm_sfn_${i}`, shX + 24, fy + 11, 120, 18, fname, 13, C.gray900));
    els.push(txt(`dm_sft_${i}`, shX + 168, fy + 11, 80, 18, ftype, 12, C.gray500));
    if (note) els.push(txt(`dm_sfnote_${i}`, shX + 272, fy + 11, 80, 18, note, 10, C.gray300));
    els.push(txt(`dm_sfact_${i}`, shX + shW - 36, fy + 11, 20, 18, '···', 12, C.gray300));
  });

  writeExcalidraw(`${BASE_DIR}/E03-data-modeling/data-models.excalidraw`, els);
}

function genDataClasses() {
  const W = 1100, H = 820;
  const PAD = 20;
  const tW = W - OX - SB - PAD * 2;
  const tX = CX + PAD;
  const els = [...appShell('dc', W, H, 'Data Models')];

  els.push(txt('dc_bc', CX + 16, OY + 20, 300, 22, 'Data Models / Data Classes', 14, C.gray700));
  els.push(...pageHeader('dc_ph', tX, CY + PAD, 'Data Classes', 'Reusable field groups shared across models'));
  els.push(...btn('dc_create', tX + tW - 120, CY + PAD, '+ New Class', 'primary', 120));
  els.push(...searchBar('dc_search', tX, CY + 82, 260));

  const tY = CY + 138;
  const cols = [['Name (slug)', 190], ['Display Name', 190], ['Fields', 80], ['Used In', 130], ['Updated', 130], ['Actions', 90]];
  els.push(tableOuter('dc_outer', tX, tY, tW, 44 + 3 * 50));
  els.push(...tableHeader('dc_th', tX, tY, tW, cols));

  [['address', 'Address', '5 fields', '3 models'], ['contact_info', 'Contact Info', '4 fields', '2 models'], ['audit_fields', 'Audit Fields', '3 fields', '8 models']].forEach(([slug, display, fields, used], i) => {
    els.push(...tableRow(`dc_r${i}`, tX, tY + 44 + i * 50, tW, [
      [slug, 190, C.primary], [display, 190], [fields, 80, C.gray700],
      [used, 130, C.gray700], ['3 days ago', 130, C.gray500], ['Edit  ···', 90, C.primary],
    ], i % 2 === 1));
  });

  // Create side sheet
  const shW = 400, shX = W - OX - shW, shH = H - OY;
  els.push(rect('dc_sh_ov', CX, CY, shX - CX, shH - CY, 'transparent', C.gray900, 0, false, { opacity: 20 }));
  els.push(rect('dc_sh', shX, OY, shW, shH, C.gray300, C.white, 2));
  els.push(txt('dc_sh_title', shX + 16, OY + 16, shW - 60, 22, 'New Data Class', 15, C.gray900));
  els.push(txt('dc_sh_close', shX + shW - 36, OY + 14, 24, 24, '✕', 14, C.gray500));
  els.push(hline('dc_sh_sep', shX, OY + 48, shW));

  let fy = OY + 64;
  [['Name (slug)', 'dc_slug', 'e.g. address'], ['Display name', 'dc_disp', 'e.g. Address'], ['Description', 'dc_desc', 'Optional']].forEach(([label, fid, ph]) => {
    els.push(txt(`${fid}_lbl`, shX + 16, fy, 200, 18, label, 12, C.gray700));
    els.push(...inputField(fid, shX + 16, fy + 20, ph, shW - 32));
    fy += 76;
  });

  els.push(txt('dc_fields_hdr', shX + 16, fy, 160, 20, 'Fields', 14, C.gray900));
  els.push(...btn('dc_add_field', shX + shW - 128, fy - 4, '+ Add Field', 'ghost', 116));
  els.push(hline('dc_fields_sep', shX + 16, fy + 28, shW - 32));

  [['street', 'Text'], ['city', 'Text'], ['postcode', 'Text']].forEach(([n, t], i) => {
    const ffy = fy + 40 + i * 48;
    els.push(rect(`dc_f_${i}`, shX + 16, ffy, shW - 32, 40, C.gray100, C.gray50, 1));
    els.push(txt(`dc_fn_${i}`, shX + 28, ffy + 11, 130, 18, n, 13, C.gray900));
    els.push(txt(`dc_ft_${i}`, shX + 200, ffy + 11, 80, 18, t, 12, C.gray500));
    els.push(txt(`dc_fdel_${i}`, shX + shW - 40, ffy + 11, 20, 18, '✕', 12, C.gray300));
  });

  els.push(...btn('dc_cancel', shX + 16, shH + OY - 56, 'Cancel', 'ghost', 100));
  els.push(...btn('dc_save', shX + shW - 140, shH + OY - 56, 'Save Class', 'primary', 128));

  writeExcalidraw(`${BASE_DIR}/E03-data-modeling/data-classes.excalidraw`, els);
}

function genRecords() {
  const W = 1100, H = 820;
  const PAD = 20;
  const tW = W - OX - SB - PAD * 2;
  const tX = CX + PAD;
  const els = [...appShell('rec', W, H, 'Data Models')];

  els.push(txt('rec_bc', CX + 16, OY + 20, 400, 22, 'Data Models / customer / Records', 14, C.gray700));
  els.push(...pageHeader('rec_ph', tX, CY + PAD, 'Customer Records'));
  els.push(...btn('rec_create', tX + tW - 128, CY + PAD, '+ New Record', 'primary', 128));

  els.push(...searchBar('rec_search', tX, CY + 82, 260));
  els.push(...selectField('rec_filter', tX + 276, CY + 82, 'Filter by field…', 180));
  els.push(...btn('rec_export', tX + tW - 116, CY + 82, 'Export CSV', 'ghost', 108));

  const tY = CY + 138;
  const cols = [['ID', 110], ['name', 180], ['email', 220], ['phone', 140], ['created_at', 120], ['Actions', 40]];
  els.push(tableOuter('rec_outer', tX, tY, tW, 44 + 5 * 50));
  els.push(...tableHeader('rec_th', tX, tY, tW, cols));

  [
    ['uuid-0001', 'Alice Johnson', 'alice@acme.com', '+1 555-0101', '2026-01-15'],
    ['uuid-0002', 'Bob Martinez', 'bob@globex.com', '+1 555-0202', '2026-01-16'],
    ['uuid-0003', 'Carol White', 'carol@initech.com', '+1 555-0303', '2026-02-01'],
    ['uuid-0004', 'Dan Brown', 'dan@umbrella.com', '+1 555-0404', '2026-02-10'],
    ['uuid-0005', 'Eve Davis', 'eve@cyberdyne.com', '+1 555-0505', '2026-03-01'],
  ].forEach(([id, name, email, phone, created], i) => {
    els.push(...tableRow(`rec_r${i}`, tX, tY + 44 + i * 50, tW, [
      [id.slice(0, 9) + '…', 110, C.gray500], [name, 180], [email, 220, C.primary],
      [phone, 140, C.gray700], [created, 120, C.gray500], ['···', 40, C.gray700],
    ], i % 2 === 1));
  });

  const pgY = tY + 44 + 5 * 50 + 16;
  els.push(txt('rec_pg', tX, pgY + 8, 300, 20, 'Showing 1–5 of 1,240 records', 12, C.gray500));
  els.push(...btn('rec_prev', tX + tW - 188, pgY, '← Prev', 'ghost', 88));
  els.push(...btn('rec_next', tX + tW - 92, pgY, 'Next →', 'ghost', 88));

  // Create record modal
  const mW = 480, mH = 380;
  const mX = CX + Math.round((W - OX - SB - mW) / 2);
  const mY = CY + 52;
  els.push(rect('rec_ov', OX, OY, W - OX, H - OY, 'transparent', C.gray900, 0, false, { opacity: 30 }));
  els.push(rect('rec_modal', mX, mY, mW, mH, C.gray300, C.white, 1, true));
  els.push(txt('rec_modal_title', mX + 20, mY + 20, mW - 40, 24, 'New Customer Record', 16, C.gray900));
  els.push(hline('rec_modal_sep', mX + 20, mY + 52, mW - 40));

  [['name', 'Full name', 'rec_mf0'], ['email', 'Email address', 'rec_mf1'], ['phone', 'Phone (optional)', 'rec_mf2']].forEach(([label, ph, fid], i) => {
    const fy = mY + 68 + i * 72;
    els.push(txt(`${fid}_lbl`, mX + 20, fy, 160, 18, label, 12, C.gray700));
    els.push(...inputField(fid, mX + 20, fy + 22, ph, mW - 40));
  });

  els.push(...btn('rec_cancel', mX + mW - 244, mY + mH - 52, 'Cancel', 'ghost', 100));
  els.push(...btn('rec_save', mX + mW - 136, mY + mH - 52, 'Save Record', 'primary', 124));

  writeExcalidraw(`${BASE_DIR}/E03-data-modeling/records.excalidraw`, els);
}

// ═══════════════════════════════════════════════════════════════════════════════
// E04 — Workflow Builder
// ═══════════════════════════════════════════════════════════════════════════════

function genWorkflows() {
  const W = 1100, H = 820;
  const PAD = 20;
  const tW = W - OX - SB - PAD * 2;
  const tX = CX + PAD;
  const els = [...appShell('wf', W, H, 'Workflows')];

  els.push(txt('wf_bc', CX + 16, OY + 20, 180, 22, 'Workflows', 14, C.gray700));
  els.push(...pageHeader('wf_ph', tX, CY + PAD, 'Workflows', 'Automate processes with visual workflows'));
  els.push(...btn('wf_create', tX + tW - 136, CY + PAD, '+ New Workflow', 'primary', 136));

  els.push(...searchBar('wf_search', tX, CY + 82, 260));
  els.push(...selectField('wf_status', tX + 276, CY + 82, 'Status: All', 160));

  const tY = CY + 138;
  const cols = [['Name', 220], ['Trigger', 160], ['Steps', 70], ['Status', 120], ['Last Run', 130], ['Actions', 110]];
  els.push(tableOuter('wf_outer', tX, tY, tW, 44 + 5 * 50));
  els.push(...tableHeader('wf_th', tX, tY, tW, cols));

  const sMap = { Active: [C.success, C.successBg], Draft: [C.gray500, C.gray100], Paused: [C.warning, C.warningBg] };
  [
    ['Customer Onboarding', 'Record Created', '5', 'Active'],
    ['Invoice Approval', 'Form Submission', '3', 'Active'],
    ['Weekly Report', 'Schedule', '4', 'Active'],
    ['Data Sync', 'Webhook', '2', 'Draft'],
    ['Archive Old Orders', 'Schedule', '3', 'Paused'],
  ].forEach(([name, trigger, steps, status], i) => {
    const [sc] = sMap[status];
    els.push(...tableRow(`wf_r${i}`, tX, tY + 44 + i * 50, tW, [
      [name, 220, C.primary], [trigger, 160, C.gray700], [steps, 70, C.gray700],
      [status, 120, sc], ['2 hours ago', 130, C.gray500], ['Edit  ···', 110, C.primary],
    ], i % 2 === 1));
  });

  const pgY = tY + 44 + 5 * 50 + 16;
  els.push(txt('wf_pg', tX, pgY + 8, 260, 20, 'Showing 1–5 of 5 workflows', 12, C.gray500));

  // Create workflow dialog
  const mW = 480, mH = 340;
  const mX = CX + Math.round((W - OX - SB - mW) / 2);
  const mY = CY + 60;
  els.push(rect('wf_ov', OX, OY, W - OX, H - OY, 'transparent', C.gray900, 0, false, { opacity: 30 }));
  els.push(rect('wf_dlg', mX, mY, mW, mH, C.gray300, C.white, 1, true));
  els.push(txt('wf_dlg_title', mX + 20, mY + 20, mW - 40, 24, 'New Workflow', 16, C.gray900));
  els.push(hline('wf_dlg_sep', mX + 20, mY + 52, mW - 40));

  [['Name', 'wf_dn', 'Workflow name'], ['Description', 'wf_dd', 'Optional description']].forEach(([label, fid, ph], i) => {
    const fy = mY + 68 + i * 72;
    els.push(txt(`${fid}_lbl`, mX + 20, fy, 140, 18, label, 12, C.gray700));
    els.push(...inputField(fid, mX + 20, fy + 22, ph, mW - 40));
  });
  els.push(txt('wf_dt_lbl', mX + 20, mY + 212, 120, 18, 'Trigger type', 12, C.gray700));
  els.push(...selectField('wf_dt', mX + 20, mY + 232, 'Select trigger…', mW - 40));

  els.push(...btn('wf_cancel', mX + mW - 244, mY + mH - 52, 'Cancel', 'ghost', 100));
  els.push(...btn('wf_dlg_create', mX + mW - 136, mY + mH - 52, 'Create', 'primary', 124));

  writeExcalidraw(`${BASE_DIR}/E04-workflow-builder/workflows.excalidraw`, els);
}

function genWorkflowEditor() {
  const W = 1240, H = 820;
  const TB = 52;       // toolbar height (editor-specific, not app shell)
  const PAL_W = 260;  // step palette
  const PROP_W = 300; // properties panel
  const CVS_W = W - PAL_W - PROP_W;
  const els = [];

  // Toolbar
  els.push(rect('we_tb', 0, 0, W, TB, C.gray300, C.white, 1));
  els.push(hline('we_tb_div', 0, TB, W, C.gray300));
  els.push(txt('we_back', 16, TB / 2 - 9, 100, 18, '← Workflows', 13, C.primary));
  els.push(vline('we_tb_sep', 120, 10, TB - 20, C.gray300));
  els.push(txt('we_name', 132, TB / 2 - 9, 220, 18, 'Customer Onboarding', 14, C.gray900));
  els.push(...badge('we_badge', 364, TB / 2 - 14, 'Active', C.success, C.successBg));
  els.push(...btn('we_save', W - 240, 8, 'Save', 'ghost', 80));
  els.push(...btn('we_activate', W - 152, 8, 'Activate', 'primary', 136));

  // Step palette (left)
  els.push(rect('we_pal', 0, TB, PAL_W, H - TB, C.gray300, C.white));
  els.push(txt('we_pal_title', 16, TB + 14, PAL_W - 32, 20, 'Step Types', 14, C.gray900));
  els.push(hline('we_pal_sep', 0, TB + 44, PAL_W));
  els.push(txt('we_pal_hint', 16, TB + 52, PAL_W - 32, 18, 'Drag onto canvas', 11, C.gray500));

  const stepTypes = [
    ['Trigger', 'Start condition', C.primary, C.infoBg, C.infoBorder],
    ['Condition', 'Branch on logic', C.warning, C.warningBg, C.warningBorder],
    ['Action', 'Perform action', C.accent, C.warningBg, C.warningBorder],
    ['Send Email', 'Notify via email', C.primary, C.infoBg, C.infoBorder],
    ['Webhook', 'Call external URL', C.primary, C.infoBg, C.infoBorder],
    ['Wait', 'Delay or await', C.gray700, C.gray100, C.gray300],
    ['End', 'Terminate path', C.danger, C.dangerBg, C.dangerBorder],
  ];
  stepTypes.forEach(([name, desc, tc, bg, stroke], i) => {
    const sy = TB + 72 + i * 52;
    els.push(rect(`we_st_${i}`, 10, sy, PAL_W - 20, 44, stroke, bg, 1, true));
    els.push(txt(`we_stn_${i}`, 24, sy + 8, PAL_W - 48, 18, name, 13, tc));
    els.push(txt(`we_std_${i}`, 24, sy + 26, PAL_W - 48, 14, desc, 10, C.gray500));
    els.push(txt(`we_stdg_${i}`, PAL_W - 28, sy + 13, 18, 18, '⠿', 12, C.gray300));
  });

  // Canvas
  els.push(rect('we_cvs', PAL_W, TB, CVS_W, H - TB, C.gray300, C.gray100));
  els.push(txt('we_cvs_hint', PAL_W + CVS_W / 2 - 60, TB + 20, 120, 18, '· · · canvas · · ·', 11, C.gray300, 'center'));

  // Workflow nodes
  const nW = 180, nH = 60;
  const nX = PAL_W + (CVS_W - nW) / 2;
  const nodes = [
    [nX, TB + 60, 'Record Created', 'Trigger', C.primary, C.infoBg, C.infoBorder, false],
    [nX, TB + 184, 'Validate Data', 'Condition', C.warning, C.warningBg, C.warningBorder, true],
    [nX - 110, TB + 308, 'Send Welcome Email', 'Action', C.accent, C.warningBg, C.warningBorder, false],
    [nX + 110, TB + 308, 'Flag for Review', 'Action', C.accent, C.warningBg, C.warningBorder, false],
    [nX, TB + 432, 'End', 'End', C.danger, C.dangerBg, C.dangerBorder, false],
  ];
  nodes.forEach(([nx, ny, label, type, tc, bg, stroke, selected], i) => {
    els.push(rect(`we_n_${i}`, nx, ny, nW, nH, selected ? C.accent : stroke, bg, selected ? 2 : 1, true));
    els.push(txt(`we_nt_${i}`, nx + 10, ny + 8, nW - 20, 16, type, 10, tc));
    els.push(txt(`we_nl_${i}`, nx + 10, ny + 28, nW - 20, 20, label, 13, C.gray900));
  });
  // connectors
  const midX = nX + nW / 2;
  els.push(vline('we_c01', midX, nodes[0][1] + nH, nodes[1][1] - nodes[0][1] - nH, C.gray500));
  els.push(vline('we_c14', midX, nodes[1][1] + nH, nodes[4][1] - nodes[1][1] - nH, C.gray500));
  els.push(hline('we_c12h', nodes[2][0] + nW / 2, nodes[2][1] + nH / 2, midX - nodes[2][0] - nW / 2, C.gray500));
  els.push(hline('we_c13h', midX, nodes[3][1] + nH / 2, nodes[3][0] - midX, C.gray500));

  // Properties panel (right)
  els.push(rect('we_props', PAL_W + CVS_W, TB, PROP_W, H - TB, C.gray300, C.white));
  els.push(txt('we_props_title', PAL_W + CVS_W + 16, TB + 14, PROP_W - 32, 20, 'Step Properties', 14, C.gray900));
  els.push(hline('we_props_sep', PAL_W + CVS_W, TB + 44, PROP_W));

  const px = PAL_W + CVS_W + 16;
  const pW = PROP_W - 32;
  let py = TB + 56;
  els.push(txt('we_pt', px, py, pW, 18, 'Type: Condition', 11, C.warning));
  py += 20;
  els.push(txt('we_pn_lbl', px, py, 100, 18, 'Step name', 12, C.gray700));
  els.push(...inputField('we_pn', px, py + 20, 'Validate Data', pW));
  py += 72;
  els.push(txt('we_pc_lbl', px, py, 100, 18, 'Condition', 12, C.gray700));
  els.push(...selectField('we_pc_field', px, py + 20, 'Select field…', pW));
  els.push(...selectField('we_pc_op', px, py + 68, 'Operator…', Math.floor(pW / 2) - 4));
  els.push(...inputField('we_pc_val', px + Math.floor(pW / 2) + 4, py + 68, 'Value', Math.ceil(pW / 2) - 4));
  py += 124;
  els.push(txt('we_pb_lbl', px, py, 100, 18, 'Branches', 12, C.gray700));
  ['True → Send Welcome Email', 'False → Flag for Review'].forEach((b, i) => {
    els.push(rect(`we_pb_${i}`, px, py + 20 + i * 44, pW, 36, C.gray300, C.gray50, 1, true));
    els.push(txt(`we_pbl_${i}`, px + 10, py + 29 + i * 44, pW - 20, 18, b, 11, C.gray700));
  });

  writeExcalidraw(`${BASE_DIR}/E04-workflow-builder/workflow-editor.excalidraw`, els);
}

// ═══════════════════════════════════════════════════════════════════════════════
// E05 — Form Builder
// ═══════════════════════════════════════════════════════════════════════════════

function genForms() {
  const W = 1100, H = 820;
  const PAD = 20;
  const tW = W - OX - SB - PAD * 2;
  const tX = CX + PAD;
  const els = [...appShell('fb', W, H, 'Forms')];

  els.push(txt('fb_bc', CX + 16, OY + 20, 140, 22, 'Forms', 14, C.gray700));
  els.push(...pageHeader('fb_ph', tX, CY + PAD, 'Forms', 'Collect data and trigger workflows'));
  els.push(...btn('fb_create', tX + tW - 116, CY + PAD, '+ New Form', 'primary', 116));
  els.push(...searchBar('fb_search', tX, CY + 82, 260));

  const tY = CY + 138;
  const cols = [['Name', 220], ['Linked Model', 170], ['Submissions', 110], ['Status', 120], ['Updated', 120], ['Actions', 70]];
  els.push(tableOuter('fb_outer', tX, tY, tW, 44 + 5 * 50));
  els.push(...tableHeader('fb_th', tX, tY, tW, cols));

  [
    ['Customer Intake', 'customer', '142', 'Published'],
    ['Support Request', 'ticket', '89', 'Published'],
    ['Order Form', 'order', '34', 'Draft'],
    ['Feedback Survey', 'feedback', '210', 'Published'],
    ['Invoice Approval', 'invoice', '12', 'Draft'],
  ].forEach(([name, model, subs, status], i) => {
    const sc = status === 'Published' ? C.success : C.gray500;
    els.push(...tableRow(`fb_r${i}`, tX, tY + 44 + i * 50, tW, [
      [name, 220, C.primary], [model, 170, C.gray700], [subs, 110, C.gray700],
      [status, 120, sc], ['2 days ago', 120, C.gray500], ['Edit  ···', 70, C.primary],
    ], i % 2 === 1));
  });

  // Create form dialog
  const mW = 480, mH = 360;
  const mX = CX + Math.round((W - OX - SB - mW) / 2);
  const mY = CY + 60;
  els.push(rect('fb_ov', OX, OY, W - OX, H - OY, 'transparent', C.gray900, 0, false, { opacity: 30 }));
  els.push(rect('fb_dlg', mX, mY, mW, mH, C.gray300, C.white, 1, true));
  els.push(txt('fb_dlg_title', mX + 20, mY + 20, mW - 40, 24, 'New Form', 16, C.gray900));
  els.push(hline('fb_dlg_sep', mX + 20, mY + 52, mW - 40));

  [['Form name', 'fb_dn', 'e.g. Customer Intake'], ['Linked data model', 'fb_dm', null], ['On submit: trigger workflow', 'fb_dw', null]].forEach(([label, fid, ph], i) => {
    const fy = mY + 68 + i * 76;
    els.push(txt(`${fid}_lbl`, mX + 20, fy, 240, 18, label, 12, C.gray700));
    if (ph) {
      els.push(...inputField(fid, mX + 20, fy + 22, ph, mW - 40));
    } else {
      els.push(...selectField(fid, mX + 20, fy + 22, i === 1 ? 'Select model…' : 'Select workflow (optional)…', mW - 40));
    }
  });

  els.push(...btn('fb_cancel', mX + mW - 244, mY + mH - 52, 'Cancel', 'ghost', 100));
  els.push(...btn('fb_dlg_create', mX + mW - 136, mY + mH - 52, 'Create', 'primary', 124));

  writeExcalidraw(`${BASE_DIR}/E05-form-builder/forms.excalidraw`, els);
}

function genFormEditor() {
  const W = 1240, H = 820;
  const TB = 52;
  const PAL_W = 220;
  const PROP_W = 280;
  const CVS_W = W - PAL_W - PROP_W;
  const els = [];

  // Toolbar
  els.push(rect('fe_tb', 0, 0, W, TB, C.gray300, C.white, 1));
  els.push(hline('fe_tb_div', 0, TB, W, C.gray300));
  els.push(txt('fe_back', 16, TB / 2 - 9, 70, 18, '← Forms', 13, C.primary));
  els.push(vline('fe_tb_sep', 96, 10, TB - 20, C.gray300));
  els.push(txt('fe_name', 108, TB / 2 - 9, 220, 18, 'Customer Intake', 14, C.gray900));
  els.push(...badge('fe_badge', 340, TB / 2 - 14, 'Draft', C.gray500, C.gray100));
  els.push(...btn('fe_preview', W - 332, 8, 'Preview', 'ghost', 88));
  els.push(...btn('fe_save', W - 236, 8, 'Save', 'ghost', 80));
  els.push(...btn('fe_publish', W - 148, 8, 'Publish', 'primary', 136));

  // Field type palette (left)
  els.push(rect('fe_pal', 0, TB, PAL_W, H - TB, C.gray300, C.white));
  els.push(txt('fe_pal_title', 16, TB + 14, PAL_W - 32, 20, 'Field Types', 14, C.gray900));
  els.push(hline('fe_pal_sep', 0, TB + 44, PAL_W));

  ['Text', 'Number', 'Email', 'Date', 'Dropdown', 'Checkbox', 'File Upload', 'Rich Text', 'Relation'].forEach((ft, i) => {
    const fy = TB + 52 + i * 40;
    els.push(rect(`fe_ft_${i}`, 10, fy, PAL_W - 20, 32, C.gray300, C.gray50, 1, true));
    els.push(txt(`fe_ftn_${i}`, 24, fy + 7, PAL_W - 52, 18, ft, 13, C.gray700));
    els.push(txt(`fe_ftd_${i}`, PAL_W - 28, fy + 7, 18, 18, '⠿', 12, C.gray300));
  });

  // Canvas
  els.push(rect('fe_cvs', PAL_W, TB, CVS_W, H - TB, C.gray300, C.gray100));

  // Form preview card in canvas
  const fX = PAL_W + 60, fW = CVS_W - 120, fY = TB + 36;
  els.push(rect('fe_form', fX, fY, fW, H - TB - 72, C.gray300, C.white, 1, true));
  els.push(txt('fe_form_title', fX + 24, fY + 20, fW - 48, 26, 'Customer Intake', 18, C.gray900));
  els.push(txt('fe_form_sub', fX + 24, fY + 50, fW - 48, 18, 'Fill out the form below', 12, C.gray500));
  els.push(hline('fe_form_sep', fX + 24, fY + 76, fW - 48));

  [['Full Name *', 'Enter full name', true], ['Email Address *', 'Enter email', false], ['Phone Number', 'Enter phone (optional)', false]].forEach(([label, ph, selected], i) => {
    const ffy = fY + 96 + i * 80;
    els.push(txt(`fe_ff_lbl_${i}`, fX + 24, ffy, 220, 18, label, 12, C.gray700));
    els.push(rect(`fe_ff_box_${i}`, fX + 24, ffy + 22, fW - 48, 40,
      selected ? C.accent : C.gray300, C.white, selected ? 2 : 1, true));
    els.push(txt(`fe_ff_ph_${i}`, fX + 36, ffy + 33, fW - 72, 18, ph, 13, C.gray500));
  });
  els.push(...btn('fe_form_submit', fX + 24, fY + 340, 'Submit', 'primary', 120));

  // Properties panel (right)
  els.push(rect('fe_props', PAL_W + CVS_W, TB, PROP_W, H - TB, C.gray300, C.white));
  els.push(txt('fe_props_title', PAL_W + CVS_W + 16, TB + 14, PROP_W - 32, 20, 'Field Properties', 14, C.gray900));
  els.push(hline('fe_props_sep', PAL_W + CVS_W, TB + 44, PROP_W));

  const px = PAL_W + CVS_W + 16;
  const pW = PROP_W - 32;
  let py = TB + 56;
  els.push(txt('fe_pt', px, py, pW, 18, 'Field type: Text', 11, C.gray500));
  py += 22;
  els.push(txt('fe_plbl_lbl', px, py, 80, 18, 'Label', 12, C.gray700));
  els.push(...inputField('fe_plbl', px, py + 20, 'Full Name', pW));
  py += 72;
  els.push(txt('fe_pph_lbl', px, py, 100, 18, 'Placeholder', 12, C.gray700));
  els.push(...inputField('fe_pph', px, py + 20, 'Enter full name', pW));
  py += 72;
  els.push(txt('fe_preq_lbl', px, py, 80, 18, 'Required', 12, C.gray700));
  // Toggle — matches S04 Toggle in template
  els.push(rect('fe_tog', px + pW - 48, py - 2, 44, 24, C.primaryDark, C.primary, 1, true));
  els.push(ellipse('fe_tog_k', px + pW - 28, py, 20, 20, C.white, C.white, 1));
  py += 40;
  els.push(txt('fe_pval_lbl', px, py, 100, 18, 'Validation', 12, C.gray700));
  els.push(...selectField('fe_pval', px, py + 20, 'None', pW));

  writeExcalidraw(`${BASE_DIR}/E05-form-builder/form-editor.excalidraw`, els);
}

function genFormSubmission() {
  const W = 1100, H = 960;
  const els = [];

  // ── Section 1: Public form page ────────────────────────────────────────────
  els.push(txt('fs_s1_hdr', OX, OY, 700, 18, 'Public Form — Submission view (unauthenticated)', 13, C.gray500));
  els.push(hline('fs_s1_div', OX, OY + 22, W - OX * 2));

  const pgW = 520;
  const pgX = (W - pgW) / 2;
  const pgY = OY + 36;
  els.push(rect('fs_bg', 0, pgY, W, 440, C.gray300, C.gray100));

  els.push(rect('fs_card', pgX, pgY + 16, pgW, 400, C.gray300, C.white, 1, true));

  // Logo — matches S18 style
  els.push(rect('fs_logo_area', pgX + pgW / 2 - 48, pgY + 28, 96, 32, C.gray300, C.gray50, 1, true));
  els.push(txt('fs_logo_t', pgX + pgW / 2 - 48, pgY + 36, 96, 20, '⬡  Axis', 13, C.primary, 'center'));

  els.push(txt('fs_title', pgX + 24, pgY + 72, pgW - 48, 28, 'Customer Intake', 20, C.gray900, 'center'));
  els.push(txt('fs_org', pgX + 24, pgY + 104, pgW - 48, 18, 'Acme Corp', 12, C.gray500, 'center'));
  els.push(hline('fs_sep', pgX + 24, pgY + 130, pgW - 48));

  [['Full Name *', 'Alice Johnson'], ['Email Address *', 'alice@example.com'], ['Phone', '+1 555-0101']].forEach(([label, val], i) => {
    const fy = pgY + 148 + i * 68;
    els.push(txt(`fs_lbl_${i}`, pgX + 24, fy, 220, 18, label, 12, C.gray700));
    els.push(rect(`fs_box_${i}`, pgX + 24, fy + 22, pgW - 48, 40, C.gray300, C.white, 1, true));
    els.push(txt(`fs_val_${i}`, pgX + 36, fy + 33, pgW - 72, 18, val, 13, C.gray900));
  });
  els.push(...btn('fs_submit', pgX + 24, pgY + 360, 'Submit Form', 'primary', pgW - 48));

  // Success state annotation
  els.push(txt('fs_note', pgX + pgW + 16, pgY + 170, 160, 60, '→ On success:\nshows confirmation\nmessage + checkmark', 11, C.gray500));

  // ── Section 2: My Tasks ────────────────────────────────────────────────────
  const s2Y = pgY + 460;
  els.push(txt('fs_s2_hdr', OX, s2Y, 700, 18, 'My Tasks — Form submission task assignment (authenticated)', 13, C.gray500));
  els.push(hline('fs_s2_div', OX, s2Y + 22, W - OX * 2));

  // Mini app shell
  const shellY = s2Y + 36;
  const shellH = 340;
  els.push(rect('fs_shell_bg', OX, shellY, W - OX * 2, shellH, C.gray300, C.gray100));
  els.push(rect('fs_shell_sb', OX, shellY, SB, shellH, C.gray300, C.white));
  els.push(rect('fs_shell_logo', OX, shellY, SB, 60, C.gray300, C.gray50));
  els.push(txt('fs_shell_logo_t', OX + 28, shellY + 18, 160, 26, '⬡  Axis', 18, C.primary));
  els.push(txt('fs_shell_nav', OX + 28, shellY + 82, 160, 18, 'My Tasks', 13, C.primary));
  els.push(rect('fs_shell_ni', OX + 8, shellY + 72, 214, 36, C.infoBorder, C.infoBg, 1));
  els.push(rect('fs_shell_nb', OX + 8, shellY + 72, 3, 36, C.primary, C.primary, 1));

  const tX2 = OX + SB + 20;
  const tW2 = W - OX * 2 - SB - 40;
  els.push(txt('fs_tasks_title', tX2, shellY + 20, 300, 26, 'My Tasks', 18, C.gray900));
  els.push(...selectField('fs_tasks_filter', tX2, shellY + 58, 'Status: Pending', 180));

  const tY2 = shellY + 112;
  const taskCols = [['Form', 180], ['Record', 180], ['Assigned to', 130], ['Status', 110], ['Due', 100], ['Action', 60]];
  els.push(tableOuter('fs_task_outer', tX2, tY2, tW2, 44 + 2 * 50));
  els.push(...tableHeader('fs_task_th', tX2, tY2, tW2, taskCols));
  [
    ['Customer Intake', 'Alice Johnson', 'John Doe', 'Pending'],
    ['Invoice Approval', 'INV-2026-042', 'Jane Smith', 'In Review'],
  ].forEach(([form, rec, assignee, status], i) => {
    const sc = status === 'Pending' ? C.warning : C.primary;
    els.push(...tableRow(`fs_tr_${i}`, tX2, tY2 + 44 + i * 50, tW2, [
      [form, 180, C.primary], [rec, 180], [assignee, 130, C.gray700],
      [status, 110, sc], ['Tomorrow', 100, C.gray500], ['Open', 60, C.primary],
    ], i % 2 === 1));
  });

  writeExcalidraw(`${BASE_DIR}/E05-form-builder/form-submission.excalidraw`, els);
}

// ═══════════════════════════════════════════════════════════════════════════════
// E06 — Workflow Engine
// ═══════════════════════════════════════════════════════════════════════════════

function genExecutions() {
  const W = 1100, H = 880;
  const PAD = 20;
  const tW = W - OX - SB - PAD * 2;
  const tX = CX + PAD;
  const els = [...appShell('ex', W, H, 'Executions')];

  els.push(txt('ex_bc', CX + 16, OY + 20, 180, 22, 'Executions', 14, C.gray700));
  els.push(...pageHeader('ex_ph', tX, CY + PAD, 'Executions', 'Monitor workflow execution history'));

  // Filters
  els.push(...searchBar('ex_search', tX, CY + 82, 220));
  els.push(...selectField('ex_wf', tX + 236, CY + 82, 'Workflow: All', 180));
  els.push(...selectField('ex_status', tX + 432, CY + 82, 'Status: All', 150));
  els.push(...selectField('ex_date', tX + 598, CY + 82, 'Date range', 140));

  // Stats row — matches S12 Stat Card pattern
  const stY = CY + 142;
  const stW = 168;
  [['Total', '1,482', C.gray900], ['Completed', '1,341', C.success], ['Failed', '87', C.danger], ['Running', '54', C.primary]].forEach(([label, val, vc], i) => {
    const sx = tX + i * (stW + 12);
    els.push(rect(`ex_stat_${i}`, sx, stY, stW, 60, C.gray300, C.white, 1, true));
    els.push(txt(`ex_sv_${i}`, sx + 16, stY + 8, stW - 32, 28, val, 22, vc));
    els.push(txt(`ex_sl_${i}`, sx + 16, stY + 38, stW - 32, 18, label, 12, C.gray500));
  });

  const tY = stY + 76;
  const cols = [['Execution ID', 150], ['Workflow', 190], ['Trigger', 130], ['Status', 120], ['Duration', 90], ['Started', 130]];
  els.push(tableOuter('ex_outer', tX, tY, tW, 44 + 5 * 50));
  els.push(...tableHeader('ex_th', tX, tY, tW, cols));

  const sMap2 = { Completed: C.success, Failed: C.danger, Running: C.primary, Cancelled: C.gray500 };
  [
    ['exec-0001', 'Customer Onboarding', 'Record Created', 'Completed', '1.2s', '10:42 AM'],
    ['exec-0002', 'Invoice Approval', 'Form Submission', 'Failed', '0.8s', '10:38 AM'],
    ['exec-0003', 'Weekly Report', 'Schedule', 'Running', '—', '10:35 AM'],
    ['exec-0004', 'Customer Onboarding', 'Record Created', 'Completed', '2.1s', '10:20 AM'],
    ['exec-0005', 'Data Sync', 'Webhook', 'Cancelled', '0.2s', '09:55 AM'],
  ].forEach(([id, wf, trigger, status, dur, started], i) => {
    const sc = sMap2[status] || C.gray700;
    els.push(...tableRow(`ex_r${i}`, tX, tY + 44 + i * 50, tW, [
      [id, 150, C.primary], [wf, 190], [trigger, 130, C.gray700],
      [status, 120, sc], [dur, 90, C.gray700], [started, 130, C.gray500],
    ], i % 2 === 1));
  });

  const pgY = tY + 44 + 5 * 50 + 16;
  els.push(txt('ex_pg', tX, pgY + 8, 320, 20, 'Showing 1–5 of 1,482 executions', 12, C.gray500));
  els.push(...btn('ex_prev', tX + tW - 188, pgY, '← Prev', 'ghost', 88));
  els.push(...btn('ex_next', tX + tW - 92, pgY, 'Next →', 'ghost', 88));

  writeExcalidraw(`${BASE_DIR}/E06-workflow-engine/executions.excalidraw`, els);
}

function genExecutionDetail() {
  const W = 1100, H = 960;
  const PAD = 20;
  const contentW = W - OX - SB - PAD * 2;
  const tX = CX + PAD;
  const els = [...appShell('ed', W, H, 'Executions')];

  els.push(txt('ed_bc', CX + 16, OY + 20, 340, 22, 'Executions / exec-0002', 14, C.gray700));

  // Page header
  els.push(txt('ed_title', tX, CY + PAD, 400, 26, 'exec-0002', 18, C.gray900));
  els.push(...badge('ed_badge', tX + 148, CY + PAD + 4, 'Failed', C.danger, C.dangerBg));
  els.push(...btn('ed_retry', tX + contentW - 100, CY + PAD, 'Retry', 'primary', 100));

  els.push(txt('ed_meta', tX, CY + 58, contentW, 18,
    'Workflow: Invoice Approval  ·  Trigger: Form Submission  ·  Started: 10:38 AM  ·  Duration: 0.8s', 11, C.gray500));

  // Error banner
  const errY = CY + 86;
  els.push(rect('ed_err_banner', tX, errY, contentW, 48, C.dangerBorder, C.dangerBg, 1, true));
  els.push(txt('ed_err_icon', tX + 14, errY + 14, 20, 20, '⚠', 14, C.danger));
  els.push(txt('ed_err_msg', tX + 40, errY + 14, contentW - 60, 20,
    'Step "Validate Invoice" failed: required field "amount" is missing', 12, C.danger));

  // Two-column layout
  const leftW = 460;
  const rightX = tX + leftW + 20;
  const rightW = contentW - leftW - 20;

  // ── Timeline (left) ────────────────────────────────────────────────────────
  const tlY = errY + 64;
  els.push(txt('ed_tl_title', tX, tlY, 300, 22, 'Execution Timeline', 15, C.gray900));
  els.push(hline('ed_tl_sep', tX, tlY + 28, leftW));

  const stepDefs = [
    ['Form Submission Received', 'completed', '10:38:00.000', '0ms'],
    ['Load Invoice Data', 'completed', '10:38:00.120', '120ms'],
    ['Validate Invoice', 'failed', '10:38:00.640', '520ms'],
    ['Send Approval Email', 'skipped', '—', '—'],
    ['Update Record Status', 'skipped', '—', '—'],
  ];
  const stepC = { completed: C.success, failed: C.danger, skipped: C.gray300 };
  const stepBg = { completed: C.successBg, failed: C.dangerBg, skipped: C.gray50 };
  const stepBorder = { completed: C.successBorder, failed: C.dangerBorder, skipped: C.gray300 };

  stepDefs.forEach(([name, status, time, dur], i) => {
    const sy = tlY + 40 + i * 60;
    // dot + connector
    els.push(rect(`ed_dot_${i}`, tX, sy + 10, 12, 12, stepC[status], stepC[status], 1, true));
    if (i < stepDefs.length - 1) els.push(vline(`ed_conn_${i}`, tX + 5, sy + 22, 48, stepC[status]));
    // step card
    els.push(rect(`ed_card_${i}`, tX + 24, sy, leftW - 24, 48, stepBorder[status], stepBg[status], 1, true));
    els.push(txt(`ed_cname_${i}`, tX + 36, sy + 8, 260, 18, name, 13, C.gray900));
    els.push(txt(`ed_cmeta_${i}`, tX + 36, sy + 28, 200, 16, `${time}  ·  ${dur}`, 11, C.gray500));
    els.push(txt(`ed_cstatus_${i}`, tX + leftW - 80, sy + 15, 68, 18, status, 11, stepC[status], 'right'));
  });

  // ── Step detail (right) ────────────────────────────────────────────────────
  const detY = tlY;
  els.push(txt('ed_det_title', rightX, detY, rightW, 22, 'Step Detail: Validate Invoice', 15, C.gray900));
  els.push(hline('ed_det_sep', rightX, detY + 28, rightW));

  // Error detail card
  const edY = detY + 40;
  els.push(rect('ed_det_err', rightX, edY, rightW, 120, C.dangerBorder, C.dangerBg, 1, true));
  els.push(txt('ed_det_err_t', rightX + 14, edY + 12, rightW - 28, 20, 'Error Details', 13, C.danger));
  [
    ['Code:', 'VALIDATION_FAILED'],
    ['Message:', 'required field "amount" is missing'],
    ['Timestamp:', '2026-05-16T10:38:00.640Z'],
  ].forEach(([k, v], i) => {
    els.push(txt(`ed_ek_${i}`, rightX + 14, edY + 36 + i * 24, 90, 18, k, 11, C.gray700));
    els.push(txt(`ed_ev_${i}`, rightX + 108, edY + 36 + i * 24, rightW - 122, 18, v, 11, C.gray900));
  });

  // Step input
  const inY = edY + 136;
  els.push(txt('ed_in_title', rightX, inY, rightW, 20, 'Step Input', 13, C.gray900));
  els.push(rect('ed_in_box', rightX, inY + 24, rightW, 88, C.gray300, C.gray50, 1, true));
  els.push(txt('ed_in_json', rightX + 12, inY + 36, rightW - 24, 64,
    '{\n  "invoice_id": "INV-2026-042",\n  "submitted_by": "jane@acme.com"\n}', 10, C.gray700));

  // Retry section
  const rtY = inY + 140;
  els.push(txt('ed_rt_title', rightX, rtY, rightW, 20, 'Retry Options', 13, C.gray900));
  els.push(hline('ed_rt_sep', rightX, rtY + 24, rightW));
  els.push(txt('ed_rt_note', rightX, rtY + 36, rightW, 36,
    'Retry will re-run from the failed step with current record data.', 11, C.gray500));
  els.push(...btn('ed_rt_step', rightX, rtY + 80, 'Retry from Failed Step', 'primary', 196));
  els.push(...btn('ed_rt_full', rightX + 204, rtY + 80, 'Retry from Start', 'ghost', 156));

  writeExcalidraw(`${BASE_DIR}/E06-workflow-engine/execution-detail.excalidraw`, els);
}

// ─── Main ─────────────────────────────────────────────────────────────────────

console.log('Generating screen wireframes…\n');

genAppShell();
genSettingsUsers();
genSettingsRoles();
genSettingsSecurity();
genAcceptInvitation();
genDataModels();
genDataClasses();
genRecords();
genWorkflows();
genWorkflowEditor();
genForms();
genFormEditor();
genFormSubmission();
genExecutions();
genExecutionDetail();

console.log('\nDone — 15 files generated.');
console.log('Next: run docs/scripts/generate-wireframes.ps1 to regenerate SVG previews.');
