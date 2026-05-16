/**
 * Generates _template.excalidraw — the Axis UI component kit.
 * Run: node docs/wireframes/generate-template.mjs
 */

import { writeFileSync } from 'fs';

// ─── Primitive builders ───────────────────────────────────────────────────────

let _seed = 1001;
const nextSeed = () => (_seed += 2);
const BASE = { angle: 0, opacity: 100, groupIds: [], isDeleted: false, boundElements: null, updated: 1700000000000, link: null, locked: false, version: 1 };

function rect(id, x, y, w, h, stroke, bg, sw = 1, rounded = false, extra = {}) {
  const s = nextSeed();
  return { ...BASE, id, type: 'rectangle', x, y, width: w, height: h, strokeColor: stroke, backgroundColor: bg, fillStyle: 'solid', strokeWidth: sw, strokeStyle: 'solid', roughness: 1, roundness: rounded ? { type: 3 } : null, seed: s, versionNonce: s + 1, ...extra };
}

function ellipse(id, x, y, w, h, stroke, bg, sw = 1, extra = {}) {
  const s = nextSeed();
  return { ...BASE, id, type: 'ellipse', x, y, width: w, height: h, strokeColor: stroke, backgroundColor: bg, fillStyle: 'solid', strokeWidth: sw, strokeStyle: 'solid', roughness: 1, roundness: { type: 3 }, seed: s, versionNonce: s + 1, ...extra };
}

function text(id, x, y, w, h, str, fontSize, color, align = 'left', extra = {}) {
  const s = nextSeed();
  return { ...BASE, id, type: 'text', x, y, width: w, height: h, strokeColor: color, backgroundColor: 'transparent', fillStyle: 'solid', strokeWidth: 1, strokeStyle: 'solid', roughness: 1, roundness: null, seed: s, versionNonce: s + 1, text: str, fontSize, fontFamily: 1, textAlign: align, verticalAlign: 'top', containerId: null, originalText: str, lineHeight: 1.25, ...extra };
}

function hline(id, x, y, w, stroke = '#dee2e6', sw = 1) {
  const s = nextSeed();
  return { ...BASE, id, type: 'line', x, y, width: w, height: 0, strokeColor: stroke, backgroundColor: 'transparent', fillStyle: 'solid', strokeWidth: sw, strokeStyle: 'solid', roughness: 0, roundness: null, seed: s, versionNonce: s + 1, points: [[0, 0], [w, 0]], lastCommittedPoint: null, startBinding: null, endBinding: null, startArrowhead: null, endArrowhead: null };
}

function vline(id, x, y, h, stroke = '#dee2e6', sw = 1) {
  const s = nextSeed();
  return { ...BASE, id, type: 'line', x, y, width: 0, height: h, strokeColor: stroke, backgroundColor: 'transparent', fillStyle: 'solid', strokeWidth: sw, strokeStyle: 'solid', roughness: 0, roundness: null, seed: s, versionNonce: s + 1, points: [[0, 0], [0, h]], lastCommittedPoint: null, startBinding: null, endBinding: null, startArrowhead: null, endArrowhead: null };
}

function sectionHeader(n, label, y) {
  return [
    text(`s${n}_lbl`, 50, y, 600, 26, `${n.toString().padStart(2, '0')} — ${label}`, 18, '#6c757d'),
    hline(`s${n}_div`, 50, y + 32, 1000),
  ];
}

// ─── Colors ───────────────────────────────────────────────────────────────────

const C = {
  primary:     '#1971c2',
  primaryDark: '#1864ab',
  danger:      '#e03131',
  dangerDark:  '#c92a2a',
  success:     '#2f9e44',
  warning:     '#e67700',
  gray900:     '#212529',
  gray700:     '#495057',
  gray500:     '#adb5bd',
  gray300:     '#dee2e6',
  gray100:     '#f8f9fa',
  gray50:      '#f1f3f5',
  white:       '#ffffff',
  // semantic backgrounds
  infoBg:      '#e7f5ff',
  successBg:   '#ebfbee',
  warningBg:   '#fff9db',
  dangerBg:    '#fff5f5',
  // borders
  infoBorder:  '#74c0fc',
  successBorder: '#8ce99a',
  warningBorder: '#ffd43b',
  dangerBorder:  '#ffa8a8',
};

// ─── Section builders ─────────────────────────────────────────────────────────

