/**
 * Axis Wireframes — Shared Component Library
 *
 * Single source of truth for all primitives, colors, layout constants, and
 * UI helpers. Imported by generate-template.mjs and generate-screens.mjs.
 */

import { writeFileSync } from 'fs';

// ─── Seed management ─────────────────────────────────────────────────────────
let _seed = 1001;
export const nextSeed = () => (_seed += 2);
export const setSeed = (seed) => {
  _seed = Math.max(1, Math.floor(seed));
};
export const BASE = { angle: 0, opacity: 100, groupIds: [], isDeleted: false, boundElements: null, updated: 1700000000000, link: null, locked: false, version: 1 };

// ─── Primitives ───────────────────────────────────────────────────────────────

export function rect(id, x, y, w, h, stroke, bg, sw = 1, rounded = false, extra = {}) {
  const s = nextSeed();
  return { ...BASE, id, type: 'rectangle', x, y, width: w, height: h, strokeColor: stroke, backgroundColor: bg, fillStyle: 'solid', strokeWidth: sw, strokeStyle: 'solid', roughness: 1, roundness: rounded ? { type: 3 } : null, seed: s, versionNonce: s + 1, ...extra };
}

export function ellipse(id, x, y, w, h, stroke, bg, sw = 1, extra = {}) {
  const s = nextSeed();
  return { ...BASE, id, type: 'ellipse', x, y, width: w, height: h, strokeColor: stroke, backgroundColor: bg, fillStyle: 'solid', strokeWidth: sw, strokeStyle: 'solid', roughness: 1, roundness: { type: 3 }, seed: s, versionNonce: s + 1, ...extra };
}

export function text(id, x, y, w, h, str, fontSize, color, align = 'left', extra = {}) {
  const s = nextSeed();
  return { ...BASE, id, type: 'text', x, y, width: w, height: h, strokeColor: color, backgroundColor: 'transparent', fillStyle: 'solid', strokeWidth: 1, strokeStyle: 'solid', roughness: 1, roundness: null, seed: s, versionNonce: s + 1, text: str, fontSize, fontFamily: 1, textAlign: align, verticalAlign: 'top', containerId: null, originalText: str, lineHeight: 1.25, ...extra };
}

export function hline(id, x, y, w, stroke = '#dee2e6', sw = 1) {
  const s = nextSeed();
  return { ...BASE, id, type: 'line', x, y, width: w, height: 0, strokeColor: stroke, backgroundColor: 'transparent', fillStyle: 'solid', strokeWidth: sw, strokeStyle: 'solid', roughness: 1, roundness: null, seed: s, versionNonce: s + 1, points: [[0, 0], [w, 0]], lastCommittedPoint: null, startBinding: null, endBinding: null, startArrowhead: null, endArrowhead: null };
}

export function vline(id, x, y, h, stroke = '#dee2e6', sw = 1) {
  const s = nextSeed();
  return { ...BASE, id, type: 'line', x, y, width: 0, height: h, strokeColor: stroke, backgroundColor: 'transparent', fillStyle: 'solid', strokeWidth: sw, strokeStyle: 'solid', roughness: 1, roundness: null, seed: s, versionNonce: s + 1, points: [[0, 0], [0, h]], lastCommittedPoint: null, startBinding: null, endBinding: null, startArrowhead: null, endArrowhead: null };
}

export function arrow(id, x, y, dx, dy, stroke, sw = 2) {
  const s = nextSeed();
  return { ...BASE, id, type: 'arrow', x, y, width: Math.abs(dx) || 1, height: Math.abs(dy) || 1, strokeColor: stroke || C.gray500, backgroundColor: 'transparent', fillStyle: 'solid', strokeWidth: sw, strokeStyle: 'solid', roughness: 1, roundness: { type: 2 }, seed: s, versionNonce: s + 1, points: [[0, 0], [dx, dy]], lastCommittedPoint: null, startBinding: null, endBinding: null, startArrowhead: null, endArrowhead: 'arrow' };
}

export function sectionHeader(n, label, y) {
  return [
    text(`s${n}_lbl`, 50, y, 600, 26, `${n.toString().padStart(2, '0')} — ${label}`, 18, '#6c757d'),
    hline(`s${n}_div`, 50, y + 32, 1000),
  ];
}

// ─── Colors ───────────────────────────────────────────────────────────────────

