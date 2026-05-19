/**
 * Axis Screen Wireframes — generate-screens.mjs
 * Run: node docs/wireframes/generate-screens.mjs
 *
 * All visual components are sourced from generate-template.mjs builders.
 * Use component(buildXxx, x, y) to place template sections into screens.
 *
 * Screen width W=1200 ensures 900px template components fit at cx=250
 * (250 + 900 = 1150 < 1200). Never reduce W below 1200.
 *
 * Screens generated:
 *   app-shell (root)
 *   E01: register-org, email-confirmation, verify-email,
 *        workspace-provisioning, pricing, settings-org,
 *        settings-org-delete-modal
 *   E02: login, register, forgot-password, change-password,
 *        settings-users, settings-roles, settings-security, accept-invitation
 *   E03: data-models, data-classes, records
 *   E04: workflows, workflow-editor
 *   E05: forms, form-editor, form-submission
 *   E06: executions, execution-detail
 */

import { buildStatsCards } from './generate-template.mjs';
import { mkdirSync } from 'fs';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';

import {
  C, SB, HDR, CX, CY,
  rect, ellipse, text, hline, vline, arrow,
  btn, inputField, selectField, badge, searchBar, pageHeader,
  appShell, component, translate, writeExcalidraw,
} from './components.mjs';

import {
  buildWorkflowCanvas,
  buildBuilderLayout,
  buildExecutionTimeline,
  buildModal,
  buildSideSheet,
  buildTable,
  buildPermissionMatrix,
} from './generate-template.mjs';

const __filename = fileURLToPath(import.meta.url);
const __dir = dirname(__filename);

// ─── Screen constants ─────────────────────────────────────────────────────────

const W   = 1200;  // screen width — must be ≥ 1150 (cx=250 + template content=900)
const H   = 700;   // screen height
const PAD = 20;    // content area padding

// Content area origin with padding
const cx = CX + PAD;          // 250 — first content element x
const cy = CY + PAD;          // 80  — first content element y
const cw = W - CX - PAD * 2;  // 930 — usable content width

// Standard app nav labels
const NAV = ['Data Models', 'Workflows', 'Forms', 'Executions', 'Settings'];

// ─── Write helper ─────────────────────────────────────────────────────────────

function write(relativePath, elements) {
  let full;
  if (/^E0\d-/.test(relativePath)) {
    // Epic wireframe → docs/epics/{epic}/wireframes/{screen}
    const [epicFolder, ...rest] = relativePath.split('/');
    full = join(__dir, '..', 'epics', epicFolder, 'wireframes', ...rest);
  } else {
    // Shared wireframe → docs/wireframes/{path}
    full = join(__dir, relativePath);
  }
  mkdirSync(dirname(full), { recursive: true });
  writeExcalidraw(full, elements);
  console.log(`✓  ${relativePath}  (${elements.length} elements)`);
}

// ─── Auth card helper — shared by all standalone auth screens ─────────────────
// Centered card: logo → divider → title → [subtitle] → fields → [extraLink] → submit → footer.
//
// headerH breakdown (no subtitle): logo-gap(16) + logo(28) + gap(16) + divider(0) + gap(16) + title(24) + gap(12) = 112
// headerH breakdown (with subtitle): +subtitle(18) + gap(6) = 136
// Each field: label(16) + gap(2) + input(40) + gap(14) = 72px
// extraLink: 'Forgot password?' shown right-aligned between fields and submit btn (adds 22px)
// Footer zone: gap(12) + divider(0) + gap(10) + footer-text(16) + gap(6) = 32px from card bottom

function authCard(prefix, { title, subtitle = null, items = [], extraLink = null }, submitLabel, footerText) {
  const cardW   = 440;
  const headerH = subtitle ? 136 : 112;  // vertical space from card top to first field
  const fieldH  = items.length * 72;     // 72px per field: label(16) + gap(2) + input(40) + gap(14)
  const cardH   = headerH + fieldH + (extraLink ? 22 : 4) + 36 + 12 + 32;
  const cardX   = Math.round((W - cardW) / 2);
  const cardY   = Math.round((H - cardH) / 2);
  const els     = [];

  // Page background
  els.push(rect(`${prefix}_bg`,   0,     0,     W,     H,     C.gray300, C.gray100, 1, false));
  // Card surface
  els.push(rect(`${prefix}_card`, cardX, cardY, cardW, cardH, C.gray300, C.white,   2, true));

  // Logo — full-width bounding box so 'center' alignment is correct
  els.push(text(`${prefix}_logo`, cardX, cardY + 16, cardW, 28, '⬡  Axis', 18, C.primary, 'center'));
  els.push(hline(`${prefix}_hdiv`, cardX, cardY + 60, cardW, C.gray300));

  // Title (always present)
  els.push(text(`${prefix}_title`, cardX + 24, cardY + 76, cardW - 48, 24, title, 17, C.gray900));

  // Optional subtitle
  if (subtitle) {
    els.push(text(`${prefix}_sub`, cardX + 24, cardY + 104, cardW - 48, 18, subtitle, 13, C.gray700));
  }

  // Fields — start at headerH from card top
  const fieldStartY = cardY + headerH;
  items.forEach(({ label, placeholder }, i) => {
    const y = fieldStartY + i * 72;
    els.push(text(`${prefix}_fl_${i}`, cardX + 24, y,      cardW - 48, 16, label,       11, C.gray500));
    els.push(...inputField(`${prefix}_fi_${i}`, cardX + 24, y + 18, cardW - 48, placeholder));
  });

  // Optional secondary link (e.g. 'Forgot password?') — right-aligned, 6px below last field
  const afterFieldsY = fieldStartY + fieldH;
  if (extraLink) {
    els.push(text(`${prefix}_xl`, cardX + 24, afterFieldsY + 6, cardW - 48, 16, extraLink, 12, C.primary, 'right'));
  }

  // Submit button — full card width minus 24px padding each side
  const btnY = afterFieldsY + (extraLink ? 22 : 4);
  const btnW = cardW - 48;
  els.push(rect(`${prefix}_sbtn`,   cardX + 24, btnY,      btnW, 36, C.accentDark, C.accent, 2, true));
  els.push(text(`${prefix}_sbtn_t`, cardX + 24, btnY + 10, btnW, 16, submitLabel,  13, C.white, 'center'));

  // Footer divider + link
  els.push(hline(`${prefix}_fdiv`,  cardX,      cardY + cardH - 32,      cardW,     C.gray300));
  els.push(text(`${prefix}_footer`, cardX + 24, cardY + cardH - 22, cardW - 48, 16, footerText, 12, C.primary, 'center'));

  return els;
}

// ─── App shell (shared layout reference) ─────────────────────────────────────

function genAppShell() {
  const dashStats = component(buildStatsCards, cx, cy, 48);

  const els = [
    // Standard app shell layout
    ...appShell('as', W, H, NAV, 0, 'Dashboard Overview'),

    // Page Header / Action Bar
    text('as_pg_title', cx, cy + 24, 300, 28, 'Dashboard Overview', 20, C.gray900),
    ...btn('as_new_btn', cx + cw - 140, cy + 20, '+ New Workflow', 'primary'),

    // Dashboard Stats Panel
    ...translate(dashStats, 0, 80),

    // Secondary panel for empty state / activity feed
    rect('as_panel2', cx, cy + 240, cw, 280, C.gray300, C.white, 1, true, { roundness: { type: 3 } }),
    rect('as_panel2_hdr', cx, cy + 240, cw, 48, 'transparent', C.gray50, 0, false, { roundness: { type: 3 } }),
    hline('as_panel2_div', cx, cy + 288, cw, C.gray300),
    text('as_panel2_title', cx + 20, cy + 254, 200, 18, 'Recent Activity', 14, C.gray900),

    // Placeholder activity rows
    text('as_act1_t', cx + 20, cy + 310, 400, 16, 'Alex Brown created workflow "Order Processing"', 13, C.gray700),
    text('as_act1_d', cx + cw - 120, cy + 310, 100, 16, '2 hours ago', 12, C.gray500, 'right'),
    hline('as_act1_div', cx + 20, cy + 342, cw - 40, C.gray100),

    text('as_act2_t', cx + 20, cy + 360, 400, 16, 'Jane Smith updated data model "Customer"', 13, C.gray700),
    text('as_act2_d', cx + cw - 120, cy + 360, 100, 16, 'Yesterday', 12, C.gray500, 'right'),
    hline('as_act2_div', cx + 20, cy + 392, cw - 40, C.gray100),

    text('as_act_view_all', cx + 20, cy + 420, 100, 16, 'View all activity →', 13, C.primary),
  ];
  write('app-shell.excalidraw', els);
}

// ─── E01 Platform Foundation ─────────────────────────────────────────────────

/**
 * Register-Org — US-001, US-004
 * Standard auth card: 5 fields, plan selection shown in subtitle.
 * cardH = 136 (subtitle) + 5×72 (fields) + 4 + 36 + 12 + 32 = 580 → centered vertically.
 */
function genRegisterOrg() {
  const els = authCard('ro', {
    title:    'Create your organization',
    subtitle: 'Plan: Free trial  ·  Change plan →',
    items: [
      { label: 'Organization name', placeholder: 'Acme Corp' },
      { label: 'Admin full name',   placeholder: 'Alex Brown' },
      { label: 'Email address',     placeholder: 'you@company.com' },
      { label: 'Password',          placeholder: '••••••••' },
      { label: 'Confirm password',  placeholder: '••••••••' },
    ],
  }, 'Create organization', 'Already have an account? Sign in');
  write('E01-platform-foundation/register-org.excalidraw', els);
}

