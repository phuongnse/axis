/**
 * Axis Screen Wireframes Generator
 * Run: node docs/wireframes/generate-screens.mjs
 *
 * Generates 15 .excalidraw files across modules E02–E06 + _shared.
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

// ─── Primitive builders ───────────────────────────────────────────────────────

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

// ─── Colors ───────────────────────────────────────────────────────────────────

const C = {
  primary:      '#667A6E',
  primaryDark:  '#4F5F57',
  accent:       '#C58B55',
  accentDark:   '#A8743E',
  danger:       '#9E4A44',
  success:      '#4D6B44',
  warning:      '#B5763D',
  gray900:      '#2B2F33',
  gray700:      '#4A5058',
  gray500:      '#8A9099',
  gray300:      '#D9D7D1',
  gray100:      '#F4F3EF',
  gray50:       '#F9F8F5',
  white:        '#FFFFFF',
  successBg:    '#EBF0E9',
  warningBg:    '#F6EEE4',
  dangerBg:     '#F3ECEA',
  infoBg:       '#EDF0EE',
};

// ─── Layout constants ─────────────────────────────────────────────────────────

const SB = 220;       // sidebar width
const HDR = 52;       // header height
const OX = 20;        // canvas origin x
const OY = 20;        // canvas origin y
const CX = OX + SB;  // content x = 240
const CY = OY + HDR; // content y = 72

// ─── Shared component helpers ─────────────────────────────────────────────────

function btn(id, x, y, label, variant = 'primary', w = 100) {
  const bg = variant === 'primary' ? C.accent : variant === 'danger' ? C.danger : C.white;
  const stroke = variant === 'primary' ? C.accentDark : variant === 'danger' ? C.danger : C.gray300;
  const color = variant === 'ghost' ? C.gray700 : C.white;
  return [
    rect(id + '_bg', x, y, w, 30, stroke, bg, 1, true),
    txt(id + '_lbl', x, y + 8, w, 16, label, 12, color, 'center'),
  ];
}

function inputField(id, x, y, placeholder, w = 200) {
  return [
    rect(id + '_box', x, y, w, 32, C.gray300, C.white, 1, true),
    txt(id + '_ph', x + 8, y + 9, w - 16, 16, placeholder, 12, C.gray500),
  ];
}

function selectField(id, x, y, placeholder, w = 200) {
  return [
    rect(id + '_box', x, y, w, 32, C.gray300, C.white, 1, true),
    txt(id + '_ph', x + 8, y + 9, w - 16, 16, placeholder, 12, C.gray500),
    txt(id + '_arr', x + w - 20, y + 9, 16, 16, '▾', 11, C.gray500),
  ];
}

function badge(id, x, y, label, color, bg) {
  return [
    rect(id + '_bg', x, y, label.length * 7 + 12, 20, color, bg, 1, true),
    txt(id + '_lbl', x + 6, y + 4, label.length * 7, 14, label, 10, color),
  ];
}

function pageHeader(id, x, y, title, subtitle = '') {
  const els = [txt(id + '_title', x, y, 500, 28, title, 20, C.gray900)];
  if (subtitle) els.push(txt(id + '_sub', x, y + 30, 500, 16, subtitle, 12, C.gray500));
  return els;
}

function searchBar(id, x, y, w = 260) {
  return [
    rect(id + '_box', x, y, w, 32, C.gray300, C.white, 1, true),
    txt(id + '_icon', x + 8, y + 9, 16, 16, '⌕', 13, C.gray500),
    txt(id + '_ph', x + 26, y + 9, w - 34, 16, 'Search…', 12, C.gray500),
  ];
}

function tableHeader(id, x, y, w, cols) {
  const els = [rect(id + '_bg', x, y, w, 36, C.gray300, C.gray100, 1)];
  let cx = x + 12;
  cols.forEach(([label, cw], i) => {
    els.push(txt(id + `_h${i}`, cx, y + 10, cw - 8, 16, label, 11, C.gray700));
    cx += cw;
  });
  return els;
}

function tableRow(id, x, y, w, cells, highlight = false) {
  const bg = highlight ? C.infoBg : C.white;
  const els = [
    rect(id + '_bg', x, y, w, 40, C.gray300, bg, 1),
    hline(id + '_div', x, y + 40, w, C.gray100),
  ];
  let cx = x + 12;
  cells.forEach(([label, cw, color], i) => {
    els.push(txt(id + `_c${i}`, cx, y + 13, cw - 8, 16, label, 12, color || C.gray900));
    cx += cw;
  });
  return els;
}

// ─── App shell components ─────────────────────────────────────────────────────

function appShell(prefix, W, H, activeNav) {
  const els = [];
  const totalH = H - OY;
  const totalW = W - OX;

  // outer container
  els.push(rect(`${prefix}_outer`, OX, OY, totalW, totalH, C.gray300, C.gray100, 1));

  // sidebar
  els.push(rect(`${prefix}_sb`, OX, OY, SB, totalH, C.gray300, C.primaryDark, 1));

  // logo area
  els.push(rect(`${prefix}_logo`, OX + 12, OY + 12, SB - 24, 28, C.primary, C.primary, 1, true));
  els.push(txt(`${prefix}_logoTxt`, OX + 20, OY + 18, 100, 18, 'AXIS', 14, C.white));

  // nav items
  const navItems = [
    'Dashboard', 'Data Models', 'Workflows', 'Forms', 'Executions', 'Pages',
  ];
  navItems.forEach((item, i) => {
    const ny = OY + 60 + i * 36;
    const isActive = item === activeNav;
    if (isActive) {
      els.push(rect(`${prefix}_nav_act_${i}`, OX + 8, ny, SB - 16, 28, 'transparent', C.primary, 0, true));
    }
    els.push(txt(`${prefix}_nav_${i}`, OX + 20, ny + 6, SB - 40, 18, item, 12, isActive ? C.white : C.gray300));
  });

  // settings link at bottom of sidebar
  els.push(hline(`${prefix}_sb_sep`, OX, OY + totalH - 44, SB, C.primary));
  els.push(txt(`${prefix}_sb_settings`, OX + 20, OY + totalH - 32, SB - 40, 18, 'Settings', 12, C.gray300));

  // header bar
  els.push(rect(`${prefix}_hdr`, CX, OY, totalW - SB, HDR, C.gray300, C.white, 1));
  els.push(hline(`${prefix}_hdr_div`, CX, OY + HDR, totalW - SB, C.gray300));

  // user avatar placeholder
  els.push(rect(`${prefix}_avatar`, OX + totalW - 44, OY + 10, 32, 32, C.gray300, C.gray100, 1, true));
  els.push(txt(`${prefix}_avatarTxt`, OX + totalW - 38, OY + 18, 20, 16, 'JD', 11, C.gray700, 'center'));

  return els;
}

// ─── File writer ──────────────────────────────────────────────────────────────

function writeExcalidraw(filePath, elements) {
  const doc = {
    type: 'excalidraw',
    version: 2,
    source: 'generate-screens.mjs',
    elements,
    appState: {
      gridSize: null,
      viewBackgroundColor: C.gray100,
    },
    files: {},
  };
  mkdirSync(dirname(filePath), { recursive: true });
  writeFileSync(filePath, JSON.stringify(doc, null, 2));
  console.log(`  ✓  ${filePath}`);
}

const BASE_DIR = 'docs/wireframes';

// ═══════════════════════════════════════════════════════════════════════════════
// _shared/app-shell
// ═══════════════════════════════════════════════════════════════════════════════

function genAppShell() {
  const W = 1100, H = 700;
  const els = [];

  // Section 1: App Shell with sidebar + header
  els.push(txt('as_title', 40, 20, 600, 28, 'App Shell — Authenticated Layout', 18, C.gray500));
  els.push(hline('as_div', 40, 52, 1020));

  const shell = appShell('as', W, H, 'Dashboard');
  els.push(...shell);

  // breadcrumb area in header
  els.push(txt('as_breadcrumb', CX + 16, OY + 16, 400, 20, 'Dashboard / Overview', 12, C.gray700));

  // content area label
  els.push(rect('as_content', CX, CY, W - OX - SB, H - OY - HDR, C.gray200, C.gray100, 1));
  els.push(txt('as_content_lbl', CX + 40, CY + 60, 400, 24, 'Content Area', 16, C.gray500, 'center'));
  els.push(txt('as_content_sub', CX + 40, CY + 88, 400, 16, `x=${CX}  y=${CY}  w=${W - OX - SB}`, 11, C.gray500, 'center'));

  // Annotation callouts
  els.push(txt('as_ann_sb', OX + 50, OY + 340, 130, 50, `Sidebar\n${SB}px wide\nSage Dark bg`, 11, C.gray300, 'center'));
  els.push(txt('as_ann_hdr', CX + 20, OY + 4, 200, 14, `Header  h=${HDR}px`, 10, C.gray500));

  // Section 2: Annotation — settings sub-pages
  const y2 = H + 40;
  els.push(txt('as2_title', 40, y2, 700, 28, 'Settings Area — sub-navigation pattern', 18, C.gray500));
  els.push(hline('as2_div', 40, y2 + 32, 1020));

  const y2c = y2 + 50;
  // settings sidebar (within content area)
  els.push(rect('as2_outer', OX, y2c, W - OX * 2, 300, C.gray300, C.gray100));
  els.push(rect('as2_mainsb', OX, y2c, SB, 300, C.gray300, C.primaryDark));
  els.push(txt('as2_mainsb_lbl', OX + 20, y2c + 10, 120, 16, 'Main Nav', 11, C.gray300));
  // settings sub-sidebar
  els.push(rect('as2_subnav', OX + SB, y2c, 160, 300, C.gray300, C.white));
  els.push(txt('as2_subnav_lbl', OX + SB + 12, y2c + 10, 140, 14, 'Settings', 13, C.gray900));
  const settingsItems = ['Users & Invites', 'Roles & Permissions', 'Security'];
  settingsItems.forEach((item, i) => {
    const sy = y2c + 36 + i * 32;
    const isFirst = i === 0;
    if (isFirst) els.push(rect(`as2_sub_act_${i}`, OX + SB + 4, sy - 4, 152, 26, 'transparent', C.infoBg, 0, true));
    els.push(txt(`as2_sub_${i}`, OX + SB + 16, sy, 130, 18, item, 12, isFirst ? C.primary : C.gray700));
  });
  // content
  els.push(rect('as2_content', OX + SB + 160, y2c, W - OX * 2 - SB - 160, 300, C.gray300, C.gray50));
  els.push(txt('as2_content_lbl', OX + SB + 180, y2c + 40, 300, 20, 'Settings Content', 14, C.gray500));

  writeExcalidraw(`${BASE_DIR}/_shared/app-shell.excalidraw`, els);
}

// ═══════════════════════════════════════════════════════════════════════════════
// E02 — Identity Access
// ═══════════════════════════════════════════════════════════════════════════════

function genSettingsUsers() {
  const W = 1100, H = 760;
  const tableW = W - OX - SB - 40;
  const els = [...appShell('su', W, H, 'Settings')];

  // header in content area
  els.push(txt('su_hdr_bc', CX + 16, OY + 16, 300, 20, 'Settings / Users & Invites', 12, C.gray700));

  // page header
  els.push(...pageHeader('su_ph', CX + 20, CY + 20, 'Users & Invites'));
  els.push(...btn('su_invite', CX + tableW - 60, CY + 20, '+ Invite', 'primary', 100));

  // search + filter row
  els.push(...searchBar('su_search', CX + 20, CY + 72, 260));
  els.push(...selectField('su_filter_status', CX + 292, CY + 72, 'Status: All', 150));
  els.push(...selectField('su_filter_role', CX + 452, CY + 72, 'Role: All', 140));

  // tabs: Users | Pending Invitations
  const tabY = CY + 118;
  els.push(rect('su_tab1_bg', CX + 20, tabY, 120, 32, C.primary, C.white, 2));
  els.push(txt('su_tab1', CX + 20, tabY + 8, 120, 18, 'Users', 13, C.primary, 'center'));
  els.push(rect('su_tab2_bg', CX + 144, tabY, 160, 32, C.gray300, C.white, 1));
  els.push(txt('su_tab2', CX + 144, tabY + 8, 160, 18, 'Pending Invitations', 13, C.gray500, 'center'));
  els.push(hline('su_tab_div', CX + 20, tabY + 32, tableW));

  // Users table
  const tY = tabY + 44;
  const cols = [['Name / Email', 260], ['Role', 140], ['Status', 100], ['Last Active', 140], ['Actions', 100]];
  els.push(...tableHeader('su_th', CX + 20, tY, tableW, cols));

  const users = [
    ['John Doe  john@acme.com', 'Admin', 'Active', '2 hours ago', ''],
    ['Jane Smith  jane@acme.com', 'Editor', 'Active', 'Yesterday', ''],
    ['Bob Wilson  bob@acme.com', 'Viewer', 'Inactive', '3 weeks ago', ''],
    ['Alice Chen  alice@acme.com', 'Editor', 'Active', '1 hour ago', ''],
  ];
  users.forEach(([name, role, status, lastActive], i) => {
    const rowY = tY + 36 + i * 40;
    const statusColor = status === 'Active' ? C.success : C.gray500;
    els.push(...tableRow(`su_row_${i}`, CX + 20, rowY, tableW, [
      [name, 260], [role, 140], [status, 100, statusColor], [lastActive, 140], ['Edit  Remove', 100, C.primary],
    ]));
  });

  // pagination
  const pgY = tY + 36 + users.length * 40 + 12;
  els.push(txt('su_pg', CX + 20, pgY, 300, 16, 'Showing 1–4 of 4 users', 11, C.gray500));
  els.push(...btn('su_pg_prev', CX + tableW - 180, pgY - 4, '← Prev', 'ghost', 80));
  els.push(...btn('su_pg_next', CX + tableW - 90, pgY - 4, 'Next →', 'ghost', 80));

  // ── Invite User modal (overlay)
  const mX = CX + 80, mY = CY + 60, mW = 440, mH = 320;
  els.push(rect('su_modal_overlay', OX, OY, W - OX, H - OY, 'transparent', C.gray900, 0, false, { opacity: 30 }));
  els.push(rect('su_modal', mX, mY, mW, mH, C.gray300, C.white, 1, true));
  els.push(txt('su_modal_title', mX + 20, mY + 20, mW - 40, 24, 'Invite User', 16, C.gray900));
  els.push(hline('su_modal_div', mX + 20, mY + 50, mW - 40));
  els.push(txt('su_modal_email_lbl', mX + 20, mY + 68, 120, 16, 'Email address', 12, C.gray700));
  els.push(...inputField('su_modal_email', mX + 20, mY + 86, 'colleague@company.com', mW - 40));
  els.push(txt('su_modal_role_lbl', mX + 20, mY + 132, 60, 16, 'Role', 12, C.gray700));
  els.push(...selectField('su_modal_role', mX + 20, mY + 150, 'Select role…', mW - 40));
  els.push(txt('su_modal_roles_note', mX + 20, mY + 196, mW - 40, 16, 'Viewer · Editor · Admin', 11, C.gray500));
  els.push(...btn('su_modal_cancel', mX + mW - 220, mY + mH - 48, 'Cancel', 'ghost', 90));
  els.push(...btn('su_modal_send', mX + mW - 120, mY + mH - 48, 'Send Invite', 'primary', 110));

  writeExcalidraw(`${BASE_DIR}/E02-identity-access/settings-users.excalidraw`, els);
}

function genSettingsRoles() {
  const W = 1100, H = 780;
  const els = [...appShell('sr', W, H, 'Settings')];

  els.push(txt('sr_hdr_bc', CX + 16, OY + 16, 300, 20, 'Settings / Roles & Permissions', 12, C.gray700));
  els.push(...pageHeader('sr_ph', CX + 20, CY + 20, 'Roles & Permissions'));
  els.push(...btn('sr_add', CX + W - OX - SB - 80, CY + 20, '+ New Role', 'primary', 110));

  // Two-panel layout: role list (left) + permission matrix (right)
  const listW = 220, matrixX = CX + listW + 20;
  const panelY = CY + 70;
  const panelH = H - OY - HDR - 80;

  // Role list panel
  els.push(rect('sr_list', CX + 20, panelY, listW, panelH, C.gray300, C.white));
  els.push(txt('sr_list_hdr', CX + 32, panelY + 12, listW - 20, 16, 'Roles', 13, C.gray900));
  els.push(hline('sr_list_sep', CX + 20, panelY + 36, listW));

  const roles = [
    ['Admin', '3 users', true],
    ['Editor', '5 users', false],
    ['Viewer', '8 users', false],
    ['Custom Role', '1 user', false],
  ];
  roles.forEach(([name, count, active], i) => {
    const ry = panelY + 44 + i * 44;
    const bg = active ? C.infoBg : C.white;
    const nameColor = active ? C.primary : C.gray900;
    els.push(rect(`sr_role_bg_${i}`, CX + 20, ry, listW, 40, active ? C.primary : C.gray100, bg));
    els.push(txt(`sr_role_name_${i}`, CX + 32, ry + 8, 120, 16, name, 13, nameColor));
    els.push(txt(`sr_role_count_${i}`, CX + 32, ry + 24, 120, 12, count, 10, C.gray500));
  });

  // Permission matrix panel
  const matrixW = W - OX - SB - listW - 40;
  els.push(rect('sr_matrix', matrixX, panelY, matrixW, panelH, C.gray300, C.white));
  els.push(txt('sr_matrix_hdr', matrixX + 12, panelY + 12, 300, 16, 'Admin — Permissions', 13, C.gray900));
  els.push(hline('sr_matrix_sep', matrixX, panelY + 36, matrixW));

  // Permission rows: resource + CRUD checkboxes
  const resources = ['Data Models', 'Records', 'Workflows', 'Forms', 'Executions', 'Users', 'Roles'];
  const actions = ['View', 'Create', 'Edit', 'Delete'];

  // column headers
  const col0W = 160;
  els.push(txt('sr_mx_res_hdr', matrixX + 12, panelY + 48, col0W, 14, 'Resource', 11, C.gray700));
  actions.forEach((a, i) => {
    els.push(txt(`sr_mx_act_hdr_${i}`, matrixX + col0W + i * 70, panelY + 48, 60, 14, a, 11, C.gray700, 'center'));
  });
  els.push(hline('sr_mx_hdr_div', matrixX + 12, panelY + 64, matrixW - 24));

  resources.forEach((res, ri) => {
    const ry = panelY + 74 + ri * 32;
    els.push(txt(`sr_mx_res_${ri}`, matrixX + 12, ry + 8, col0W, 16, res, 12, C.gray900));
    actions.forEach((_, ai) => {
      const cx2 = matrixX + col0W + ai * 70 + 20;
      // checkbox: Admin has all checked
      els.push(rect(`sr_mx_chk_${ri}_${ai}`, cx2, ry + 6, 18, 18, C.primary, C.successBg, 1, true));
      els.push(txt(`sr_mx_chk_mark_${ri}_${ai}`, cx2 + 3, ry + 7, 14, 14, '✓', 11, C.success, 'center'));
    });
    if (ri < resources.length - 1) els.push(hline(`sr_mx_div_${ri}`, matrixX + 12, ry + 32, matrixW - 24, C.gray100));
  });

  // Save button
  els.push(...btn('sr_save', matrixX + matrixW - 110, panelY + panelH - 44, 'Save Changes', 'primary', 120));

  writeExcalidraw(`${BASE_DIR}/E02-identity-access/settings-roles.excalidraw`, els);
}

function genSettingsSecurity() {
  const W = 1100, H = 700;
  const els = [...appShell('ss', W, H, 'Settings')];

  els.push(txt('ss_hdr_bc', CX + 16, OY + 16, 300, 20, 'Settings / Security', 12, C.gray700));
  els.push(...pageHeader('ss_ph', CX + 20, CY + 20, 'Security'));

  const cardX = CX + 20, cardW = W - OX - SB - 40;

  // Change Password card
  let cardY = CY + 72;
  els.push(rect('ss_pwd_card', cardX, cardY, cardW, 220, C.gray300, C.white, 1, true));
  els.push(txt('ss_pwd_title', cardX + 20, cardY + 16, 300, 20, 'Change Password', 14, C.gray900));
  els.push(hline('ss_pwd_sep', cardX + 20, cardY + 44, cardW - 40));

  els.push(txt('ss_pwd_cur_lbl', cardX + 20, cardY + 60, 160, 14, 'Current password', 12, C.gray700));
  els.push(...inputField('ss_pwd_cur', cardX + 200, cardY + 56, '••••••••', 260));
  els.push(txt('ss_pwd_new_lbl', cardX + 20, cardY + 104, 160, 14, 'New password', 12, C.gray700));
  els.push(...inputField('ss_pwd_new', cardX + 200, cardY + 100, '••••••••', 260));
  els.push(txt('ss_pwd_conf_lbl', cardX + 20, cardY + 148, 160, 14, 'Confirm password', 12, C.gray700));
  els.push(...inputField('ss_pwd_conf', cardX + 200, cardY + 144, '••••••••', 260));
  els.push(...btn('ss_pwd_save', cardX + cardW - 160, cardY + 184, 'Update Password', 'primary', 140));

  // Active Sessions card
  cardY += 236;
  els.push(rect('ss_sess_card', cardX, cardY, cardW, 240, C.gray300, C.white, 1, true));
  els.push(txt('ss_sess_title', cardX + 20, cardY + 16, 300, 20, 'Active Sessions', 14, C.gray900));
  els.push(hline('ss_sess_sep', cardX + 20, cardY + 44, cardW - 40));

  const sessions = [
    ['Chrome · macOS · 192.168.1.1', 'Current session', true],
    ['Firefox · Windows · 10.0.0.5', 'Last seen 2 hours ago', false],
    ['Mobile · iOS · 172.16.0.8', 'Last seen yesterday', false],
  ];
  sessions.forEach(([device, meta, isCurrent], i) => {
    const sy = cardY + 56 + i * 52;
    els.push(txt(`ss_sess_dev_${i}`, cardX + 20, sy, 380, 16, device, 13, C.gray900));
    els.push(txt(`ss_sess_meta_${i}`, cardX + 20, sy + 18, 380, 14, meta, 11, C.gray500));
    if (!isCurrent) {
      els.push(...btn(`ss_sess_revoke_${i}`, cardX + cardW - 110, sy + 8, 'Revoke', 'danger', 90));
    } else {
      els.push(...badge(`ss_sess_cur_badge_${i}`, cardX + cardW - 110, sy + 10, 'Current', C.success, C.successBg));
    }
    if (i < sessions.length - 1) els.push(hline(`ss_sess_div_${i}`, cardX + 20, sy + 48, cardW - 40, C.gray100));
  });
  els.push(...btn('ss_sess_revoke_all', cardX + cardW - 160, cardY + 200, 'Revoke All Others', 'danger', 150));

  writeExcalidraw(`${BASE_DIR}/E02-identity-access/settings-security.excalidraw`, els);
}

function genAcceptInvitation() {
  // Public page — no app shell
  const W = 800, H = 500;
  const els = [];

  // centered card
  const cW = 420, cH = 380;
  const cX = (W - cW) / 2, cY = (H - cH) / 2;

  els.push(rect('ai_bg', 0, 0, W, H, C.gray300, C.gray100));
  els.push(rect('ai_card', cX, cY, cW, cH, C.gray300, C.white, 1, true));

  // logo
  els.push(rect('ai_logo', cX + cW / 2 - 30, cY + 24, 60, 24, C.primary, C.primary, 1, true));
  els.push(txt('ai_logo_txt', cX + cW / 2 - 22, cY + 30, 44, 14, 'AXIS', 12, C.white, 'center'));

  els.push(txt('ai_title', cX + 20, cY + 64, cW - 40, 28, 'Accept Invitation', 20, C.gray900, 'center'));
  els.push(txt('ai_sub', cX + 20, cY + 96, cW - 40, 16, "You've been invited to join Acme Corp.", 12, C.gray500, 'center'));

  els.push(hline('ai_div', cX + 20, cY + 120, cW - 40));

  // form
  els.push(txt('ai_name_lbl', cX + 20, cY + 136, 120, 14, 'Full name', 12, C.gray700));
  els.push(...inputField('ai_name', cX + 20, cY + 152, 'Your name', cW - 40));

  els.push(txt('ai_email_lbl', cX + 20, cY + 200, 120, 14, 'Email', 12, C.gray700));
  els.push(rect('ai_email_box', cX + 20, cY + 216, cW - 40, 32, C.gray300, C.gray100, 1, true));
  els.push(txt('ai_email_val', cX + 28, cY + 225, cW - 56, 16, 'invited@company.com', 12, C.gray700));

  els.push(txt('ai_pwd_lbl', cX + 20, cY + 264, 120, 14, 'Create password', 12, C.gray700));
  els.push(...inputField('ai_pwd', cX + 20, cY + 280, '••••••••', cW - 40));

  els.push(...btn('ai_accept', cX + 20, cY + cH - 52, 'Accept & Create Account', 'primary', cW - 40));

  writeExcalidraw(`${BASE_DIR}/E02-identity-access/accept-invitation.excalidraw`, els);
}

// ═══════════════════════════════════════════════════════════════════════════════
// E03 — Data Modeling
// ═══════════════════════════════════════════════════════════════════════════════

function genDataModels() {
  const W = 1100, H = 780;
  const tableW = W - OX - SB - 40;
  const els = [...appShell('dm', W, H, 'Data Models')];

  els.push(txt('dm_hdr_bc', CX + 16, OY + 16, 300, 20, 'Data Models', 12, C.gray700));
  els.push(...pageHeader('dm_ph', CX + 20, CY + 20, 'Data Models', 'Define custom data structures for your workspace'));
  els.push(...btn('dm_create', CX + tableW - 60, CY + 20, '+ New Model', 'primary', 110));

  els.push(...searchBar('dm_search', CX + 20, CY + 76, 260));

  // table
  const tY = CY + 124;
  const cols = [['Name', 220], ['Display Name', 200], ['Fields', 80], ['Records', 90], ['Updated', 140], ['Actions', 110]];
  els.push(...tableHeader('dm_th', CX + 20, tY, tableW, cols));

  const models = [
    ['customer', 'Customer', '12 fields', '1,240', '2 hours ago'],
    ['order', 'Order', '8 fields', '4,891', 'Yesterday'],
    ['product', 'Product', '15 fields', '342', '3 days ago'],
    ['invoice', 'Invoice', '10 fields', '2,104', 'Last week'],
  ];
  models.forEach(([name, display, fields, records, updated], i) => {
    const rowY = tY + 36 + i * 40;
    els.push(...tableRow(`dm_row_${i}`, CX + 20, rowY, tableW, [
      [name, 220, C.primary], [display, 200], [fields, 80, C.gray700], [records, 90, C.gray700],
      [updated, 140, C.gray500], ['Edit  Fields  ···', 110, C.primary],
    ]));
  });

  // ── Field editor side sheet
  const shW = 380, shX = W - OX - shW;
  const shY = OY;
  const shH = H - OY;
  els.push(rect('dm_sh_overlay', CX, CY, shX - CX, shH - CY, 'transparent', C.gray900, 0, false, { opacity: 15 }));
  els.push(rect('dm_sh', shX, shY, shW, shH, C.gray300, C.white, 2));
  els.push(txt('dm_sh_title', shX + 16, shY + 16, shW - 60, 22, 'customer — Fields', 15, C.gray900));
  els.push(txt('dm_sh_close', shX + shW - 36, shY + 14, 24, 24, '✕', 14, C.gray500));
  els.push(hline('dm_sh_sep', shX, shY + 48, shW));

  // field list in side sheet
  const fields = [
    ['id', 'UUID', 'System'],
    ['name', 'Text', ''],
    ['email', 'Email', ''],
    ['phone', 'Text', 'Optional'],
    ['created_at', 'DateTime', 'System'],
  ];
  els.push(...btn('dm_sh_add', shX + shW - 120, shY + 56, '+ Add Field', 'primary', 110));
  fields.forEach(([fname, ftype, note], i) => {
    const fy = shY + 56 + i * 44;
    els.push(rect(`dm_sh_f_bg_${i}`, shX + 12, fy, shW - 24, 36, C.gray100, C.gray50));
    els.push(txt(`dm_sh_f_name_${i}`, shX + 22, fy + 10, 120, 16, fname, 12, C.gray900));
    els.push(txt(`dm_sh_f_type_${i}`, shX + 160, fy + 10, 80, 16, ftype, 12, C.gray500));
    if (note) els.push(txt(`dm_sh_f_note_${i}`, shX + 260, fy + 10, 80, 16, note, 10, C.gray300));
  });

  writeExcalidraw(`${BASE_DIR}/E03-data-modeling/data-models.excalidraw`, els);
}

function genDataClasses() {
  const W = 1100, H = 780;
  const tableW = W - OX - SB - 40;
  const els = [...appShell('dc', W, H, 'Data Models')];

  els.push(txt('dc_hdr_bc', CX + 16, OY + 16, 300, 20, 'Data Models / Data Classes', 12, C.gray700));
  els.push(...pageHeader('dc_ph', CX + 20, CY + 20, 'Data Classes', 'Reusable field groups shared across models'));
  els.push(...btn('dc_create', CX + tableW - 60, CY + 20, '+ New Class', 'primary', 110));

  els.push(...searchBar('dc_search', CX + 20, CY + 76, 260));

  const tY = CY + 124;
  const cols = [['Name', 200], ['Display Name', 200], ['Fields', 80], ['Used In', 140], ['Updated', 140], ['Actions', 80]];
  els.push(...tableHeader('dc_th', CX + 20, tY, tableW, cols));

  const classes = [
    ['address', 'Address', '5 fields', '3 models'],
    ['contact_info', 'Contact Info', '4 fields', '2 models'],
    ['audit_fields', 'Audit Fields', '3 fields', '8 models'],
  ];
  classes.forEach(([name, display, fields, usedIn], i) => {
    const rowY = tY + 36 + i * 40;
    els.push(...tableRow(`dc_row_${i}`, CX + 20, rowY, tableW, [
      [name, 200, C.primary], [display, 200], [fields, 80, C.gray700], [usedIn, 140, C.gray700],
      ['3 days ago', 140, C.gray500], ['Edit  ···', 80, C.primary],
    ]));
  });

  // Create/Edit side sheet
  const shW = 400, shX = W - OX - shW;
  els.push(rect('dc_sh_overlay', CX, CY, shX - CX, H - CY - OY, 'transparent', C.gray900, 0, false, { opacity: 15 }));
  els.push(rect('dc_sh', shX, OY, shW, H - OY, C.gray300, C.white, 2));
  els.push(txt('dc_sh_title', shX + 16, OY + 16, shW - 60, 22, 'New Data Class', 15, C.gray900));
  els.push(txt('dc_sh_close', shX + shW - 36, OY + 14, 24, 24, '✕', 14, C.gray500));
  els.push(hline('dc_sh_sep', shX, OY + 48, shW));

  const fY = OY + 64;
  els.push(txt('dc_sh_name_lbl', shX + 16, fY, 100, 14, 'Name (slug)', 12, C.gray700));
  els.push(...inputField('dc_sh_name', shX + 16, fY + 18, 'e.g. address', shW - 32));
  els.push(txt('dc_sh_disp_lbl', shX + 16, fY + 66, 100, 14, 'Display name', 12, C.gray700));
  els.push(...inputField('dc_sh_disp', shX + 16, fY + 84, 'e.g. Address', shW - 32));
  els.push(txt('dc_sh_desc_lbl', shX + 16, fY + 132, 100, 14, 'Description', 12, C.gray700));
  els.push(...inputField('dc_sh_desc', shX + 16, fY + 150, 'Optional description', shW - 32));

  els.push(txt('dc_sh_fields_lbl', shX + 16, fY + 200, 200, 14, 'Fields', 13, C.gray900));
  els.push(...btn('dc_sh_add_field', shX + shW - 120, fY + 196, '+ Add Field', 'ghost', 110));
  els.push(hline('dc_sh_f_sep', shX + 16, fY + 218, shW - 32));

  // sample fields
  [['street', 'Text'], ['city', 'Text'], ['postcode', 'Text']].forEach(([n, t], i) => {
    const fy2 = fY + 228 + i * 40;
    els.push(rect(`dc_sh_f_${i}`, shX + 16, fy2, shW - 32, 32, C.gray100, C.gray50));
    els.push(txt(`dc_sh_fn_${i}`, shX + 26, fy2 + 9, 120, 14, n, 12, C.gray900));
    els.push(txt(`dc_sh_ft_${i}`, shX + 180, fy2 + 9, 80, 14, t, 12, C.gray500));
    els.push(txt(`dc_sh_fdel_${i}`, shX + shW - 40, fy2 + 9, 20, 14, '✕', 12, C.gray300));
  });

  els.push(...btn('dc_sh_cancel', shX + 16, H - OY - 52, 'Cancel', 'ghost', 100));
  els.push(...btn('dc_sh_save', shX + shW - 130, H - OY - 52, 'Save Class', 'primary', 120));

  writeExcalidraw(`${BASE_DIR}/E03-data-modeling/data-classes.excalidraw`, els);
}

function genRecords() {
  const W = 1100, H = 780;
  const tableW = W - OX - SB - 40;
  const els = [...appShell('rec', W, H, 'Data Models')];

  els.push(txt('rec_hdr_bc', CX + 16, OY + 16, 400, 20, 'Data Models / Customer / Records', 12, C.gray700));
  els.push(...pageHeader('rec_ph', CX + 20, CY + 20, 'Customer Records'));
  els.push(...btn('rec_create', CX + tableW - 60, CY + 20, '+ New Record', 'primary', 110));

  els.push(...searchBar('rec_search', CX + 20, CY + 76, 260));
  els.push(...selectField('rec_filter', CX + 292, CY + 76, 'Filter by field', 180));
  els.push(...btn('rec_export', CX + tableW - 100, CY + 76, 'Export CSV', 'ghost', 100));

  const tY = CY + 124;
  const cols = [['ID', 100], ['name', 180], ['email', 220], ['phone', 140], ['created_at', 140], ['Actions', 60]];
  els.push(...tableHeader('rec_th', CX + 20, tY, tableW, cols));

  const records = [
    ['uuid-0001', 'Alice Johnson', 'alice@acme.com', '+1 555-0101', '2026-01-15'],
    ['uuid-0002', 'Bob Martinez', 'bob@globex.com', '+1 555-0202', '2026-01-16'],
    ['uuid-0003', 'Carol White', 'carol@initech.com', '+1 555-0303', '2026-02-01'],
    ['uuid-0004', 'Dan Brown', 'dan@umbrella.com', '+1 555-0404', '2026-02-10'],
    ['uuid-0005', 'Eve Davis', 'eve@cyberdyne.com', '+1 555-0505', '2026-03-01'],
  ];
  records.forEach(([id, name, email, phone, created], i) => {
    const rowY = tY + 36 + i * 40;
    els.push(...tableRow(`rec_row_${i}`, CX + 20, rowY, tableW, [
      [id.slice(0, 8) + '…', 100, C.gray500], [name, 180], [email, 220, C.primary], [phone, 140, C.gray700],
      [created, 140, C.gray500], ['···', 60, C.gray700],
    ]));
  });

  const pgY = tY + 36 + records.length * 40 + 12;
  els.push(txt('rec_pg_info', CX + 20, pgY, 300, 16, 'Showing 1–5 of 1,240 records', 11, C.gray500));
  els.push(...btn('rec_pg_prev', CX + tableW - 180, pgY - 4, '← Prev', 'ghost', 80));
  els.push(...btn('rec_pg_next', CX + tableW - 90, pgY - 4, 'Next →', 'ghost', 80));

  // Create Record modal
  const mX = CX + 100, mY = CY + 40, mW = 480, mH = 380;
  els.push(rect('rec_modal_ov', OX, OY, W - OX, H - OY, 'transparent', C.gray900, 0, false, { opacity: 30 }));
  els.push(rect('rec_modal', mX, mY, mW, mH, C.gray300, C.white, 1, true));
  els.push(txt('rec_modal_title', mX + 20, mY + 20, mW - 40, 22, 'New Customer Record', 15, C.gray900));
  els.push(hline('rec_modal_sep', mX + 20, mY + 50, mW - 40));

  [['name', 'Full name'], ['email', 'Email address'], ['phone', 'Phone number (optional)']].forEach(([f, ph], i) => {
    const fy = mY + 68 + i * 62;
    els.push(txt(`rec_modal_lbl_${i}`, mX + 20, fy, 120, 14, f, 12, C.gray700));
    els.push(...inputField(`rec_modal_f_${i}`, mX + 20, fy + 18, ph, mW - 40));
  });

  els.push(...btn('rec_modal_cancel', mX + mW - 230, mY + mH - 48, 'Cancel', 'ghost', 90));
  els.push(...btn('rec_modal_save', mX + mW - 130, mY + mH - 48, 'Save Record', 'primary', 120));

  writeExcalidraw(`${BASE_DIR}/E03-data-modeling/records.excalidraw`, els);
}

// ═══════════════════════════════════════════════════════════════════════════════
// E04 — Workflow Builder
// ═══════════════════════════════════════════════════════════════════════════════

function genWorkflows() {
  const W = 1100, H = 760;
  const tableW = W - OX - SB - 40;
  const els = [...appShell('wf', W, H, 'Workflows')];

  els.push(txt('wf_hdr_bc', CX + 16, OY + 16, 200, 20, 'Workflows', 12, C.gray700));
  els.push(...pageHeader('wf_ph', CX + 20, CY + 20, 'Workflows', 'Automate processes with visual workflows'));
  els.push(...btn('wf_create', CX + tableW - 60, CY + 20, '+ New Workflow', 'primary', 120));

  els.push(...searchBar('wf_search', CX + 20, CY + 76, 260));
  els.push(...selectField('wf_filter_status', CX + 292, CY + 76, 'Status: All', 150));

  const tY = CY + 124;
  const cols = [['Name', 220], ['Trigger', 160], ['Steps', 70], ['Status', 110], ['Last Run', 140], ['Actions', 100]];
  els.push(...tableHeader('wf_th', CX + 20, tY, tableW, cols));

  const workflows = [
    ['Customer Onboarding', 'Record Created', '5', 'Active'],
    ['Invoice Approval', 'Form Submission', '3', 'Active'],
    ['Weekly Report', 'Schedule', '4', 'Active'],
    ['Data Sync', 'Webhook', '2', 'Draft'],
    ['Archive Old Orders', 'Schedule', '3', 'Paused'],
  ];
  const statusColors = { Active: C.success, Draft: C.gray500, Paused: C.warning };
  const statusBgs = { Active: C.successBg, Draft: C.gray100, Paused: C.warningBg };

  workflows.forEach(([name, trigger, steps, status], i) => {
    const rowY = tY + 36 + i * 40;
    els.push(...tableRow(`wf_row_${i}`, CX + 20, rowY, tableW, [
      [name, 220, C.primary], [trigger, 160, C.gray700], [steps, 70, C.gray700],
      [status, 110, statusColors[status]], ['2 hours ago', 140, C.gray500], ['Edit  ···', 100, C.primary],
    ]));
  });

  const pgY = tY + 36 + workflows.length * 40 + 12;
  els.push(txt('wf_pg', CX + 20, pgY, 300, 16, 'Showing 1–5 of 5 workflows', 11, C.gray500));

  // Create workflow dialog
  const mX = CX + 100, mY = CY + 60, mW = 480, mH = 320;
  els.push(rect('wf_dlg_ov', OX, OY, W - OX, H - OY, 'transparent', C.gray900, 0, false, { opacity: 30 }));
  els.push(rect('wf_dlg', mX, mY, mW, mH, C.gray300, C.white, 1, true));
  els.push(txt('wf_dlg_title', mX + 20, mY + 20, mW - 40, 22, 'New Workflow', 15, C.gray900));
  els.push(hline('wf_dlg_sep', mX + 20, mY + 50, mW - 40));

  els.push(txt('wf_dlg_name_lbl', mX + 20, mY + 66, 100, 14, 'Name', 12, C.gray700));
  els.push(...inputField('wf_dlg_name', mX + 20, mY + 82, 'Workflow name', mW - 40));

  els.push(txt('wf_dlg_desc_lbl', mX + 20, mY + 128, 100, 14, 'Description', 12, C.gray700));
  els.push(...inputField('wf_dlg_desc', mX + 20, mY + 144, 'Optional description', mW - 40));

  els.push(txt('wf_dlg_trigger_lbl', mX + 20, mY + 190, 100, 14, 'Trigger type', 12, C.gray700));
  els.push(...selectField('wf_dlg_trigger', mX + 20, mY + 206, 'Select trigger…', mW - 40));

  els.push(...btn('wf_dlg_cancel', mX + mW - 230, mY + mH - 48, 'Cancel', 'ghost', 90));
  els.push(...btn('wf_dlg_create', mX + mW - 130, mY + mH - 48, 'Create', 'primary', 110));

  writeExcalidraw(`${BASE_DIR}/E04-workflow-builder/workflows.excalidraw`, els);
}

function genWorkflowEditor() {
  const W = 1200, H = 800;
  // No standard sidebar — full-screen editor with its own chrome
  const TOOLBAR_H = 48;
  const STEP_PANEL_W = 280;
  const PROP_PANEL_W = 320;
  const CANVAS_W = W - STEP_PANEL_W - PROP_PANEL_W;
  const els = [];

  // Top toolbar
  els.push(rect('we_toolbar', 0, 0, W, TOOLBAR_H, C.gray300, C.white, 1));
  els.push(txt('we_back', 12, TOOLBAR_H / 2 - 8, 80, 18, '← Workflows', 12, C.primary));
  els.push(vline('we_tb_sep1', 100, 8, TOOLBAR_H - 16, C.gray300));
  els.push(txt('we_wf_name', 112, TOOLBAR_H / 2 - 9, 200, 20, 'Customer Onboarding', 14, C.gray900));
  els.push(...badge('we_status', 324, TOOLBAR_H / 2 - 10, 'Active', C.success, C.successBg));
  els.push(...btn('we_save', W - 220, 9, 'Save', 'ghost', 70));
  els.push(...btn('we_activate', W - 140, 9, 'Activate', 'primary', 100));
  els.push(hline('we_tb_div', 0, TOOLBAR_H, W, C.gray300));

  // Step palette panel (left)
  els.push(rect('we_palette', 0, TOOLBAR_H, STEP_PANEL_W, H - TOOLBAR_H, C.gray300, C.white));
  els.push(txt('we_palette_title', 12, TOOLBAR_H + 12, STEP_PANEL_W - 24, 18, 'Steps', 13, C.gray900));
  els.push(hline('we_palette_sep', 0, TOOLBAR_H + 36, STEP_PANEL_W));
  els.push(txt('we_palette_sub', 12, TOOLBAR_H + 44, STEP_PANEL_W - 24, 14, 'Drag to canvas', 10, C.gray500));

  const stepTypes = [
    ['Trigger', 'Start the workflow', C.primary, C.infoBg],
    ['Condition', 'Branch on logic', C.warning, C.warningBg],
    ['Action', 'Perform an action', C.accent, C.warningBg],
    ['Wait', 'Delay / await event', C.gray700, C.gray100],
    ['Send Email', 'Notify via email', C.primary, C.infoBg],
    ['Webhook', 'Call external URL', C.primary, C.infoBg],
    ['End', 'Terminate path', C.danger, C.dangerBg],
  ];
  stepTypes.forEach(([name, desc, color, bg], i) => {
    const sy = TOOLBAR_H + 64 + i * 48;
    els.push(rect(`we_st_${i}`, 10, sy, STEP_PANEL_W - 20, 40, color, bg, 1, true));
    els.push(txt(`we_st_name_${i}`, 22, sy + 8, 140, 16, name, 12, color));
    els.push(txt(`we_st_desc_${i}`, 22, sy + 24, 200, 12, desc, 10, C.gray500));
  });

  // Canvas area
  els.push(rect('we_canvas', STEP_PANEL_W, TOOLBAR_H, CANVAS_W, H - TOOLBAR_H, C.gray300, C.gray100));
  // grid dots (represented as faint pattern description)
  els.push(txt('we_canvas_hint', STEP_PANEL_W + 10, TOOLBAR_H + 10, 200, 14, '· · · canvas area · · ·', 10, C.gray300, 'center'));

  // Workflow nodes on canvas
  const nx = STEP_PANEL_W + 60;
  const nodeW = 180, nodeH = 60;

  const nodes = [
    [nx, TOOLBAR_H + 60, 'Record Created', 'Trigger', C.primary, C.infoBg],
    [nx, TOOLBAR_H + 180, 'Validate Data', 'Condition', C.warning, C.warningBg],
    [nx - 100, TOOLBAR_H + 300, 'Send Welcome Email', 'Action', C.accent, C.warningBg],
    [nx + 100, TOOLBAR_H + 300, 'Flag for Review', 'Action', C.accent, C.warningBg],
    [nx, TOOLBAR_H + 420, 'End', 'End', C.danger, C.dangerBg],
  ];
  nodes.forEach(([nx2, ny, label, type, color, bg], i) => {
    const isSelected = i === 1;
    els.push(rect(`we_node_${i}`, nx2, ny, nodeW, nodeH, isSelected ? C.accent : color, bg, isSelected ? 2 : 1, true));
    els.push(txt(`we_node_type_${i}`, nx2 + 10, ny + 8, nodeW - 20, 14, type, 10, color));
    els.push(txt(`we_node_label_${i}`, nx2 + 10, ny + 26, nodeW - 20, 18, label, 12, C.gray900));
  });

  // connector lines between nodes (simple vertical lines)
  [0, 1, 3, 4].forEach((i) => {
    const [nx2, ny] = nodes[i];
    const [nx3, ny3] = nodes[i + 1] || nodes[4];
    const startY = ny + nodeH;
    const endY = ny3;
    if (Math.abs(nx2 - nx3) < 10) {
      els.push(vline(`we_conn_${i}`, nx2 + nodeW / 2, startY, endY - startY, C.gray500));
    }
  });
  // branch lines from condition node
  els.push(hline('we_branch_h1', nodes[1][0] + nodeW / 2, nodes[2][1] + nodeH / 2, -(100 + nodeW / 2), C.gray500));
  els.push(hline('we_branch_h2', nodes[1][0] + nodeW / 2, nodes[3][1] + nodeH / 2, 100 + nodeW / 2, C.gray500));

  // Properties panel (right) — showing selected step
  els.push(rect('we_props', STEP_PANEL_W + CANVAS_W, TOOLBAR_H, PROP_PANEL_W, H - TOOLBAR_H, C.gray300, C.white));
  els.push(txt('we_props_title', STEP_PANEL_W + CANVAS_W + 12, TOOLBAR_H + 12, PROP_PANEL_W - 24, 18, 'Step Properties', 13, C.gray900));
  els.push(hline('we_props_sep', STEP_PANEL_W + CANVAS_W, TOOLBAR_H + 36, PROP_PANEL_W));

  const px = STEP_PANEL_W + CANVAS_W + 12;
  const py = TOOLBAR_H + 48;
  els.push(txt('we_props_type', px, py, PROP_PANEL_W - 24, 14, 'Condition', 11, C.warning));
  els.push(txt('we_props_name_lbl', px, py + 20, 100, 14, 'Step name', 12, C.gray700));
  els.push(...inputField('we_props_name', px, py + 36, 'Validate Data', PROP_PANEL_W - 24));

  els.push(txt('we_props_cond_lbl', px, py + 86, 100, 14, 'Condition', 12, C.gray700));
  els.push(...selectField('we_props_field', px, py + 102, 'Select field…', PROP_PANEL_W - 24));
  els.push(...selectField('we_props_op', px, py + 144, 'Operator…', (PROP_PANEL_W - 24) / 2 - 4));
  els.push(...inputField('we_props_val', px + (PROP_PANEL_W - 24) / 2 + 4, py + 144, 'Value', (PROP_PANEL_W - 24) / 2 - 4));

  els.push(txt('we_props_branches', px, py + 192, PROP_PANEL_W - 24, 14, 'Branches', 12, C.gray700));
  ['True → Send Welcome Email', 'False → Flag for Review'].forEach((branch, i) => {
    els.push(rect(`we_props_br_${i}`, px, py + 210 + i * 36, PROP_PANEL_W - 24, 28, C.gray300, C.gray50, 1, true));
    els.push(txt(`we_props_br_lbl_${i}`, px + 8, py + 218 + i * 36, PROP_PANEL_W - 36, 16, branch, 11, C.gray700));
  });

  writeExcalidraw(`${BASE_DIR}/E04-workflow-builder/workflow-editor.excalidraw`, els);
}

// ═══════════════════════════════════════════════════════════════════════════════
// E05 — Form Builder
// ═══════════════════════════════════════════════════════════════════════════════

function genForms() {
  const W = 1100, H = 760;
  const tableW = W - OX - SB - 40;
  const els = [...appShell('fb', W, H, 'Forms')];

  els.push(txt('fb_hdr_bc', CX + 16, OY + 16, 200, 20, 'Forms', 12, C.gray700));
  els.push(...pageHeader('fb_ph', CX + 20, CY + 20, 'Forms', 'Collect data and trigger workflows'));
  els.push(...btn('fb_create', CX + tableW - 60, CY + 20, '+ New Form', 'primary', 100));

  els.push(...searchBar('fb_search', CX + 20, CY + 76, 260));

  const tY = CY + 124;
  const cols = [['Name', 220], ['Linked Model', 180], ['Submissions', 110], ['Status', 110], ['Updated', 120], ['Actions', 100]];
  els.push(...tableHeader('fb_th', CX + 20, tY, tableW, cols));

  const forms = [
    ['Customer Intake', 'customer', '142', 'Published'],
    ['Support Request', 'ticket', '89', 'Published'],
    ['Order Form', 'order', '34', 'Draft'],
    ['Feedback Survey', 'feedback', '210', 'Published'],
    ['Invoice Approval', 'invoice', '12', 'Draft'],
  ];
  forms.forEach(([name, model, submissions, status], i) => {
    const rowY = tY + 36 + i * 40;
    const statusColor = status === 'Published' ? C.success : C.gray500;
    els.push(...tableRow(`fb_row_${i}`, CX + 20, rowY, tableW, [
      [name, 220, C.primary], [model, 180, C.gray700], [submissions, 110, C.gray700],
      [status, 110, statusColor], ['2 days ago', 120, C.gray500], ['Edit  View  ···', 100, C.primary],
    ]));
  });

  // Create Form dialog
  const mX = CX + 100, mY = CY + 60, mW = 480, mH = 320;
  els.push(rect('fb_dlg_ov', OX, OY, W - OX, H - OY, 'transparent', C.gray900, 0, false, { opacity: 30 }));
  els.push(rect('fb_dlg', mX, mY, mW, mH, C.gray300, C.white, 1, true));
  els.push(txt('fb_dlg_title', mX + 20, mY + 20, mW - 40, 22, 'New Form', 15, C.gray900));
  els.push(hline('fb_dlg_sep', mX + 20, mY + 50, mW - 40));

  els.push(txt('fb_dlg_name_lbl', mX + 20, mY + 66, 100, 14, 'Form name', 12, C.gray700));
  els.push(...inputField('fb_dlg_name', mX + 20, mY + 82, 'e.g. Customer Intake', mW - 40));

  els.push(txt('fb_dlg_model_lbl', mX + 20, mY + 128, 140, 14, 'Linked data model', 12, C.gray700));
  els.push(...selectField('fb_dlg_model', mX + 20, mY + 144, 'Select model…', mW - 40));

  els.push(txt('fb_dlg_wf_lbl', mX + 20, mY + 190, 160, 14, 'On submit: trigger workflow', 12, C.gray700));
  els.push(...selectField('fb_dlg_wf', mX + 20, mY + 206, 'Select workflow (optional)…', mW - 40));

  els.push(...btn('fb_dlg_cancel', mX + mW - 230, mY + mH - 48, 'Cancel', 'ghost', 90));
  els.push(...btn('fb_dlg_create', mX + mW - 130, mY + mH - 48, 'Create', 'primary', 110));

  writeExcalidraw(`${BASE_DIR}/E05-form-builder/forms.excalidraw`, els);
}

function genFormEditor() {
  const W = 1200, H = 800;
  const TOOLBAR_H = 48;
  const PALETTE_W = 240;
  const PROPS_W = 280;
  const CANVAS_W = W - PALETTE_W - PROPS_W;
  const els = [];

  // Toolbar
  els.push(rect('fe_toolbar', 0, 0, W, TOOLBAR_H, C.gray300, C.white, 1));
  els.push(txt('fe_back', 12, TOOLBAR_H / 2 - 8, 60, 18, '← Forms', 12, C.primary));
  els.push(vline('fe_tb_sep', 82, 8, TOOLBAR_H - 16, C.gray300));
  els.push(txt('fe_form_name', 94, TOOLBAR_H / 2 - 9, 220, 20, 'Customer Intake', 14, C.gray900));
  els.push(...badge('fe_status', 328, TOOLBAR_H / 2 - 10, 'Draft', C.gray500, C.gray100));
  els.push(...btn('fe_preview', W - 320, 9, 'Preview', 'ghost', 80));
  els.push(...btn('fe_save', W - 230, 9, 'Save', 'ghost', 70));
  els.push(...btn('fe_publish', W - 150, 9, 'Publish', 'primary', 100));
  els.push(hline('fe_tb_div', 0, TOOLBAR_H, W, C.gray300));

  // Field palette (left)
  els.push(rect('fe_palette', 0, TOOLBAR_H, PALETTE_W, H - TOOLBAR_H, C.gray300, C.white));
  els.push(txt('fe_pal_title', 12, TOOLBAR_H + 12, PALETTE_W - 24, 18, 'Field Types', 13, C.gray900));
  els.push(hline('fe_pal_sep', 0, TOOLBAR_H + 36, PALETTE_W));

  const fieldTypes = ['Text', 'Number', 'Email', 'Date', 'Dropdown', 'Checkbox', 'File Upload', 'Rich Text', 'Relation'];
  fieldTypes.forEach((ft, i) => {
    const fy = TOOLBAR_H + 44 + i * 36;
    els.push(rect(`fe_ft_${i}`, 10, fy, PALETTE_W - 20, 28, C.gray300, C.gray50, 1, true));
    els.push(txt(`fe_ft_lbl_${i}`, 22, fy + 7, PALETTE_W - 40, 16, ft, 12, C.gray700));
    els.push(txt(`fe_ft_drag_${i}`, PALETTE_W - 30, fy + 7, 18, 16, '⠿', 12, C.gray300));
  });

  // Form canvas (center)
  els.push(rect('fe_canvas', PALETTE_W, TOOLBAR_H, CANVAS_W, H - TOOLBAR_H, C.gray300, C.gray100));
  // Form preview in canvas
  const fX = PALETTE_W + 60, fW = CANVAS_W - 120, fYS = TOOLBAR_H + 40;
  els.push(rect('fe_form_bg', fX, fYS, fW, H - TOOLBAR_H - 80, C.gray300, C.white, 1, true));
  els.push(txt('fe_form_title', fX + 24, fYS + 20, fW - 48, 28, 'Customer Intake', 18, C.gray900));
  els.push(txt('fe_form_sub', fX + 24, fYS + 52, fW - 48, 16, 'Fill out the form below', 12, C.gray500));
  els.push(hline('fe_form_sep', fX + 24, fYS + 76, fW - 48));

  // form fields
  const formFields = [
    ['Full Name *', 'Enter full name'],
    ['Email Address *', 'Enter email'],
    ['Phone Number', 'Enter phone (optional)'],
  ];
  formFields.forEach(([label, ph], i) => {
    const ffy = fYS + 96 + i * 72;
    els.push(txt(`fe_form_f_lbl_${i}`, fX + 24, ffy, 200, 14, label, 12, C.gray700));
    // selected field has highlight border
    const isSelected = i === 0;
    els.push(rect(`fe_form_f_box_${i}`, fX + 24, ffy + 18, fW - 48, 36, isSelected ? C.accent : C.gray300, C.white, isSelected ? 2 : 1, true));
    els.push(txt(`fe_form_f_ph_${i}`, fX + 34, ffy + 28, fW - 68, 16, ph, 12, C.gray500));
  });

  // submit button in form
  els.push(...btn('fe_form_submit', fX + 24, fYS + 340, 'Submit', 'primary', 120));

  // Properties panel (right)
  els.push(rect('fe_props', PALETTE_W + CANVAS_W, TOOLBAR_H, PROPS_W, H - TOOLBAR_H, C.gray300, C.white));
  els.push(txt('fe_props_title', PALETTE_W + CANVAS_W + 12, TOOLBAR_H + 12, PROPS_W - 24, 18, 'Field Properties', 13, C.gray900));
  els.push(hline('fe_props_sep', PALETTE_W + CANVAS_W, TOOLBAR_H + 36, PROPS_W));

  const ppX = PALETTE_W + CANVAS_W + 12;
  const ppY = TOOLBAR_H + 48;
  els.push(txt('fe_pp_type', ppX, ppY, PROPS_W - 24, 14, 'Field type: Text', 11, C.gray500));
  els.push(txt('fe_pp_label_lbl', ppX, ppY + 20, 80, 14, 'Label', 12, C.gray700));
  els.push(...inputField('fe_pp_label', ppX, ppY + 36, 'Full Name', PROPS_W - 24));
  els.push(txt('fe_pp_ph_lbl', ppX, ppY + 82, 80, 14, 'Placeholder', 12, C.gray700));
  els.push(...inputField('fe_pp_ph', ppX, ppY + 98, 'Enter full name', PROPS_W - 24));
  els.push(txt('fe_pp_req_lbl', ppX, ppY + 144, 80, 14, 'Required', 12, C.gray700));
  els.push(rect('fe_pp_req_toggle', ppX + 180, ppY + 140, 40, 22, C.primary, C.primary, 1, true));
  els.push(rect('fe_pp_req_knob', ppX + 200, ppY + 143, 16, 16, C.white, C.white, 1, true));
  els.push(txt('fe_pp_val_lbl', ppX, ppY + 180, 100, 14, 'Validation', 12, C.gray700));
  els.push(...selectField('fe_pp_val_type', ppX, ppY + 196, 'None', PROPS_W - 24));

  writeExcalidraw(`${BASE_DIR}/E05-form-builder/form-editor.excalidraw`, els);
}

function genFormSubmission() {
  const W = 1100, H = 900;
  const els = [];

  // Section 1: Public form submission page
  els.push(txt('fs_s1_title', 40, 20, 600, 22, 'Public Form — Submission view (unauthenticated)', 16, C.gray500));
  els.push(hline('fs_s1_div', 40, 48, 1020));

  const pgW = 580, pgX = (W - pgW) / 2;
  els.push(rect('fs_bg', 0, 60, W, 420, C.gray300, C.gray100));
  els.push(rect('fs_card', pgX, 80, pgW, 380, C.gray300, C.white, 1, true));
  els.push(txt('fs_logo', pgX + pgW / 2 - 24, 96, 48, 18, 'AXIS', 13, C.primary, 'center'));
  els.push(txt('fs_title', pgX + 24, 122, pgW - 48, 28, 'Customer Intake', 18, C.gray900, 'center'));
  els.push(txt('fs_sub', pgX + 24, 154, pgW - 48, 16, 'Acme Corp · Fill out the form below', 12, C.gray500, 'center'));
  els.push(hline('fs_sep', pgX + 24, 178, pgW - 48));

  [['Full Name *', 'Alice Johnson'], ['Email Address *', 'alice@example.com'], ['Phone', '+1 555-0101']].forEach(([lbl, val], i) => {
    const fy = 192 + i * 62;
    els.push(txt(`fs_f_lbl_${i}`, pgX + 24, fy, 200, 14, lbl, 12, C.gray700));
    els.push(rect(`fs_f_box_${i}`, pgX + 24, fy + 18, pgW - 48, 34, C.gray300, C.white, 1, true));
    els.push(txt(`fs_f_val_${i}`, pgX + 34, fy + 27, pgW - 68, 16, val, 12, C.gray900));
  });

  els.push(...btn('fs_submit', pgX + 24, 376 + 12, 'Submit Form', 'primary', pgW - 48));

  // Success state annotation
  els.push(txt('fs_success_note', pgX + pgW + 20, 200, 180, 60, '→ Success state\nshows confirmation\nmessage', 11, C.gray500));

  // Section 2: My Tasks (authenticated — task assignment view)
  const s2Y = 520;
  els.push(txt('fs_s2_title', 40, s2Y, 700, 22, 'My Tasks — Form submission task assignment (authenticated)', 16, C.gray500));
  els.push(hline('fs_s2_div', 40, s2Y + 28, 1020));

  const shell2 = appShell('fs_shell', W, H - s2Y + OY, 'Forms');
  const shellYOffset = s2Y + 28;
  // We need to draw the shell relative to s2Y
  els.push(rect('fs_shell2_outer', OX, shellYOffset, W - OX * 2, 320, C.gray300, C.gray100));
  els.push(rect('fs_shell2_sb', OX, shellYOffset, SB, 320, C.gray300, C.primaryDark));
  els.push(txt('fs_shell2_nav', OX + 20, shellYOffset + 20, SB - 40, 18, 'My Tasks ←', 12, C.white));

  const taskTableW = W - OX * 2 - SB - 20;
  const tX2 = OX + SB + 10;
  const tY2 = shellYOffset + 10;
  els.push(...pageHeader('fs_tasks_hdr', tX2, tY2, 'My Tasks'));
  els.push(...selectField('fs_tasks_filter', tX2, tY2 + 44, 'Status: Pending', 160));
  const taskCols = [['Form', 180], ['Record', 180], ['Assigned', 120], ['Status', 100], ['Due', 100], ['Action', 80]];
  els.push(...tableHeader('fs_tasks_th', tX2, tY2 + 86, taskTableW, taskCols));
  const tasks = [
    ['Customer Intake', 'Alice Johnson', 'John Doe', 'Pending'],
    ['Invoice Approval', 'INV-2026-042', 'Jane Smith', 'In Review'],
  ];
  tasks.forEach(([form, rec, assignee, status], i) => {
    const rowY2 = tY2 + 86 + 36 + i * 40;
    const sColor = status === 'Pending' ? C.warning : C.primary;
    els.push(...tableRow(`fs_task_row_${i}`, tX2, rowY2, taskTableW, [
      [form, 180, C.primary], [rec, 180], [assignee, 120, C.gray700], [status, 100, sColor],
      ['Tomorrow', 100, C.gray500], ['Open', 80, C.primary],
    ]));
  });

  writeExcalidraw(`${BASE_DIR}/E05-form-builder/form-submission.excalidraw`, els);
}

// ═══════════════════════════════════════════════════════════════════════════════
// E06 — Workflow Engine
// ═══════════════════════════════════════════════════════════════════════════════

function genExecutions() {
  const W = 1100, H = 780;
  const tableW = W - OX - SB - 40;
  const els = [...appShell('ex', W, H, 'Executions')];

  els.push(txt('ex_hdr_bc', CX + 16, OY + 16, 200, 20, 'Executions', 12, C.gray700));
  els.push(...pageHeader('ex_ph', CX + 20, CY + 20, 'Executions', 'Monitor workflow execution history'));

  // filter bar
  els.push(...searchBar('ex_search', CX + 20, CY + 76, 220));
  els.push(...selectField('ex_filter_wf', CX + 252, CY + 76, 'Workflow: All', 180));
  els.push(...selectField('ex_filter_status', CX + 444, CY + 76, 'Status: All', 140));
  els.push(...selectField('ex_filter_date', CX + 596, CY + 76, 'Date range', 140));

  // stats row
  const statsY = CY + 122;
  const stats = [
    ['Total', '1,482', C.gray900],
    ['Completed', '1,341', C.success],
    ['Failed', '87', C.danger],
    ['Running', '54', C.primary],
  ];
  stats.forEach(([label, value, color], i) => {
    const sx = CX + 20 + i * 180;
    els.push(rect(`ex_stat_${i}`, sx, statsY, 160, 56, C.gray300, C.white, 1, true));
    els.push(txt(`ex_stat_val_${i}`, sx + 16, statsY + 10, 130, 24, value, 20, color));
    els.push(txt(`ex_stat_lbl_${i}`, sx + 16, statsY + 36, 130, 14, label, 11, C.gray500));
  });

  // table
  const tY = statsY + 72;
  const cols = [['Execution ID', 160], ['Workflow', 180], ['Trigger', 130], ['Status', 110], ['Duration', 90], ['Started', 130], ['Actions', 60]];
  els.push(...tableHeader('ex_th', CX + 20, tY, tableW, cols));

  const statusMap = { Completed: C.success, Failed: C.danger, Running: C.primary, Cancelled: C.gray500 };
  const executions = [
    ['exec-0001', 'Customer Onboarding', 'Record Created', 'Completed', '1.2s', '10:42 AM'],
    ['exec-0002', 'Invoice Approval', 'Form Submission', 'Failed', '0.8s', '10:38 AM'],
    ['exec-0003', 'Weekly Report', 'Schedule', 'Running', '—', '10:35 AM'],
    ['exec-0004', 'Customer Onboarding', 'Record Created', 'Completed', '2.1s', '10:20 AM'],
    ['exec-0005', 'Data Sync', 'Webhook', 'Cancelled', '0.2s', '09:55 AM'],
  ];
  executions.forEach(([id, wf, trigger, status, duration, started], i) => {
    const rowY = tY + 36 + i * 40;
    els.push(...tableRow(`ex_row_${i}`, CX + 20, rowY, tableW, [
      [id, 160, C.primary], [wf, 180], [trigger, 130, C.gray700],
      [status, 110, statusMap[status] || C.gray700], [duration, 90, C.gray700],
      [started, 130, C.gray500], ['View', 60, C.primary],
    ]));
  });

  const pgY = tY + 36 + executions.length * 40 + 12;
  els.push(txt('ex_pg', CX + 20, pgY, 300, 16, 'Showing 1–5 of 1,482 executions', 11, C.gray500));
  els.push(...btn('ex_pg_prev', CX + tableW - 180, pgY - 4, '← Prev', 'ghost', 80));
  els.push(...btn('ex_pg_next', CX + tableW - 90, pgY - 4, 'Next →', 'ghost', 80));

  writeExcalidraw(`${BASE_DIR}/E06-workflow-engine/executions.excalidraw`, els);
}

function genExecutionDetail() {
  const W = 1100, H = 860;
  const contentW = W - OX - SB - 40;
  const els = [...appShell('ed', W, H, 'Executions')];

  els.push(txt('ed_hdr_bc', CX + 16, OY + 16, 400, 20, 'Executions / exec-0002', 12, C.gray700));

  // Page header with status
  els.push(txt('ed_title', CX + 20, CY + 20, 400, 28, 'exec-0002', 20, C.gray900));
  els.push(...badge('ed_status', CX + 160, CY + 26, 'Failed', C.danger, C.dangerBg));
  els.push(...btn('ed_retry', CX + contentW - 100, CY + 20, 'Retry', 'primary', 90));

  // Meta row
  els.push(txt('ed_meta', CX + 20, CY + 58, 600, 16, 'Workflow: Invoice Approval  ·  Trigger: Form Submission  ·  Started: 10:38 AM  ·  Duration: 0.8s', 11, C.gray500));

  // Error banner
  const errY = CY + 86;
  els.push(rect('ed_err_banner', CX + 20, errY, contentW, 44, C.danger, C.dangerBg, 1, true));
  els.push(txt('ed_err_icon', CX + 32, errY + 14, 20, 18, '⚠', 13, C.danger));
  els.push(txt('ed_err_msg', CX + 56, errY + 14, contentW - 80, 16, 'Step "Validate Invoice" failed: required field "amount" is missing', 12, C.danger));

  // Two-column layout
  const leftW = 480, rightW = contentW - leftW - 20;
  const rightX = CX + leftW + 40;

  // Left: Execution Timeline
  const tlY = errY + 60;
  els.push(txt('ed_tl_title', CX + 20, tlY, 200, 18, 'Execution Timeline', 14, C.gray900));
  els.push(hline('ed_tl_sep', CX + 20, tlY + 24, leftW));

  const steps = [
    ['Form Submission Received', 'completed', '10:38:00.000', '0ms'],
    ['Load Invoice Data', 'completed', '10:38:00.120', '120ms'],
    ['Validate Invoice', 'failed', '10:38:00.640', '520ms'],
    ['Send Approval Email', 'skipped', '—', '—'],
    ['Update Record Status', 'skipped', '—', '—'],
  ];

  const stepColors = { completed: C.success, failed: C.danger, skipped: C.gray300 };
  const stepBgs = { completed: C.successBg, failed: C.dangerBg, skipped: C.gray50 };

  steps.forEach(([name, status, time, dur], i) => {
    const sy = tlY + 36 + i * 52;
    // timeline dot
    els.push(rect(`ed_tl_dot_${i}`, CX + 20, sy + 8, 12, 12, stepColors[status], stepColors[status], 1, true));
    // connector line
    if (i < steps.length - 1) {
      els.push(vline(`ed_tl_line_${i}`, CX + 25, sy + 20, 40, stepColors[status]));
    }
    // step card
    els.push(rect(`ed_tl_card_${i}`, CX + 44, sy, leftW - 44, 44, stepColors[status], stepBgs[status], 1, true));
    els.push(txt(`ed_tl_name_${i}`, CX + 56, sy + 8, 200, 16, name, 12, C.gray900));
    els.push(txt(`ed_tl_meta_${i}`, CX + 56, sy + 26, 200, 12, `${time}  ·  ${dur}`, 10, C.gray500));
    els.push(txt(`ed_tl_status_${i}`, CX + 44 + leftW - 100, sy + 14, 88, 16, status, 11, stepColors[status], 'right'));
  });

  // Right: Step error detail
  const detY = tlY;
  els.push(txt('ed_det_title', rightX, detY, rightW, 18, 'Step Detail: Validate Invoice', 14, C.gray900));
  els.push(hline('ed_det_sep', rightX, detY + 24, rightW));

  // Error detail card
  els.push(rect('ed_det_err', rightX, detY + 36, rightW, 120, C.danger, C.dangerBg, 1, true));
  els.push(txt('ed_det_err_title', rightX + 12, detY + 50, rightW - 24, 16, 'Error Details', 12, C.danger));
  els.push(txt('ed_det_err_code', rightX + 12, detY + 70, rightW - 24, 14, 'Code: VALIDATION_FAILED', 11, C.gray700));
  els.push(txt('ed_det_err_msg', rightX + 12, detY + 88, rightW - 24, 14, 'Message: required field "amount" is missing', 11, C.gray700));
  els.push(txt('ed_det_err_step', rightX + 12, detY + 106, rightW - 24, 14, 'Step: Validate Invoice (step 3 of 5)', 11, C.gray700));
  els.push(txt('ed_det_err_ts', rightX + 12, detY + 124, rightW - 24, 14, 'Timestamp: 2026-05-16T10:38:00.640Z', 11, C.gray500));

  // Input/Output data
  els.push(txt('ed_det_in_title', rightX, detY + 172, rightW, 16, 'Step Input', 13, C.gray900));
  els.push(rect('ed_det_in', rightX, detY + 192, rightW, 80, C.gray300, C.gray50, 1, true));
  els.push(txt('ed_det_in_json', rightX + 12, detY + 204, rightW - 24, 60,
    '{\n  "invoice_id": "INV-2026-042",\n  "submitted_by": "jane@acme.com"\n}', 10, C.gray700));

  // Retry section
  els.push(txt('ed_retry_title', rightX, detY + 288, rightW, 16, 'Retry Options', 13, C.gray900));
  els.push(hline('ed_retry_sep', rightX, detY + 308, rightW));
  els.push(txt('ed_retry_note', rightX, detY + 320, rightW, 32, 'Retry will re-run from the failed step\nwith current record data.', 11, C.gray500));
  els.push(...btn('ed_retry_btn', rightX, detY + 364, 'Retry from Failed Step', 'primary', 200));
  els.push(...btn('ed_retry_full', rightX + 210, detY + 364, 'Retry from Start', 'ghost', 160));

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