function buildColorPalette(y0) {
  const swatches = [
    ['Primary',     C.primaryDark, C.primary],
    ['Pri. Dark',   '#1339b0',     C.primaryDark],
    ['Danger',      C.dangerDark,  C.danger],
    ['Success',     '#276b31',     C.success],
    ['Warning',     '#b36200',     C.warning],
    ['Gray 900',    '#111419',     C.gray900],
    ['Gray 700',    C.gray900,     C.gray700],
    ['Gray 500',    C.gray700,     C.gray500],
    ['Gray 300',    C.gray500,     C.gray300],
    ['Gray 100',    C.gray300,     C.gray100],
    ['White',       C.gray300,     C.white],
  ];
  const els = [...sectionHeader(1, 'Color Palette', y0)];
  const yS = y0 + 50, yL = y0 + 108;
  swatches.forEach(([label, stroke, bg], i) => {
    const x = 50 + i * 68;
    els.push(rect(`pal_sw_${i}`, x, yS, 52, 52, stroke, bg, 2, false));
    els.push(text(`pal_lbl_${i}`, x, yL, 60, 30, label, 10, C.gray700, 'center'));
  });
  return els;
}

function buildTypography(y0) {
  const els = [...sectionHeader(2, 'Typography', y0)];
  const yC = y0 + 48;
  const samples = [
    ['H1 · 28px',   'Page Title — Main Heading',              28, C.gray900],
    ['H2 · 22px',   'Section Heading',                         22, C.gray900],
    ['H3 · 18px',   'Card Title',                              18, C.gray900],
    ['Body · 14px', 'Body — Regular paragraph text for reading.', 14, C.gray700],
    ['Small · 12px','Small — Secondary / helper information.',  12, C.gray700],
    ['Caption · 11px','Caption · 2026-01-01 · Meta info',       11, C.gray500],
    ['Link · 14px', 'Link text — navigates somewhere',         14, C.primary],
  ];
  let y = yC;
  samples.forEach(([label, sample, fs, color], i) => {
    els.push(text(`typ_lbl_${i}`, 50, y + 4, 120, 20, label, 10, C.gray500));
    els.push(text(`typ_txt_${i}`, 180, y, 700, fs * 1.6, sample, fs, color));
    y += Math.max(fs * 1.8, 32);
  });
  return els;
}

function buildButtons(y0) {
  const els = [...sectionHeader(3, 'Buttons', y0)];
  const yR1 = y0 + 66, yR2 = y0 + 140;

  // Row 1 — Variants
  const variants = [
    ['btn_primary',   'Primary',   C.primaryDark, C.primary,   C.white,  2, true,  50],
    ['btn_secondary', 'Secondary', C.gray500,     C.gray100,   C.gray900,1, true, 180],
    ['btn_ghost',     'Ghost',     C.gray900,     'transparent',C.gray900,1, true, 310],
    ['btn_danger',    'Danger',    C.dangerDark,  C.danger,    C.white,  2, true,  440],
    ['btn_disabled',  'Disabled',  C.gray300,     C.gray100,   C.gray300,1, true,  570],
  ];
  variants.forEach(([id, label, stroke, bg, textColor, sw, rounded, x]) => {
    els.push(rect(id,        x, yR1, 120, 40, stroke, bg, sw, rounded));
    els.push(text(`${id}_t`, x, yR1 + 10, 120, 20, label, 14, textColor, 'center'));
  });

  // Row 2 — Sizes + icon
  els.push(rect('btn_sm',   50,  yR2, 96,  32, C.primaryDark, C.primary,   2, true));
  els.push(text('btn_sm_t', 50,  yR2 + 7, 96, 18, 'Small',     13, C.white, 'center'));
  els.push(rect('btn_lg',   158, yR2, 140, 44, C.primaryDark, C.primary,   2, true));
  els.push(text('btn_lg_t', 158, yR2 + 11, 140, 22, 'Large',   15, C.white, 'center'));
  els.push(rect('btn_icon', 312, yR2, 130, 36, C.primaryDark, C.primary,   2, true));
  els.push(text('btn_icon_t',312,yR2 + 8, 130, 20, '+ Add Item',13, C.white, 'center'));
  els.push(rect('btn_load', 456, yR2, 130, 36, C.gray300,     C.gray100,   1, true));
  els.push(text('btn_load_t',456,yR2 + 8, 130, 20, '⟳  Loading…',13, C.gray500, 'center'));

  // State labels
  els.push(text('btn_r1_lbl', 50, y0 + 46, 200, 16, 'Variants', 11, C.gray500));
  els.push(text('btn_r2_lbl', 50, y0 + 120, 200, 16, 'Sizes', 11, C.gray500));
  return els;
}