/**
 * Email-Confirmation — US-001 success state, US-002 resend
 * Informational card (no form): envelope icon, check-email message, resend link.
 */
function genEmailConfirmation() {
  const cardW = 440;
  const cardH = 300;
  const cardX = Math.round((W - cardW) / 2);
  const cardY = Math.round((H - cardH) / 2);
  const els   = [];

  els.push(rect('ec_bg',   0,     0,     W,     H,     C.gray300, C.gray100, 1, false));
  els.push(rect('ec_card', cardX, cardY, cardW, cardH, C.gray300, C.white,   2, true));

  // Logo row
  els.push(text('ec_logo',  cardX,      cardY + 16,  cardW,      28, '⬡  Axis',            18, C.primary, 'center'));
  els.push(hline('ec_hdiv', cardX,      cardY + 60,  cardW,          C.gray300));

  // Envelope icon (US-001 success state)
  els.push(text('ec_icon',  cardX,      cardY + 76,  cardW,      36, '✉',                  28, C.primary, 'center'));

  // Title + body
  els.push(text('ec_title', cardX + 24, cardY + 128, cardW - 48, 24, 'Check your email',   17, C.gray900));
  els.push(text('ec_body1', cardX + 24, cardY + 160, cardW - 48, 18, 'We sent a verification link to:', 13, C.gray700));
  els.push(text('ec_body2', cardX + 24, cardY + 182, cardW - 48, 18, 'alex@company.com',   13, C.gray900));

  // Resend link (US-002)
  els.push(text('ec_resend', cardX + 24, cardY + 220, cardW - 48, 16, "Didn't receive it?  Resend email →", 12, C.primary, 'center'));

  // Footer
  els.push(hline('ec_fdiv',   cardX,      cardY + cardH - 32, cardW,      C.gray300));
  els.push(text('ec_footer',  cardX + 24, cardY + cardH - 22, cardW - 48, 16, 'Back to sign in', 12, C.primary, 'center'));

  write('E01-platform-foundation/email-confirmation.excalidraw', els);
}

/**
 * Verify-Email — US-002 (all 4 outcome states)
 * 2×2 grid of state cards: success, expired, already-used, invalid.
 * Each card: 440×200. Grid centred at W=1200.
 */
function genVerifyEmail() {
  const els   = [];
  const cardW = 440;
  const cardH = 200;
  const gapX  = 60;
  const gapY  = 44;   // row gap; first 20px reserved for state label above card
  const col1X = Math.round((W - (cardW * 2 + gapX)) / 2);  // 130
  const col2X = col1X + cardW + gapX;
  const row1Y = 100;
  const row2Y = row1Y + cardH + gapY;

  els.push(rect('ve_bg', 0, 0, W, H, C.gray300, C.gray100, 1, false));
  els.push(text('ve_pg', 0, 48, W, 20, 'Email Verification — States', 13, C.gray500, 'center'));

  const states = [
    {
      id: 've_ok',   x: col1X, y: row1Y,
      stateLbl: '✓  Success',       lblColor: C.success,
      icon: '✓', iconColor: C.success,
      title: 'Email verified!',
      body:  'Your account is ready. Signing you in…',
      btnLabel: null, btnVariant: null,
    },
    {
      id: 've_exp',  x: col2X, y: row1Y,
      stateLbl: 'Expired link',     lblColor: C.warning,
      icon: '⏱', iconColor: C.warning,
      title: 'Verification link expired',
      body:  'This link was valid for 24 hours.',
      btnLabel: 'Resend verification email', btnVariant: 'secondary',
    },
    {
      id: 've_used', x: col1X, y: row2Y,
      stateLbl: 'Already verified', lblColor: C.gray500,
      icon: '✓', iconColor: C.gray500,
      title: 'Already verified',
      body:  'This link has already been used. Please sign in.',
      btnLabel: 'Sign in', btnVariant: 'ghost',
    },
    {
      id: 've_inv',  x: col2X, y: row2Y,
      stateLbl: 'Invalid token',    lblColor: C.danger,
      icon: '✕', iconColor: C.danger,
      title: 'Invalid verification link',
      body:  'This link is invalid or has been tampered with.',
      btnLabel: null, btnVariant: null,
    },
  ];

  states.forEach(({ id, x, y, stateLbl, lblColor, icon, iconColor, title, body, btnLabel, btnVariant }) => {
    els.push(text(`${id}_lbl`,   x,      y - 20,  cardW,      16, stateLbl, 12, lblColor));
    els.push(rect(`${id}_card`,  x,      y,       cardW,      cardH, C.gray300, C.white, 2, true));
    // Mini logo
    els.push(text(`${id}_logo`,  x,      y + 12,  cardW,      18, '⬡  Axis', 12, C.primary, 'center'));
    els.push(hline(`${id}_hdiv`, x,      y + 36,  cardW,      C.gray300));
    // Status icon
    els.push(text(`${id}_icon`,  x,      y + 46,  cardW,      26, icon, 20, iconColor, 'center'));
    // Title + body
    els.push(text(`${id}_title`, x + 20, y + 84,  cardW - 40, 20, title, 14, C.gray900));
    els.push(text(`${id}_body`,  x + 20, y + 108, cardW - 40, 32, body,  12, C.gray700));
    // Optional CTA
    if (btnLabel) {
      els.push(...btn(`${id}_cta`, x + 20, y + cardH - 44, btnLabel, btnVariant));
    }
  });

  write('E01-platform-foundation/verify-email.excalidraw', els);
}

/**
 * Workspace-Provisioning — US-003 (2 states side by side)
 * Left:  In-progress — spinner + step 2 active.
 * Right: Failed (after 3 retries) — error icon + failed step + contact link.
 *
 * W=1200 split at x=600. Each panel: 520px usable, centred at x=300/900.
 * Steps y baseline: 278. 4 × 40px → bottom 438.
 */
function genWorkspaceProvisioning() {
  const els = [];

  els.push(rect('wp_bg', 0, 0, W, H, C.gray300, C.gray100, 1, false));

  // Shared header row
  els.push(text('wp_logo',    0, 24, W, 28, '⬡  Axis',                  18, C.primary, 'center'));
  els.push(text('wp_heading', 0, 60, W, 18, 'Workspace Setup — States', 12, C.gray500, 'center'));
  els.push(vline('wp_div', W / 2, 86, H - 86, C.gray300));

  // ── Left: In-progress ────────────────────────────────────────────────────────
  const lX    = 40;
  const lW    = W / 2 - 80;  // 520
  const lMidX = W / 4;        // 300

  els.push(text('wp_l_lbl', lX, 90, lW, 16, '↻  In progress', 12, C.primary));
  els.push(ellipse('wp_l_spin',   lMidX - 28, 116, 56, 56, C.infoBorder, C.infoBg, 2));
  els.push(text('wp_l_spin_t',    lMidX - 28, 131, 56, 26, '↻', 18, C.primary, 'center'));
  els.push(text('wp_l_title', lX, 190, lW, 26, 'Setting up your workspace…', 18, C.gray900, 'center'));
  els.push(text('wp_l_org',   lX, 222, lW, 18, 'For Acme Corp',              13, C.accent,  'center'));
  els.push(text('wp_l_sub',   lX, 246, lW, 14, "Don't close this tab.",      11, C.gray500, 'center'));

  const stepsY  = 278;
  const lStepsX = lMidX - 160;  // 140
  [
    { icon: '✓', label: 'Email verified',          c: C.success },
    { icon: '↻', label: 'Creating your workspace', c: C.primary },
    { icon: '○', label: 'Configuring defaults',    c: C.gray300 },
    { icon: '○', label: 'Assigning admin role',    c: C.gray300 },
  ].forEach(({ icon, label, c }, i) => {
    const y = stepsY + i * 40;
    els.push(text(`wp_l_si_${i}`, lStepsX,      y, 20,  20, icon,  14, c));
    els.push(text(`wp_l_sl_${i}`, lStepsX + 28, y, 280, 20, label, 13, c === C.gray300 ? C.gray300 : C.gray700));
  });
  els.push(text('wp_l_note', lX, stepsY + 4 * 40 + 8, lW, 14,
    'Retrying automatically if this takes longer.', 10, C.gray300, 'center'));

  // ── Right: Failed (after 3 retries) ─────────────────────────────────────────
  const rX    = W / 2 + 40;    // 640
  const rW    = W / 2 - 80;    // 520
  const rMidX = (W * 3) / 4;   // 900

  els.push(text('wp_r_lbl', rX, 90, rW, 16, '✕  Failed (after 3 retries)', 12, C.danger));
  els.push(ellipse('wp_r_err',   rMidX - 28, 116, 56, 56, C.dangerBorder, C.dangerBg, 2));
  els.push(text('wp_r_err_t',    rMidX - 28, 131, 56, 26, '✕', 18, C.danger, 'center'));
  els.push(text('wp_r_title', rX, 190, rW, 26, 'Setup failed',                                        18, C.gray900, 'center'));
  els.push(text('wp_r_body',  rX, 222, rW, 36, 'Provisioning failed after 3 attempts.\nOur team has been notified.', 12, C.gray700, 'center'));

  const rStepsX = rMidX - 160;  // 740
  [
    { icon: '✓', label: 'Email verified',          c: C.success },
    { icon: '✕', label: 'Creating your workspace', c: C.danger  },
    { icon: '○', label: 'Configuring defaults',    c: C.gray300 },
    { icon: '○', label: 'Assigning admin role',    c: C.gray300 },
  ].forEach(({ icon, label, c }, i) => {
    const y = stepsY + i * 40;
    els.push(text(`wp_r_si_${i}`, rStepsX,      y, 20,  20, icon,  14, c));
    els.push(text(`wp_r_sl_${i}`, rStepsX + 28, y, 280, 20, label, 13, c === C.gray300 ? C.gray300 : C.gray700));
  });
  els.push(text('wp_r_supp', rX, stepsY + 4 * 40 + 8, rW, 14,
    'Contact support if the issue persists →', 11, C.primary, 'center'));

  write('E01-platform-foundation/workspace-provisioning.excalidraw', els);
}

