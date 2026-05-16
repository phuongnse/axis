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

function arrow(id, x, y, dx, dy, stroke, sw = 2) {
  const s = nextSeed();
  return { ...BASE, id, type: 'arrow', x, y, width: Math.abs(dx) || 1, height: Math.abs(dy) || 1, strokeColor: stroke || C.gray500, backgroundColor: 'transparent', fillStyle: 'solid', strokeWidth: sw, strokeStyle: 'solid', roughness: 1, roundness: { type: 2 }, seed: s, versionNonce: s + 1, points: [[0, 0], [dx, dy]], lastCommittedPoint: null, startBinding: null, endBinding: null, startArrowhead: null, endArrowhead: 'arrow' };
}

function sectionHeader(n, label, y) {
  return [
    text(`s${n}_lbl`, 50, y, 600, 26, `${n.toString().padStart(2, '0')} — ${label}`, 18, '#6c757d'),
    hline(`s${n}_div`, 50, y + 32, 1000),
  ];
}

// ─── Colors ───────────────────────────────────────────────────────────────────

const C = {
  // — Industrial Calm —
  // Primary: sage green (nav, icons, subtle highlights)
  // Accent:  amber clay (CTA buttons, key actions)
  primary:     '#667A6E',  // sage green
  primaryDark: '#4F5F57',  // sage dark
  accent:      '#C58B55',  // amber clay
  accentDark:  '#A8743E',  // amber clay dark
  danger:      '#9E4A44',  // muted brick
  dangerDark:  '#7D3A35',
  success:     '#4D6B44',  // moss green
  warning:     '#B5763D',  // amber clay (earthy)
  // Neutrals — warm, not blue-cast
  gray900:     '#2B2F33',  // near-black warm charcoal
  gray700:     '#4A5058',
  gray500:     '#8A9099',
  gray300:     '#D9D7D1',  // warm border
  gray100:     '#F4F3EF',  // warm off-white (page bg)
  gray50:      '#F9F8F5',
  white:       '#FFFFFF',  // surface (cards, panels)
  // Semantic backgrounds — earthy tones, low saturation
  infoBg:      '#EDF0EE',  // sage tint
  successBg:   '#EBF0E9',  // moss tint
  warningBg:   '#F6EEE4',  // clay tint
  dangerBg:    '#F3ECEA',  // brick tint
  // Borders
  infoBorder:  '#A8BAB1',  // sage muted
  successBorder:'#7FA676',  // moss muted
  warningBorder:'#C9975E',  // clay muted
  dangerBorder: '#C08078',  // brick muted
};

// ─── Section builders ─────────────────────────────────────────────────────────