function buildFormControls(y0) {
  const els = [...sectionHeader(4, 'Form Controls', y0)];
  const yC = y0 + 48;

  // ── Col 1: Text input states (x=50) ──
  const inputStates = [
    ['Default',  C.gray500,   C.gray100,  1, 'email@company.com',  C.gray300],
    ['Focus',    C.primary,   C.white,    2, 'email@company.com',  C.gray500],
    ['Error',    C.danger,    C.dangerBg, 2, 'invalid-email',      C.danger],
    ['Disabled', C.gray300,   C.gray50,   1, 'Disabled',           C.gray300],
  ];
  inputStates.forEach(([state, stroke, bg, sw, placeholder, phColor], i) => {
    const y = yC + i * 78;
    els.push(text(`inp_lbl_${i}`, 50, y, 100, 16, state, 11, C.gray500));
    els.push(rect(`inp_${i}`, 50, y + 18, 280, 40, stroke, bg, sw, true));
    els.push(text(`inp_ph_${i}`, 62, y + 29, 250, 18, placeholder, 13, phColor));
    if (state === 'Error') {
      els.push(text(`inp_err`, 50, y + 62, 280, 14, '✕  Invalid email address', 11, C.danger));
    }
  });

  // ── Col 2: Other inputs (x=380) ──
  const x2 = 380;

  // Password
  els.push(text('pw_lbl', x2, yC, 100, 16, 'Password', 11, C.gray500));
  els.push(rect('pw_inp', x2, yC + 18, 280, 40, C.gray500, C.gray100, 1, true));
  els.push(text('pw_dots', x2 + 12, yC + 29, 150, 18, '••••••••', 13, C.gray700));
  els.push(text('pw_eye', x2 + 248, yC + 29, 20, 18, '👁', 13, C.gray500));

  // Search
  els.push(text('srch_lbl', x2, yC + 78, 100, 16, 'Search', 11, C.gray500));
  els.push(rect('srch_inp', x2, yC + 96, 280, 40, C.gray500, C.gray100, 1, true));
  els.push(text('srch_icon', x2 + 10, yC + 107, 20, 18, '⌕', 14, C.gray500));
  els.push(text('srch_ph', x2 + 34, yC + 107, 230, 18, 'Search records…', 13, C.gray300));

  // Textarea
  els.push(text('ta_lbl', x2, yC + 156, 100, 16, 'Textarea', 11, C.gray500));
  els.push(rect('ta_inp', x2, yC + 174, 280, 80, C.gray500, C.gray100, 1, true));
  els.push(text('ta_ph', x2 + 12, yC + 185, 250, 18, 'Enter description…', 13, C.gray300));

  // Select
  els.push(text('sel_lbl', x2, yC + 274, 120, 16, 'Select / Dropdown', 11, C.gray500));
  els.push(rect('sel_inp', x2, yC + 292, 280, 40, C.gray500, C.gray100, 1, true));
  els.push(text('sel_ph', x2 + 12, yC + 303, 230, 18, 'Choose an option…', 13, C.gray300));
  els.push(text('sel_arr', x2 + 250, yC + 303, 20, 18, '▾', 13, C.gray700));

  // ── Col 3: Checkbox / Radio / Toggle (x=710) ──
  const x3 = 710;

  els.push(text('chk_grp_lbl', x3, yC, 100, 16, 'Checkbox', 11, C.gray500));
  els.push(rect('chk_off', x3, yC + 20, 18, 18, C.gray500, C.white, 1, false));
  els.push(text('chk_off_lbl', x3 + 26, yC + 20, 120, 18, 'Unchecked', 13, C.gray700));
  els.push(rect('chk_on', x3, yC + 50, 18, 18, C.primary, C.primary, 1, false));
  els.push(text('chk_check', x3 + 2, yC + 51, 14, 16, '✓', 11, C.white));
  els.push(text('chk_on_lbl', x3 + 26, yC + 50, 120, 18, 'Checked', 13, C.gray700));

  els.push(text('rad_grp_lbl', x3, yC + 90, 100, 16, 'Radio', 11, C.gray500));
  els.push(ellipse('rad_off', x3, yC + 110, 18, 18, C.gray500, C.white, 1));
  els.push(text('rad_off_lbl', x3 + 26, yC + 110, 120, 18, 'Unselected', 13, C.gray700));
  els.push(ellipse('rad_on', x3, yC + 140, 18, 18, C.primary, C.white, 2));
  els.push(ellipse('rad_dot', x3 + 5, yC + 145, 8, 8, C.primary, C.primary, 1));
  els.push(text('rad_on_lbl', x3 + 26, yC + 140, 120, 18, 'Selected', 13, C.gray700));

  els.push(text('tog_grp_lbl', x3, yC + 180, 100, 16, 'Toggle', 11, C.gray500));
  els.push(rect('tog_off', x3, yC + 200, 44, 24, C.gray300, C.gray300, 1, true));
  els.push(ellipse('tog_off_c', x3 + 2, yC + 202, 20, 20, C.white, C.white, 1));
  els.push(text('tog_off_lbl', x3 + 54, yC + 202, 40, 18, 'Off', 13, C.gray700));
  els.push(rect('tog_on', x3, yC + 236, 44, 24, C.primaryDark, C.primary, 1, true));
  els.push(ellipse('tog_on_c', x3 + 22, yC + 238, 20, 20, C.white, C.white, 1));
  els.push(text('tog_on_lbl', x3 + 54, yC + 238, 40, 18, 'On', 13, C.gray700));

  return els;
}

