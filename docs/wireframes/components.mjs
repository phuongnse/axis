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
export const CX  = 296;    // content origin x (after sidebar)
export const CY  = 60;   // content origin y (after header)

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

// ─── File writer ──────────────────────────────────────────────────────────────

export function writeExcalidraw(filePath, elements) {
  const output = JSON.stringify({
    type: 'excalidraw',
    version: 2,
    source: 'https://excalidraw.com',
    elements,
    appState: { gridSize: 8, viewBackgroundColor: '#ffffff' },
    files: {},
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

  // Main Page Background (Canvas area)
  els.push(rect(`${prefix}_bg`, 0, 0, W, H, C.gray300, C.gray50, 1, false));

  // --- ACTIVITY BAR (Far Left, Dark Theme) ---
  const actW = 56;
  els.push(rect(`${prefix}_actbar`, 0, 0, actW, H, C.gray900, C.gray900, 1, false));

  // App Logo
  els.push(text(`${prefix}_logo`, 14, 16, 28, 28, '⬡', 24, C.white, 'center'));

  // Activity Icons
  const icons = ['⌘', '👥', '🗃', '⚡', '📝', '⚙'];
  icons.forEach((icon, i) => {
    const y = 80 + i * 56;
    const active = i === activeIdx;
    if (active) {
       els.push(rect(`${prefix}_act_bg_${i}`, 8, y - 8, 40, 40, 'transparent', '#4A5058', 0, true, { roundness: { type: 3 } }));
       // left indicator strip
       els.push(rect(`${prefix}_act_ind_${i}`, 0, y - 8, 3, 40, C.accent, C.accent, 1, false));
    }
    els.push(text(`${prefix}_act_ic_${i}`, 14, y, 28, 28, icon, 18, active ? C.white : C.gray500, 'center'));
  });

  // User Avatar (Bottom)
  els.push(ellipse(`${prefix}_act_av`, 12, H - 48, 32, 32, C.gray500, 'transparent', 1));
  els.push(text(`${prefix}_act_avt`, 12, H - 39, 32, 16, 'AB', 12, C.gray300, 'center'));


  // --- CONTEXT PANEL (Secondary Sidebar) ---
  const ctxW = 240;
  els.push(rect(`${prefix}_ctxpanel`, actW, 0, ctxW, H, C.gray300, C.white, 1, false));

  // Context Header
  els.push(text(`${prefix}_ctx_title`, actW + 20, 20, 180, 24, 'Workflow Engine', 16, C.gray900));
  els.push(hline(`${prefix}_ctx_div1`, actW, 60, ctxW, C.gray300));

  // Context Nav Items
  const navStartY = 80;
  navItems.forEach((label, i) => {
    const y = navStartY + i * 36;
    const active = i === activeIdx;
    const bg     = active ? C.infoBg      : 'transparent';
    const tc     = active ? C.primary     : C.gray700;

    if (active) {
       els.push(rect(`${prefix}_ni_${i}`, actW + 8, y, ctxW - 16, 32, 'transparent', bg, 0, true, { roundness: { type: 3 } }));
    }
    els.push(text(`${prefix}_nl_${i}`, actW + 24, y + 8, 170, 16, label, 13, tc));
  });


  // --- WORKSPACE HEADER (Top area over the canvas) ---
  const hdrX = actW + ctxW;
  const hdrH = 60;
  els.push(rect(`${prefix}_hdr`, hdrX, 0, W - hdrX, hdrH, C.gray300, C.white, 1, false));

  // Breadcrumb / Page Title
  els.push(text(`${prefix}_page_title`, hdrX + 24, 20, 300, 20, pageTitle, 16, C.gray900));

  // Global Actions (Search & Notif)
  const rightX = W - 200;
  els.push(rect(`${prefix}_srch`, rightX, 12, 140, 36, C.gray300, C.gray50, 1, true));
  els.push(text(`${prefix}_srch_t`, rightX + 12, 22, 100, 16, '⌕ Search...', 12, C.gray500));
  els.push(text(`${prefix}_notif`, rightX + 156, 20, 24, 24, '🔔', 16, C.gray700));

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

/** Text input. h=40, placeholder at y+11, 13px gray500. */
export function inputField(prefix, x, y, w, placeholder = '') {
  return [
    rect(`${prefix}_inp`, x, y, w, 40, C.gray300, C.white, 1, true),
    text(`${prefix}_ph`, x + 12, y + 11, w - 24, 18, placeholder, 13, C.gray500),
  ];
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