/**
 * Settings-Org Delete Modal — US-007
 * Settings-org page (dimmed) + confirmation modal centred.
 *
 * Modal: 480×280. Input to type org name. Delete button disabled (gray) until match.
 * mX=360, mY=210. Right edge=840, bottom=490.
 */
function genSettingsOrgDeleteModal() {
  const navIdx = 4;
  const els    = [];

  // ── Background: abbreviated settings-org (for context behind modal) ──────────
  els.push(...appShell('sdm', W, H, NAV, navIdx, 'Settings — Organization'));
  els.push(text('sdm_bg_h',  cx,      cy,           300, 28, 'Organization Profile', 20, C.gray900));
  els.push(rect('sdm_bg_dz', cx,      cy + 280, cw, 72, C.dangerBorder, C.dangerBg, 1, true));
  els.push(text('sdm_bg_dt', cx + 16, cy + 296,     300, 18, 'Delete organization',  14, C.danger));

  // Semi-transparent overlay
  els.push(rect('sdm_overlay', 0, 0, W, H, 'transparent', C.gray900, 0, false, { opacity: 40 }));

  // ── Modal card ────────────────────────────────────────────────────────────────
  const mW = 480;
  const mH = 280;
  const mX = Math.round((W - mW) / 2);  // 360
  const mY = Math.round((H - mH) / 2);  // 210

  els.push(rect('sdm_card', mX, mY, mW, mH, C.gray700, C.white, 2, true));

  // Header
  els.push(text('sdm_title', mX + 20,      mY + 18, 380, 24, 'Delete organization', 16, C.danger));
  els.push(text('sdm_close', mX + mW - 40, mY + 16, 20,  20, '×',                   18, C.gray500));
  els.push(hline('sdm_hdiv', mX, mY + 52, mW, C.gray300));

  // Warning body
  els.push(text('sdm_body', mX + 20, mY + 68, mW - 40, 52,
    'This action is permanent and cannot be undone.\nAll data will be deleted after a 30-day grace period.', 13, C.gray700));

  // Confirm input
  els.push(text('sdm_inp_l', mX + 20, mY + 130, mW - 40, 16, "Type 'Acme Corp' to confirm:", 12, C.gray500));
  els.push(...inputField('sdm_inp', mX + 20, mY + 150, mW - 40, 'Acme Corp'));

  // Footer
  els.push(hline('sdm_fdiv', mX, mY + mH - 56, mW, C.gray300));

  const cancelW = 'Cancel'.length * 8 + 32;             // 80
  const delW    = 'Delete organization'.length * 8 + 32; // 184
  const delBtnX    = mX + mW - 20 - delW;               // 636
  const cancelBtnX = delBtnX - 8 - cancelW;             // 548

  els.push(...btn('sdm_cancel', cancelBtnX, mY + mH - 44, 'Cancel', 'ghost'));

  // Delete button — disabled state until input matches exactly
  els.push(rect('sdm_del',   delBtnX, mY + mH - 44, delW, 36, C.gray300, C.gray100, 1, true));
  els.push(text('sdm_del_t', delBtnX, mY + mH - 34, delW, 16, 'Delete organization', 13, C.gray300, 'center'));
  els.push(text('sdm_hint',  mX + 20, mY + mH - 36, 240,  14, 'Enabled when name matches exactly', 10, C.gray300));

  write('E01-platform-foundation/settings-org-delete-modal.excalidraw', els);
}

/**
 * Pricing — US-004 (plan selection before registration), US-010 (public pricing page)
 * Public marketing page (no app shell). 3-column plan cards.
 * Signed-in users see "Current plan" badge on their active plan.
 */
function genPricing() {
  const els = [];
  els.push(rect('pr_bg', 0, 0, W, H, C.gray300, C.gray100, 1, false));

  // Slim marketing header
  els.push(rect('pr_mhdr', 0, 0, W, 60, C.gray300, C.white, 1, false));
  els.push(text('pr_logo', 30, 18, 150, 26, '⬡  Axis', 18, C.primary));
  const siW = 'Sign in'.length * 8 + 32;     // 88
  const gsW = 'Get started'.length * 8 + 32; // 120
  els.push(...btn('pr_gst', W - 16 - gsW,              12, 'Get started', 'primary'));
  els.push(...btn('pr_si',  W - 16 - gsW - 8 - siW,    12, 'Sign in',     'ghost'));

  // Hero text
  els.push(text('pr_h1', 0, 80,  W, 30, 'Simple, transparent pricing',                               22, C.gray900, 'center'));
  els.push(text('pr_h2', 0, 118, W, 18, 'Start free. Upgrade as you grow. No credit card required.', 13, C.gray500, 'center'));

  // Plan cards — 3 × 300px wide, 25px gaps, centred at W=1200
  const planCardW = 300;
  const planCardH = 360;
  const planGap   = 25;
  const planY     = 150;
  const planX0    = Math.round((W - (3 * planCardW + 2 * planGap)) / 2);  // 125

  const plans = [
    {
      id: 'free', name: 'Free',       price: '$0',     priceSub: 'forever free',
      features: ['3 workflows', '1,000 executions / mo', '3 users', '1 GB storage', 'Community support'],
      cta: 'Get started free', ctaVariant: 'ghost',    highlight: false, currentPlan: false,
    },
    {
      id: 'pro',  name: 'Pro',        price: '$49',    priceSub: 'per month',
      features: ['20 workflows', '10,000 executions / mo', '10 users', '10 GB storage', 'Email support'],
      cta: 'Start free trial',  ctaVariant: 'primary', highlight: true,  currentPlan: true,
    },
    {
      id: 'ent',  name: 'Enterprise', price: 'Custom', priceSub: 'contact us',
      features: ['Unlimited workflows', 'Unlimited executions', 'Unlimited users', 'Unlimited storage', 'Dedicated support'],
      cta: 'Contact sales',     ctaVariant: 'secondary', highlight: false, currentPlan: false,
    },
  ];

  plans.forEach(({ id, name, price, priceSub, features, cta, ctaVariant, highlight, currentPlan }, planIdx) => {
    const px     = planX0 + planIdx * (planCardW + planGap);
    const stroke = highlight ? C.primary : C.gray300;
    const sw     = highlight ? 2 : 1;

    els.push(rect(`pr_${id}_c`, px, planY, planCardW, planCardH, stroke, C.white, sw, true));

    // "Current plan" badge (shown for signed-in users on their active plan)
    if (currentPlan) {
      const bdgW = 'Current plan'.length * 8 + 24;  // 120
      els.push(rect(`pr_${id}_bdg`,   px + planCardW - bdgW - 12, planY + 12, bdgW, 22, C.infoBorder, C.infoBg,  1, true));
      els.push(text(`pr_${id}_bdg_t`, px + planCardW - bdgW - 12, planY + 17, bdgW, 14, 'Current plan', 10, C.primary, 'center'));
    }

    // Plan name + price
    els.push(text(`pr_${id}_nm`, px + 20, planY + 20, planCardW - 40, 22, name,     16, C.gray900));
    els.push(text(`pr_${id}_pr`, px + 20, planY + 50, planCardW - 40, 34, price,    26, C.gray900));
    els.push(text(`pr_${id}_ps`, px + 20, planY + 90, planCardW - 40, 16, priceSub, 12, C.gray500));
    els.push(hline(`pr_${id}_div`, px, planY + 114, planCardW, C.gray300));

    // Feature list
    features.forEach((f, j) => {
      els.push(text(`pr_${id}_f${j}`, px + 20, planY + 126 + j * 30, planCardW - 40, 18, `✓  ${f}`, 12, C.gray700));
    });

    // Full-width CTA button (raw elements so it spans the card)
    const ctaDivY = planY + planCardH - 52;
    els.push(hline(`pr_${id}_cdiv`, px, ctaDivY, planCardW, C.gray300));
    const [stroke2, bg2, tc2, sw2] =
      ctaVariant === 'primary'   ? [C.accentDark, C.accent, C.white,   2] :
      ctaVariant === 'ghost'     ? [C.gray300,    C.white,  C.gray700, 1] :
                                   [C.primary,    C.infoBg, C.primary, 1];  // secondary
    const ctaBtnY = ctaDivY + 8;
    const ctaBtnW = planCardW - 40;
    els.push(rect(`pr_${id}_cb`,   px + 20, ctaBtnY,      ctaBtnW, 36, stroke2, bg2, sw2, true));
    els.push(text(`pr_${id}_cb_t`, px + 20, ctaBtnY + 10, ctaBtnW, 16, cta,     13,  tc2, 'center'));
  });

  write('E01-platform-foundation/pricing.excalidraw', els);
}