function buildBadges(y0) {
  const els = [...sectionHeader(5, 'Badges & Tags', y0)];
  const yC = y0 + 68;
  const badges = [
    ['Default',  C.gray300,     C.gray50,      C.gray700,  'Default',  50],
    ['Primary',  C.infoBorder,  C.infoBg,      C.primary,  'Active',   155],
    ['Success',  C.successBorder,C.successBg,  C.success,  'Complete', 250],
    ['Warning',  C.warningBorder,C.warningBg,  C.warning,  'Pending',  375],
    ['Danger',   C.dangerBorder, C.dangerBg,   C.danger,   'Error',    480],
    ['Dark',     C.gray900,     C.gray900,      C.white,    'Archived', 565],
  ];
  badges.forEach(([label, stroke, bg, textColor, content, x], i) => {
    const w = content.length * 8 + 24;
    els.push(text(`bdg_lbl_${i}`, x, y0 + 46, 80, 14, label, 10, C.gray500));
    els.push(rect(`bdg_${i}`, x, yC, w, 28, stroke, bg, 1, true));
    els.push(text(`bdg_t_${i}`, x, yC + 6, w, 16, content, 12, textColor, 'center'));
  });
  return els;
}

function buildNavigation(y0) {
  const els = [...sectionHeader(6, 'Navigation', y0)];
  const yC = y0 + 62;

  // Breadcrumb
  els.push(text('bc_lbl', 50, y0 + 44, 120, 14, 'Breadcrumb', 11, C.gray500));
  els.push(text('bc_home', 50, yC, 50, 20, 'Home', 14, C.primary));
  els.push(text('bc_sep1', 104, yC, 12, 20, '/', 14, C.gray500));
  els.push(text('bc_models', 118, yC, 100, 20, 'Data Models', 14, C.primary));
  els.push(text('bc_sep2', 222, yC, 12, 20, '/', 14, C.gray500));
  els.push(text('bc_cur', 236, yC, 120, 20, 'User Schema', 14, C.gray700));

  // Tabs
  const yT = yC + 60;
  els.push(text('tab_lbl', 50, yT - 16, 120, 14, 'Tabs', 11, C.gray500));
  els.push(hline('tab_line', 50, yT + 36, 500, C.gray300, 1));
  const tabs = [['Overview', 50, true], ['Settings', 160, false], ['History', 256, false]];
  tabs.forEach(([label, x, active]) => {
    els.push(text(`tab_${label}`, x, yT, 100, 24, label, 14, active ? C.primary : C.gray700));
    if (active) els.push(rect(`tab_ul_${label}`, x, yT + 30, label.length * 8.5 + 4, 3, C.primary, C.primary, 1, false));
  });

  // Pagination
  const yP = yT + 80;
  els.push(text('pag_lbl', 50, yP - 16, 120, 14, 'Pagination', 11, C.gray500));
  const pages = [['‹', false], ['1', true], ['2', false], ['3', false], ['…', false], ['12', false], ['›', false]];
  pages.forEach(([p, active], i) => {
    const x = 50 + i * 44;
    const stroke = active ? C.primaryDark : C.gray300;
    const bg = active ? C.primary : C.gray100;
    const tc = active ? C.white : C.gray700;
    els.push(rect(`pag_btn_${i}`, x, yP, 36, 36, stroke, bg, 1, true));
    els.push(text(`pag_t_${i}`, x, yP + 8, 36, 20, p, 13, tc, 'center'));
  });

  return els;
}