export const C = {
  // — Industrial Calm —
  primary:     '#667A6E',
  primaryDark: '#4F5F57',
  accent:      '#C58B55',
  accentDark:  '#A8743E',
  danger:      '#9E4A44',
  dangerDark:  '#7D3A35',
  success:     '#4D6B44',
  warning:     '#B5763D',
  gray900:     '#2B2F33',
  gray700:     '#4A5058',
  gray500:     '#8A9099',
  gray300:     '#D9D7D1',
  gray100:     '#F4F3EF',
  gray50:      '#F9F8F5',
  white:       '#FFFFFF',
  infoBg:      '#EDF0EE',
  successBg:   '#EBF0E9',
  warningBg:   '#F6EEE4',
  dangerBg:    '#F3ECEA',
  infoBorder:  '#A8BAB1',
  successBorder:'#7FA676',
  warningBorder:'#C9975E',
  dangerBorder: '#C08078',
};

// ─── Layout constants ─────────────────────────────────────────────────────────

export const SB  = 230;   // sidebar width (matches S18 buildAppShell)
export const HDR = 60;    // header height (matches S18 buildAppShell)
export const OX  = 0;     // screen origin x (sidebar starts here)
export const OY  = 0;     // screen origin y

// Content area starts at (SB, HDR) inside the screen
export const CX  = 240;    // content origin x (after sidebar)
export const CY  = 56;   // content origin y (after header)

// ─── Placement helpers ────────────────────────────────────────────────────────

/**
 * Shift all elements by (dx, dy).
 * Line points are relative, so only x/y anchors change.
 */
export function translate(els, dx, dy) {
  return els.map(el => ({ ...el, x: el.x + dx, y: el.y + dy }));
}

/**
 * Extract a template section and place it at (targetX, targetY).
 *
 * Template convention: x=50 left margin, content starts at yC = y0 + contentDy.
 * Calling builderFn(0) gives elements anchored to (50, contentDy).
 * We strip the first 2 elements (sectionHeader label + hline) then translate
 * so the content origin lands exactly at (targetX, targetY).
 */
export function component(builderFn, targetX, targetY, contentDy = 48) {
  const els = builderFn(0);
  const content = els.slice(2);  // remove sectionHeader (label + hline)
  return translate(content, targetX - 50, targetY - contentDy);
}

/**
 * Place a template block with no section header (e.g. buildAuthExternalSignInBlock).
 * Do not use component() for those — slice(2) would drop the first two content elements.
 */
export function componentContent(builderFn, targetX, targetY, contentDy = 48) {
  const els = builderFn(0);
  return translate(els, targetX - 50, targetY - contentDy);
}

// ─── File writer ──────────────────────────────────────────────────────────────

export function writeExcalidraw(filePath, elements, files = {}) {
  const output = JSON.stringify({
    type: 'excalidraw',
    version: 2,
    source: 'https://excalidraw.com',
    elements,
    appState: { gridSize: 8, viewBackgroundColor: '#ffffff' },
    files,
  });
  writeFileSync(filePath, output, 'utf-8');
}

// ─── Parameterised App Shell ──────────────────────────────────────────────────
// Matches generate-template.mjs S18 buildAppShell exactly.
// Sidebar: 230px wide, header: 60px tall.
// Nav items: 214×36px, 8px x-inset, starting y = HDR+12, spaced 44px.
// Active item: C.infoBg bg + C.infoBorder stroke + 3px left accent bar + C.primary text.
// Logo: gray50 area 230×60, text '⬡  Axis' 18px C.primary at (30, 18).