/**
 * Settings-Org — US-005, US-006, US-007
 * App shell (Settings nav active). Three sections:
 *   1. Organization Profile — name, logo, timezone, language, creation date
 *   2. Usage — 3 metric cards (workflows, executions, users) + plan badge
 *   3. Danger Zone — delete organization with confirmation description
 *
 * Layout math (all y values from screen top):
 *   cy=80 → profY=124 → row2Y=212 → div1Y=302 → mY=354 → div2Y=442 → dboxY=494
 */
function genSettingsOrg() {
  const navIdx = 4;
  const els    = [];

  els.push(...appShell('so', W, H, NAV, navIdx, 'Settings — Organization'));

  // ── 1. Organization Profile ───────────────────────────────────────────────────
  els.push(...pageHeader('so_ph', cx, cy, cw, 'Organization Profile', [
    { label: 'Save changes', variant: 'primary' },
  ]));

  const profY = cy + 44;  // 124 — first content row starts 16px below the page-header title

  // Logo upload square
  els.push(rect('so_lgbx',  cx,      profY,      80,  80, C.gray300, C.gray50, 1, false));
  els.push(text('so_lgic',  cx,      profY + 24, 80,  32, '⬡',           20, C.primary, 'center'));
  els.push(text('so_lglnk', cx + 4,  profY + 62, 72,  14, 'Change logo', 10, C.primary, 'center'));

  // Organization name
  els.push(text('so_nm_l', cx + 96, profY + 12, 300, 16, 'Organization name', 11, C.gray500));
  els.push(...inputField('so_nm', cx + 96, profY + 30, 500, 'Acme Corp'));

  // Timezone + Language (row 2 — starts at profY+88 = 212)
  const row2Y = profY + 88;
  els.push(text('so_tz_l',   cx + 96,       row2Y,      240, 16, 'Timezone',    11, C.gray500));
  els.push(...selectField('so_tz',   cx + 96,       row2Y + 18, 280, 'UTC+0 · London'));
  els.push(text('so_ln_l',   cx + 96 + 296, row2Y,      180, 16, 'Language',    11, C.gray500));
  els.push(...selectField('so_lang', cx + 96 + 296, row2Y + 18, 200, 'English (US)'));

  // Created date meta (row2 bottom = row2Y+58 = 270; meta 8px below)
  const row2Bottom = row2Y + 58;   // label(16) + gap(2) + input(40)
  els.push(text('so_meta', cx + 96, row2Bottom + 8, 300, 14, 'Created  January 1, 2024', 11, C.gray300));

  // ── 2. Usage ─────────────────────────────────────────────────────────────────
  const div1Y  = row2Bottom + 32;  // 302
  els.push(hline('so_div1', cx, div1Y, cw, C.gray300));

  const usageY = div1Y + 20;       // 322
  els.push(text('so_use_h', cx, usageY, 120, 22, 'Usage', 15, C.gray900));

  // Inline plan badge + upgrade link
  const planBdgW = 'Pro'.length * 8 + 24;  // 48
  els.push(rect('so_pl_b',   cx + 128,                 usageY,     planBdgW, 22, C.infoBorder, C.infoBg, 1, true));
  els.push(text('so_pl_b_t', cx + 128,                 usageY + 4, planBdgW, 14, 'Pro',        11, C.primary, 'center'));
  els.push(text('so_pl_lnk', cx + 128 + planBdgW + 8,  usageY + 4, 80,       14, 'Upgrade →',  11, C.primary));

  const mY = usageY + 32;          // 354
  const mW = Math.floor((cw - 40) / 3);  // 296
  const metrics = [
    { id: 'wf',  label: 'Workflows',             val: '7',     sub: 'of 20 limit' },
    { id: 'ex',  label: 'Executions this month', val: '1,234', sub: 'of 10,000 limit' },
    { id: 'usr', label: 'Team members',           val: '4',     sub: 'of 10 limit' },
  ];
  metrics.forEach(({ id, label, val, sub }, i) => {
    const mx = cx + i * (mW + 20);
    els.push(rect(`so_m_${id}`,     mx,      mY,      mW, 72, C.gray300, C.white, 1, true));
    els.push(text(`so_m_${id}_l`,   mx + 12, mY + 10, mW - 24, 16, label, 11, C.gray500));
    els.push(text(`so_m_${id}_v`,   mx + 12, mY + 28, mW - 24, 24, val,   20, C.gray900));
    els.push(text(`so_m_${id}_sub`, mx + 12, mY + 54, mW - 24, 14, sub,   11, C.gray300));
  });

  // ── 3. Danger Zone ───────────────────────────────────────────────────────────
  const div2Y   = mY + 72 + 16;    // 442
  els.push(hline('so_div2', cx, div2Y, cw, C.gray300));

  const dangerY = div2Y + 20;      // 462
  els.push(text('so_dng_h', cx, dangerY, 300, 22, 'Danger Zone', 15, C.danger));

  const dboxY = dangerY + 32;      // 494
  const dboxH = H - dboxY - PAD;   // 186
  els.push(rect('so_dbox', cx, dboxY, cw, dboxH, C.dangerBorder, C.dangerBg, 1, true));
  els.push(text('so_dbox_t', cx + 16, dboxY + 16, 400, 18, 'Delete organization', 14, C.danger));
  els.push(text('so_dbox_d', cx + 16, dboxY + 38, 620, 14,
    'Permanently delete this organization and all its data. A 30-day grace period applies.', 12, C.gray700));

  // Delete button right-aligned inside the danger box (danger variant)
  const delLabel = 'Delete organization';
  const delBtnX  = cx + cw - (delLabel.length * 8 + 32);  // 996
  els.push(...btn('so_del', delBtnX, dboxY + 12, delLabel, 'danger'));

  write('E01-platform-foundation/settings-org.excalidraw', els);
}

// ─── E02 Identity & Access — Auth screens (no sidebar) ───────────────────────

function genLogin() {
  const els = authCard('li', {
    title: 'Sign in to Axis',
    items: [
      { label: 'Email address', placeholder: 'you@company.com' },
      { label: 'Password',      placeholder: '••••••••' },
    ],
    extraLink: 'Forgot password?',
  }, 'Sign in', "Don't have an account? Sign up");
  write('E02-identity-access/login.excalidraw', els);
}

function genRegister() {
  const els = authCard('reg', {
    title: 'Create your account',
    subtitle: null,
    items: [
      { label: 'Full name',     placeholder: 'Alex Brown' },
      { label: 'Email address', placeholder: 'you@company.com' },
      { label: 'Password',      placeholder: '••••••••' },
    ],
  }, 'Create account', 'Already have an account? Sign in');
  write('E02-identity-access/register.excalidraw', els);
}

function genForgotPassword() {
  const els = authCard('fp', {
    title: 'Reset your password',
    subtitle: 'Enter your email and we will send you a reset link.',
    items: [
      { label: 'Email address', placeholder: 'you@company.com' },
    ],
  }, 'Send reset link', 'Remember your password? Sign in');
  write('E02-identity-access/forgot-password.excalidraw', els);
}

function genChangePassword() {
  const els = authCard('cp', {
    title: 'Choose a new password',
    subtitle: null,
    items: [
      { label: 'New password',     placeholder: '••••••••' },
      { label: 'Confirm password', placeholder: '••••••••' },
    ],
  }, 'Set new password', 'Back to sign in');
  write('E02-identity-access/change-password.excalidraw', els);
}

function genAcceptInvitation() {
  const els = authCard('ai', {
    title: 'You have been invited',
    subtitle: 'Join Acme Corp on Axis',
    items: [
      { label: 'Organization',    placeholder: 'Acme Corp' },
      { label: 'Choose a password', placeholder: '••••••••' },
    ],
  }, 'Accept Invitation', 'Already have an account? Sign in');
  write('E02-identity-access/accept-invitation.excalidraw', els);
}

// ─── E02 Identity & Access — Settings screens (with sidebar) ─────────────────