function buildSidebarNav(y0) {
  const els = [...sectionHeader(7, 'Sidebar Navigation', y0)];
  const yC = y0 + 48;

  // Sidebar container
  els.push(rect('snav_bg', 50, yC, 240, 342, C.gray300, C.white, 1, false));

  // Logo area
  els.push(rect('snav_logo_area', 50, yC, 240, 60, C.gray300, C.gray100, 1, false));
  els.push(rect('snav_logo_icon', 66, yC + 10, 38, 38, C.primaryDark, C.primary, 2, true));
  els.push(text('snav_logo_t', 114, yC + 20, 80, 20, 'Axis', 16, C.gray900));

  // Nav items
  const navItems = [
    ['Data Models', false],
    ['Workflows',   true],
    ['Form Builder',false],
    ['Pages',       false],
  ];
  navItems.forEach(([label, active], i) => {
    const y = yC + 72 + i * 56;
    const bg = active ? C.infoBg : 'transparent';
    const stroke = active ? C.infoBorder : 'transparent';
    const tc = active ? C.primary : C.gray700;
    els.push(rect(`snav_item_${i}`, 58, y, 224, 38, stroke, bg, 1, false));
    if (active) els.push(rect(`snav_accent_${i}`, 58, y, 3, 38, C.primary, C.primary, 1, false));
    els.push(rect(`snav_icon_${i}`, 70, y + 11, 16, 16, active ? C.primary : C.gray500, active ? C.infoBg : C.gray100, 1, false));
    els.push(text(`snav_t_${i}`, 94, y + 10, 160, 18, label, 13, tc));
  });

  // User area bottom
  els.push(hline('snav_user_div', 58, yC + 296, 224, C.gray300));
  els.push(ellipse('snav_avatar', 66, yC + 304, 32, 32, C.infoBorder, C.infoBg, 1));
  els.push(text('snav_avatar_t', 66, yC + 312, 32, 16, 'AB', 11, C.primary, 'center'));
  els.push(text('snav_user', 106, yC + 308, 140, 16, 'Alex Brown', 12, C.gray900));

  return els;
}

function buildTable(y0) {
  const els = [...sectionHeader(8, 'Table', y0)];
  const yC = y0 + 48;
  const W = 850, cols = [220, 160, 180, 160, 130];
  const xCols = cols.reduce((acc, w, i) => [...acc, i === 0 ? 50 : acc[acc.length - 1] + cols[i - 1]], []);

  // Outer border (transparent fill — rows/header provide fills, lines provide separators)
  els.push(rect('tbl_outer', 50, yC, W, 194, C.gray300, 'transparent', 1, false));

  // Header
  els.push(rect('tbl_header', 50, yC, W, 44, 'transparent', C.gray100, 0, false));
  els.push(hline('tbl_header_div', 50, yC + 44, W, C.gray300));
  const headers = ['Name', 'Type', 'Created', 'Status', 'Actions'];
  headers.forEach((h, i) => els.push(text(`tbl_h_${i}`, xCols[i] + 12, yC + 12, cols[i] - 16, 20, h, 13, C.gray900)));

  // Column dividers
  xCols.slice(1).forEach((x, i) => els.push(vline(`tbl_vl_${i}`, x, yC, 194, C.gray300)));

  // Rows
  const rows = [
    ['User Profile',      'Data Model', 'Jan 12, 2026', 'Active',  C.white],
    ['Order Workflow',    'Workflow',   'Jan 10, 2026', 'Draft',   C.gray50],
    ['Contact Form',      'Form',       'Jan 8, 2026',  'Pending', C.white],
  ];
  const statusColors = { Active: [C.successBorder, C.successBg, C.success], Draft: [C.gray300, C.gray50, C.gray700], Pending: [C.warningBorder, C.warningBg, C.warning] };

  rows.forEach(([name, type, date, status, rowBg], i) => {
    const y = yC + 44 + i * 50;
    els.push(rect(`tbl_row_${i}`, 50, y, W, 50, 'transparent', rowBg, 0, false));
    if (i < rows.length - 1) els.push(hline(`tbl_hl_${i}`, 50, y + 50, W, C.gray300));
    els.push(text(`tbl_name_${i}`, xCols[0] + 12, y + 15, cols[0] - 16, 20, name, 13, C.gray900));
    els.push(text(`tbl_type_${i}`, xCols[1] + 12, y + 15, cols[1] - 16, 20, type, 13, C.gray700));
    els.push(text(`tbl_date_${i}`, xCols[2] + 12, y + 15, cols[2] - 16, 20, date, 13, C.gray700));
    const [bs, bb, bt] = statusColors[status];
    els.push(rect(`tbl_badge_${i}`, xCols[3] + 12, y + 12, 80, 24, bs, bb, 1, true));
    els.push(text(`tbl_bs_${i}`, xCols[3] + 12, y + 17, 80, 14, status, 11, bt, 'center'));
    els.push(rect(`tbl_edit_${i}`, xCols[4] + 12, y + 12, 48, 26, C.gray300, C.gray100, 1, true));
    els.push(text(`tbl_edit_t_${i}`, xCols[4] + 12, y + 17, 48, 14, 'Edit', 11, C.gray900, 'center'));
  });

  // Footer (below outer rect)
  els.push(text('tbl_foot', 62, yC + 206, 300, 20, 'Showing 1–20 of 143 records', 12, C.gray500));

  return els;
}

