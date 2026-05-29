/**
 * Axis UI Component Kit — _template.excalidraw
 * Run: node docs/wireframes/generate-template.mjs
 *
 * TOC — 38 sections
 * ─── Foundations ─────────────────── S01 Color Palette
 *                                     S02 Typography
 *                                     S03 Buttons
 * ─── Input & Forms ───────────────── S04 Form Controls
 *                                     S05 Date & Time Picker
 *                                     S06 File Upload
 *                                     S07 Rich Text Editor
 *                                     S08 Code / JSON Editor
 * ─── Data Display ────────────────── S09 Badges & Tags
 *                                     S10 Table
 *                                     S11 Editable Table / Data Grid
 *                                     S12 Cards & Display
 *                                     S13 Empty States
 *                                     S14 Skeleton Loaders
 * ─── Navigation & Layout ─────────── S15 Navigation
 *                                     S16 Sidebar Navigation
 *                                     S17 Tabs
 *                                     S18 App Shell
 * ─── Feedback & Overlays ─────────── S19 Feedback & Overlays
 *                                     S20 Modal / Dialog
 *                                     S21 Side Sheet / Drawer
 *                                     S22 Command Palette
 *                                     S23 Notifications & Activity Feed
 *                                     S24 Tooltip & Popover
 * ─── Interaction Patterns ────────── S25 Dropdown & Context Menu
 *                                     S26 Drag & Drop / Sortable
 *                                     S27 Utilities
 *                                     S28 Permission Matrix
 *                                     S29 Color & Icon Picker
 * ─── Axis App Patterns ───────────── S30 Workflow Canvas
 *                                     S31 Builder Layout
 *                                     S32 Execution Timeline
 *                                     S33 Field Type Picker
 *                                     S34 Relation / Lookup Field
 *                                     S35 Dashboard & Analytics Stats
 *                                     S36 Advanced Filters / Query Builder
 *                                     S37 Dual Listbox / Transfer List
 * ─── Auth (registration) ─────────── S38 External sign-in
 */

import { fileURLToPath } from 'url';
import {
  nextSeed, BASE,
  rect, ellipse, text, hline, vline, arrow, sectionHeader,
  C, fieldLabel, REQUIRED_MARKER_GAP,
  writeExcalidraw,
} from './components.mjs';

// ─── Section builders ─────────────────────────────────────────────────────────

/** Inner width of a 440px auth card (24px padding each side). */
export const AUTH_INNER_W = 392;
export const AUTH_PROVIDER_BTN_SIZE = 44;
export const AUTH_PROVIDER_GAP = 20;
/** Icon row (44px) + gap (12px) + “or” divider (28px) — stack height after `componentContent(buildAuthExternalSignInBlock, …)`. */
export const AUTH_EXTERNAL_SIGN_IN_BLOCK_H = AUTH_PROVIDER_BTN_SIZE + 12 + 28;