function genSettingsUsers() {
  const navIdx = 4;
  // Toolbar (search + btn): 40px height. Table starts 16px below → cy+56
  const tblY = cy + 56;
  const tblH = H - tblY - PAD;
  const cols  = [280, 120, 140, 120, 130, 60];
  const xC    = cols.reduce((a, w, i) => [...a, i === 0 ? cx : a[a.length - 1] + cols[i - 1]], []);
  const tblW  = cols.reduce((s, w) => s + w, 0);

  const headers = ['Name / Email', 'Role', 'Status', 'Last Login', 'Invited', 'Actions'];
  const rows = [
    { name: 'Alex Brown', email: 'alex@acme.com',  role: 'Admin',  status: 'active',  login: '2 min ago', inv: 'Jan 1' },
    { name: 'Jane Smith', email: 'jane@acme.com',  role: 'Editor', status: 'active',  login: '1 hr ago',  inv: 'Jan 5' },
    { name: 'Mark J.',    email: 'mark@acme.com',  role: 'Viewer', status: 'pending', login: '—',         inv: 'Today' },
    { name: 'Sarah Lee',  email: 'sarah@acme.com', role: 'Editor', status: 'active',  login: 'Yesterday', inv: 'Jan 8' },
  ];

  const els = [
    ...appShell('su', W, H, NAV, navIdx, 'Settings — Users'),

    // Toolbar: search left, invite button right
    ...searchBar('su_s', cx, cy, 280),
    ...btn('su_inv', cx + cw - 152, cy + 2, '+ Invite User'),

    // Table — 16px gap below toolbar (table border serves as separator)
    rect('su_tbl', cx, tblY, tblW, tblH, C.gray300, 'transparent', 1, false),
    rect('su_th', cx, tblY, tblW, 44, 'transparent', C.gray100, 0, false),
    hline('su_hdiv', cx, tblY + 44, tblW, C.gray300),
    ...headers.map((h, i) => text(`su_h_${i}`, xC[i] + 12, tblY + 12, cols[i] - 16, 20, h, 13, C.gray900)),
    ...xC.slice(1).map((x, i) => vline(`su_vl_${i}`, x, tblY, tblH, C.gray300)),
    ...rows.flatMap(({ name, email, role, status, login, inv }, i) => {
      const y = tblY + 44 + i * 50;
      const sv = status === 'active' ? 'success' : 'warning';
      return [
        rect(`su_row_${i}`, cx, y, tblW, 50, 'transparent', i % 2 === 0 ? C.white : C.gray50, 0, false),
        ...(i < rows.length - 1 ? [hline(`su_hl_${i}`, cx, y + 50, tblW, C.gray300)] : []),
        text(`su_nm_${i}`, xC[0] + 12, y + 8,  cols[0] - 16, 18, name,  13, C.gray900),
        text(`su_em_${i}`, xC[0] + 12, y + 28, cols[0] - 16, 14, email, 11, C.gray500),
        text(`su_rl_${i}`, xC[1] + 12, y + 15, cols[1] - 16, 20, role,  13, C.gray700),
        ...badge(`su_st_${i}`, xC[2] + 12, y + 11, status === 'active' ? 'Active' : 'Pending', sv),
        text(`su_lg_${i}`, xC[3] + 12, y + 15, cols[3] - 16, 20, login, 12, C.gray700),
        text(`su_iv_${i}`, xC[4] + 12, y + 15, cols[4] - 16, 20, inv,   12, C.gray700),
        ...btn(`su_edit_${i}`, xC[5] + 6, y + 7, '…', 'ghost'),
      ];
    }),
    text('su_foot', cx + 12, tblY + tblH + 4, 300, 18, 'Showing 1–4 of 12 users', 12, C.gray500),
  ];
  write('E02-identity-access/settings-users.excalidraw', els);
}

function genSettingsRoles() {
  const navIdx = 4;
  // Use permission matrix from template (S28) — exact same visual as component kit
  // buildPermissionMatrix uses contentDy=48; table w = 190 + 4×120 = 670px
  // Hint text (h=16) ends at cy+52. Matrix placed 10px below → cy+62
  const matrixEls = component(buildPermissionMatrix, cx, cy + 62);
  const els = [
    ...appShell('sr', W, H, NAV, navIdx, 'Settings — Roles & Permissions'),
    text('sr_title', cx, cy, 300, 22, 'Roles & Permissions', 16, C.gray900),
    hline('sr_tdiv', cx, cy + 26, cw, C.gray300),
    text('sr_hint', cx, cy + 36, 500, 16, 'Configure which actions each role can perform across the platform.', 12, C.gray500),
    // Permission matrix from S28 placed below the header
    ...matrixEls,
  ];
  write('E02-identity-access/settings-roles.excalidraw', els);
}

function genSettingsSecurity() {
  const navIdx = 4;
  // Each setting group: title (22) + divider (1) + 16 gap + rows (~64px each)
  const els = [
    ...appShell('ss', W, H, NAV, navIdx, 'Settings — Security'),

    text('ss_s1', cx, cy, 300, 22, 'Password Policy', 16, C.gray900),
    hline('ss_s1div', cx, cy + 26, cw, C.gray300),
    // Row layout rule: label(h=16) center-aligned with input(h=40) → labelY = inputY + 12
    //                  label(h=16) center-aligned with toggle(h=24) → labelY = inputY + 4
    text('ss_min_lbl',  cx, cy + 56,  280, 16, 'Minimum password length', 13, C.gray700),
    ...inputField('ss_min',  cx + 320, cy + 44, 100, '12'),
    text('ss_up_lbl',   cx, cy + 106, 280, 16, 'Require uppercase letters', 13, C.gray700),
    rect('ss_up_chk',   cx + 320, cy + 104, 20, 20, C.primary, C.primary, 1, false),
    text('ss_up_chkt',  cx + 320, cy + 105, 20, 16, '✓', 11, C.white, 'center'),
    text('ss_exp_lbl',  cx, cy + 148, 280, 16, 'Password expiry (days)', 13, C.gray700),
    ...inputField('ss_exp',  cx + 320, cy + 136, 100, '90'),

    text('ss_s2', cx, cy + 200, 300, 22, 'Multi-Factor Authentication', 16, C.gray900),
    hline('ss_s2div', cx, cy + 226, cw, C.gray300),
    text('ss_mfa_lbl', cx, cy + 252, 280, 16, 'Require MFA for all users', 13, C.gray700),
    rect('ss_mfa_tog',  cx + 320, cy + 248, 44, 24, C.primary,     C.primary, 2, true),
    rect('ss_mfa_knob', cx + 342, cy + 250, 20, 20, C.primaryDark, C.white,   1, true),

    text('ss_s3', cx, cy + 304, 300, 22, 'Session Management', 16, C.gray900),
    hline('ss_s3div', cx, cy + 330, cw, C.gray300),
    text('ss_sess_lbl', cx, cy + 360, 280, 16, 'Session timeout (minutes)', 13, C.gray700),
    ...inputField('ss_sess', cx + 320, cy + 348, 100, '60'),

    ...btn('ss_save', cx + cw - 140, cy + 430, 'Save Changes'),
  ];
  write('E02-identity-access/settings-security.excalidraw', els);
}

// ─── E03 Data Modeling ────────────────────────────────────────────────────────

function genDataModels() {
  const navIdx = 0;
  // Toolbar ends at cy+40; cards start 16px below = cy+56
  const cardW = 264, cardH = 130, gap = 16;
  const cols3 = 3;

  const els = [
    ...appShell('dm', W, H, NAV, navIdx, 'Data Models'),
    ...searchBar('dm_s', cx, cy, 280),
    ...btn('dm_add', cx + cw - 144, cy + 2, '+ New Model'),
    ...[
      { name: 'User Profile', fields: 8,  records: '1.2k', status: 'active' },
      { name: 'Order',        fields: 12, records: '4.8k', status: 'active' },
      { name: 'Product',      fields: 6,  records: '312',  status: 'active' },
      { name: 'Invoice',      fields: 14, records: '890',  status: 'active' },
      { name: 'Task',         fields: 9,  records: '2.1k', status: 'draft'  },
      { name: 'Company',      fields: 7,  records: '156',  status: 'active' },
    ].flatMap(({ name, fields, records, status }, i) => {
      const col = i % cols3, row = Math.floor(i / cols3);
      const x = cx + col * (cardW + gap);
      const y = cy + 56 + row * (cardH + gap);
      return [
        rect(`dm_card_${i}`, x, y, cardW, cardH, C.gray300, C.white, 1, true),
        rect(`dm_ch_${i}`,   x, y, cardW, 48, C.gray300, C.gray50, 1, false, { roundness: null }),
        text(`dm_cn_${i}`,   x + 14, y + 14, cardW - 60, 20, name, 14, C.gray900),
        ...badge(`dm_st_${i}`, x + cardW - 72, y + 12, status === 'active' ? 'Active' : 'Draft', status === 'active' ? 'success' : 'draft'),
        text(`dm_fi_${i}`, x + 14, y + 64, 120, 16, `${fields} fields`,   12, C.gray500),
        text(`dm_re_${i}`, x + 14, y + 84, 120, 16, `${records} records`, 12, C.gray500),
        hline(`dm_ca_${i}`, x, y + cardH - 36, cardW, C.gray300),
        ...btn(`dm_open_${i}`, x + 14, y + cardH - 28, 'Open', 'secondary'),
      ];
    }),
  ];
  write('E03-data-modeling/data-models.excalidraw', els);
}

function genDataClasses() {
  const navIdx = 0;
  // Toolbar (40px) + 16px gap = 56px before table
  const tblY = cy + 56;
  const tblH = H - tblY - PAD;
  const cols = [200, 110, 180, 80, 110, 150];
  const xC   = cols.reduce((a, w, i) => [...a, i === 0 ? cx : a[a.length - 1] + cols[i - 1]], []);
  const tblW = cols.reduce((s, w) => s + w, 0);
  const headers = ['Class Name', 'Module', 'Description', 'Fields', 'Status', 'Actions'];
  const rows = [
    { name: 'UserProfile',  mod: 'Identity',  desc: 'User account data',  fields: 8,  status: 'active' },
    { name: 'OrderRecord',  mod: 'Commerce',  desc: 'Order transactions',  fields: 12, status: 'active' },
    { name: 'ProductItem',  mod: 'Inventory', desc: 'Catalog products',    fields: 6,  status: 'draft'  },
    { name: 'InvoiceEntry', mod: 'Finance',   desc: 'Billing records',     fields: 14, status: 'active' },
  ];
  const els = [
    ...appShell('dc', W, H, NAV, navIdx, 'Data Classes'),
    ...searchBar('dc_s', cx, cy, 280),
    ...btn('dc_add', cx + cw - 148, cy + 2, '+ New Class'),
    rect('dc_tbl', cx, tblY, tblW, tblH, C.gray300, 'transparent', 1, false),
    rect('dc_th', cx, tblY, tblW, 44, 'transparent', C.gray100, 0, false),
    hline('dc_hdiv', cx, tblY + 44, tblW, C.gray300),
    ...headers.map((h, i) => text(`dc_h_${i}`, xC[i] + 12, tblY + 12, cols[i] - 16, 20, h, 13, C.gray900)),
    ...xC.slice(1).map((x, i) => vline(`dc_vl_${i}`, x, tblY, tblH, C.gray300)),
    ...rows.flatMap(({ name, mod, desc, fields, status }, i) => {
      const y = tblY + 44 + i * 50;
      return [
        rect(`dc_row_${i}`, cx, y, tblW, 50, 'transparent', i % 2 === 0 ? C.white : C.gray50, 0, false),
        ...(i < rows.length - 1 ? [hline(`dc_hl_${i}`, cx, y + 50, tblW, C.gray300)] : []),
        text(`dc_nm_${i}`, xC[0] + 12, y + 15, cols[0] - 16, 20, name,       13, C.gray900),
        text(`dc_md_${i}`, xC[1] + 12, y + 15, cols[1] - 16, 20, mod,        12, C.gray700),
        text(`dc_ds_${i}`, xC[2] + 12, y + 15, cols[2] - 16, 20, desc,       12, C.gray700),
        text(`dc_fi_${i}`, xC[3] + 12, y + 15, cols[3] - 16, 20, `${fields}`,13, C.gray700),
        ...badge(`dc_st_${i}`, xC[4] + 12, y + 11, status === 'active' ? 'Active' : 'Draft', status === 'active' ? 'success' : 'draft'),
        ...btn(`dc_ed_${i}`, xC[5] + 12, y + 11, 'Edit', 'ghost'),
      ];
    }),
  ];
  write('E03-data-modeling/data-classes.excalidraw', els);
}