function buildCards(y0) {
  const els = [...sectionHeader(9, 'Cards & Display', y0)];
  const yC = y0 + 72;

  // Standard card
  els.push(text('card_lbl', 50, y0 + 46, 100, 14, 'Content Card', 11, C.gray500));
  els.push(rect('card', 50, yC, 300, 180, C.gray300, C.white, 1, true));
  els.push(rect('card_hdr', 50, yC, 300, 52, C.gray300, C.gray100, 1, false, { roundness: null }));
  els.push(text('card_title', 66, yC + 16, 200, 20, 'Record Details', 15, C.gray900));
  els.push(hline('card_hdiv', 50, yC + 52, 300, C.gray300));
  els.push(rect('card_line1', 66, yC + 72, 200, 14, C.gray300, C.gray100, 1, false, { roughness: 0 }));
  els.push(rect('card_line2', 66, yC + 96, 160, 14, C.gray300, C.gray100, 1, false, { roughness: 0 }));
  els.push(rect('card_line3', 66, yC + 120, 180, 14, C.gray300, C.gray100, 1, false, { roughness: 0 }));

  // Stat card
  els.push(text('stat_lbl', 390, y0 + 46, 100, 14, 'Stat Card', 11, C.gray500));
  els.push(rect('stat_card', 390, yC, 200, 120, C.gray300, C.white, 1, true));
  els.push(text('stat_num', 390, yC + 18, 200, 44, '1,248', 32, C.gray900, 'center'));
  els.push(text('stat_lbl2', 390, yC + 66, 200, 20, 'Total Records', 13, C.gray700, 'center'));
  els.push(text('stat_trend', 390, yC + 92, 200, 18, '↑ 12% this month', 12, C.success, 'center'));

  // Avatars
  els.push(text('av_lbl', 640, y0 + 46, 120, 14, 'Avatars', 11, C.gray500));
  const avatarSizes = [[32, 'SM', 640], [42, 'MD', 690], [56, 'LG', 750]];
  avatarSizes.forEach(([size, label, x]) => {
    els.push(ellipse(`av_${label}`, x, yC, size, size, C.infoBorder, C.infoBg, 2));
    els.push(text(`av_${label}_t`, x, yC + size * 0.3, size, size * 0.4, 'AB', size * 0.35, C.primary, 'center'));
    els.push(text(`av_${label}_lbl`, x, yC + size + 6, size, 14, label, 10, C.gray500, 'center'));
  });

  // Empty state
  els.push(text('empty_lbl', 50, yC + 198, 120, 14, 'Empty State', 11, C.gray500));
  els.push(rect('empty_outer', 50, yC + 216, 380, 200, C.gray300, C.white, 1, true, { strokeStyle: 'dashed' }));
  els.push(ellipse('empty_icon', 190, yC + 240, 56, 56, C.gray300, C.gray50, 1));
  els.push(text('empty_icon_t', 190, yC + 257, 56, 22, '?', 18, C.gray500, 'center'));
  els.push(text('empty_title', 50, yC + 310, 380, 22, 'No records found', 15, C.gray900, 'center'));
  els.push(text('empty_sub', 50, yC + 334, 380, 18, 'Create your first record to get started.', 12, C.gray500, 'center'));
  els.push(rect('empty_cta', 155, yC + 360, 170, 36, C.primaryDark, C.primary, 2, true));
  els.push(text('empty_cta_t', 155, yC + 368, 170, 20, '+ Create Record', 13, C.white, 'center'));

  return els;
}