export function buildColorPalette(y0) {
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

export function buildTypography(y0) {
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

export function buildButtons(y0) {
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

export function buildFormControls(y0) {
  const els = [...sectionHeader(4, 'Form Controls', y0)];
  const yC = y0 + 48;

  // ── Col 1: Text input states (x=50) ──
  els.push(text('inp_req_note', 50, yC, 320, 14,
    `Required labels — * is ${REQUIRED_MARKER_GAP}px after label (C.danger)`, 10, C.gray500));
  els.push(...fieldLabel('inp_req', 50, yC + 18, 'Organization name', { required: true }));
  els.push(rect('inp_req_demo', 50, yC + 36, 280, 40, C.gray300, C.white, 1, true));
  els.push(text('inp_req_ph', 62, yC + 47, 250, 18, 'Acme Corp', 13, C.gray500));

  const statesY = yC + 90;
  const inputStates = [
    ['Default',  C.gray300,   C.white,    1, 'email@company.com',  C.gray500],
    ['Focus',    C.primary,   C.white,    2, 'email@company.com',  C.gray500],
    ['Error',    C.danger,    C.dangerBg, 2, 'invalid-email',      C.danger],
    ['Disabled', C.gray300,   C.gray100,  1, 'Disabled',           C.gray300],
  ];
  inputStates.forEach(([state, stroke, bg, sw, placeholder, phColor], i) => {
    const y = statesY + i * 78;
    els.push(...fieldLabel(`inp_lbl_${i}`, 50, y, 'Email address', { required: true }));
    els.push(rect(`inp_${i}`, 50, y + 18, 280, 40, stroke, bg, sw, true));
    els.push(text(`inp_ph_${i}`, 62, y + 29, 250, 18, placeholder, 13, phColor));
    if (state === 'Error') {
      els.push(text(`inp_err`, 50, y + 62, 280, 14, '✕  Invalid email address', 11, C.danger));
    }
  });

  // ── Col 2: Other inputs (x=380) ──
  const x2 = 380;

  // Password
  els.push(...fieldLabel('pw_lbl', x2, yC, 'Password', { required: true }));
  els.push(rect('pw_inp', x2, yC + 18, 280, 40, C.gray300, C.white, 1, true));
  els.push(text('pw_dots', x2 + 12, yC + 29, 150, 18, '••••••••', 13, C.gray700));
  els.push(text('pw_eye', x2 + 248, yC + 29, 20, 18, '👁', 13, C.gray500));

  // Search
  els.push(text('srch_lbl', x2, yC + 78, 100, 16, 'Search', 11, C.gray500));
  els.push(rect('srch_inp', x2, yC + 96, 280, 40, C.gray300, C.white, 1, true));
  els.push(text('srch_icon', x2 + 10, yC + 107, 20, 18, '⌕', 14, C.gray500));
  els.push(text('srch_ph', x2 + 34, yC + 107, 230, 18, 'Search records…', 13, C.gray300));

  // Textarea
  els.push(text('ta_lbl', x2, yC + 156, 100, 16, 'Textarea', 11, C.gray500));
  els.push(rect('ta_inp', x2, yC + 174, 280, 80, C.gray300, C.white, 1, true));
  els.push(text('ta_ph', x2 + 12, yC + 185, 250, 18, 'Enter description…', 13, C.gray300));

  // Select
  els.push(text('sel_lbl', x2, yC + 274, 120, 16, 'Select / Dropdown', 11, C.gray500));
  els.push(rect('sel_inp', x2, yC + 292, 280, 40, C.gray300, C.white, 1, true));
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

export function buildBadges(y0) {
  const els = [...sectionHeader(9, 'Badges & Tags', y0)];
  const yC = y0 + 68;
  const badges = [
    ['Default',  C.gray300,     C.gray50,      C.gray700,  'Default',  50],
    ['Primary',  C.primaryDark, C.primary,     C.white,    'Active',   155],
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

export function buildNavigation(y0) {
  const els = [...sectionHeader(15, 'Navigation', y0)];
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

export function buildSidebarNav(y0) {
  const els = [...sectionHeader(16, 'Sidebar Navigation', y0)];
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

export function buildTable(y0) {
  const els = [...sectionHeader(10, 'Table', y0)];
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
  const statusColors = { Active: [C.primaryDark, C.primary, C.white], Draft: [C.gray300, C.gray50, C.gray700], Pending: [C.warningBorder, C.warningBg, C.warning] };

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

export function buildCards(y0) {
  const els = [...sectionHeader(12, 'Cards & Display', y0)];
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

export function buildFeedback(y0) {
  const els = [...sectionHeader(19, 'Feedback & Overlays', y0)];
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

export function buildModal(y0) {
  const els = [...sectionHeader(20, 'Modal / Dialog', y0)];
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

export function buildAppShell(y0) {
  const els = [...sectionHeader(18, 'App Shell', y0)];
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

export function buildDateTimePicker(y0) {
  const els = [...sectionHeader(5, 'Date & Time Picker', y0)];
  const yC = y0 + 68;

  // ── Date Picker — calendar popup (x=50) ──
  els.push(text('dp_lbl', 50, y0 + 46, 100, 14, 'Date Picker', 11, C.gray500));
  els.push(rect('dp_inp', 50, yC, 224, 40, C.primary, C.infoBg, 2, true));
  els.push(text('dp_val', 62, yC + 11, 180, 18, '📅  Mar 15, 2026', 13, C.gray700));

  const cx = 50, cy = yC + 48, cw = 252;
  els.push(rect('dp_cal', cx, cy, cw, 244, C.gray300, C.white, 1, false));
  els.push(rect('dp_hdr', cx, cy, cw, 44, 'transparent', C.gray50, 0, false));
  els.push(text('dp_prev', cx + 10, cy + 13, 18, 18, '‹', 15, C.gray700));
  els.push(text('dp_mo', cx, cy + 13, cw, 18, 'March  2026', 14, C.gray900, 'center'));
  els.push(text('dp_nxt', cx + 224, cy + 13, 18, 18, '›', 15, C.gray700));
  els.push(hline('dp_hdiv', cx, cy + 44, cw, C.gray300));

  ['S','M','T','W','T','F','S'].forEach((d, i) =>
    els.push(text(`dp_wd_${i}`, cx + 8 + i * 34, cy + 52, 26, 16, d, 11, C.gray500, 'center')));

  const calRows = [
    [1,2,3,4,5,6,7], [8,9,10,11,12,13,14],
    [15,16,17,18,19,20,21], [22,23,24,25,26,27,28],
    [29,30,31,null,null,null,null],
  ];
  calRows.forEach((row, ri) => {
    row.forEach((d, ci) => {
      if (!d) return;
      const dx = cx + 8 + ci * 34, dy = cy + 76 + ri * 32;
      if (d === 15) {
        els.push(ellipse(`dp_sel`, dx + 3, dy, 24, 24, C.accentDark, C.accent, 1));
        els.push(text(`dp_d_${ri}_${ci}`, dx, dy + 4, 30, 16, `${d}`, 12, C.white, 'center'));
      } else if (d === 8) {
        els.push(ellipse(`dp_tod`, dx + 3, dy, 24, 24, C.primary, 'transparent', 1));
        els.push(text(`dp_d_${ri}_${ci}`, dx, dy + 4, 30, 16, `${d}`, 12, C.primary, 'center'));
      } else {
        els.push(text(`dp_d_${ri}_${ci}`, dx, dy + 4, 30, 16, `${d}`, 12, (ci === 0 || ci === 6) ? C.gray300 : C.gray700, 'center'));
      }
    });
  });

  // ── Time Picker (x=346) ──
  els.push(text('tp_lbl', 346, y0 + 46, 100, 14, 'Time Picker', 11, C.gray500));
  els.push(rect('tp_inp', 346, yC, 180, 40, C.gray300, C.gray100, 1, true));
  els.push(text('tp_val', 358, yC + 11, 140, 18, '🕐  14:30', 13, C.gray700));
  els.push(rect('tp_popup', 346, yC + 48, 180, 162, C.gray300, C.white, 1, false));
  // Hour
  els.push(text('tp_h_lbl', 352, yC + 58, 56, 14, 'Hour', 10, C.gray500, 'center'));
  els.push(text('tp_h_up',  352, yC + 76, 56, 16, '▴', 13, C.gray700, 'center'));
  els.push(rect('tp_h_box', 347, yC + 96, 66, 34, C.primary, C.infoBg, 2, true));
  els.push(text('tp_h_v',   347, yC + 106, 66, 16, '14', 16, C.primary, 'center'));
  els.push(text('tp_h_dn',  352, yC + 134, 56, 16, '▾', 13, C.gray700, 'center'));
  // Colon
  els.push(text('tp_col', 415, yC + 108, 14, 16, ':', 18, C.gray900));
  // Minute
  els.push(text('tp_m_lbl', 432, yC + 58, 56, 14, 'Min', 10, C.gray500, 'center'));
  els.push(text('tp_m_up',  432, yC + 76, 56, 16, '▴', 13, C.gray700, 'center'));
  els.push(rect('tp_m_box', 427, yC + 96, 66, 34, C.primary, C.infoBg, 2, true));
  els.push(text('tp_m_v',   427, yC + 106, 66, 16, '30', 16, C.primary, 'center'));
  els.push(text('tp_m_dn',  432, yC + 134, 56, 16, '▾', 13, C.gray700, 'center'));
  els.push(rect('tp_ok', 363, yC + 162, 96, 28, C.accentDark, C.accent, 2, true));
  els.push(text('tp_ok_t', 363, yC + 170, 96, 16, 'Apply', 12, C.white, 'center'));

  // ── Date Range (x=576) ──
  els.push(text('dr_lbl', 576, y0 + 46, 100, 14, 'Date Range', 11, C.gray500));
  els.push(rect('dr_inp', 576, yC, 266, 40, C.gray300, C.gray100, 1, true));
  els.push(text('dr_val', 588, yC + 11, 236, 18, 'Mar 10 → Mar 20, 2026', 13, C.gray700));
  els.push(rect('dr_cal', 576, yC + 48, 266, 200, C.gray300, C.white, 1, false));
  els.push(rect('dr_hdr', 576, yC + 48, 266, 44, 'transparent', C.gray50, 0, false));
  els.push(text('dr_mo', 576, yC + 61, 266, 18, '< March 2026 >', 13, C.gray900, 'center'));
  els.push(hline('dr_hdiv', 576, yC + 92, 266, C.gray300));
  ['S','M','T','W','T','F','S'].forEach((d, i) =>
    els.push(text(`dr_wd_${i}`, 584 + i * 36, yC + 100, 28, 14, d, 11, C.gray500, 'center')));
  // Range: days 10-20. Row 1 col 2–6 = days 10-14; Row 2 col 0–5 = days 15-20.
  for (let ri = 0; ri < 4; ri++) {
    for (let ci = 0; ci < 7; ci++) {
      const inRange = (ri === 1 && ci >= 2) || (ri === 2 && ci <= 5);
      const isStart = ri === 1 && ci === 2;
      const isEnd   = ri === 2 && ci === 5;
      const ex = 584 + ci * 36, ey = yC + 120 + ri * 34;
      if (isStart || isEnd) {
        els.push(ellipse(`dr_ep_${ri}_${ci}`, ex + 2, ey, 24, 24, C.accentDark, C.accent, 1));
        els.push(text(`dr_en_${ri}_${ci}`, ex, ey + 5, 28, 14, isStart ? '10' : '20', 11, C.white, 'center'));
      } else if (inRange) {
        els.push(rect(`dr_rng_${ri}_${ci}`, ex, ey + 2, 32, 22, 'transparent', C.infoBg, 0, false));
      }
    }
  }

  return els;
}

export function buildEditableTable(y0) {
  const els = [...sectionHeader(11, 'Editable Table / Data Grid', y0)];
  const yC = y0 + 48;

  // Bulk action bar (visible when rows selected)
  els.push(rect('edt_bar', 50, yC, 850, 44, C.infoBorder, C.infoBg, 1, false));
  els.push(text('edt_bar_t', 66, yC + 13, 200, 18, '2 items selected', 13, C.primary));
  els.push(rect('edt_bar_del', 600, yC + 8, 88, 28, C.dangerDark, C.danger, 2, true));
  els.push(text('edt_bar_del_t', 600, yC + 14, 88, 16, '✕  Delete', 12, C.white, 'center'));
  els.push(rect('edt_bar_exp', 698, yC + 8, 96, 28, C.gray300, C.gray100, 1, true));
  els.push(text('edt_bar_exp_t', 698, yC + 14, 96, 16, '⤓  Export', 12, C.gray700, 'center'));
  els.push(text('edt_bar_clr', 806, yC + 14, 80, 16, '× Clear', 12, C.gray700));

  // Table
  const tY = yC + 52;
  const cols = [44, 210, 130, 150, 160, 156];
  const xC = cols.reduce((a, w, i) => [...a, i === 0 ? 50 : a[a.length - 1] + cols[i - 1]], []);
  const W = 850;

  els.push(rect('edt_outer', 50, tY, W, 212, C.gray300, 'transparent', 1, false));
  els.push(rect('edt_hdr', 50, tY, W, 44, 'transparent', C.gray100, 0, false));
  els.push(hline('edt_hdiv', 50, tY + 44, W, C.gray300));
  // Select-all checkbox (indeterminate)
  els.push(rect('edt_chk_all', xC[0] + 13, tY + 13, 18, 18, C.primary, C.primary, 1, false));
  els.push(text('edt_chk_all_m', xC[0] + 13, tY + 14, 18, 16, '—', 10, C.white, 'center'));
  ['Name', 'Type', 'Status', 'Created', 'Actions'].forEach((h, i) =>
    els.push(text(`edt_h_${i}`, xC[i + 1] + 12, tY + 12, cols[i + 1] - 16, 20, h, 13, C.gray900)));
  xC.slice(1).forEach((x, i) => els.push(vline(`edt_vl_${i}`, x, tY, 212, C.gray300)));

  const rows = [
    { name: 'User Profile',    type: 'Data Model', status: 'Active', date: 'Jan 12, 2026', sel: true,  edit: false },
    { name: 'Order Workflow',  type: 'Workflow',   status: 'Draft',  date: 'Jan 10, 2026', sel: true,  edit: false },
    { name: 'Contact Form',    type: 'Form',       status: 'Active', date: 'Jan 8, 2026',  sel: false, edit: true  },
    { name: 'Invoice Page',    type: 'Page',       status: 'Draft',  date: 'Jan 5, 2026',  sel: false, edit: false },
  ];
  const sc = { Active: [C.primaryDark, C.primary, C.white], Draft: [C.gray300, C.gray50, C.gray700] };

  rows.forEach(({ name, type, status, date, sel, edit }, i) => {
    const y = tY + 44 + i * 42;
    els.push(rect(`edt_row_${i}`, 50, y, W, 42, 'transparent', sel ? C.infoBg : i % 2 === 0 ? C.white : C.gray50, 0, false));
    if (i < rows.length - 1) els.push(hline(`edt_hl_${i}`, 50, y + 42, W, C.gray300));
    // Checkbox
    els.push(rect(`edt_chk_${i}`, xC[0] + 13, y + 12, 18, 18, sel ? C.primary : C.gray500, sel ? C.primary : C.white, 1, false));
    if (sel) els.push(text(`edt_chk_t_${i}`, xC[0] + 13, y + 13, 18, 16, '✓', 11, C.white, 'center'));
    // Name — editing on row 2
    if (edit) {
      els.push(rect(`edt_ni_${i}`, xC[1] + 10, y + 6, cols[1] - 22, 30, C.primary, C.infoBg, 2, true));
      els.push(text(`edt_nv_${i}`, xC[1] + 16, y + 14, cols[1] - 34, 16, name, 12, C.gray900));
      els.push(rect(`edt_cur_${i}`, xC[1] + 16 + name.length * 6.8, y + 10, 2, 18, C.primary, C.primary, 0, false));
    } else {
      els.push(text(`edt_nm_${i}`, xC[1] + 12, y + 13, cols[1] - 16, 18, name, 13, C.gray900));
    }
    els.push(text(`edt_ty_${i}`, xC[2] + 12, y + 13, cols[2] - 16, 18, type, 12, C.gray700));
    const [bs, bb, bt] = sc[status];
    els.push(rect(`edt_bdg_${i}`, xC[3] + 12, y + 10, 70, 22, bs, bb, 1, true));
    els.push(text(`edt_bdt_${i}`, xC[3] + 12, y + 14, 70, 14, status, 11, bt, 'center'));
    els.push(text(`edt_dt_${i}`, xC[4] + 12, y + 13, cols[4] - 16, 18, date, 12, C.gray700));
    els.push(rect(`edt_eb_${i}`, xC[5] + 10, y + 9, 44, 24, C.gray300, C.gray100, 1, true));
    els.push(text(`edt_et_${i}`, xC[5] + 10, y + 14, 44, 14, '✎  Edit', 10, C.gray700, 'center'));
    els.push(rect(`edt_db_${i}`, xC[5] + 62, y + 9, 44, 24, C.dangerBorder, C.dangerBg, 1, true));
    els.push(text(`edt_dt2_${i}`, xC[5] + 62, y + 14, 44, 14, '✕', 11, C.danger, 'center'));
  });
  els.push(text('edt_foot', 62, tY + 224, 300, 18, 'Showing 1–4 of 143 records', 12, C.gray500));

  return els;
}

export function buildCommandPalette(y0) {
  const els = [...sectionHeader(22, 'Command Palette', y0)];
  const yC = y0 + 68;

  els.push(text('cp_hint', 50, y0 + 46, 220, 14, 'Global Command Palette  ⌘K to open', 11, C.gray500));

  // Scrim
  els.push(rect('cp_scrim', 50, yC, 900, 330, C.gray900, C.gray900, 1, false, { opacity: 25, roughness: 0 }));

  // Dialog
  const dx = 210, dy = yC + 28, dw = 480, dh = 274;
  els.push(rect('cp_dlg', dx, dy, dw, dh, C.gray300, C.white, 2, true));

  // Search row
  els.push(text('cp_icon', dx + 16, dy + 18, 20, 20, '⌕', 16, C.gray500));
  els.push(text('cp_inp', dx + 42, dy + 19, 360, 18, 'Search commands, pages, records…', 14, C.gray300));
  els.push(rect('cp_esc', dx + 430, dy + 15, 32, 22, C.gray300, C.gray100, 1, true));
  els.push(text('cp_esc_t', dx + 430, dy + 19, 32, 14, 'esc', 9, C.gray500, 'center'));
  els.push(hline('cp_sdiv', dx, dy + 52, dw, C.gray300));

  // Result groups
  const groups = [
    { label: 'Recent', items: [
      { icon: '▶', label: 'Order Workflow',       sub: 'Workflow' },
      { icon: '⬡', label: 'User Profile Schema',  sub: 'Data Model' },
    ]},
    { label: 'Actions', items: [
      { icon: '+', label: 'Create new workflow',   sub: '⌘ Action' },
      { icon: '+', label: 'Add data model field',  sub: '⌘ Action' },
    ]},
    { label: 'Navigation', items: [
      { icon: '→', label: 'Go to Data Models',     sub: '⌘ Navigate' },
    ]},
  ];

  let ry = dy + 62;
  groups.forEach(({ label, items }, gi) => {
    els.push(text(`cp_gl_${gi}`, dx + 14, ry, dw - 28, 14, label, 10, C.gray500));
    ry += 22;
    items.forEach(({ icon, label: iLabel, sub }, ii) => {
      const active = gi === 0 && ii === 0;
      if (active) els.push(rect(`cp_hi_${gi}_${ii}`, dx + 6, ry - 2, dw - 12, 34, C.infoBorder, C.infoBg, 1, true));
      els.push(text(`cp_ic_${gi}_${ii}`, dx + 16, ry + 5, 20, 20, icon, 13, active ? C.primary : C.gray700));
      els.push(text(`cp_lb_${gi}_${ii}`, dx + 40, ry + 7, 310, 16, iLabel, 13, active ? C.primary : C.gray700));
      els.push(text(`cp_sb_${gi}_${ii}`, dx + 366, ry + 7, 94, 16, sub, 10, C.gray500, 'right'));
      ry += 32;
    });
    ry += 6;
  });

  // Footer
  els.push(hline('cp_fdiv', dx, dy + dh - 32, dw, C.gray300));
  els.push(text('cp_fhint', dx + 14, dy + dh - 22, dw - 28, 16, '↑↓  navigate    ↵  select    esc  dismiss', 10, C.gray500));

  return els;
}

export function buildFileUpload(y0) {
  const els = [...sectionHeader(6, 'File Upload', y0)];
  const yC = y0 + 68;

  // ── Default drop zone (x=50) ──
  els.push(text('fu_lbl', 50, y0 + 46, 100, 14, 'Default State', 11, C.gray500));
  els.push(rect('fu_zone', 50, yC, 380, 140, C.gray300, C.gray50, 1, false, { strokeStyle: 'dashed', roughness: 0 }));
  els.push(ellipse('fu_ic_bg', 215, yC + 28, 44, 44, C.gray300, C.gray100, 1));
  els.push(text('fu_ic', 215, yC + 40, 44, 22, '⤒', 16, C.gray500, 'center'));
  els.push(text('fu_title', 50, yC + 82, 380, 18, 'Drag & drop files here', 13, C.gray700, 'center'));
  els.push(rect('fu_browse', 145, yC + 108, 120, 28, C.primary, C.infoBg, 1, true));
  els.push(text('fu_browse_t', 145, yC + 116, 120, 16, 'Browse files', 12, C.primary, 'center'));
  els.push(text('fu_hint', 50, yC + 146, 380, 14, 'PDF · DOCX · PNG  up to 20 MB', 10, C.gray500, 'center'));

  // ── Drag-over state (x=464) ──
  els.push(text('fu_act_lbl', 464, y0 + 46, 120, 14, 'Drag-over State', 11, C.gray500));
  els.push(rect('fu_act', 464, yC, 380, 140, C.infoBorder, C.infoBg, 2, false, { strokeStyle: 'dashed', roughness: 0 }));
  els.push(ellipse('fu_act_ic', 629, yC + 28, 44, 44, C.infoBorder, C.infoBg, 2));
  els.push(text('fu_act_ic_t', 629, yC + 40, 44, 22, '⤒', 16, C.primary, 'center'));
  els.push(text('fu_act_title', 464, yC + 82, 380, 18, 'Drop to upload', 14, C.primary, 'center'));
  els.push(text('fu_act_sub', 464, yC + 106, 380, 16, 'Release to start uploading', 12, C.primary, 'center', { opacity: 75 }));

  // ── File list ──
  els.push(text('fl_lbl', 50, yC + 158, 120, 14, 'Uploaded Files', 11, C.gray500));
  const files = [
    { name: 'report-2026.pdf',    size: '2.4 MB', state: 'done' },
    { name: 'schema-export.json', size: '145 KB', state: 'uploading', pct: 65 },
    { name: 'cover-image.png',    size: '1.1 MB', state: 'error' },
  ];
  files.forEach(({ name, size, state, pct }, i) => {
    const y = yC + 178 + i * 56;
    els.push(rect(`fl_item_${i}`, 50, y, 794, 48, C.gray300, C.white, 1, false));
    els.push(rect(`fl_ic_${i}`, 62, y + 10, 28, 28, C.gray300, C.gray100, 1, false));
    els.push(text(`fl_ic_t_${i}`, 62, y + 18, 28, 14, '📄', 10, C.gray500, 'center'));
    els.push(text(`fl_nm_${i}`, 100, y + 8, 290, 16, name, 12, C.gray900));
    els.push(text(`fl_sz_${i}`, 100, y + 26, 100, 14, size, 10, C.gray500));
    if (state === 'done') {
      els.push(text(`fl_st_${i}`, 660, y + 17, 60, 16, '✓  Done', 11, C.success, 'center'));
    } else if (state === 'uploading') {
      els.push(rect(`fl_tr_${i}`, 400, y + 20, 240, 8, C.gray300, C.gray100, 1, true));
      els.push(rect(`fl_fl_${i}`, 400, y + 20, Math.round(240 * pct / 100), 8, C.primaryDark, C.primary, 1, true));
      els.push(text(`fl_pc_${i}`, 650, y + 15, 36, 14, `${pct}%`, 10, C.primary, 'right'));
    } else {
      els.push(text(`fl_er_${i}`, 648, y + 17, 80, 16, '✕  Failed', 11, C.danger, 'center'));
    }
    els.push(text(`fl_rm_${i}`, 778, y + 16, 16, 16, '×', 14, C.gray500));
    if (i < files.length - 1) els.push(hline(`fl_div_${i}`, 50, y + 48, 794, C.gray300));
  });

  return els;
}

export function buildNotifications(y0) {
  const els = [...sectionHeader(23, 'Notifications & Activity Feed', y0)];
  const yC = y0 + 68;

  // ── Notification panel (x=50) ──
  els.push(text('no_lbl', 50, y0 + 46, 140, 14, 'Notification Panel', 11, C.gray500));
  // Bell trigger with badge
  els.push(ellipse('no_bell', 50, yC, 40, 40, C.gray300, C.gray100, 1));
  els.push(text('no_bell_t', 50, yC + 10, 40, 20, '🔔', 14, C.gray700, 'center'));
  els.push(ellipse('no_badge', 72, yC, 18, 18, C.dangerDark, C.danger, 1));
  els.push(text('no_badge_t', 72, yC + 4, 18, 12, '3', 9, C.white, 'center'));

  // Panel dropdown
  els.push(rect('no_panel', 50, yC + 48, 344, 306, C.gray300, C.white, 1, false));
  els.push(rect('no_phdr', 50, yC + 48, 344, 48, 'transparent', C.gray50, 0, false));
  els.push(text('no_ptitle', 66, yC + 62, 160, 20, 'Notifications', 14, C.gray900));
  els.push(text('no_mark', 292, yC + 62, 88, 20, 'Mark all read', 11, C.primary, 'right'));
  els.push(hline('no_hdiv', 50, yC + 96, 344, C.gray300));

  const notifs = [
    { icon: '✓', sc: [C.successBorder, C.successBg, C.success], title: 'Workflow completed',       sub: 'Run #1042 finished in 14 ms', time: '2m ago',    unread: true  },
    { icon: '⚠', sc: [C.warningBorder, C.warningBg, C.warning], title: 'Retry limit reached',      sub: 'Send Email failed 3 times',   time: '1h ago',    unread: true  },
    { icon: 'ℹ', sc: [C.infoBorder,   C.infoBg,    C.primary], title: 'New form submission',      sub: 'Contact Form — Jane Smith',   time: '3h ago',    unread: false },
    { icon: '✓', sc: [C.successBorder, C.successBg, C.success], title: 'Schema migration applied', sub: 'User Profile updated',        time: 'Yesterday', unread: false },
  ];
  notifs.forEach(({ icon, sc: [stroke, bg, tc], title, sub, time, unread }, i) => {
    const y = yC + 96 + i * 54;
    if (unread) els.push(rect(`no_unrd_${i}`, 50, y, 344, 54, 'transparent', C.infoBg, 0, false));
    els.push(ellipse(`no_ic_${i}`, 64, y + 14, 26, 26, stroke, bg, 1));
    els.push(text(`no_ic_t_${i}`, 64, y + 21, 26, 14, icon, 11, tc, 'center'));
    els.push(text(`no_ti_${i}`, 100, y + 10, 196, 16, title, 12, C.gray900));
    els.push(text(`no_su_${i}`, 100, y + 28, 196, 14, sub, 10, C.gray500));
    els.push(text(`no_tm_${i}`, 308, y + 10, 72, 16, time, 10, C.gray500, 'right'));
    if (unread) els.push(ellipse(`no_dot_${i}`, 372, y + 20, 8, 8, C.primary, C.primary, 1));
    if (i < notifs.length - 1) els.push(hline(`no_div_${i}`, 58, y + 54, 328, C.gray300));
  });
  els.push(text('no_all', 50, yC + 312, 344, 18, 'View all notifications →', 12, C.primary, 'center'));

  // ── Activity Feed (x=446) ──
  els.push(text('af_lbl', 446, y0 + 46, 120, 14, 'Activity Feed', 11, C.gray500));
  els.push(rect('af_panel', 446, yC, 408, 354, C.gray300, C.white, 1, false));
  els.push(rect('af_hdr', 446, yC, 408, 48, 'transparent', C.gray50, 0, false));
  els.push(text('af_title', 462, yC + 14, 200, 20, 'Recent Activity', 14, C.gray900));
  els.push(hline('af_hdiv', 446, yC + 48, 408, C.gray300));

  const acts = [
    { av: 'AB', name: 'Alex Brown', action: 'ran workflow',    target: 'Order Processing', time: '2 min ago',  type: 'success' },
    { av: 'JS', name: 'Jane Smith',  action: 'created form',   target: 'Contact Form v2',  time: '1 hr ago',   type: 'info'    },
    { av: 'MJ', name: 'Mark J.',     action: 'updated schema', target: 'User Profile',     time: '2 hr ago',   type: 'info'    },
    { av: 'SL', name: 'Sarah Lee',   action: 'deleted item',   target: 'Test Workflow',    time: 'Yesterday',  type: 'danger'  },
    { av: 'AB', name: 'Alex Brown',  action: 'published page', target: 'Dashboard v1',     time: '2 days ago', type: 'success' },
  ];
  const atc = { success: C.success, info: C.primary, danger: C.danger };
  acts.forEach(({ av, name, action, target, time, type }, i) => {
    const y = yC + 56 + i * 56;
    if (i < acts.length - 1) els.push(vline(`af_vl_${i}`, 469, y + 34, 22, C.gray300));
    els.push(ellipse(`af_av_${i}`, 456, y + 4, 30, 30, C.infoBorder, C.infoBg, 1));
    els.push(text(`af_av_t_${i}`, 456, y + 12, 30, 14, av, 10, C.primary, 'center'));
    els.push(text(`af_nm_${i}`, 496, y + 4, 120, 16, name, 12, C.gray900));
    els.push(text(`af_ac_${i}`, 496, y + 22, 130, 14, `${action}:`, 10, C.gray500));
    els.push(text(`af_tg_${i}`, 496 + action.length * 6 + 6, y + 22, 180, 14, target, 10, atc[type]));
    els.push(text(`af_tm_${i}`, 732, y + 4, 100, 16, time, 10, C.gray500, 'right'));
  });

  return els;
}

export function buildPermissionMatrix(y0) {
  const els = [...sectionHeader(28, 'Permission Matrix', y0)];
  const yC = y0 + 48;

  const roles = ['Admin', 'Editor', 'Viewer', 'Guest'];
  const lw = 190, rw = 120, hH = 48, rH = 38;
  const tW = lw + roles.length * rw;

  // Header
  els.push(rect('pm_hdr', 50, yC, tW, hH, C.gray300, C.gray100, 1, false));
  roles.forEach((role, ri) => {
    const x = 50 + lw + ri * rw;
    els.push(vline(`pm_hvl_${ri}`, x, yC, hH, C.gray300));
    els.push(text(`pm_rl_${ri}`, x, yC + hH / 2 - 9, rw, 18, role, 13, C.gray900, 'center'));
  });
  els.push(hline('pm_hdiv', 50, yC + hH, tW, C.gray300, 2));

  const groups = [
    { label: 'Data Models', rows: [
      { name: 'View records',   checks: [true, true,  true,  false] },
      { name: 'Create records', checks: [true, true,  false, false] },
      { name: 'Delete records', checks: [true, false, false, false] },
    ]},
    { label: 'Workflows', rows: [
      { name: 'View workflows', checks: [true, true,  true,  false] },
      { name: 'Run workflows',  checks: [true, true,  false, false] },
      { name: 'Edit workflows', checks: [true, false, false, false] },
    ]},
    { label: 'Settings', rows: [
      { name: 'View settings',  checks: [true, false, false, false] },
      { name: 'Edit settings',  checks: [true, false, false, false] },
    ]},
  ];

  let gy = yC + hH;
  groups.forEach(({ label, rows }, gi) => {
    // Group header
    els.push(rect(`pm_gh_${gi}`, 50, gy, tW, 34, C.gray300, C.gray50, 1, false));
    els.push(text(`pm_ghl_${gi}`, 62, gy + 10, lw - 12, 16, label, 12, C.gray700));
    roles.forEach((_, ri) => els.push(vline(`pm_gvl_${gi}_${ri}`, 50 + lw + ri * rw, gy, 34, C.gray300)));
    els.push(hline(`pm_gh_div_${gi}`, 50, gy + 34, tW, C.gray300));
    gy += 34;

    rows.forEach(({ name, checks }, pi) => {
      els.push(rect(`pm_row_${gi}_${pi}`, 50, gy, tW, rH, 'transparent', pi % 2 === 0 ? C.white : C.gray50, 0, false));
      els.push(text(`pm_pn_${gi}_${pi}`, 62, gy + 11, lw - 12, 16, name, 12, C.gray700));
      roles.forEach((_, ri) => {
        els.push(vline(`pm_pvl_${gi}_${pi}_${ri}`, 50 + lw + ri * rw, gy, rH, C.gray300));
        const bx = 50 + lw + ri * rw + rw / 2 - 9, by = gy + rH / 2 - 9;
        els.push(rect(`pm_chk_${gi}_${pi}_${ri}`, bx, by, 18, 18, checks[ri] ? C.primary : C.gray300, checks[ri] ? C.primary : C.white, 1, false));
        if (checks[ri]) els.push(text(`pm_chkt_${gi}_${pi}_${ri}`, bx, by + 1, 18, 16, '✓', 11, C.white, 'center'));
      });
      els.push(hline(`pm_pdiv_${gi}_${pi}`, 50, gy + rH, tW, C.gray300));
      gy += rH;
    });
    gy += 4;
  });

  // Save button
  els.push(rect('pm_save', 50 + tW - 130, gy + 10, 130, 36, C.accentDark, C.accent, 2, true));
  els.push(text('pm_save_t', 50 + tW - 130, gy + 18, 130, 18, 'Save Permissions', 12, C.white, 'center'));

  return els;
}

export function buildDropdownContextMenu(y0) {
  const els = [...sectionHeader(25, 'Dropdown & Context Menu', y0)];
  const yC = y0 + 68;

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

export function buildDragDrop(y0) {
  const els = [...sectionHeader(26, 'Drag & Drop / Sortable', y0)];
  const yC = y0 + 68;

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

export function buildWorkflowCanvas(y0) {
  const els = [...sectionHeader(30, 'Workflow Canvas', y0)];
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

export function buildBuilderLayout(y0) {
  const els = [...sectionHeader(31, 'Builder Layout', y0)];
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

export function buildExecutionTimeline(y0) {
  const els = [...sectionHeader(32, 'Execution Timeline', y0)];
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

export function buildUtilities(y0) {
  const els = [...sectionHeader(27, 'Utilities', y0)];
  const yC = y0 + 68;

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
  const yS = yC + 220;
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

export function buildRichTextEditor(y0) {
  const els = [...sectionHeader(7, 'Rich Text Editor', y0)];
  const yC = y0 + 48;

  // ── Toolbar ──
  els.push(rect('rte_toolbar', 50, yC, 700, 36, C.gray300, C.gray50, 1, true));
  const toolbarItems = [
    { label: 'B', active: true,  w: 28 },
    { label: 'I', active: false, w: 28 },
    { label: 'U', active: false, w: 28 },
    { sep: true },
    { label: 'H1', active: false, w: 32 },
    { label: 'H2', active: false, w: 32 },
    { sep: true },
    { label: '☰', active: false, w: 28 },
    { label: '≡', active: false, w: 28 },
    { sep: true },
    { label: '🔗', active: false, w: 28 },
    { label: '⌘ Variable', active: false, w: 90 },
  ];
  let tx = 62;
  toolbarItems.forEach((item, i) => {
    if (item.sep) {
      els.push(vline(`rte_sep_${i}`, tx, yC + 6, 24, C.gray300));
      tx += 12;
    } else {
      const bg = item.active ? C.infoBg : 'transparent';
      const stroke = item.active ? C.primary : C.gray300;
      const tc = item.active ? C.primary : C.gray700;
      els.push(rect(`rte_tb_${i}`, tx, yC + 4, item.w, 28, stroke, bg, 1, true));
      els.push(text(`rte_tb_t_${i}`, tx, yC + 10, item.w, 16, item.label, 11, tc, 'center'));
      tx += item.w + 4;
    }
  });

  // ── Content area ──
  const yBody = yC + 44;
  els.push(rect('rte_body', 50, yBody, 700, 220, C.gray300, C.white, 1, false));
  els.push(text('rte_h',  62, yBody + 14, 400, 26, 'Welcome to {{workflow_name}}', 18, C.gray900));
  els.push(text('rte_p1', 62, yBody + 52, 676, 18, 'Hi {{user_name}}, your request has been submitted successfully.', 13, C.gray700));
  els.push(text('rte_p2', 62, yBody + 74, 676, 18, 'Reference: {{record_id}}  ·  Status: {{status}}', 13, C.gray700));
  els.push(text('rte_p3', 62, yBody + 114, 550, 18, 'Please review the details below and take action.', 13, C.gray700));
  els.push(rect('rte_var_bg', 62, yBody + 152, 138, 22, C.infoBorder, C.infoBg, 1, true));
  els.push(text('rte_var_t', 66, yBody + 155, 132, 16, '⌘ {{action_button}}', 12, C.primary));
  els.push(vline('rte_cursor', 206, yBody + 152, 22, C.primary, 2));

  // ── Footer / word count ──
  const yFoot = yBody + 228;
  els.push(rect('rte_foot', 50, yFoot, 700, 28, C.gray300, C.gray50, 1, false));
  els.push(text('rte_wc', 620, yFoot + 7, 120, 14, '87 words', 11, C.gray500, 'right'));

  return els;
}

export function buildCodeEditor(y0) {
  const els = [...sectionHeader(8, 'Code / JSON Editor', y0)];
  const yC = y0 + 68;
  els.push(text('ce_json_lbl', 50,  y0 + 46, 120, 14, 'JSON Editor',   11, C.gray500));
  els.push(text('ce_http_lbl', 510, y0 + 46, 140, 14, 'HTTP Request',  11, C.gray500));

  // ── JSON editor panel (dark theme) ──
  const editorW = 440;
  els.push(rect('ce_panel', 50, yC, editorW, 240, '#374151', '#1E2228', 1, false));
  const codeLines = [
    ['{',                               '#D4D4D4'],
    ['  "id": "rec_01hxyz…",',          '#9CDCFE'],
    ['  "status": "pending",',           '#9CDCFE'],
    ['  "amount": 14500,',               '#9CDCFE'],
    ['  "fields": {',                    '#D4D4D4'],
    ['    "name": "Acme Corp",',         '#CE9178'],
    ['    "owner": "{{current_user}}"',  '#CE9178'],
    ['  },',                             '#D4D4D4'],
    ['}',                                '#D4D4D4'],
  ];
  codeLines.forEach(([line, color], i) => {
    const y = yC + 14 + i * 24;
    els.push(text(`ce_ln_${i}`,   58, y, 20, 16, `${i + 1}`, 11, '#6B7280', 'right'));
    els.push(text(`ce_code_${i}`, 84, y, editorW - 40, 16, line, 12, color));
  });

  // ── HTTP request configurator ──
  const rx = 510;
  const rW = 290;

  els.push(rect('ce_method',   rx,      yC, 64,      36, C.accentDark, C.accent, 2, true));
  els.push(text('ce_method_t', rx,      yC + 10, 64, 16, 'POST', 13, C.white, 'center'));
  els.push(rect('ce_url',      rx + 68, yC, rW - 68, 36, C.gray300, C.gray50, 1, true));
  els.push(text('ce_url_t',    rx + 74, yC + 10, rW - 80, 16, '/api/workflows/{{id}}/run', 12, C.gray700));

  const tabs = ['Body', 'Headers', 'Params'];
  tabs.forEach((tab, i) => {
    const active = i === 0;
    const tabX = rx + i * 100;
    els.push(rect(`ce_tab_${i}`,   tabX, yC + 44, 92, 28, active ? C.primary : C.gray300, active ? C.infoBg : 'transparent', active ? 2 : 1, true));
    els.push(text(`ce_tab_t_${i}`, tabX, yC + 52, 92, 16, tab, 12, active ? C.primary : C.gray500, 'center'));
  });

  const yBodyP = yC + 80;
  els.push(rect('ce_body_panel', rx, yBodyP, rW, 152, C.gray300, C.gray50, 1, false));
  const bodyLines = ['{', '  "trigger": "manual",', '  "input": {', '    "record_id": "{{id}}"', '  }', '}'];
  bodyLines.forEach((line, i) => {
    els.push(text(`ce_bline_${i}`, rx + 10, yBodyP + 10 + i * 22, rW - 20, 16, line, 12, C.gray700));
  });

  return els;
}

export function buildSideSheet(y0) {
  const els = [...sectionHeader(21, 'Side Sheet / Drawer', y0)];
  const yC = y0 + 48;

  // ── Dimmed app context (left) ──
  els.push(rect('ss_app', 50, yC, 310, 360, C.gray300, C.gray100, 1, false));
  [[62,14,200,12],[62,44,260,8],[62,62,220,8],[62,80,240,8],[62,108,260,8],[62,126,200,8]].forEach(([x,dy,w,h], i) =>
    els.push(rect(`ss_bg_${i}`, x, yC + dy, w, h, 'transparent', C.gray300, 0, true)));

  // ── Sheet panel ──
  const sx = 360, sw = 300;
  els.push(rect('ss_panel', sx, yC, sw, 360, C.gray300, C.white, 2, false));

  // Header
  els.push(rect('ss_hdr', sx, yC, sw, 50, C.gray300, C.gray50, 1, false, { roundness: null }));
  els.push(text('ss_title', sx + 16, yC + 15, sw - 50, 20, 'Record Detail', 14, C.gray900));
  els.push(text('ss_close', sx + sw - 26, yC + 13, 20, 24, '×', 18, C.gray500));
  els.push(hline('ss_hdr_line', sx, yC + 50, sw, C.gray300));

  // Body fields
  const fields = [
    ['Name',    'Acme Corporation'],
    ['Status',  'Active'],
    ['Owner',   'Jane Smith'],
    ['Created', '2026-01-15'],
  ];
  fields.forEach(([label, value], i) => {
    const y = yC + 60 + i * 58;
    els.push(text(`ss_lbl_${i}`, sx + 16, y, 100, 13, label, 11, C.gray500));
    els.push(rect(`ss_inp_${i}`, sx + 16, y + 16, sw - 32, 32, C.gray300, C.gray50, 1, true));
    els.push(text(`ss_val_${i}`, sx + 26, y + 24, sw - 52, 16, value, 12, C.gray700));
  });

  // Sticky footer
  const yFt = yC + 308;
  els.push(hline('ss_foot_line', sx, yFt, sw, C.gray300));
  els.push(rect('ss_save',     sx + 16,       yFt + 14, 110, 32, C.accentDark, C.accent,      2, true));
  els.push(text('ss_save_t',   sx + 16,       yFt + 22, 110, 16, 'Save changes', 12, C.white,  'center'));
  els.push(rect('ss_cancel',   sx + 134,      yFt + 14,  80, 32, C.gray300, 'transparent',    1, true));
  els.push(text('ss_cancel_t', sx + 134,      yFt + 22,  80, 16, 'Cancel',       12, C.gray500, 'center'));
  els.push(rect('ss_del',      sx + sw - 74,  yFt + 14,  58, 32, C.dangerDark, C.dangerBg,    1, true));
  els.push(text('ss_del_t',    sx + sw - 74,  yFt + 22,  58, 16, 'Delete',       12, C.danger,  'center'));

  // Resize handle on left edge
  els.push(rect('ss_handle', sx - 5, yC + 155, 10, 50, C.gray300, C.gray300, 1, true));

  return els;
}

export function buildEmptyStates(y0) {
  const els = [...sectionHeader(13, 'Empty States', y0)];
  const yC = y0 + 48;

  const states = [
    { id: 'es_list', x: 50,  label: 'List / Table',  icon: '□',  title: 'No records yet',       msg: 'Create your first record.',   cta: '+ New Record' },
    { id: 'es_srch', x: 265, label: 'No Results',     icon: '◎',  title: 'No results found',     msg: 'Try different search terms.', cta: null },
    { id: 'es_feed', x: 480, label: 'Feed / Inbox',   icon: '⬡',  title: 'All caught up',        msg: 'No new notifications.',       cta: null },
    { id: 'es_err',  x: 695, label: 'Error',          icon: '⚠',  title: 'Something went wrong', msg: 'Failed to load. Try again.',  cta: 'Retry' },
  ];

  states.forEach(({ id, x, label, icon, title, msg, cta }) => {
    els.push(text(`${id}_sub`,   x, yC,      190, 14, label, 11, C.gray500));
    const yCard = yC + 18;
    els.push(rect(`${id}_card`,  x, yCard, 190, 156, C.gray300, C.gray50, 1, true));
    els.push(ellipse(`${id}_ico_bg`, x + 70, yCard + 20, 50, 50, C.gray300, C.gray100, 1));
    els.push(text(`${id}_ico`,   x + 70, yCard + 32, 50, 26, icon, 18, C.gray500, 'center'));
    els.push(text(`${id}_title`, x + 10, yCard + 82, 170, 18, title, 13, C.gray900, 'center'));
    els.push(text(`${id}_msg`,   x + 10, yCard + 103, 170, 28, msg,  11, C.gray500, 'center'));
    if (cta) {
      els.push(rect(`${id}_btn`,   x + 35, yCard + 130, 120, 22, C.accentDark, C.accent, 2, true));
      els.push(text(`${id}_btn_t`, x + 35, yCard + 134, 120, 14, cta, 11, C.white, 'center'));
    }
  });

  return els;
}

export function buildSkeletonLoaders(y0) {
  const els = [...sectionHeader(14, 'Skeleton Loaders', y0)];
  const yC = y0 + 68;
  els.push(text('sk_tbl_lbl',  50,  y0 + 46, 100, 14, 'Table Rows', 11, C.gray500));
  els.push(text('sk_card_lbl', 390, y0 + 46, 60,  14, 'Card',       11, C.gray500));
  els.push(text('sk_form_lbl', 630, y0 + 46, 80,  14, 'Form Panel', 11, C.gray500));

  // ── Table skeleton (header + 3 rows) ──
  const colDefs = [24, 100, 62, 46, 20];
  for (let row = 0; row < 4; row++) {
    const y = yC + row * 42;
    const isHeader = row === 0;
    els.push(rect(`sk_tbl_row_${row}`, 50, y, 300, 38, C.gray300, isHeader ? C.gray50 : C.white, 1, false));
    let cx = 58;
    colDefs.forEach((w, ci) => {
      const bh = 8;
      els.push(rect(`sk_tbl_bar_${row}_${ci}`, cx, y + 15, isHeader ? Math.round(w * 0.65) : w, bh, 'transparent', C.gray300, 0, true));
      cx += w + 8;
    });
    els.push(hline(`sk_tbl_div_${row}`, 50, y + 38, 300, C.gray300));
  }

  // ── Card skeleton ──
  els.push(rect('sk_card', 390, yC, 200, 200, C.gray300, C.white, 1, true));
  els.push(rect('sk_card_img', 391, yC + 1, 198, 78, 'transparent', C.gray300, 0, false, { roundness: null }));
  els.push(rect('sk_card_t1', 402, yC + 94,  140, 10, 'transparent', C.gray300, 0, true));
  els.push(rect('sk_card_t2', 402, yC + 112, 100, 9,  'transparent', C.gray300, 0, true));
  els.push(rect('sk_card_t3', 402, yC + 136, 176, 9,  'transparent', C.gray300, 0, true));
  els.push(rect('sk_card_t4', 402, yC + 152, 140, 9,  'transparent', C.gray300, 0, true));
  els.push(rect('sk_card_btn',402, yC + 174, 76,  18, C.gray300, C.gray300, 1, true));

  // ── Form panel skeleton ──
  els.push(rect('sk_form', 630, yC, 240, 200, C.gray300, C.white, 1, true));
  const formBars = [
    [12, 14,  80,  8 ],
    [12, 28,  216, 28],
    [12, 70,  60,  8 ],
    [12, 84,  216, 28],
    [12, 126, 70,  8 ],
    [12, 140, 216, 28],
  ];
  formBars.forEach(([dx, dy, w, h], i) => {
    els.push(rect(`sk_form_bar_${i}`, 630 + dx, yC + dy, w, h, 'transparent', C.gray300, 0, true));
  });

  return els;
}

export function buildTabs(y0) {
  const els = [...sectionHeader(17, 'Tabs', y0)];
  const yC = y0 + 68;
  els.push(text('tab_ul_lbl',   50,  y0 + 46, 160, 14, 'Underline (default)', 11, C.gray500));
  els.push(text('tab_pill_lbl', 510, y0 + 46, 120, 14, 'Pill / Segment',      11, C.gray500));

  // ── Underline tabs ──
  const ulLabels = ['Overview', 'Fields', 'Relations', 'History'];
  els.push(hline('tab_ul_base', 50, yC + 36, 430, C.gray300));
  ulLabels.forEach((label, i) => {
    const active = i === 0;
    const x = 50 + i * 108;
    els.push(text(`tab_ul_t_${i}`, x, yC + 12, 100, 20, label, 13, active ? C.primary : C.gray500, 'center'));
    if (active) els.push(hline(`tab_ul_ind_${i}`, x + 4, yC + 36, 92, C.primary, 2));
  });
  els.push(rect('tab_ul_panel', 50, yC + 44, 430, 52, C.gray300, C.gray50, 1, false));
  els.push(text('tab_ul_panel_t', 62, yC + 58, 300, 16, 'Tab panel content renders here', 12, C.gray300));

  // ── Pill tabs ──
  const pillLabels = ['All', 'Active', 'Draft', 'Archived'];
  els.push(rect('tab_pill_wrap', 510, yC, 300, 36, C.gray300, C.gray100, 1, true));
  pillLabels.forEach((label, i) => {
    const active = i === 0;
    const x = 514 + i * 72;
    if (active) els.push(rect(`tab_pill_bg_${i}`, x, yC + 4, 68, 28, C.gray300, C.white, 1, true));
    els.push(text(`tab_pill_t_${i}`, x, yC + 10, 68, 16, label, 12, active ? C.gray900 : C.gray500, 'center'));
  });

  // ── Tabs with badge counts ──
  const yBadge = yC + 56;
  const badgeTabs = [['Inbox', '3'], ['Sent', ''], ['Archived', '12']];
  els.push(hline('tab_badge_base', 510, yBadge + 36, 300, C.gray300));
  badgeTabs.forEach(([label, count], i) => {
    const active = i === 0;
    const x = 510 + i * 100;
    els.push(text(`tab_badge_t_${i}`, x, yBadge + 12, 88, 18, label, 13, active ? C.primary : C.gray500, 'center'));
    if (count) {
      els.push(rect(`tab_badge_bg_${i}`, x + 58, yBadge + 15, 22, 14, active ? C.primary : C.gray300, active ? C.infoBg : C.gray100, 1, true));
      els.push(text(`tab_badge_n_${i}`, x + 58, yBadge + 17, 22, 12, count, 10, active ? C.primary : C.gray500, 'center'));
    }
    if (active) els.push(hline(`tab_badge_ind_${i}`, x + 4, yBadge + 36, 80, C.primary, 2));
  });

  return els;
}

export function buildTooltipPopover(y0) {
  const els = [...sectionHeader(24, 'Tooltip & Popover', y0)];
  const yC = y0 + 68;
  els.push(text('ttp_tt_lbl',  50,  y0 + 46, 80,  14, 'Tooltip',           11, C.gray500));
  els.push(text('ttp_pop_lbl', 310, y0 + 46, 80,  14, 'Popover',           11, C.gray500));
  els.push(text('ttp_cnf_lbl', 610, y0 + 46, 160, 14, 'Confirm / Destruct', 11, C.gray500));

  // ── Tooltip (dark + light) ──
  els.push(rect('ttp_dark_box', 50, yC, 180, 28, C.gray900, C.gray900, 1, true));
  els.push(text('ttp_dark_t',   50, yC + 7, 180, 14, 'Publish to all tenants', 11, C.white, 'center'));
  els.push(rect('ttp_dark_arr', 127, yC + 26, 8, 8, C.gray900, C.gray900, 0, false));
  els.push(rect('ttp_dark_btn', 80, yC + 44, 100, 30, C.gray300, C.gray50, 1, true));
  els.push(text('ttp_dark_btn_t', 80, yC + 52, 100, 14, '? Help', 12, C.gray700, 'center'));

  els.push(rect('ttp_light_btn', 80, yC + 100, 100, 18, C.gray300, C.gray50, 1, true));
  els.push(rect('ttp_light_arr', 127, yC + 116, 8, 8, C.gray300, C.white, 1, false));
  els.push(rect('ttp_light_box', 50, yC + 122, 180, 26, C.gray300, C.white, 1, true));
  els.push(text('ttp_light_t',   50, yC + 129, 180, 14, 'Last updated 3 min ago', 11, C.gray700, 'center'));

  // ── Popover (config panel) ──
  els.push(rect('ttp_pop_card', 310, yC, 260, 186, C.gray300, C.white, 2, true));
  els.push(text('ttp_pop_title', 326, yC + 16, 200, 18, 'Configure step', 14, C.gray900));
  els.push(hline('ttp_pop_div', 310, yC + 42, 260, C.gray300));
  els.push(text('ttp_pop_lbl1', 326, yC + 54, 80, 13, 'Timeout (s)', 11, C.gray500));
  els.push(rect('ttp_pop_inp1', 326, yC + 70, 228, 32, C.gray300, C.gray50, 1, true));
  els.push(text('ttp_pop_ph1',  334, yC + 80, 150, 14, '30', 12, C.gray300));
  els.push(text('ttp_pop_lbl2', 326, yC + 116, 100, 13, 'Retry on fail', 11, C.gray500));
  els.push(rect('ttp_pop_chk',  326, yC + 132, 16, 16, C.primary, C.infoBg, 2, true));
  els.push(text('ttp_pop_chk_t',348, yC + 133, 180, 13, 'Enable automatic retry', 12, C.gray700));
  els.push(rect('ttp_pop_save',  390, yC + 160, 76, 20, C.accentDark, C.accent, 2, true));
  els.push(text('ttp_pop_save_t',390, yC + 164, 76, 12, 'Save', 11, C.white, 'center'));
  els.push(rect('ttp_pop_cancel',472, yC + 160, 60, 20, C.gray300, 'transparent', 1, true));
  els.push(text('ttp_pop_cancel_t', 472, yC + 164, 60, 12, 'Cancel', 11, C.gray500, 'center'));

  // ── Destructive confirm popover ──
  els.push(rect('ttp_cnf_card',  610, yC, 240, 128, C.gray300, C.white, 2, true));
  els.push(text('ttp_cnf_title', 626, yC + 16, 200, 18, 'Delete record?', 14, C.gray900));
  els.push(text('ttp_cnf_msg',   626, yC + 38, 200, 32, 'This action cannot be undone. All data will be removed.', 11, C.gray500));
  els.push(rect('ttp_cnf_del',   626, yC + 96, 88, 22, C.dangerDark, C.danger, 2, true));
  els.push(text('ttp_cnf_del_t', 626, yC + 101, 88, 12, 'Delete', 12, C.white, 'center'));
  els.push(rect('ttp_cnf_no',    724, yC + 96, 66, 22, C.gray300, 'transparent', 1, true));
  els.push(text('ttp_cnf_no_t',  724, yC + 101, 66, 12, 'Cancel', 12, C.gray500, 'center'));

  return els;
}

export function buildFieldTypePicker(y0) {
  const els = [...sectionHeader(33, 'Field Type Picker', y0)];
  const yC = y0 + 48;

  const types = [
    ['Text',        '𝐓',  false],
    ['Long Text',   '¶',  false],
    ['Number',      '#',  false],
    ['Currency',    '$',  true ],
    ['Date',        '📅', false],
    ['DateTime',    '⏱',  false],
    ['Boolean',     '◎',  false],
    ['Select',      '▾',  false],
    ['Multi-select','▾▾', false],
    ['Relation',    '⇄',  false],
    ['File',        '📎', false],
    ['Formula',     'ƒ',  false],
    ['User',        '👤', false],
    ['Email',       '@',  false],
    ['URL',         '🔗', false],
  ];

  const cellW = 140, cellH = 54, cols = 5;
  types.forEach(([label, icon, active], i) => {
    const col = i % cols;
    const row = Math.floor(i / cols);
    const x = 50 + col * (cellW + 8);
    const y = yC + row * (cellH + 8);
    const stroke = active ? C.primary : C.gray300;
    const bg     = active ? C.infoBg  : C.white;
    els.push(rect(`ftp_cell_${i}`, x, y, cellW, cellH, stroke, bg, active ? 2 : 1, true));
    els.push(text(`ftp_ico_${i}`,  x + 12, y + 14, 22, 22, icon,  15, active ? C.primary : C.gray500));
    els.push(text(`ftp_lbl_${i}`,  x + 38, y + 17, cellW - 48, 20, label, 13, active ? C.primary : C.gray700));
  });

  return els;
}

export function buildRelationLookup(y0) {
  const els = [...sectionHeader(34, 'Relation / Lookup Field', y0)];
  const yC = y0 + 68;
  els.push(text('rel_view_lbl', 50,  y0 + 46, 140, 14, 'Field (view mode)',   11, C.gray500));
  els.push(text('rel_pop_lbl',  370, y0 + 46, 120, 14, 'Lookup Popup',        11, C.gray500));
  els.push(text('rel_cell_lbl', 720, y0 + 46, 180, 14, 'Table Cell (multi)',  11, C.gray500));

  // ── Relation field in view mode ──
  els.push(rect('rel_inp', 50, yC, 290, 40, C.gray300, C.white, 1, true));
  let cx = 58;
  ['Acme Corp', 'TechFlow Ltd'].forEach((rec, i) => {
    const w = rec.length * 7 + 26;
    els.push(rect(`rel_chip_${i}`,   cx, yC + 10, w, 22, C.primary, C.infoBg, 1, true));
    els.push(text(`rel_chip_t_${i}`, cx + 6, yC + 13, w - 20, 14, rec, 11, C.primary));
    els.push(text(`rel_chip_x_${i}`, cx + w - 14, yC + 14, 10, 12, '×', 10, C.primary));
    cx += w + 6;
  });
  els.push(text('rel_add', cx + 2, yC + 13, 50, 14, '+ Add', 11, C.gray300));

  // ── Lookup popup ──
  els.push(rect('rel_pop', 370, yC, 310, 244, C.gray300, C.white, 2, true));
  els.push(rect('rel_pop_srch', 380, yC + 10, 290, 32, C.gray300, C.gray50, 1, true));
  els.push(text('rel_pop_srch_t', 390, yC + 18, 200, 16, '🔍  Search records…', 12, C.gray300));
  const popRows = [
    ['Acme Corp',      true ],
    ['TechFlow Ltd',   true ],
    ['Bright Systems', false],
    ['Nova Partners',  false],
    ['Vertex Inc.',    false],
  ];
  popRows.forEach(([name, checked], i) => {
    const y = yC + 52 + i * 36;
    const hover = i === 2;
    if (hover) els.push(rect(`rel_pop_hover_${i}`, 374, y, 302, 32, 'transparent', C.gray50, 0, false));
    els.push(rect(`rel_pop_chk_${i}`,   380, y + 8, 16, 16, checked ? C.primary : C.gray300, checked ? C.infoBg : C.white, checked ? 2 : 1, true));
    if (checked) els.push(text(`rel_pop_chk_t_${i}`, 380, y + 8, 16, 16, '✓', 10, C.primary, 'center'));
    els.push(text(`rel_pop_name_${i}`, 404, y + 9, 260, 16, name, 12, C.gray900));
  });

  // ── Table cell with multi-relation chips ──
  els.push(rect('rel_cell', 720, yC, 240, 100, C.gray300, C.white, 1, false));
  els.push(rect('rel_cell_hdr', 720, yC, 240, 32, C.gray300, C.gray50, 1, false, { roundness: null }));
  els.push(text('rel_cell_hdr_t', 732, yC + 9, 180, 16, 'Related Accounts', 12, C.gray700));
  let tcx = 730;
  ['Acme Corp', 'TechFlow'].forEach((chip, i) => {
    const w = chip.length * 7 + 20;
    els.push(rect(`rel_cell_chip_${i}`,   tcx, yC + 40, w, 22, C.primary, C.infoBg, 1, true));
    els.push(text(`rel_cell_chip_t_${i}`, tcx + 6, yC + 43, w - 8, 14, chip, 11, C.primary));
    tcx += w + 6;
  });
  els.push(text('rel_cell_more', tcx + 2, yC + 43, 30, 14, '+2', 11, C.gray500));

  return els;
}


export function buildStatsCards(y0) {
  const els = [...sectionHeader(35, 'Dashboard & Analytics Stats', y0)];
  const yC = y0 + 48;

  // Basic Stat Card
  els.push(rect('stat_basic', 50, yC, 240, 110, C.gray300, C.white, 1, true));
  els.push(text('stat_basic_lbl', 66, yC + 16, 200, 14, 'Total Workflows', 12, C.gray500));
  els.push(text('stat_basic_val', 66, yC + 40, 200, 36, '1,248', 28, C.gray900));
  els.push(text('stat_basic_sub', 66, yC + 80, 200, 14, '+12% from last month', 11, C.success));

  // Stat Card with Mini Chart/Trend
  els.push(rect('stat_trend', 310, yC, 280, 110, C.gray300, C.white, 1, true));
  els.push(text('stat_trend_lbl', 326, yC + 16, 200, 14, 'Active Executions', 12, C.gray500));
  els.push(text('stat_trend_val', 326, yC + 40, 100, 36, '42', 28, C.gray900));

  // Mini line chart (trend)
  els.push(hline('stat_trend_chart_base', 460, yC + 76, 100, C.gray100, 1));
  const s = nextSeed();
  els.push({
    ...({ type: 'line', strokeColor: C.primary, backgroundColor: 'transparent', fillStyle: 'solid', strokeWidth: 2, strokeStyle: 'solid', roughness: 1, roundness: { type: 2 }, seed: s, versionNonce: s + 1, lastCommittedPoint: null, startBinding: null, endBinding: null, startArrowhead: null, endArrowhead: null }),
    id: 'stat_trend_line',
    x: 460,
    y: yC + 60,
    width: 100,
    height: 30,
    points: [[0, 10], [20, 20], [40, 5], [60, 15], [80, -5], [100, -10]]
  });

  els.push(text('stat_trend_sub', 326, yC + 80, 100, 14, 'Live view', 11, C.gray500));

  // Stat Card with Progress
  els.push(rect('stat_prog', 610, yC, 240, 110, C.gray300, C.white, 1, true));
  els.push(text('stat_prog_lbl', 626, yC + 16, 200, 14, 'Storage Used', 12, C.gray500));
  els.push(text('stat_prog_val', 626, yC + 36, 200, 24, '45.2 GB', 18, C.gray900));
  els.push(text('stat_prog_max', 770, yC + 44, 60, 14, '/ 100 GB', 11, C.gray500));

  els.push(rect('stat_prog_track', 626, yC + 76, 208, 8, 'transparent', C.gray100, 0, true, { roundness: { type: 3 } }));
  els.push(rect('stat_prog_fill',  626, yC + 76, 94,  8, 'transparent', C.warning, 0, true, { roundness: { type: 3 } }));

  return els;
}


export function buildAdvancedFilters(y0) {
  const els = [...sectionHeader(36, 'Advanced Filters / Query Builder', y0)];
  const yC = y0 + 48;

  els.push(rect('adv_filt_panel', 50, yC, 600, 200, C.gray300, C.white, 1, false));

  // Header
  els.push(rect('adv_filt_hdr', 50, yC, 600, 40, 'transparent', C.gray50, 0, false));
  els.push(hline('adv_filt_div_top', 50, yC + 40, 600, C.gray300));
  els.push(text('adv_filt_title', 66, yC + 10, 200, 18, 'Filters', 14, C.gray900));
  els.push(text('adv_filt_match', 350, yC + 12, 200, 14, 'Match', 12, C.gray700));
  els.push(rect('adv_filt_op_sel', 396, yC + 6, 80, 28, C.gray300, C.white, 1, true));
  els.push(text('adv_filt_op_val', 404, yC + 12, 50, 14, 'All', 12, C.gray900));
  els.push(text('adv_filt_op_arr', 456, yC + 11, 14, 14, '▾', 12, C.gray700));
  els.push(text('adv_filt_rules', 484, yC + 12, 100, 14, 'of the following rules:', 12, C.gray700));

  // Rule 1
  const r1y = yC + 56;
  els.push(rect('adv_filt_r1_fld', 66, r1y, 140, 32, C.gray300, C.white, 1, true));
  els.push(text('adv_filt_r1_fld_t', 74, r1y + 8, 100, 14, 'Status', 12, C.gray900));
  els.push(text('adv_filt_r1_fld_a', 186, r1y + 8, 14, 14, '▾', 12, C.gray700));

  els.push(rect('adv_filt_r1_op', 214, r1y, 120, 32, C.gray300, C.white, 1, true));
  els.push(text('adv_filt_r1_op_t', 222, r1y + 8, 80, 14, 'is equal to', 12, C.gray900));
  els.push(text('adv_filt_r1_op_a', 314, r1y + 8, 14, 14, '▾', 12, C.gray700));

  els.push(rect('adv_filt_r1_val', 342, r1y, 160, 32, C.gray300, C.white, 1, true));
  els.push(text('adv_filt_r1_val_t', 350, r1y + 8, 120, 14, 'Active', 12, C.gray900));
  els.push(text('adv_filt_r1_val_a', 482, r1y + 8, 14, 14, '▾', 12, C.gray700));

  els.push(text('adv_filt_r1_del', 510, r1y + 8, 20, 14, '×', 16, C.gray500));

  // Rule 2
  const r2y = yC + 96;
  els.push(rect('adv_filt_r2_fld', 66, r2y, 140, 32, C.gray300, C.white, 1, true));
  els.push(text('adv_filt_r2_fld_t', 74, r2y + 8, 100, 14, 'Created Date', 12, C.gray900));
  els.push(text('adv_filt_r2_fld_a', 186, r2y + 8, 14, 14, '▾', 12, C.gray700));

  els.push(rect('adv_filt_r2_op', 214, r2y, 120, 32, C.gray300, C.white, 1, true));
  els.push(text('adv_filt_r2_op_t', 222, r2y + 8, 80, 14, 'is after', 12, C.gray900));
  els.push(text('adv_filt_r2_op_a', 314, r2y + 8, 14, 14, '▾', 12, C.gray700));

  els.push(rect('adv_filt_r2_val', 342, r2y, 160, 32, C.gray300, C.white, 1, true));
  els.push(text('adv_filt_r2_val_t', 350, r2y + 8, 120, 14, '2023-01-01', 12, C.gray900));
  els.push(text('adv_filt_r2_val_i', 480, r2y + 8, 14, 14, '📅', 10, C.gray700));

  els.push(text('adv_filt_r2_del', 510, r2y + 8, 20, 14, '×', 16, C.gray500));

  // Footer / Add Rule
  els.push(hline('adv_filt_div_bot', 50, yC + 144, 600, C.gray300));
  els.push(text('adv_filt_add_rule', 66, yC + 160, 100, 14, '+ Add Rule', 12, C.primary));
  els.push(text('adv_filt_add_grp', 170, yC + 160, 100, 14, '+ Add Group', 12, C.primary));

  // Apply button
  els.push(rect('adv_filt_apply', 540, yC + 154, 80, 28, C.accentDark, C.accent, 2, true));
  els.push(text('adv_filt_apply_t', 540, yC + 161, 80, 14, 'Apply', 11, C.white, 'center'));

  return els;
}


export function buildDualListbox(y0) {
  const els = [...sectionHeader(37, 'Dual Listbox / Transfer List', y0)];
  const yC = y0 + 48;

  // Left List (Available)
  els.push(rect('dual_left_panel', 50, yC, 240, 260, C.gray300, C.white, 1, false));
  els.push(rect('dual_left_hdr', 50, yC, 240, 40, 'transparent', C.gray50, 0, false));
  els.push(hline('dual_left_div1', 50, yC + 40, 240, C.gray300));
  els.push(text('dual_left_title', 62, yC + 10, 160, 18, 'Available Permissions', 12, C.gray900));
  els.push(text('dual_left_count', 240, yC + 12, 40, 14, '12', 11, C.gray500, 'right'));

  // Search
  els.push(rect('dual_left_search', 58, yC + 48, 224, 32, C.gray300, C.white, 1, true));
  els.push(text('dual_left_search_t', 66, yC + 56, 160, 16, '⌕ Search...', 12, C.gray500));
  els.push(hline('dual_left_div2', 50, yC + 88, 240, C.gray300));

  // Left Items
  const leftItems = ['users:read', 'users:write', 'users:delete', 'roles:read', 'roles:write'];
  leftItems.forEach((item, i) => {
    const iy = yC + 88 + i * 32;
    if (i === 1 || i === 3) {
      // Selected state
      els.push(rect(`dual_left_sel_${i}`, 50, iy, 240, 32, 'transparent', C.infoBg, 0, false));
      els.push(rect(`dual_left_chk_${i}`, 62, iy + 8, 16, 16, C.primary, C.primary, 1, true));
      els.push(text(`dual_left_chkt_${i}`, 62, iy + 8, 16, 16, '✓', 11, C.white, 'center'));
    } else {
      els.push(rect(`dual_left_chk_${i}`, 62, iy + 8, 16, 16, C.gray300, C.white, 1, true));
    }
    els.push(text(`dual_left_itmt_${i}`, 86, iy + 9, 180, 16, item, 12, C.gray900));
  });

  // Transfer Buttons (Middle)
  const mx = 302;
  const my = yC + 110;
  els.push(rect('dual_btn_all_right', mx, my, 36, 32, C.gray300, C.white, 1, true));
  els.push(text('dual_btn_all_right_t', mx, my + 8, 36, 16, '⟫', 14, C.gray700, 'center'));

  els.push(rect('dual_btn_right', mx, my + 40, 36, 32, C.primary, C.infoBg, 1, true));
  els.push(text('dual_btn_right_t', mx, my + 48, 36, 16, '⟩', 14, C.primary, 'center'));

  els.push(rect('dual_btn_left', mx, my + 80, 36, 32, C.gray300, C.white, 1, true));
  els.push(text('dual_btn_left_t', mx, my + 88, 36, 16, '⟨', 14, C.gray300, 'center'));

  els.push(rect('dual_btn_all_left', mx, my + 120, 36, 32, C.gray300, C.white, 1, true));
  els.push(text('dual_btn_all_left_t', mx, my + 128, 36, 16, '⟪', 14, C.gray300, 'center'));

  // Right List (Assigned)
  const rx = 350;
  els.push(rect('dual_right_panel', rx, yC, 240, 260, C.gray300, C.white, 1, false));
  els.push(rect('dual_right_hdr', rx, yC, 240, 40, 'transparent', C.gray50, 0, false));
  els.push(hline('dual_right_div1', rx, yC + 40, 240, C.gray300));
  els.push(text('dual_right_title', rx + 12, yC + 10, 160, 18, 'Assigned Permissions', 12, C.gray900));
  els.push(text('dual_right_count', rx + 190, yC + 12, 40, 14, '3', 11, C.gray500, 'right'));

  // Search
  els.push(rect('dual_right_search', rx + 8, yC + 48, 224, 32, C.gray300, C.white, 1, true));
  els.push(text('dual_right_search_t', rx + 16, yC + 56, 160, 16, '⌕ Search...', 12, C.gray500));
  els.push(hline('dual_right_div2', rx, yC + 88, 240, C.gray300));

  // Right Items
  const rightItems = ['models:read', 'models:write', 'workflows:read'];
  rightItems.forEach((item, i) => {
    const iy = yC + 88 + i * 32;
    els.push(rect(`dual_right_chk_${i}`, rx + 12, iy + 8, 16, 16, C.gray300, C.white, 1, true));
    els.push(text(`dual_right_itmt_${i}`, rx + 36, iy + 9, 180, 16, item, 12, C.gray900));
    els.push(text(`dual_right_del_${i}`, rx + 210, iy + 8, 20, 16, '×', 16, C.gray500));
  });

  return els;
}

/**
 * SSO icon row + “or” divider only (x=50). Used by screens via component() — no kit caption.
 */
export function buildAuthExternalSignInBlock(yC) {
  const innerW = AUTH_INNER_W;
  const btnSize = AUTH_PROVIDER_BTN_SIZE;
  const gap = AUTH_PROVIDER_GAP;
  const rowW = btnSize * 3 + gap * 2;
  const startX = 50 + Math.round((innerW - rowW) / 2);
  const providers = [
    ['ext_ms', '⊞', '#2563eb', '#eff6ff'],
    ['ext_go', 'G', '#dc2626', '#fef2f2'],
    ['ext_gh', '⎇', '#1f2937', '#f3f4f6'],
  ];
  const els = [];

  providers.forEach(([id, icon, stroke, bg], i) => {
    const bx = startX + i * (btnSize + gap);
    els.push(rect(id, bx, yC, btnSize, btnSize, stroke, bg, 1, true));
    els.push(text(`${id}_ic`, bx, yC + 11, btnSize, 22, icon, 18, stroke, 'center'));
  });

  const yOr = yC + btnSize + 12;
  const mid = 50 + innerW / 2;
  els.push(hline('ext_or_l', 50, yOr + 10, innerW / 2 - 28, C.gray300));
  els.push(text('ext_or_t', mid - 20, yOr, 40, 16, 'or', 11, C.gray500, 'center'));
  els.push(hline('ext_or_r', mid + 28, yOr + 10, innerW / 2 - 28, C.gray300));

  return els;
}

/** S38 kit section — same block as screens; section title only (no extra captions). */
export function buildAuthExternalSignIn(y0) {
  const els = [...sectionHeader(38, 'External sign-in', y0)];
  const yC = y0 + 48;
  els.push(...buildAuthExternalSignInBlock(yC));
  return els;
}

export function buildColorIconPicker(y0) {
  const els = [...sectionHeader(29, 'Color & Icon Picker', y0)];
  const yC = y0 + 68;
  els.push(text('clr_lbl', 50,  y0 + 46, 120, 14, 'Color Picker', 11, C.gray500));
  els.push(text('icp_lbl', 360, y0 + 46, 120, 14, 'Icon Picker',  11, C.gray500));

  // ── Color swatches 6 × 4 ──
  const swatchColors = [
    '#9E4A44','#B5763D','#4D6B44','#667A6E','#4F5F57','#2B2F33',
    '#C58B55','#D4956B','#7FA676','#8A9099','#D9D7D1','#F4F3EF',
    '#F3ECEA','#F6EEE4','#EBF0E9','#EDF0EE','#FFFFFF','#1E2228',
    '#FBBF24','#60A5FA','#A78BFA','#F472B6','#34D399','#F87171',
  ];
  swatchColors.forEach((color, i) => {
    const col = i % 6;
    const row = Math.floor(i / 6);
    els.push(rect(`clr_sw_${i}`, 50 + col * 42, yC + row * 42, 36, 36, C.gray300, color, 1, true));
  });

  // Hex input
  const yHex = yC + 4 * 42 + 12;
  els.push(rect('clr_hex_input',  50, yHex, 244, 36, C.gray300, C.white, 1, true));
  els.push(rect('clr_hex_swatch', 56, yHex + 6, 24, 24, C.gray300, C.accent, 1, true));
  els.push(text('clr_hex_val',    86, yHex + 10, 150, 16, '#C58B55', 13, C.gray700));

  // ── Icon search + grid ──
  const rx = 360;
  const rW = 440;

  els.push(rect('icp_search',   rx, yC, rW, 36, C.gray300, C.gray50, 1, true));
  els.push(text('icp_search_t', rx + 12, yC + 10, 200, 16, '🔍  Search icons…', 13, C.gray300));

  const cats = ['All', 'UI', 'Arrows', 'Files', 'Status'];
  cats.forEach((cat, i) => {
    const active = i === 0;
    const chipX = rx + i * 88;
    els.push(rect(`icp_cat_${i}`,   chipX, yC + 44, 80, 26, active ? C.primary : C.gray300, active ? C.infoBg : 'transparent', active ? 2 : 1, true));
    els.push(text(`icp_cat_t_${i}`, chipX, yC + 52, 80, 14, cat, 11, active ? C.primary : C.gray500, 'center'));
  });

  const yGrid = yC + 78;
  const iconSymbols = ['⬡','⚙','✓','✗','⟳','→','←','↑','↓','⊕','⊖','⊙','□','◎','▷','⋯','≡','⌘','⚠','☆'];
  iconSymbols.forEach((sym, i) => {
    const col = i % 5;
    const row = Math.floor(i / 5);
    const x = rx + col * 88;
    const y = yGrid + row * 56;
    els.push(rect(`icp_ic_${i}`, x, y, 80, 48, C.gray300, C.gray50, 1, true));
    els.push(text(`icp_is_${i}`, x, y + 10, 80, 28, sym, 18, C.gray700, 'center'));
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

// ─── Run if executed directly ─────────────────────────────────────────────────
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const GAP = 60;
  const allElements = [];
  let currentY = 30;

  for (const builder of [
    // ── Foundations ───────────────────────────────────────────────────────────
    buildColorPalette,           // S01
    buildTypography,             // S02
    buildButtons,                // S03
    // ── Input & Forms ─────────────────────────────────────────────────────────
    buildFormControls,           // S04
    buildDateTimePicker,         // S05
    buildFileUpload,             // S06
    buildRichTextEditor,         // S07
    buildCodeEditor,             // S08
    // ── Data Display ──────────────────────────────────────────────────────────
    buildBadges,                 // S09
    buildTable,                  // S10
    buildEditableTable,          // S11
    buildCards,                  // S12
    buildEmptyStates,            // S13
    buildSkeletonLoaders,        // S14
    // ── Navigation & Layout ───────────────────────────────────────────────────
    buildNavigation,             // S15
    buildSidebarNav,             // S16
    buildTabs,                   // S17
    buildAppShell,               // S18
    // ── Feedback & Overlays ───────────────────────────────────────────────────
    buildFeedback,               // S19
    buildModal,                  // S20
    buildSideSheet,              // S21
    buildCommandPalette,         // S22
    buildNotifications,          // S23
    buildTooltipPopover,         // S24
    // ── Interaction Patterns ──────────────────────────────────────────────────
    buildDropdownContextMenu,    // S25
    buildDragDrop,               // S26
    buildUtilities,              // S27
    buildPermissionMatrix,       // S28
    buildColorIconPicker,        // S29
    // ── Axis App Patterns ─────────────────────────────────────────────────────
    buildWorkflowCanvas,         // S30
    buildBuilderLayout,          // S31
    buildExecutionTimeline,      // S32
    buildFieldTypePicker,        // S33
    buildRelationLookup,         // S34
    buildStatsCards,             // S35
    buildAdvancedFilters,        // S36
    buildDualListbox,            // S37
    buildAuthExternalSignIn,     // S38
  ]) {
    const els = builder(currentY);
    allElements.push(...els);
    currentY = sectionBottom(els) + GAP;
  }

  const elements = allElements;
  writeExcalidraw(fileURLToPath(new URL('./_template.excalidraw', import.meta.url)), elements);
  console.log(`✓ Generated _template.excalidraw — ${elements.length} elements`);
}