function genRecords() {
  const navIdx = 0;
  // Breadcrumb (18px) + gap(8) + search+filter row (40px) + gap(16) = 82px before table
  const tblY = cy + 82;
  const tblH = H - tblY - PAD;
  const cols = [210, 110, 140, 130, 120, 80];
  const xC   = cols.reduce((a, w, i) => [...a, i === 0 ? cx : a[a.length - 1] + cols[i - 1]], []);
  const tblW = cols.reduce((s, w) => s + w, 0);
  const headers = ['Name', 'Status', 'Owner', 'Created', 'Updated', 'Actions'];
  const rows = [
    { name: 'Acme Corporation', status: 'active',  owner: 'Alex Brown', created: 'Jan 12', updated: 'Today'     },
    { name: 'Globex Inc',       status: 'pending', owner: 'Jane Smith',  created: 'Jan 10', updated: '1h ago'   },
    { name: 'Initech Ltd',      status: 'active',  owner: 'Mark J.',     created: 'Jan 8',  updated: 'Yesterday'},
    { name: 'Umbrella Corp',    status: 'draft',   owner: 'Sarah Lee',   created: 'Jan 5',  updated: '3d ago'   },
  ];
  const els = [
    ...appShell('rec', W, H, NAV, navIdx, 'Records — User Profile'),
    // Breadcrumb
    text('rec_bc', cx, cy, 320, 18, 'Data Models  ›  User Profile', 12, C.gray500),
    // Toolbar: search + filter (below breadcrumb with 8px gap)
    ...searchBar('rec_s', cx, cy + 26, 260),
    ...selectField('rec_flt', cx + 276, cy + 26, 160, 'Filter by status'),
    ...btn('rec_add', cx + cw - 136, cy + 28, '+ New Record'),
    // Table (16px below toolbar bottom: cy+26+40+16 = cy+82)
    rect('rec_tbl', cx, tblY, tblW, tblH, C.gray300, 'transparent', 1, false),
    rect('rec_th', cx, tblY, tblW, 44, 'transparent', C.gray100, 0, false),
    hline('rec_hdiv', cx, tblY + 44, tblW, C.gray300),
    ...headers.map((h, i) => text(`rec_h_${i}`, xC[i] + 12, tblY + 12, cols[i] - 16, 20, h, 13, C.gray900)),
    ...xC.slice(1).map((x, i) => vline(`rec_vl_${i}`, x, tblY, tblH, C.gray300)),
    ...rows.flatMap(({ name, status, owner, created, updated }, i) => {
      const y = tblY + 44 + i * 50;
      const sv = status === 'active' ? 'success' : status === 'pending' ? 'warning' : 'draft';
      return [
        rect(`rec_row_${i}`, cx, y, tblW, 50, 'transparent', i % 2 === 0 ? C.white : C.gray50, 0, false),
        ...(i < rows.length - 1 ? [hline(`rec_hl_${i}`, cx, y + 50, tblW, C.gray300)] : []),
        text(`rec_nm_${i}`, xC[0] + 12, y + 15, cols[0] - 16, 20, name,   13, C.gray900),
        ...badge(`rec_st_${i}`, xC[1] + 12, y + 11, status.charAt(0).toUpperCase() + status.slice(1), sv),
        text(`rec_ow_${i}`, xC[2] + 12, y + 15, cols[2] - 16, 20, owner,   12, C.gray700),
        text(`rec_cr_${i}`, xC[3] + 12, y + 15, cols[3] - 16, 20, created, 12, C.gray700),
        text(`rec_up_${i}`, xC[4] + 12, y + 15, cols[4] - 16, 20, updated, 12, C.gray700),
        ...btn(`rec_ed_${i}`, xC[5] + 12, y + 11, 'Edit', 'ghost'),
      ];
    }),
    text('rec_foot', cx + 12, tblY + tblH + 4, 300, 18, 'Showing 1–4 of 142 records', 12, C.gray500),
  ];
  write('E03-data-modeling/records.excalidraw', els);
}

// ─── E04 Workflow Builder ─────────────────────────────────────────────────────

function genWorkflows() {
  const navIdx = 1;
  // Toolbar (40px) + 16px gap = 56px before table
  const tblY = cy + 56;
  const tblH = H - tblY - PAD;
  const cols = [220, 90, 130, 130, 110, 100, 100];
  const xC   = cols.reduce((a, w, i) => [...a, i === 0 ? cx : a[a.length - 1] + cols[i - 1]], []);
  const tblW = cols.reduce((s, w) => s + w, 0);
  const headers = ['Workflow Name', 'Status', 'Trigger', 'Last Run', 'Runs (30d)', 'Duration', 'Actions'];
  const rows = [
    { name: 'Order Processing',  status: 'active', trigger: 'Form Submit', run: '2 min ago',  runs: 142, dur: '14 ms'  },
    { name: 'User Onboarding',   status: 'active', trigger: 'Schedule',    run: '1 hr ago',   runs: 28,  dur: '22 ms'  },
    { name: 'Invoice Generator', status: 'draft',  trigger: 'Manual',      run: '—',          runs: 0,   dur: '—'      },
    { name: 'Email Drip',        status: 'active', trigger: 'Schedule',    run: 'Yesterday',  runs: 56,  dur: '8 ms'   },
    { name: 'Approval Flow',     status: 'paused', trigger: 'Form Submit', run: '3 days ago', runs: 14,  dur: '203 ms' },
  ];
  const els = [
    ...appShell('wf', W, H, NAV, navIdx, 'Workflows'),
    ...searchBar('wf_s', cx, cy, 200),
    ...selectField('wf_flt', cx + 216, cy, 160, 'All statuses'),
    ...btn('wf_add', cx + cw - 168, cy + 2, '+ New Workflow'),
    rect('wf_tbl', cx, tblY, tblW, tblH, C.gray300, 'transparent', 1, false),
    rect('wf_th', cx, tblY, tblW, 44, 'transparent', C.gray100, 0, false),
    hline('wf_hdiv', cx, tblY + 44, tblW, C.gray300),
    ...headers.map((h, i) => text(`wf_h_${i}`, xC[i] + 12, tblY + 12, cols[i] - 16, 20, h, 13, C.gray900)),
    ...xC.slice(1).map((x, i) => vline(`wf_vl_${i}`, x, tblY, tblH, C.gray300)),
    ...rows.flatMap(({ name, status, trigger, run, runs, dur }, i) => {
      const y = tblY + 44 + i * 50;
      const sv = status === 'active' ? 'success' : status === 'paused' ? 'warning' : 'draft';
      return [
        rect(`wf_row_${i}`, cx, y, tblW, 50, 'transparent', i % 2 === 0 ? C.white : C.gray50, 0, false),
        ...(i < rows.length - 1 ? [hline(`wf_hl_${i}`, cx, y + 50, tblW, C.gray300)] : []),
        text(`wf_nm_${i}`, xC[0] + 12, y + 15, cols[0] - 16, 20, name,    13, C.gray900),
        ...badge(`wf_st_${i}`, xC[1] + 12, y + 11, status.charAt(0).toUpperCase() + status.slice(1), sv),
        text(`wf_tr_${i}`, xC[2] + 12, y + 15, cols[2] - 16, 20, trigger, 12, C.gray700),
        text(`wf_ru_${i}`, xC[3] + 12, y + 15, cols[3] - 16, 20, run,     12, C.gray700),
        text(`wf_rn_${i}`, xC[4] + 12, y + 15, cols[4] - 16, 20, `${runs}`, 12, C.gray700),
        text(`wf_du_${i}`, xC[5] + 12, y + 15, cols[5] - 16, 20, dur,     12, C.gray700),
        ...btn(`wf_ed_${i}`, xC[6] + 12, y + 11, 'Edit', 'ghost'),
      ];
    }),
  ];
  write('E04-workflow-builder/workflows.excalidraw', els);
}