function buildFeedback(y0) {
  const els = [...sectionHeader(10, 'Feedback & Overlays', y0)];
  const yC = y0 + 68;

  // Toasts
  els.push(text('toast_lbl', 50, y0 + 46, 120, 14, 'Toast Notifications', 11, C.gray500));
  const toasts = [
    ['success', C.successBorder, C.successBg, C.success, '✓  Record saved',     'Changes have been persisted.',  50,  yC],
    ['error',   C.dangerBorder,  C.dangerBg,  C.danger,  '✕  Error occurred',   'Failed to save changes.',       420, yC],
    ['warning', C.warningBorder, C.warningBg, C.warning, '⚠  Warning',          'Session expiring soon.',        50,  yC + 80],
    ['info',    C.infoBorder,    C.infoBg,    C.primary, 'ℹ  Processing',       'Workflow is running…',          420, yC + 80],
  ];
  toasts.forEach(([id, stroke, bg, tc, title, sub, x, y]) => {
    els.push(rect(`toast_${id}`, x, y, 340, 64, stroke, bg, 1, true));
    els.push(text(`toast_${id}_t`, x + 14, y + 10, 280, 18, title, 13, tc));
    els.push(text(`toast_${id}_s`, x + 14, y + 32, 280, 16, sub, 11, C.gray700));
    els.push(text(`toast_${id}_x`, x + 308, y + 10, 20, 18, '×', 14, C.gray500));
  });

  // Alert banner
  const yA = yC + 172;
  els.push(text('alert_lbl', 50, yA - 16, 100, 14, 'Alert Banner', 11, C.gray500));
  els.push(rect('alert', 50, yA, 760, 52, C.infoBorder, C.infoBg, 1, true));
  els.push(text('alert_t', 64, yA + 14, 560, 24, 'ℹ  Your trial expires in 7 days. Upgrade to continue access.', 13, C.primary));
  els.push(rect('alert_btn', 668, yA + 10, 100, 32, C.primaryDark, C.primary, 2, true));
  els.push(text('alert_btn_t', 668, yA + 18, 100, 16, 'Upgrade', 13, C.white, 'center'));

  // Progress bar
  const yPr = yA + 80;
  els.push(text('prog_lbl', 50, yPr - 16, 200, 14, 'Progress Bar', 11, C.gray500));
  els.push(text('prog_info', 50, yPr, 200, 18, 'Uploading file… 65%', 13, C.gray700));
  els.push(rect('prog_track', 50, yPr + 24, 400, 12, C.gray300, C.gray100, 1, true));
  els.push(rect('prog_fill', 50, yPr + 24, 260, 12, C.primaryDark, C.primary, 1, true));

  // Skeleton
  const ySk = yA + 152;
  els.push(text('skel_lbl', 50, ySk - 16, 120, 14, 'Skeleton Loader', 11, C.gray500));
  [[300, 0], [220, 26], [270, 52]].forEach(([w, dy], i) => {
    els.push(rect(`skel_${i}`, 50, ySk + dy, w, 16, C.gray300, C.gray300, 1, false, { roughness: 0 }));
  });

  // Spinner
  els.push(text('spin_lbl', 500, yPr - 16, 100, 14, 'Spinner', 11, C.gray500));
  els.push(ellipse('spin_outer', 500, yPr, 40, 40, C.gray300, 'transparent', 3));
  els.push(ellipse('spin_inner', 500, yPr, 40, 40, C.primary, 'transparent', 3, { strokeStyle: 'dashed' }));

  return els;
}

function buildModal(y0) {
  const els = [...sectionHeader(11, 'Modal / Dialog', y0)];
  const yC = y0 + 48;

  // Overlay
  els.push(rect('modal_overlay', 50, yC, 760, 300, C.gray700, C.gray900, 1, false, { opacity: 20, roughness: 0 }));

  // Card
  const mx = 210, my = yC + 32;
  els.push(rect('modal_card', mx, my, 440, 236, C.gray700, C.white, 2, true));
  els.push(text('modal_title', mx + 20, my + 18, 340, 24, 'Confirm Delete', 16, C.gray900));
  els.push(text('modal_close', mx + 400, my + 16, 20, 20, '×', 18, C.gray500));
  els.push(hline('modal_hdiv', mx, my + 52, 440, C.gray300));
  els.push(text('modal_body', mx + 20, my + 68, 400, 60, 'Are you sure you want to delete this record?\nThis action cannot be undone.', 14, C.gray700));
  els.push(hline('modal_fdiv', mx, my + 178, 440, C.gray300));
  els.push(rect('modal_cancel', mx + 240, my + 192, 100, 36, C.gray300, C.gray100, 1, true));
  els.push(text('modal_cancel_t', mx + 240, my + 200, 100, 18, 'Cancel', 13, C.gray700, 'center'));
  els.push(rect('modal_confirm', mx + 352, my + 192, 68, 36, C.dangerDark, C.danger, 2, true));
  els.push(text('modal_confirm_t', mx + 352, my + 200, 68, 18, 'Delete', 13, C.white, 'center'));

  return els;
}