function buildColorPalette(y0) {
  const swatches = [
    ['Sage',        C.primaryDark, C.primary],
    ['Sage Dark',   '#374840',     C.primaryDark],
    ['Amber Clay',  C.accentDark,  C.accent],
    ['Brick',       C.dangerDark,  C.danger],
    ['Moss',        '#2E4027',     C.success],
    ['Gray 900',    '#1a1d20',     C.gray900],
    ['Gray 700',    C.gray900,     C.gray700],
    ['Gray 500',    C.gray700,     C.gray500],
    ['Gray 300',    C.gray500,     C.gray300],
    ['Gray 100',    C.gray300,     C.gray100],
    ['Surface',     C.gray300,     C.white],
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

  // Row 1 — Variants (Primary = amber clay CTA, Secondary/Ghost use sage)
  const variants = [
    ['btn_primary',   'Primary',   C.accentDark,  C.accent,    C.white,  2, true,  50],
    ['btn_secondary', 'Secondary', C.primary,     C.infoBg,    C.primary,1, true, 180],
    ['btn_ghost',     'Ghost',     C.gray300,     'transparent',C.gray700,1, true, 310],
    ['btn_danger',    'Danger',    C.dangerDark,  C.danger,    C.white,  2, true,  440],
    ['btn_disabled',  'Disabled',  C.gray300,     C.gray100,   C.gray500,1, true,  570],
  ];
  variants.forEach(([id, label, stroke, bg, textColor, sw, rounded, x]) => {
    els.push(rect(id,        x, yR1, 120, 40, stroke, bg, sw, rounded));
    els.push(text(`${id}_t`, x, yR1 + 10, 120, 20, label, 14, textColor, 'center'));
  });

  // Row 2 — Sizes + icon (amber clay)
  els.push(rect('btn_sm',   50,  yR2, 96,  32, C.accentDark, C.accent,   2, true));
  els.push(text('btn_sm_t', 50,  yR2 + 7, 96, 18, 'Small',     13, C.white, 'center'));
  els.push(rect('btn_lg',   158, yR2, 140, 44, C.accentDark, C.accent,   2, true));
  els.push(text('btn_lg_t', 158, yR2 + 11, 140, 22, 'Large',   15, C.white, 'center'));
  els.push(rect('btn_icon', 312, yR2, 130, 36, C.accentDark, C.accent,   2, true));
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
  els.push(rect('empty_cta', 155, yC + 360, 170, 36, C.accentDark, C.accent, 2, true));
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
  els.push(rect('alert_btn', 668, yA + 10, 100, 32, C.accentDark, C.accent, 2, true));
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

  // Page bg (warm off-white)
  els.push(rect('shell_bg', 50, yC, W, H, C.gray300, C.gray100, 1, false));

  // Sidebar (white surface, sage accents)
  els.push(rect('shell_sidebar', 50, yC, 230, H, C.gray300, C.white, 1, false));
  els.push(rect('shell_logo', 50, yC, 230, 60, C.gray300, C.gray50, 1, false));
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

  // Header (white surface)
  els.push(rect('shell_header', 280, yC, 670, 60, C.gray300, C.white, 1, false));
  els.push(text('shell_page_title', 300, yC + 18, 200, 24, 'Data Models', 18, C.gray900));
  els.push(rect('shell_srch', 680, yC + 12, 160, 36, C.gray300, C.gray100, 1, true));
  els.push(text('shell_srch_t', 694, yC + 22, 140, 16, '⌕  Search…', 12, C.gray500));
  els.push(ellipse('shell_notif', 862, yC + 12, 36, 36, C.gray300, C.gray100, 1));
  els.push(text('shell_notif_t', 862, yC + 21, 36, 18, '🔔', 12, C.gray700, 'center'));
  els.push(ellipse('shell_av', 908, yC + 12, 36, 36, C.infoBorder, C.infoBg, 1));
  els.push(text('shell_av_t', 908, yC + 21, 36, 18, 'AB', 12, C.primary, 'center'));

  // Content area
  els.push(text('shell_breadcrumb', 300, yC + 76, 300, 18, 'Data Models', 12, C.gray500));
  els.push(rect('shell_toolbar', 280, yC + 96, 670, 48, 'transparent', 'transparent', 0, false));
  els.push(rect('shell_add_btn', 820, yC + 104, 120, 32, C.accentDark, C.accent, 2, true));
  els.push(text('shell_add_t', 820, yC + 112, 120, 16, '+ Add Model', 12, C.white, 'center'));
  // Content cards (white surface on warm bg)
  [[290, 152], [500, 152], [710, 152]].forEach(([x, dy], i) => {
    els.push(rect(`shell_card_${i}`, x, yC + dy, 190, 140, C.gray300, C.white, 1, true));
    els.push(rect(`shell_ch_${i}`, x, yC + dy, 190, 42, C.gray300, C.gray50, 1, false, { roundness: null }));
    els.push(text(`shell_ct_${i}`, x + 12, yC + dy + 12, 150, 18, ['User Profile', 'Order', 'Product'][i], 13, C.gray900));
  });

  return els;
}

function buildDropdownContextMenu(y0) {
  const els = [...sectionHeader(13, 'Dropdown & Context Menu', y0)];
  const yC = y0 + 48;

  // ── Dropdown open state (x=50) ──
  els.push(text('dd_lbl', 50, y0 + 46, 150, 14, 'Select Dropdown (open)', 11, C.gray500));
  els.push(rect('dd_trigger', 50, yC, 210, 40, C.primary, C.infoBg, 2, true));
  els.push(text('dd_trigger_t', 62, yC + 11, 160, 18, 'Assign to user…', 13, C.gray700));
  els.push(text('dd_arr', 234, yC + 11, 18, 18, '▴', 12, C.gray700));
  els.push(rect('dd_list', 50, yC + 46, 210, 176, C.gray300, C.white, 1, false));
  els.push(rect('dd_srch', 58, yC + 54, 194, 32, C.gray300, C.gray100, 1, true));
  els.push(text('dd_srch_t', 70, yC + 63, 170, 16, '⌕  Search…', 12, C.gray300));
  const ddItems = [['Alex Brown', false, false], ['Jane Smith', true, false], ['Mark Johnson', false, false], ['Sarah Lee', false, true]];
  ddItems.forEach(([name, active, disabled], i) => {
    const y = yC + 96 + i * 30;
    if (active) els.push(rect(`dd_hi_${i}`, 58, y, 194, 28, 'transparent', C.infoBg, 0, false));
    els.push(text(`dd_chk_${i}`, 62, y + 6, 14, 16, active ? '✓' : '', 11, C.primary));
    els.push(text(`dd_item_${i}`, 82, y + 6, 160, 16, name, 12, disabled ? C.gray300 : active ? C.primary : C.gray700));
  });

  // ── Context Menu (x=320) ──
  els.push(text('ctx_lbl', 320, y0 + 46, 120, 14, 'Context Menu', 11, C.gray500));
  els.push(rect('ctx_menu', 320, yC, 200, 214, C.gray300, C.white, 1, false));
  const ctxGroups = [
    [['✎  Edit', false, false], ['⧉  Duplicate', false, false]],
    null,
    [['⤓  Archive', false, false], ['✕  Delete', false, true]],
  ];
  let ctxY = yC + 8;
  ctxGroups.forEach((group, gi) => {
    if (!group) { els.push(hline(`ctx_div_${gi}`, 328, ctxY, 184, C.gray300)); ctxY += 14; return; }
    group.forEach(([label, active, danger]) => {
      if (active) els.push(rect(`ctx_hi_${ctxY}`, 328, ctxY, 184, 36, 'transparent', C.infoBg, 0, false));
      els.push(text(`ctx_t_${ctxY}`, 340, ctxY + 9, 160, 18, label, 13, danger ? C.danger : C.gray700));
      ctxY += 36;
    });
  });

  return els;
}

function buildDragDrop(y0) {
  const els = [...sectionHeader(14, 'Drag & Drop / Sortable', y0)];
  const yC = y0 + 48;

  // ── Sortable list (x=50) ──
  els.push(text('dnd_lbl', 50, y0 + 46, 120, 14, 'Sortable List', 11, C.gray500));
  const dndItems = ['Validate Input', 'Fetch Records', 'Apply Rules', 'Send Email', 'Update Status'];
  dndItems.forEach((label, i) => {
    const y = yC + i * 50;
    const drag = i === 2;
    if (drag) {
      els.push(rect(`dnd_slot_${i}`, 50, y, 370, 42, C.gray300, C.gray50, 1, false, { strokeStyle: 'dashed', roughness: 0 }));
      els.push(rect(`dnd_item_${i}`, 58, y - 6, 370, 42, C.infoBorder, C.infoBg, 2, false));
      els.push(text(`dnd_h_${i}`, 70, y + 4, 16, 16, '⠿', 12, C.infoBorder));
      els.push(text(`dnd_l_${i}`, 94, y + 4, 300, 16, `Step ${i + 1} — ${label}`, 12, C.primary));
    } else {
      els.push(rect(`dnd_item_${i}`, 50, y, 370, 42, C.gray300, C.white, 1, false));
      els.push(text(`dnd_h_${i}`, 62, y + 14, 16, 16, '⠿', 12, C.gray300));
      els.push(text(`dnd_l_${i}`, 86, y + 14, 300, 16, `Step ${i + 1} — ${label}`, 12, C.gray700));
    }
    if (i < dndItems.length - 1 && !drag) els.push(hline(`dnd_div_${i}`, 50, y + 42, 370, C.gray300));
  });

  // ── Kanban board (x=480) ──
  els.push(text('kan_lbl', 480, y0 + 46, 120, 14, 'Kanban Board', 11, C.gray500));
  const kanCols = [
    { label: 'To Do',       bg: C.gray50,    items: ['Approval gate', 'Send receipt'] },
    { label: 'In Progress', bg: C.infoBg,    items: ['Processing'] },
    { label: 'Done',        bg: C.successBg, items: ['User notified', 'Record saved'] },
  ];
  kanCols.forEach(({ label, bg, items: kanItems }, ci) => {
    const x = 480 + ci * 168;
    els.push(rect(`kan_col_${ci}`, x, yC, 154, 256, C.gray300, bg, 1, false));
    els.push(text(`kan_ht_${ci}`, x + 10, yC + 10, 110, 18, label, 12, C.gray700));
    els.push(text(`kan_cnt_${ci}`, x + 124, yC + 10, 20, 18, `${kanItems.length}`, 11, C.gray500, 'right'));
    els.push(hline(`kan_div_${ci}`, x, yC + 32, 154, C.gray300));
    kanItems.forEach((item, ii) => {
      els.push(rect(`kan_i_${ci}_${ii}`, x + 8, yC + 44 + ii * 56, 138, 44, C.gray300, C.white, 1, true));
      els.push(text(`kan_it_${ci}_${ii}`, x + 18, yC + 58 + ii * 56, 110, 16, item, 11, C.gray700));
    });
    els.push(text(`kan_add_${ci}`, x + 10, yC + 218, 120, 18, '+ Add card', 11, C.gray500));
  });

  return els;
}

function buildWorkflowCanvas(y0) {
  const els = [...sectionHeader(15, 'Workflow Canvas', y0)];
  const yC = y0 + 48;

  // Canvas bg
  els.push(rect('wc_bg', 50, yC, 900, 290, C.gray300, C.gray50, 1, false, { roughness: 0 }));

  // Nodes
  els.push(rect('wc_n_trigger', 100, yC + 100, 130, 56, C.primaryDark, C.primary, 2, true));
  els.push(text('wc_n_trigger_t', 100, yC + 116, 130, 18, '⚡ Trigger', 13, C.white, 'center'));
  els.push(text('wc_n_trigger_s', 100, yC + 136, 130, 14, 'Form Submitted', 10, C.white, 'center', { opacity: 85 }));

  els.push(rect('wc_n_a1', 300, yC + 100, 130, 56, C.gray300, C.white, 2, true));
  els.push(text('wc_n_a1_t', 300, yC + 116, 130, 18, '▶ Action', 13, C.gray700, 'center'));
  els.push(text('wc_n_a1_s', 300, yC + 136, 130, 14, 'Validate Input', 10, C.gray500, 'center'));

  // Selected node (condition)
  els.push(rect('wc_n_cond_sel', 494, yC + 88, 142, 80, C.primary, 'transparent', 2, true, { strokeStyle: 'dashed' }));
  els.push(rect('wc_n_cond', 498, yC + 92, 134, 72, C.infoBorder, C.infoBg, 2, true));
  els.push(text('wc_n_cond_t', 498, yC + 106, 134, 18, '⋯ Condition', 13, C.primary, 'center'));
  els.push(text('wc_n_cond_s', 498, yC + 126, 134, 14, 'Status = Active?', 10, C.primary, 'center'));

  els.push(rect('wc_n_email', 440, yC + 220, 120, 50, C.gray300, C.white, 2, true));
  els.push(text('wc_n_email_t', 440, yC + 238, 120, 18, '✉ Send Email', 12, C.gray700, 'center'));

  els.push(rect('wc_n_end', 640, yC + 220, 120, 50, C.dangerBorder, C.dangerBg, 2, true));
  els.push(text('wc_n_end_t', 640, yC + 238, 120, 18, '✕ Reject', 12, C.danger, 'center'));

  // Arrows
  els.push(arrow('wc_arr_ta1', 230, yC + 128, 70, 0, C.gray500));
  els.push(arrow('wc_arr_a1c', 430, yC + 128, 68, 0, C.gray500));
  els.push(arrow('wc_arr_ce',  540, yC + 164, -40, 56, C.success, 1));
  els.push(arrow('wc_arr_cr',  620, yC + 164,  80, 56, C.danger, 1));
  els.push(text('wc_yes', 490, yC + 186, 30, 14, 'Yes', 10, C.success));
  els.push(text('wc_no',  634, yC + 186, 24, 14, 'No', 10, C.danger));

  // Minimap + zoom controls
  els.push(rect('wc_minimap', 820, yC + 212, 120, 80, C.gray300, C.gray100, 1, false, { roughness: 0 }));
  els.push(text('wc_minimap_lbl', 820, yC + 216, 120, 14, 'Minimap', 9, C.gray500, 'center'));
  els.push(text('wc_zoom', 66, yC + 302, 80, 16, '100%  ⊡ Fit  ↺', 11, C.gray700));

  return els;
}

function buildBuilderLayout(y0) {
  const els = [...sectionHeader(16, 'Builder Layout', y0)];
  const yC = y0 + 48;
  const H = 300;

  // Outer frame
  els.push(rect('bl_frame', 50, yC, 900, H + 44, C.gray300, C.gray100, 1, false));

  // Top toolbar
  els.push(rect('bl_tb', 50, yC, 900, 44, C.gray300, C.white, 1, false));
  els.push(text('bl_tb_title', 66, yC + 13, 200, 20, '⬡  Workflow Builder', 14, C.primary));
  const tools = ['↖ Select', '✋ Pan', '⬡ Node'];
  tools.forEach((t, i) => {
    const active = i === 0;
    els.push(rect(`bl_tool_${i}`, 380 + i * 88, yC + 6, 78, 32, active ? C.infoBorder : C.gray300, active ? C.infoBg : 'transparent', 1, true));
    els.push(text(`bl_tool_t_${i}`, 380 + i * 88, yC + 14, 78, 16, t, 11, active ? C.primary : C.gray700, 'center'));
  });
  els.push(rect('bl_save', 832, yC + 8, 80, 28, C.accentDark, C.accent, 2, true));
  els.push(text('bl_save_t', 832, yC + 14, 80, 16, '✓ Save', 12, C.white, 'center'));

  // Left panel: component library
  els.push(rect('bl_left', 50, yC + 44, 160, H, C.gray300, C.white, 1, false));
  els.push(text('bl_left_t', 62, yC + 56, 130, 18, 'Components', 12, C.gray700));
  els.push(hline('bl_left_div', 58, yC + 78, 144, C.gray300));
  ['⚡ Trigger', '▶ Action', '⋯ Condition', '✉ Notify', '⤓ End'].forEach((c, i) => {
    els.push(rect(`bl_c_${i}`, 60, yC + 90 + i * 42, 140, 34, C.gray300, C.gray50, 1, false));
    els.push(text(`bl_ct_${i}`, 72, yC + 100 + i * 42, 116, 14, c, 11, C.gray700));
  });

  // Center canvas
  els.push(rect('bl_canvas', 210, yC + 44, 550, H, C.gray300, C.gray50, 1, false, { roughness: 0 }));
  els.push(rect('bl_n1', 270, yC + 140, 120, 50, C.primaryDark, C.primary, 2, true));
  els.push(text('bl_n1_t', 270, yC + 158, 120, 16, '⚡ Trigger', 12, C.white, 'center'));
  els.push(rect('bl_n2', 460, yC + 140, 120, 50, C.gray300, C.white, 2, true));
  els.push(text('bl_n2_t', 460, yC + 158, 120, 16, '▶ Action', 12, C.gray700, 'center'));
  els.push(rect('bl_n3', 650, yC + 140, 100, 50, C.gray300, C.white, 2, true));
  els.push(text('bl_n3_t', 650, yC + 158, 100, 16, '⋯ Cond.', 12, C.gray700, 'center'));
  els.push(arrow('bl_a1', 390, yC + 165, 70, 0, C.gray500));
  els.push(arrow('bl_a2', 580, yC + 165, 70, 0, C.gray500));

  // Right panel: properties
  els.push(rect('bl_right', 760, yC + 44, 190, H, C.gray300, C.white, 1, false));
  els.push(text('bl_right_t', 772, yC + 56, 160, 18, 'Properties', 12, C.gray700));
  els.push(hline('bl_right_div', 768, yC + 78, 174, C.gray300));
  els.push(text('bl_rp_lbl1', 772, yC + 92, 80, 14, 'Node Name', 10, C.gray500));
  els.push(rect('bl_rp_i1', 772, yC + 108, 166, 32, C.gray300, C.gray100, 1, true));
  els.push(text('bl_rp_v1', 784, yC + 118, 142, 14, 'Validate Input', 12, C.gray700));
  els.push(text('bl_rp_lbl2', 772, yC + 154, 80, 14, 'Action Type', 10, C.gray500));
  els.push(rect('bl_rp_i2', 772, yC + 170, 166, 32, C.gray300, C.gray100, 1, true));
  els.push(text('bl_rp_v2', 784, yC + 180, 140, 14, 'HTTP Request', 12, C.gray700));
  els.push(text('bl_rp_arr', 920, yC + 180, 12, 14, '▾', 10, C.gray700));

  return els;
}

function buildExecutionTimeline(y0) {
  const els = [...sectionHeader(17, 'Execution Timeline', y0)];
  const yC = y0 + 48;

  // Panel
  els.push(rect('et_panel', 50, yC, 620, 292, C.gray300, C.white, 1, false));
  els.push(rect('et_hdr', 50, yC, 620, 52, C.gray300, C.gray50, 1, false));
  els.push(text('et_title', 66, yC + 14, 280, 22, 'Run #1042', 16, C.gray900));
  els.push(rect('et_badge', 372, yC + 16, 80, 24, C.successBorder, C.successBg, 1, true));
  els.push(text('et_badge_t', 372, yC + 21, 80, 14, '✓ Success', 11, C.success, 'center'));
  els.push(text('et_meta', 464, yC + 20, 190, 16, '14 ms total · 2 min ago', 11, C.gray500));
  els.push(hline('et_hdiv', 50, yC + 52, 620, C.gray300));

  const steps = [
    { label: 'Form Submitted',  status: 'success', dur: '2 ms',  desc: 'Trigger' },
    { label: 'Validate Input',  status: 'success', dur: '5 ms',  desc: 'Action' },
    { label: 'Check Approval',  status: 'running', dur: '…',     desc: 'Condition' },
    { label: 'Send Email',      status: 'waiting', dur: '—',     desc: 'Action' },
    { label: 'Update Record',   status: 'waiting', dur: '—',     desc: 'Action' },
  ];
  const sc = {
    success: [C.successBorder, C.successBg, C.success, '✓'],
    running: [C.infoBorder,    C.infoBg,    C.primary, '●'],
    waiting: [C.gray300,       C.gray50,    C.gray500, '○'],
  };

  steps.forEach(({ label, status, dur, desc }, i) => {
    const y = yC + 60 + i * 46;
    const [stroke, bg, tc, icon] = sc[status];
    if (i < steps.length - 1) els.push(vline(`et_vl_${i}`, 81, y + 26, 20, C.gray300));
    els.push(ellipse(`et_ic_${i}`, 68, y + 4, 26, 26, stroke, bg, 2));
    els.push(text(`et_ic_t_${i}`, 68, y + 11, 26, 14, icon, 11, tc, 'center'));
    els.push(text(`et_lbl_${i}`, 106, y + 4, 260, 18, label, 13, C.gray900));
    els.push(text(`et_desc_${i}`, 106, y + 22, 120, 14, desc, 10, C.gray500));
    els.push(text(`et_dur_${i}`, 530, y + 4, 60, 18, dur, 12, status === 'waiting' ? C.gray300 : C.gray700, 'right'));
  });

  return els;
}

function buildUtilities(y0) {
  const els = [...sectionHeader(18, 'Utilities', y0)];
  const yC = y0 + 48;

  // ── Tooltip (x=50) ──
  els.push(text('tt_lbl', 50, y0 + 46, 80, 14, 'Tooltip', 11, C.gray500));
  els.push(rect('tt_bubble', 50, yC, 186, 34, C.gray900, C.gray900, 1, true, { roughness: 0 }));
  els.push(text('tt_t', 62, yC + 9, 162, 16, 'Save all changes', 11, C.white));
  els.push(rect('tt_caret', 138, yC + 32, 10, 8, C.gray900, C.gray900, 0, false, { roughness: 0 }));
  els.push(rect('tt_btn', 96, yC + 46, 94, 34, C.gray300, C.gray100, 1, true));
  els.push(text('tt_btn_t', 96, yC + 55, 94, 16, 'Save Draft', 12, C.gray700, 'center'));

  // ── Accordion (x=280) ──
  els.push(text('acc_lbl', 280, y0 + 46, 80, 14, 'Accordion', 11, C.gray500));
  const accItems = [
    { title: 'General Settings', open: true, body: 'Workspace name, timezone\nand language settings.' },
    { title: 'Permissions', open: false },
    { title: 'Integrations', open: false },
  ];
  let accY = yC;
  accItems.forEach(({ title, open, body }, i) => {
    els.push(rect(`acc_row_${i}`, 280, accY, 280, 44, C.gray300, open ? C.infoBg : C.white, 1, false));
    els.push(text(`acc_rt_${i}`, 294, accY + 14, 220, 18, title, 13, open ? C.primary : C.gray700));
    els.push(text(`acc_arr_${i}`, 538, accY + 14, 16, 18, open ? '▴' : '▾', 12, C.gray500));
    accY += 44;
    if (open && body) {
      els.push(rect(`acc_body_${i}`, 280, accY, 280, 68, C.gray300, C.gray50, 1, false));
      els.push(text(`acc_bc_${i}`, 294, accY + 12, 258, 40, body, 12, C.gray700));
      accY += 68;
    }
    if (i < accItems.length - 1) els.push(hline(`acc_div_${i}`, 280, accY, 280, C.gray300));
  });

  // ── Stepper (x=50, row 2) ──
  const yS = yC + 186;
  els.push(text('stp_lbl', 50, yS - 18, 80, 14, 'Stepper', 11, C.gray500));
  const stepItems = ['Details', 'Fields', 'Relations', 'Publish'];
  stepItems.forEach((label, i) => {
    const x = 80 + i * 170;
    const done = i < 2, current = i === 2;
    const bg = done ? C.primary : current ? C.infoBg : C.gray100;
    const stroke = done ? C.primaryDark : current ? C.infoBorder : C.gray300;
    const tc = done ? C.white : current ? C.primary : C.gray500;
    els.push(ellipse(`stp_c_${i}`, x, yS, 30, 30, stroke, bg, 2));
    els.push(text(`stp_n_${i}`, x, yS + 8, 30, 16, done ? '✓' : `${i + 1}`, 12, tc, 'center'));
    els.push(text(`stp_l_${i}`, x - 20, yS + 36, 70, 14, label, 10, tc, 'center'));
    if (i < stepItems.length - 1) els.push(hline(`stp_line_${i}`, x + 30, yS + 15, 140, done ? C.primary : C.gray300, done ? 2 : 1));
  });

  // ── Tag / Chip input (x=50, row 3) ──
  const yT = yS + 80;
  els.push(text('chip_lbl', 50, yT - 18, 80, 14, 'Tag Input', 11, C.gray500));
  els.push(rect('chip_wrap', 50, yT, 490, 44, C.gray300, C.white, 1, true));
  const chips = ['workflow', 'active', 'production'];
  let cx = 62;
  chips.forEach((chip) => {
    const w = chip.length * 7 + 28;
    els.push(rect(`chip_bg_${chip}`, cx, yT + 10, w, 24, C.infoBorder, C.infoBg, 1, true));
    els.push(text(`chip_t_${chip}`, cx + 6, yT + 14, w - 20, 16, chip, 11, C.primary));
    els.push(text(`chip_x_${chip}`, cx + w - 16, yT + 15, 10, 14, '×', 10, C.primary));
    cx += w + 8;
  });
  els.push(text('chip_ph', cx + 2, yT + 14, 100, 16, 'Add tag…', 12, C.gray300));

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
  buildDropdownContextMenu,
  buildDragDrop,
  buildWorkflowCanvas,
  buildBuilderLayout,
  buildExecutionTimeline,
  buildUtilities,
]) {
  const els = builder(currentY);
  allElements.push(...els);
  currentY = sectionBottom(els) + GAP;
}

const elements = allElements;

const output = JSON.stringify({ type: 'excalidraw', version: 2, source: 'https://excalidraw.com', elements, appState: { gridSize: 8, viewBackgroundColor: '#ffffff' }, files: {} }, null, 2);

writeFileSync(new URL('./_template.excalidraw', import.meta.url), output, 'utf-8');
console.log(`✓ Generated _template.excalidraw — ${elements.length} elements`);