function genWorkflowEditor() {
  const navIdx = 1;
  // component(buildWorkflowCanvas, cx, cy) places the 900×340 canvas at (cx, cy).
  // Canvas bg: x=cx=250, w=900 → ends at x=1150 < W=1200. ✓
  const els = [
    ...appShell('we', W, H, NAV, navIdx, 'Workflow Editor — Order Processing'),
    ...component(buildWorkflowCanvas, cx, cy),
  ];
  write('E04-workflow-builder/workflow-editor.excalidraw', els);
}

// ─── E05 Form Builder ─────────────────────────────────────────────────────────

function genForms() {
  const navIdx = 2;
  const tblY = cy + 56;
  const tblH = H - tblY - PAD;
  const cols = [220, 80, 160, 150, 120, 90];
  const xC   = cols.reduce((a, w, i) => [...a, i === 0 ? cx : a[a.length - 1] + cols[i - 1]], []);
  const tblW = cols.reduce((s, w) => s + w, 0);
  const headers = ['Form Name', 'Status', 'Linked Workflow', 'Last Submission', 'Submissions', 'Actions'];
  const rows = [
    { name: 'Contact Form',      status: 'active', wf: 'Order Processing', sub: '5 min ago', total: 342 },
    { name: 'Onboarding Survey', status: 'active', wf: 'User Onboarding',  sub: '1 hr ago',  total: 128 },
    { name: 'Feedback Form',     status: 'draft',  wf: '—',                sub: '—',         total: 0   },
    { name: 'Support Request',   status: 'active', wf: 'Approval Flow',    sub: 'Yesterday', total: 67  },
  ];
  const els = [
    ...appShell('frm', W, H, NAV, navIdx, 'Forms'),
    ...searchBar('frm_s', cx, cy, 240),
    ...btn('frm_add', cx + cw - 140, cy + 2, '+ New Form'),
    rect('frm_tbl', cx, tblY, tblW, tblH, C.gray300, 'transparent', 1, false),
    rect('frm_th', cx, tblY, tblW, 44, 'transparent', C.gray100, 0, false),
    hline('frm_hdiv', cx, tblY + 44, tblW, C.gray300),
    ...headers.map((h, i) => text(`frm_h_${i}`, xC[i] + 12, tblY + 12, cols[i] - 16, 20, h, 13, C.gray900)),
    ...xC.slice(1).map((x, i) => vline(`frm_vl_${i}`, x, tblY, tblH, C.gray300)),
    ...rows.flatMap(({ name, status, wf, sub, total }, i) => {
      const y = tblY + 44 + i * 50;
      return [
        rect(`frm_row_${i}`, cx, y, tblW, 50, 'transparent', i % 2 === 0 ? C.white : C.gray50, 0, false),
        ...(i < rows.length - 1 ? [hline(`frm_hl_${i}`, cx, y + 50, tblW, C.gray300)] : []),
        text(`frm_nm_${i}`, xC[0] + 12, y + 15, cols[0] - 16, 20, name,     13, C.gray900),
        ...badge(`frm_st_${i}`, xC[1] + 12, y + 11, status === 'active' ? 'Active' : 'Draft', status === 'active' ? 'success' : 'draft'),
        text(`frm_wf_${i}`, xC[2] + 12, y + 15, cols[2] - 16, 20, wf,       12, C.gray700),
        text(`frm_sb_${i}`, xC[3] + 12, y + 15, cols[3] - 16, 20, sub,      12, C.gray700),
        text(`frm_to_${i}`, xC[4] + 12, y + 15, cols[4] - 16, 20, `${total}`,12, C.gray700),
        ...btn(`frm_ed_${i}`, xC[5] + 12, y + 11, 'Edit', 'ghost'),
      ];
    }),
  ];
  write('E05-form-builder/forms.excalidraw', els);
}

function genFormEditor() {
  // Mirrors buildBuilderLayout geometry (S31) exactly — same panel widths and heights.
  // Content: form-specific (field palette, form canvas, field properties).
  // IDs use 'fe_' prefix (not 'bl_') to avoid conflicts if ever used together.
  const fW = 900, toolbarH = 44, panelH = 300;
  const lW = 160, cW = 550, rW = 190;   // panel widths — must match S31
  const fx = cx, fy = cy;

  const els = [
    ...appShell('fe', W, H, NAV, 2, 'Form Editor — Contact Form'),

    // Outer frame (matches bl_frame in S31)
    rect('fe_frame', fx, fy, fW, toolbarH + panelH, C.gray300, C.gray100, 1, false),

    // Toolbar (matches bl_tb dimensions)
    rect('fe_tb', fx, fy, fW, toolbarH, C.gray300, C.white, 1, false),
    text('fe_tb_title', fx + 16, fy + 13, 200, 20, '⬡  Form Builder', 14, C.primary),
    ...['Preview', 'Settings'].flatMap((t, i) => [
      rect(`fe_tool_${i}`, fx + 500 + i * 96, fy + 6, 86, 32, C.gray300, 'transparent', 1, true),
      text(`fe_tool_t_${i}`, fx + 500 + i * 96, fy + 14, 86, 16, t, 11, C.gray700, 'center'),
    ]),
    rect('fe_save', fx + fW - 88, fy + 8, 80, 28, C.accentDark, C.accent, 2, true),
    text('fe_save_t', fx + fW - 88, fy + 14, 80, 16, '✓ Save', 12, C.white, 'center'),

    // Left panel: field type palette (matches bl_left)
    rect('fe_left', fx, fy + toolbarH, lW, panelH, C.gray300, C.white, 1, false),
    text('fe_left_t', fx + 12, fy + toolbarH + 12, 136, 18, 'Field Types', 12, C.gray700),
    hline('fe_left_div', fx + 8, fy + toolbarH + 34, lW - 16, C.gray300),
    ...['⬜  Text', '🔢  Number', '⊞  Select', '📅  Date', '☑  Checkbox', '📎  File'].flatMap((f, i) => [
      rect(`fe_f_${i}`, fx + 10, fy + toolbarH + 46 + i * 40, lW - 20, 32, C.gray300, C.gray50, 1, false),
      text(`fe_ft_${i}`, fx + 22, fy + toolbarH + 56 + i * 40, lW - 34, 14, f, 11, C.gray700),
    ]),

    // Center canvas: form preview (matches bl_canvas roughness:0)
    rect('fe_canvas', fx + lW, fy + toolbarH, cW, panelH, C.gray300, C.gray50, 1, false, { roughness: 0 }),
    rect('fe_form_hdr', fx + lW + 20, fy + toolbarH + 14, cW - 40, 40, C.gray300, C.white, 1, true),
    text('fe_form_title', fx + lW + 36, fy + toolbarH + 26, 200, 18, 'Contact Form', 14, C.gray900),
    // Full Name (selected — primary border, matching S04 focus state)
    text('fe_fn_lbl', fx + lW + 20, fy + toolbarH + 66, 160, 14, 'Full Name *', 11, C.gray500),
    rect('fe_fn_inp', fx + lW + 20, fy + toolbarH + 82, cW - 40, 40, C.primary, C.infoBg, 2, true),
    text('fe_fn_ph',  fx + lW + 32, fy + toolbarH + 93, 200, 18, 'Enter your name…', 13, C.gray500),
    // Email
    text('fe_em_lbl', fx + lW + 20, fy + toolbarH + 134, 160, 14, 'Email Address *', 11, C.gray500),
    rect('fe_em_inp', fx + lW + 20, fy + toolbarH + 150, cW - 40, 40, C.gray300, C.white, 1, true),
    text('fe_em_ph',  fx + lW + 32, fy + toolbarH + 161, 200, 18, 'you@company.com', 13, C.gray300),
    // Message
    text('fe_msg_lbl', fx + lW + 20, fy + toolbarH + 202, 160, 14, 'Message', 11, C.gray500),
    rect('fe_msg_inp', fx + lW + 20, fy + toolbarH + 218, cW - 40, 64, C.gray300, C.white, 1, true),
    text('fe_msg_ph',  fx + lW + 32, fy + toolbarH + 229, 200, 18, 'Your message…', 13, C.gray300),

    // Right panel: field properties (matches bl_right)
    rect('fe_right', fx + lW + cW, fy + toolbarH, rW, panelH, C.gray300, C.white, 1, false),
    text('fe_right_t', fx + lW + cW + 12, fy + toolbarH + 12, 160, 18, 'Properties', 12, C.gray700),
    hline('fe_right_div', fx + lW + cW + 8, fy + toolbarH + 34, rW - 16, C.gray300),
    text('fe_rp_lbl1', fx + lW + cW + 12, fy + toolbarH + 48,  120, 14, 'Label',       10, C.gray500),
    rect('fe_rp_i1',   fx + lW + cW + 12, fy + toolbarH + 64,  rW - 24, 32, C.gray300, C.gray100, 1, true),
    text('fe_rp_v1',   fx + lW + cW + 22, fy + toolbarH + 74,  140, 14, 'Full Name',   12, C.gray700),
    text('fe_rp_lbl2', fx + lW + cW + 12, fy + toolbarH + 108, 120, 14, 'Required',    10, C.gray500),
    rect('fe_rp_chk',  fx + lW + cW + 12, fy + toolbarH + 124,  18,  18, C.primary, C.primary, 1, false),
    text('fe_rp_chkt', fx + lW + cW + 12, fy + toolbarH + 125,  18,  16, '✓', 11, C.white, 'center'),
    text('fe_rp_lbl3', fx + lW + cW + 12, fy + toolbarH + 156, 120, 14, 'Placeholder', 10, C.gray500),
    rect('fe_rp_i3',   fx + lW + cW + 12, fy + toolbarH + 172, rW - 24, 32, C.gray300, C.gray100, 1, true),
    text('fe_rp_v3',   fx + lW + cW + 22, fy + toolbarH + 182, 140, 14, 'Enter your name…', 11, C.gray700),
    text('fe_rp_lbl4', fx + lW + cW + 12, fy + toolbarH + 216, 120, 14, 'Field Type',  10, C.gray500),
    rect('fe_rp_i4',   fx + lW + cW + 12, fy + toolbarH + 232, rW - 24, 32, C.gray300, C.gray100, 1, true),
    text('fe_rp_v4',   fx + lW + cW + 22, fy + toolbarH + 242, 120, 14, 'Text',        12, C.gray700),
    text('fe_rp_arr',  fx + lW + cW + rW - 26, fy + toolbarH + 242, 14, 14, '▾', 10, C.gray700),
  ];
  write('E05-form-builder/form-editor.excalidraw', els);
}