function buildAppShell(y0) {
  const els = [...sectionHeader(12, 'App Shell', y0)];
  const yC = y0 + 48;
  const W = 900, H = 520;

  // Page bg
  els.push(rect('shell_bg', 50, yC, W, H, C.gray300, C.gray50, 1, false));

  // Sidebar
  els.push(rect('shell_sidebar', 50, yC, 230, H, C.gray300, C.white, 1, false));
  els.push(rect('shell_logo', 50, yC, 230, 60, C.gray300, C.gray100, 1, false));
  els.push(text('shell_logo_t', 80, yC + 18, 150, 26, '⬡  Axis', 18, C.primary));
  const shellItems = [['Data Models', false], ['Workflows', true], ['Forms', false], ['Pages', false], ['Settings', false]];
  shellItems.forEach(([label, active], i) => {
    const y = yC + 72 + i * 44;
    const bg = active ? C.infoBg : 'transparent';
    const tc = active ? C.primary : C.gray700;
    els.push(rect(`shell_ni_${i}`, 58, y, 214, 36, active ? C.infoBorder : 'transparent', bg, 1, false));
    if (active) els.push(rect(`shell_acc_${i}`, 58, y, 3, 36, C.primary, C.primary, 1, false));
    els.push(text(`shell_nl_${i}`, 80, y + 9, 170, 18, label, 13, tc));
  });
  els.push(hline('shell_user_div', 58, yC + 468, 214, C.gray300));
  els.push(ellipse('shell_uav', 66, yC + 476, 32, 32, C.infoBorder, C.infoBg, 1));
  els.push(text('shell_un', 106, yC + 484, 140, 16, 'Alex Brown', 12, C.gray900));

  // Header
  els.push(rect('shell_header', 280, yC, 670, 60, C.gray300, C.white, 1, false));
  els.push(text('shell_page_title', 300, yC + 18, 200, 24, 'Data Models', 18, C.gray900));
  els.push(rect('shell_srch', 680, yC + 12, 160, 36, C.gray300, C.gray100, 1, true));
  els.push(text('shell_srch_t', 694, yC + 22, 140, 16, '⌕  Search…', 12, C.gray300));
  els.push(ellipse('shell_notif', 862, yC + 12, 36, 36, C.gray300, C.gray100, 1));
  els.push(text('shell_notif_t', 862, yC + 21, 36, 18, '🔔', 12, C.gray700, 'center'));
  els.push(ellipse('shell_av', 908, yC + 12, 36, 36, C.infoBorder, C.infoBg, 1));
  els.push(text('shell_av_t', 908, yC + 21, 36, 18, 'AB', 12, C.primary, 'center'));

  // Content area
  els.push(text('shell_breadcrumb', 300, yC + 76, 300, 18, 'Data Models', 12, C.gray500));
  els.push(rect('shell_toolbar', 280, yC + 96, 670, 48, 'transparent', 'transparent', 0, false));
  els.push(rect('shell_add_btn', 820, yC + 104, 120, 32, C.primaryDark, C.primary, 2, true));
  els.push(text('shell_add_t', 820, yC + 112, 120, 16, '+ Add Model', 12, C.white, 'center'));
  // Content cards
  [[290, 152], [500, 152], [710, 152]].forEach(([x, dy], i) => {
    els.push(rect(`shell_card_${i}`, x, yC + dy, 190, 140, C.gray300, C.white, 1, true));
    els.push(rect(`shell_ch_${i}`, x, yC + dy, 190, 42, C.gray300, C.gray100, 1, false, { roundness: null }));
    els.push(text(`shell_ct_${i}`, x + 12, yC + dy + 12, 150, 18, ['User Profile', 'Order', 'Product'][i], 13, C.gray900));
  });

  return els;
}

// ─── Compose all sections (auto-stacking) ────────────────────────────────────

function sectionBottom(els) {
  return Math.max(...els.map(e => {
    if (e.type === 'line') {
      const pts = e.points || [[0, 0]];
      return e.y + Math.max(...pts.map(p => p[1]));
    }
    return e.y + (e.height || 0);
  }));
}

const GAP = 60;
const allElements = [];
let currentY = 30;

for (const builder of [
  buildColorPalette,
  buildTypography,
  buildButtons,
  buildFormControls,
  buildBadges,
  buildNavigation,
  buildSidebarNav,
  buildTable,
  buildCards,
  buildFeedback,
  buildModal,
  buildAppShell,
]) {
  const els = builder(currentY);
  allElements.push(...els);
  currentY = sectionBottom(els) + GAP;
}

const elements = allElements;

const output = JSON.stringify({ type: 'excalidraw', version: 2, source: 'https://excalidraw.com', elements, appState: { gridSize: 8, viewBackgroundColor: '#ffffff' }, files: {} }, null, 2);

writeFileSync(new URL('./_template.excalidraw', import.meta.url), output, 'utf-8');
console.log(`✓ Generated _template.excalidraw — ${elements.length} elements`);