export function appShell(prefix, W, H, navItems, activeIdx, pageTitle) {
  const els = [];

  // Main Page Background
  els.push(rect(`${prefix}_bg`, 0, 0, W, H, C.gray300, C.gray50, 1, false));

  // --- HYBRID COLLAPSIBLE SIDEBAR ---
  const sbW = 240;
  els.push(rect(`${prefix}_sidebar`, 0, 0, sbW, H, C.gray300, C.white, 1, false));

  // Workspace / Tenant Header
  els.push(text(`${prefix}_logo`, 20, 20, 24, 24, '⬡', 20, C.primary));
  els.push(text(`${prefix}_ws_name`, 54, 23, 100, 18, 'Acme Corp', 14, C.gray900));
  els.push(text(`${prefix}_ws_arr`, 158, 23, 16, 16, '▾', 14, C.gray500));

  // Collapse Sidebar Action
  els.push(rect(`${prefix}_sb_coll_bg`, sbW - 36, 16, 24, 24, 'transparent', C.gray100, 0, true, { roundness: { type: 3 } }));
  els.push(text(`${prefix}_sb_coll_t`, sbW - 36, 19, 24, 16, '◂', 14, C.gray500, 'center'));
  els.push(text(`${prefix}_sb_hint`, sbW - 38, 40, 28, 12, 'Cmd+\\', 9, C.gray300, 'center'));

  // Nav items (Expanded state with icons and labels)
  const navStartY = 80;
  const icons = ['🗂', '👥', '🗃', '⚡', '📝', '⚙'];
  navItems.forEach((label, i) => {
    const y = navStartY + i * 40;
    const active = i === activeIdx;
    const bg     = active ? C.infoBg      : 'transparent';
    const tc     = active ? C.primary     : C.gray700;
    const icon   = icons[i] || '•';

    els.push(rect(`${prefix}_ni_${i}`, 12, y, sbW - 24, 32, 'transparent', bg, 0, true, { roundness: { type: 3 } }));
    els.push(text(`${prefix}_nic_${i}`, 20, y + 6, 20, 20, icon, 14, tc, 'center'));
    els.push(text(`${prefix}_nl_${i}`, 48, y + 8, 140, 16, label, 13, tc));
  });

  // User Profile at bottom left
  els.push(hline(`${prefix}_u_div`, 12, H - 60, sbW - 24, C.gray300));
  els.push(ellipse(`${prefix}_uav`, 16, H - 46, 32, 32, C.infoBorder, C.infoBg, 1));
  els.push(text(`${prefix}_un`, 56, H - 38, 120, 16, 'Alex Brown', 12, C.gray900));


  // --- TOP HEADER (Breadcrumb + CmdK) ---
  const hdrH = 56;
  els.push(rect(`${prefix}_hdr`, sbW, 0, W - sbW, hdrH, C.gray300, C.white, 1, false));

  // Breadcrumb
  els.push(text(`${prefix}_bc`, sbW + 24, 20, 200, 16, `Axis  /  ${pageTitle}`, 13, C.gray700));

  // Command Palette (Centered in the remaining workspace width)
  const cmdW = 320;
  const cmdX = sbW + ((W - sbW) / 2) - (cmdW / 2);
  els.push(rect(`${prefix}_cmd_bg`, cmdX, 12, cmdW, 32, C.gray300, C.gray50, 1, true, { roundness: { type: 3 } }));
  els.push(text(`${prefix}_cmd_t`, cmdX + 12, 19, 140, 16, '⌕  Search anything...', 12, C.gray500));
  els.push(rect(`${prefix}_cmd_key`, cmdX + cmdW - 40, 16, 32, 20, C.gray300, C.white, 1, true, { roundness: { type: 3 } }));
  els.push(text(`${prefix}_cmd_key_t`, cmdX + cmdW - 35, 19, 24, 14, '⌘K', 10, C.gray500));

  // Right Actions
  els.push(text(`${prefix}_notif`, W - 44, 18, 24, 24, '🔔', 16, C.gray700));

  // --- FLOATING HELP/FOOTER (Bottom Right) ---
  els.push(ellipse(`${prefix}_help`, W - 56, H - 56, 40, 40, C.gray300, C.white, 1));
  els.push(text(`${prefix}_help_ic`, W - 56, H - 46, 40, 20, '?', 16, C.gray700, 'center'));

  return els;
}

// ─── Convenience UI builders ──────────────────────────────────────────────────
// Dimensions match template canonical values exactly (from wireframes.md table).

/** Primary or ghost button. h=36, sw=2 primary / sw=1 ghost. Text y+10, 13px. */
export function btn(prefix, x, y, label, variant = 'primary') {
  const w = label.length * 8 + 32;
  const [stroke, bg, tc, sw] =
    variant === 'primary' ? [C.accentDark, C.accent,    C.white,   2] :
    variant === 'danger'  ? [C.dangerDark, C.danger,    C.white,   2] :
    variant === 'ghost'   ? [C.gray300,    C.white,     C.gray700, 1] :
                            [C.primary,    C.infoBg,    C.primary, 1]; // secondary
  return [
    rect(`${prefix}_btn`, x, y, w, 36, stroke, bg, sw, true),
    text(`${prefix}_btn_t`, x, y + 10, w, 16, label, 13, tc, 'center'),
  ];
}

/** Gap between label text and required * (px) — same on every field. */
export const REQUIRED_MARKER_GAP = 10;