function genFormSubmission() {
  // Use buildSideSheet from S21 placed at (cx, cy).
  // The section renders dimmed app context (x=50→cx, w=310) + panel (x=360→cx+310, w=300).
  // Translation: dx = cx-50 = 200, dy = cy-48 = 32
  // Result: app bg at x=250–560, panel at x=560–860. All within W=1200. ✓
  const sideEls = component(buildSideSheet, cx, cy);
  const els = [
    ...appShell('fs', W, H, NAV, 2, 'Form Submission — Contact Form'),
    ...sideEls,
    // ss_title inside the panel header already reads 'Record Detail' — no extra labels needed
  ];
  write('E05-form-builder/form-submission.excalidraw', els);
}

// ─── E06 Workflow Engine ──────────────────────────────────────────────────────

function genExecutions() {
  const navIdx = 3;
  // Toolbar (search + 2× select + btn): 40px. Table starts 16px below → cy+56
  const tblY = cy + 56;
  const tblH = H - tblY - PAD;
  const cols = [120, 100, 150, 120, 120, 100, 100, 60];
  const xC   = cols.reduce((a, w, i) => [...a, i === 0 ? cx : a[a.length - 1] + cols[i - 1]], []);
  const tblW = cols.reduce((s, w) => s + w, 0);
  const headers = ['Execution ID', 'Status', 'Workflow', 'Trigger', 'Started', 'Duration', 'Triggered by', 'Actions'];
  const rows = [
    { id: 'exec-1042', status: 'success', wf: 'Order Processing',  trigger: 'Form Submit', started: '2 min ago', dur: '14 ms',  by: 'System'     },
    { id: 'exec-1041', status: 'running', wf: 'User Onboarding',   trigger: 'Schedule',    started: '5 min ago', dur: '…',      by: 'Schedule'   },
    { id: 'exec-1040', status: 'failed',  wf: 'Invoice Generator', trigger: 'Manual',      started: '1 hr ago',  dur: '203 ms', by: 'Alex Brown' },
    { id: 'exec-1039', status: 'success', wf: 'Approval Flow',     trigger: 'Form Submit', started: 'Yesterday', dur: '8 ms',   by: 'Jane Smith' },
    { id: 'exec-1038', status: 'success', wf: 'Email Drip',        trigger: 'Schedule',    started: 'Yesterday', dur: '22 ms',  by: 'Schedule'   },
  ];
  const statusMap = { success: ['success','Completed'], running: ['info','Running'], failed: ['danger','Failed'] };
  const els = [
    ...appShell('ex', W, H, NAV, navIdx, 'Executions'),
    ...searchBar('ex_s', cx, cy, 200),
    ...selectField('ex_st', cx + 216, cy, 160, 'All statuses'),
    ...selectField('ex_wf', cx + 392, cy, 200, 'All workflows'),
    ...btn('ex_exp', cx + cw - 120, cy + 2, 'Export CSV', 'ghost'),
    rect('ex_tbl', cx, tblY, tblW, tblH, C.gray300, 'transparent', 1, false),
    rect('ex_th', cx, tblY, tblW, 44, 'transparent', C.gray100, 0, false),
    hline('ex_hdiv', cx, tblY + 44, tblW, C.gray300),
    ...headers.map((h, i) => text(`ex_h_${i}`, xC[i] + 12, tblY + 12, cols[i] - 16, 20, h, 13, C.gray900)),
    ...xC.slice(1).map((x, i) => vline(`ex_vl_${i}`, x, tblY, tblH, C.gray300)),
    ...rows.flatMap(({ id, status, wf, trigger, started, dur, by }, i) => {
      const y = tblY + 44 + i * 50;
      const [sv, sl] = statusMap[status];
      return [
        rect(`ex_row_${i}`, cx, y, tblW, 50, 'transparent', i % 2 === 0 ? C.white : C.gray50, 0, false),
        ...(i < rows.length - 1 ? [hline(`ex_hl_${i}`, cx, y + 50, tblW, C.gray300)] : []),
        text(`ex_id_${i}`, xC[0] + 12, y + 15, cols[0] - 16, 20, id,      12, C.primary),
        ...badge(`ex_st_${i}`, xC[1] + 8, y + 11, sl, sv),
        text(`ex_wf_${i}`, xC[2] + 12, y + 15, cols[2] - 16, 20, wf,      12, C.gray700),
        text(`ex_tr_${i}`, xC[3] + 12, y + 15, cols[3] - 16, 20, trigger, 12, C.gray700),
        text(`ex_st2_${i}`,xC[4] + 12, y + 15, cols[4] - 16, 20, started, 12, C.gray700),
        text(`ex_du_${i}`, xC[5] + 12, y + 15, cols[5] - 16, 20, dur,     12, C.gray700),
        text(`ex_by_${i}`, xC[6] + 12, y + 15, cols[6] - 16, 20, by,      12, C.gray700),
        ...btn(`ex_vw_${i}`, xC[7] + 8, y + 11, '→', 'ghost'),
      ];
    }),
    text('ex_foot', cx + 12, tblY + tblH + 4, 300, 18, 'Showing 1–5 of 1,247 executions', 12, C.gray500),
  ];
  write('E06-workflow-engine/executions.excalidraw', els);
}

function genExecutionDetail() {
  const navIdx = 3;
  // Meta header section heights:
  //   breadcrumb (18) + gap(8) + title row (28) + gap(8) = 62px of content above timeline
  // Add 16px breathing room → timeline at cy + 78 (rounded to cy + 80)
  // Timeline panel height = 292px → ends at cy+80+292 = cy+372
  // Retry history at cy+390 gives 18px gap.

  const timelineY = cy + 80;
  const timelineEls = component(buildExecutionTimeline, cx, timelineY);

  const els = [
    ...appShell('ed', W, H, NAV, navIdx, 'Execution Detail — exec-1042'),

    // Breadcrumb
    text('ed_bc', cx, cy, 400, 18, 'Executions  ›  exec-1042', 12, C.gray500),

    // Execution meta row (below breadcrumb with 8px gap)
    text('ed_title',  cx,                cy + 30, 220, 28, 'Run #1042',          20, C.gray900),
    ...badge('ed_st', cx + 188,          cy + 34, 'Completed', 'success'),
    text('ed_meta',   cx + 316,          cy + 38, 340, 16, 'Order Processing · 14 ms · 2 min ago', 12, C.gray500),
    ...btn('ed_retry_ctx', cx + cw - 328, cy + 26, 'Retry with context…', 'ghost'),
    ...btn('ed_retry',     cx + cw - 168, cy + 26, 'Retry', 'ghost'),

    // Execution timeline from S32 — positioned 16px below meta row bottom (cy+30+28+16 = cy+74 → use cy+80)
    ...timelineEls,

    // Retry history (below timeline: cy+80+292+18 = cy+390)
    text('ed_rh_title', cx, cy + 390, 200, 20, 'Retry History', 14, C.gray900),
    rect('ed_rh_panel', cx, cy + 416, 620, 120, C.gray300, C.white, 1, false),
    rect('ed_rh_hdr',   cx, cy + 416, 620,  40, 'transparent', C.gray100, 0, false),
    hline('ed_rh_div',  cx, cy + 456, 620, C.gray300),
    text('ed_rh_r0', cx + 16, cy + 464,  580, 18, '#1 · Completed · 1 hr ago · Triggered by Alex Brown',    12, C.gray700),
    text('ed_rh_r1', cx + 16, cy + 496,  580, 18, '#0 (original) · Failed · 2 hr ago · Triggered by Schedule', 12, C.gray700),
  ];
  write('E06-workflow-engine/execution-detail.excalidraw', els);
}

// ─── Main ─────────────────────────────────────────────────────────────────────

// Shared
genAppShell();

// E01 — Platform Foundation
genRegisterOrg();
genEmailConfirmation();
genVerifyEmail();
genWorkspaceProvisioning();
genPricing();
genSettingsOrg();
genSettingsOrgDeleteModal();

// E02 — auth screens (no sidebar)
genLogin();
genRegister();
genForgotPassword();
genChangePassword();
genAcceptInvitation();

// E02 — settings screens (with sidebar)
genSettingsUsers();
genSettingsRoles();
genSettingsSecurity();

// E03
genDataModels();
genDataClasses();
genRecords();

// E04
genWorkflows();
genWorkflowEditor();

// E05
genForms();
genFormEditor();
genFormSubmission();

// E06
genExecutions();
genExecutionDetail();

console.log('\n✅  All screen wireframes generated.');