/** Help row: small ? icon then help copy (icon before text). */
export const HELP_ICON_SIZE = 12;
export const HELP_TEXT_ICON_GAP = 6;
export const FIELD_LABEL_H = 16;
export const FIELD_HELP_TEXT_H = 14;
/** Gap between label row and help text row. */
export const FIELD_LABEL_TO_HELP_GAP = 6;
/** Gap between label (or help row) and the control below. */
export const FIELD_HELP_TO_INPUT_GAP = 8;

/** Pixel width of label copy at 11px — positions the required * after the last glyph. */
export function labelTextWidth(str, fontSize = 11) {
  const scale = fontSize / 11;
  let w = 0;
  for (const ch of str) {
    if (ch === ' ') w += 3.2 * scale;
    else if (ch >= 'A' && ch <= 'Z') w += 6.4 * scale;
    else w += 5.5 * scale;
  }
  return Math.ceil(w);
}

/**
 * Form field label with optional required marker (* in C.danger).
 * Use on every user-editable field label in screen wireframes.
 */
export function fieldLabel(prefix, x, y, label, { required = false, color = C.gray500 } = {}) {
  return fieldLabelBlock(prefix, x, y, 400, label, { required, color }).els;
}

/**
 * Label row + optional help (? icon) + help text line.
 * Returns label block height and input top offset for stacking fields.
 */
export function fieldLabelBlock(prefix, x, y, innerW, label, {
  required = false,
  helpText = null,
  color = C.gray500,
} = {}) {
  const labelW = Math.max(8, labelTextWidth(label, 11));
  const els = [text(`${prefix}_fl`, x, y, labelW, FIELD_LABEL_H, label, 11, color)];
  if (required) {
    els.push(text(`${prefix}_req`, x + labelW + REQUIRED_MARKER_GAP, y, 8, FIELD_LABEL_H, '*', 11, C.danger));
  }
  let labelBlockH = FIELD_LABEL_H;
  if (helpText) {
    const helpY = y + FIELD_LABEL_H + FIELD_LABEL_TO_HELP_GAP;
    const iconY = helpY + Math.round((FIELD_HELP_TEXT_H - HELP_ICON_SIZE) / 2);
    els.push(ellipse(`${prefix}_help_ic`, x, iconY, HELP_ICON_SIZE, HELP_ICON_SIZE, C.primaryDark, C.infoBg, 1.5));
    els.push(text(`${prefix}_help_q`, x, iconY + 1, HELP_ICON_SIZE, 10, '?', 9, C.primaryDark, 'center'));
    const textX = x + HELP_ICON_SIZE + HELP_TEXT_ICON_GAP;
    els.push(text(`${prefix}_help`, textX, helpY, innerW - HELP_ICON_SIZE - HELP_TEXT_ICON_GAP, FIELD_HELP_TEXT_H, helpText, 10, C.gray700));
    labelBlockH = FIELD_LABEL_H + FIELD_LABEL_TO_HELP_GAP + FIELD_HELP_TEXT_H;
  }
  const inputY = y + labelBlockH + FIELD_HELP_TO_INPUT_GAP;
  return { els, labelBlockH, inputY };
}

/** Right inset when a password reveal toggle is shown (px). */
export const PASSWORD_INPUT_PAD_RIGHT = 40;

/** Password fields: eye toggle on the right inside the 40px input. */
export const PASSWORD_TOGGLE_BTN = 28;

export function isPasswordLabel(label) {
  return /password/i.test(label);
}

/** Eye icon control (wireframe — show/hide password). */
export function passwordRevealToggle(prefix, inputX, inputY, inputW) {
  const btn = PASSWORD_TOGGLE_BTN;
  const bx = inputX + inputW - btn - 6;
  const by = inputY + 6;
  const cx = bx + btn / 2;
  const cy = by + btn / 2;
  return [
    ellipse(`${prefix}_pw_tgl`, bx, by, btn, btn, C.gray300, C.gray50, 1),
    ellipse(`${prefix}_pw_eye`, cx - 7, cy - 3, 14, 9, C.gray500, 'transparent', 1.25),
    ellipse(`${prefix}_pw_pupil`, cx - 2.5, cy - 1.5, 5, 5, C.gray500, C.gray500, 0),
  ];
}

/** Text input. h=40, placeholder at y+11, 13px gray500. */
export function inputField(prefix, x, y, w, placeholder = '', { password = false } = {}) {
  const padR = password ? PASSWORD_INPUT_PAD_RIGHT : 24;
  const els = [
    rect(`${prefix}_inp`, x, y, w, 40, C.gray300, C.white, 1, true),
    text(`${prefix}_ph`, x + 12, y + 11, w - 12 - padR, 18, placeholder, 13, C.gray500),
  ];
  if (password) {
    els.push(...passwordRevealToggle(`${prefix}_pw`, x, y, w));
  }
  return els;
}

/** Select / dropdown. h=40, arrow at x+w-22. */
export function selectField(prefix, x, y, w, placeholder = '') {
  return [
    rect(`${prefix}_sel`, x, y, w, 40, C.gray300, C.white, 1, true),
    text(`${prefix}_ph`, x + 12, y + 11, w - 34, 18, placeholder, 13, C.gray300),
    text(`${prefix}_arr`, x + w - 22, y + 11, 18, 18, '▾', 13, C.gray700),
  ];
}

/** Badge. h=28, width=label.length×8+24. Text at y+6, 12px, centered. */
export function badge(prefix, x, y, label, variant = 'active') {
  const w = label.length * 8 + 24;
  const [stroke, bg, tc] =
    variant === 'active'   ? [C.primaryDark,  C.primary,   C.white]   :
    variant === 'draft'    ? [C.gray300,       C.gray50,    C.gray700] :
    variant === 'success'  ? [C.successBorder, C.successBg, C.success] :
    variant === 'warning'  ? [C.warningBorder, C.warningBg, C.warning] :
    variant === 'danger'   ? [C.dangerBorder,  C.dangerBg,  C.danger]  :
                             [C.infoBorder,    C.infoBg,    C.primary]; // info
  return [
    rect(`${prefix}_bdg`, x, y, w, 28, stroke, bg, 1, true),
    text(`${prefix}_bdg_t`, x, y + 6, w, 16, label, 12, tc, 'center'),
  ];
}

/** Semantic text/border color for state screens. variant: success | warning | danger | info | neutral */
export function semanticVariantColor(variant = 'info') {
  return variant === 'success' ? C.success :
    variant === 'warning' ? C.warning :
    variant === 'danger'  ? C.danger  :
    variant === 'neutral' ? C.gray700 :
                            C.primary;
}

export function semanticVariantBorder(variant = 'info') {
  return variant === 'success' ? C.successBorder :
    variant === 'warning' ? C.warningBorder :
    variant === 'danger'  ? C.dangerBorder  :
    variant === 'neutral' ? C.gray300       :
                            C.infoBorder;
}

/**
 * Canonical state headline — icon + colored title on one row, short semantic underline.
 * Use on all platform-foundation auth outcome cards for a consistent layout (no circular badges).
 */
export function stateHeadline(prefix, x, y, w, icon, variant, title, titleFontSize = 14) {
  const tc = semanticVariantColor(variant);
  const iconCol = 28;
  const rowH = titleFontSize >= 16 ? 26 : 24;
  return [
    text(`${prefix}_ic`, x, y + 2, iconCol, rowH, icon, titleFontSize + 2, tc),
    text(`${prefix}_ti`, x + iconCol + 6, y, w - iconCol - 6, rowH + 6, title, titleFontSize, tc),
    hline(`${prefix}_ulc`, x, y + rowH + 8, Math.min(64, w), semanticVariantBorder(variant), 2),
  ];
}

/** Search bar (gray input with magnifier icon). h=40. */
export function searchBar(prefix, x, y, w) {
  return [
    rect(`${prefix}_srch`, x, y, w, 40, C.gray300, C.white, 1, true),
    text(`${prefix}_srch_icon`, x + 10, y + 11, 20, 18, '⌕', 14, C.gray500),
    text(`${prefix}_srch_ph`, x + 34, y + 11, w - 46, 18, 'Search…', 13, C.gray300),
  ];
}

/** Page header bar with title, optional breadcrumb, and action buttons.
 *  actions = array of { label, variant } passed to btn(). */
export function pageHeader(prefix, x, y, W, title, actions = []) {
  const els = [];
  els.push(text(`${prefix}_title`, x, y, 400, 28, title, 20, C.gray900));
  let bx = x + W;
  [...actions].reverse().forEach((a, i) => {
    const w = a.label.length * 8 + 32;
    bx -= w + (i > 0 ? 10 : 0);
    els.push(...btn(`${prefix}_act_${i}`, bx, y - 4, a.label, a.variant || 'primary'));
  });
  return els;
}
