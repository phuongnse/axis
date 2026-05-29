/**
 * Axis Screen Wireframes — generate-screens.mjs
 * Run: node docs/wireframes/generate-screens.mjs
 *
 * Reusable UI blocks: blocks.mjs (auth, fields, SSO). Large kit sections: generate-template.mjs via component().
 * Primitives only in components.mjs. Do not duplicate block geometry in this file.
 *
 * Screen width W=1200 ensures 900px template components fit at cx=250
 * (250 + 900 = 1150 < 1200). Never reduce W below 1200.
 *
 * Screens generated:
 *   app-shell (root)
 *   platform-foundation: register-org, register-org-complete, register-org-complete-states,
 *        register-org-states, register-org-provider-states,
 *        email-confirmation, verify-email, verify-email-rate-limit, workspace-provisioning,
 *        pricing, settings-org, settings-org-upload-states, settings-org-profile-states,
 *        settings-org-usage-error, settings-org-free-plan, settings-org-access-denied,
 *        settings-org-deletion-scheduled, settings-org-delete-modal, settings-org-delete-states
 *   identity-access: login, register, forgot-password, change-password,
 *        settings-users, settings-roles, settings-security, accept-invitation
 *   data-modeling: data-models, data-classes, records
 *   workflow-builder: workflows, workflow-editor
 *   form-builder: forms, form-editor, form-submission
 *   workflow-engine: executions, execution-detail
 */

import { buildStatsCards } from './generate-template.mjs';
import { mkdirSync } from 'fs';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';

import {
  C, SB, HDR, CX, CY,
  rect, ellipse, text, hline, vline, arrow,
  btn, inputField, selectField, badge, searchBar, pageHeader, fieldLabel,
  appShell, component, translate, writeExcalidraw, setSeed,
  stateHeadline, semanticVariantColor, wrappedTextBlock,
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

import {
  AUTH_CARD_W,
  AUTH_CARD_PAD_X,
  AUTH_EXTERNAL_SIGN_IN_BLOCK_H,
  AUTH_HEADER_H,
  AUTH_HEADER_H_SUBTITLE,
  AUTH_CARD_FOOTER_ZONE,
  AUTH_FIELD_STACK_GAP,
  AUTH_SUBMIT_AFTER_GAP,
  measureAuthCardHeight,
  authScreenCanvasHeight,
  buildAxisLogo,
  placeAuthExternalSignIn,
  buildAuthCardBrandBar,
  buildAuthCardHeader,
  buildAuthCardFooter,
  buildAuthCardBackFooter,
  buildAuthCardCenteredInlineRow,
  buildAuthSubmitButton,
  authFormField,
  authTermsRow,
  authCard,
  mergeExcalidrawFiles,
  REGISTER_ORG_ENTRY_FIELDS,
  PASSWORD_CRITERIA_PARTIAL,
  paintRegisterOrgEntryFields,
  paintRegisterOrgCompleteFields,
} from './blocks.mjs';

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

// platform-foundation auth outcome cards — shared shell + headline rhythm
const AUTH_CARD_PAD = 20;
const AUTH_SHELL_H = 36;       // mini logo + divider
const AUTH_HEADLINE_H = 34;    // icon row + short underline
const AUTH_BODY_GAP = 6;

function deterministicSeedForScreen(screenKey) {
  let hash = 0;
  for (let i = 0; i < screenKey.length; i += 1) {
    hash = (hash * 31 + screenKey.charCodeAt(i)) >>> 0;
  }
  return 1001 + (hash % 500000) * 2;
}

function runScreen(screenKey, generator) {
  const filter = process.env.SCREEN_FILTER ?? '';
  if (filter && !screenKey.includes(filter)) {
    return;
  }
  setSeed(deterministicSeedForScreen(screenKey));
  generator();
}

// ─── Write helper ─────────────────────────────────────────────────────────────
// Outputs land flat in docs/use-cases/{domain}/{use-case}/ (see patterns.md).

const SCREEN_USE_CASE_OVERRIDES = {
  'platform-foundation/register-org.excalidraw':
    'platform-foundation/register-org/register-org.excalidraw',
  'platform-foundation/register-org-states.excalidraw':
    'platform-foundation/register-org/register-org-states.excalidraw',
  'platform-foundation/register-org-complete.excalidraw':
    'platform-foundation/register-org/register-org-complete.excalidraw',
  'platform-foundation/register-org-complete-states.excalidraw':
    'platform-foundation/register-org/register-org-complete-states.excalidraw',
  'platform-foundation/register-org-provider-states.excalidraw':
    'platform-foundation/register-org/register-org-provider-states.excalidraw',
  'platform-foundation/email-confirmation.excalidraw':
    'platform-foundation/register-org/email-confirmation.excalidraw',
  'platform-foundation/workspace-provisioning.excalidraw':
    'platform-foundation/provision-tenant/workspace-provisioning.excalidraw',
  'platform-foundation/pricing.excalidraw':
    'platform-foundation/view-plans/pricing.excalidraw',
  'platform-foundation/settings-org.excalidraw':
    'platform-foundation/org-settings/settings-org.excalidraw',
  'platform-foundation/settings-org-upload-states.excalidraw':
    'platform-foundation/org-profile/settings-org-upload-states.excalidraw',
  'platform-foundation/settings-org-profile-states.excalidraw':
    'platform-foundation/org-profile/settings-org-profile-states.excalidraw',
  'platform-foundation/settings-org-usage-error.excalidraw':
    'platform-foundation/org-settings/settings-org-usage-error.excalidraw',
  'platform-foundation/settings-org-free-plan.excalidraw':
    'platform-foundation/org-settings/settings-org-free-plan.excalidraw',
  'platform-foundation/settings-org-access-denied.excalidraw':
    'platform-foundation/org-settings/settings-org-access-denied.excalidraw',
  'platform-foundation/settings-org-deletion-scheduled.excalidraw':
    'platform-foundation/delete-org/settings-org-deletion-scheduled.excalidraw',
  'platform-foundation/settings-org-delete-modal.excalidraw':
    'platform-foundation/delete-org/settings-org-delete-modal.excalidraw',
  'platform-foundation/settings-org-delete-states.excalidraw':
    'platform-foundation/delete-org/settings-org-delete-states.excalidraw',
  'identity-access/login.excalidraw': 'identity-access/sign-in/login.excalidraw',
  'identity-access/login-unverified.excalidraw':
    'identity-access/sign-in/login-unverified.excalidraw',
  'identity-access/register.excalidraw': 'identity-access/sign-in/register.excalidraw',
  'identity-access/forgot-password.excalidraw':
    'identity-access/reset-password/forgot-password.excalidraw',
  'identity-access/change-password.excalidraw':
    'identity-access/change-password/change-password.excalidraw',
  'identity-access/accept-invitation.excalidraw':
    'identity-access/accept-invite/accept-invitation.excalidraw',
  'identity-access/settings-users.excalidraw':
    'identity-access/invite-user/settings-users.excalidraw',
  'identity-access/settings-roles.excalidraw':
    'identity-access/list-roles/settings-roles.excalidraw',
  'identity-access/settings-security.excalidraw':
    'identity-access/sessions/settings-security.excalidraw',
  'data-modeling/data-models.excalidraw': 'data-modeling/create-model/data-models.excalidraw',
  'data-modeling/data-classes.excalidraw':
    'data-modeling/create-model/data-classes.excalidraw',
  'data-modeling/records.excalidraw': 'data-modeling/create-model/records.excalidraw',
  'workflow-builder/workflows.excalidraw':
    'workflow-builder/list-workflows/workflows.excalidraw',
  'workflow-builder/workflow-editor.excalidraw':
    'workflow-builder/create-workflow/workflow-editor.excalidraw',
  'form-builder/forms.excalidraw': 'form-builder/list-forms/forms.excalidraw',
  'form-builder/form-editor.excalidraw': 'form-builder/create-form/form-editor.excalidraw',
  'form-builder/form-submission.excalidraw':
    'form-builder/create-form/form-submission.excalidraw',
  'workflow-engine/executions.excalidraw':
    'workflow-engine/org-execution-history/executions.excalidraw',
  'workflow-engine/execution-detail.excalidraw':
    'workflow-engine/isolated-steps/execution-detail.excalidraw',
};

function resolveUseCaseWireframePath(relativePath) {
  if (SCREEN_USE_CASE_OVERRIDES[relativePath]) {
    return SCREEN_USE_CASE_OVERRIDES[relativePath];
  }
  const parts = relativePath.split('/');
  if (parts.length >= 3) {
    return relativePath;
  }
  if (parts.length === 2) {
    const [domain, file] = parts;
    const slug = file.replace(/\.excalidraw$/, '');
    return `${domain}/${slug}/${file}`;
  }
  return relativePath;
}

function write(relativePath, elements, files = {}) {
  const resolved = resolveUseCaseWireframePath(relativePath);
  const parts = resolved.split('/');
  let full;
  if (parts.length === 1) {
    full = join(__dir, resolved);
  } else {
    full = join(__dir, '..', 'use-cases', ...parts);
  }
  mkdirSync(dirname(full), { recursive: true });
  writeExcalidraw(full, elements, files);
  const fileCount = Object.keys(files).length;
  const filesNote = fileCount > 0 ? `, ${fileCount} embedded file(s)` : '';
  console.log(`✓  ${resolved}  (${elements.length} elements${filesNote})`);
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

// ─── platform-foundation Platform Foundation ─────────────────────────────────────────────────

/**
 * Register-Org — self-service org registration (email/password + ADR-027 providers).
 * SSO buttons, email form, auto-generated slug, Terms/Privacy acceptance.
 */
function genRegisterOrg() {
  const cardW = AUTH_CARD_W;
  const cardX = Math.round((W - cardW) / 2);
  const cardY = 16;
  const headerH = AUTH_HEADER_H;
  const els = [];
  let wireFiles = {};

  let y = cardY + headerH;
  const contentEls = [];

  const header = buildAuthCardHeader('ro', cardX, cardY, cardW, 'Create your organization');
  contentEls.push(...header.els);
  wireFiles = mergeExcalidrawFiles(wireFiles, header.files);
  contentEls.push(...placeAuthExternalSignIn(cardX + AUTH_CARD_PAD_X, y));
  y += AUTH_EXTERNAL_SIGN_IN_BLOCK_H + AUTH_FIELD_STACK_GAP;

  y = paintRegisterOrgEntryFields(contentEls, 'ro', cardX, y, cardW, REGISTER_ORG_ENTRY_FIELDS);

  const { els: termsEls, blockH: termsH } = authTermsRow('ro_terms', cardX, y, cardW, { checked: true });
  contentEls.push(...termsEls);
  y += termsH + AUTH_FIELD_STACK_GAP;

  contentEls.push(...buildAuthSubmitButton('ro', cardX, y, cardW, 'Create organization'));
  y += 36 + AUTH_SUBMIT_AFTER_GAP;

  const cardH = measureAuthCardHeight(cardY, y, contentEls);
  const screenH = authScreenCanvasHeight(cardY, cardH, H);

  els.push(rect('ro_bg', 0, 0, W, screenH, C.gray300, C.gray100, 1, false));
  els.push(rect('ro_card', cardX, cardY, cardW, cardH, C.gray300, C.white, 2, true));
  els.push(...contentEls);
  els.push(...buildAuthCardFooter('ro', cardX, cardY, cardW, cardH, {
    lead: 'Already have an account? ',
    link: 'Sign in',
  }));

  write('platform-foundation/register-org.excalidraw', els, wireFiles);
}

/**
 * Register-Org complete — post-OAuth step (ADR-027). Org name + Terms collected here;
 * email read-only from provider; admin name pre-filled and editable.
 */
function genRegisterOrgComplete() {
  const cardW = AUTH_CARD_W;
  const cardX = Math.round((W - cardW) / 2);
  const cardY = 40;
  const headerH = AUTH_HEADER_H_SUBTITLE;
  const els = [];

  const contentEls = [];
  let wireFiles = {};
  const rocHeader = buildAuthCardHeader(
    'roc', cardX, cardY, cardW, 'Finish setting up your organization', 'Signed in with Microsoft');
  contentEls.push(...rocHeader.els);
  wireFiles = mergeExcalidrawFiles(wireFiles, rocHeader.files);

  let y = cardY + headerH;
  y = paintRegisterOrgCompleteFields(contentEls, 'roc', cardX, y, cardW);
  y += AUTH_FIELD_STACK_GAP;

  contentEls.push(...buildAuthSubmitButton('roc', cardX, y, cardW, 'Create organization'));
  y += 36 + AUTH_SUBMIT_AFTER_GAP;

  const cardH = measureAuthCardHeight(cardY, y, contentEls);
  const screenH = authScreenCanvasHeight(cardY, cardH, H);

  els.push(rect('roc_bg', 0, 0, W, screenH, C.gray300, C.gray100, 1, false));
  els.push(rect('roc_card', cardX, cardY, cardW, cardH, C.gray300, C.white, 2, true));
  els.push(...contentEls);
  els.push(...buildAuthCardFooter('roc', cardX, cardY, cardW, cardH, '← Back to registration'));

  write('platform-foundation/register-org-complete.excalidraw', els, wireFiles);
}

/**
 * Register-Org complete — validation states (org name, terms) after external sign-in.
 */
function genRegisterOrgCompleteStates() {
  const els = [];
  let wireFiles = {};
  const panelW = 520;
  const panelH = 520;
  const gap = 40;
  const startX = Math.round((W - (panelW * 2 + gap)) / 2);
  const y0 = 48;

  els.push(rect('rocs_bg', 0, 0, W, H, C.gray300, C.gray100, 1, false));
  els.push(text('rocs_pg', 0, 16, W, 20,
    'Post-OAuth completion — validation', 13, C.gray500, 'center'));

  const drawPanel = ({ id, x, lbl, orgErr, terms }) => {
    const cardY = y0 + 28;
    const prefix = `rocs_${id}`;
    els.push(text(`${prefix}_lbl`, x, y0, panelW, 16, lbl, 12, C.danger));
    els.push(rect(`${prefix}_card`, x, cardY, panelW, panelH, C.gray300, C.white, 2, true));
    const panelHeader = buildAuthCardHeader(
      prefix, x, cardY, panelW, 'Finish setting up your organization', 'Signed in with Google');
    els.push(...panelHeader.els);
    wireFiles = mergeExcalidrawFiles(wireFiles, panelHeader.files);

    const btnY = cardY + panelH - 68;
    paintRegisterOrgCompleteFields(els, prefix, x, cardY + AUTH_HEADER_H_SUBTITLE, panelW, {
      orgName: orgErr ? 'A' : 'Acme Corp',
      orgErr,
      terms,
    });
    els.push(...buildAuthSubmitButton(prefix, x, btnY, panelW, 'Create organization'));
  };

  drawPanel({
    id: 'val',
    x: startX,
    lbl: 'Organization name validation',
    orgErr: 'Must be between 2 and 100 characters.',
    terms: { checked: true },
  });
  drawPanel({
    id: 'terms',
    x: startX + panelW + gap,
    lbl: 'Terms not accepted',
    orgErr: null,
    terms: { checked: false, errorMsg: 'You must accept the Terms of Service and Privacy Policy.' },
  });

  write('platform-foundation/register-org-complete-states.excalidraw', els, wireFiles);
}

/** Draw one register-org error-state panel (shared by states screens). Returns embedded logo files. */
function registerOrgStatePanel(els, { id, x, y0, panelW, panelH, lbl, lblColor, serverBanner, fields, terms }) {
  const cardY = y0 + 28;
  const prefix = `ros_${id}`;
  els.push(text(`${prefix}_lbl`, x, y0, panelW, 16, lbl, 12, lblColor));
  els.push(rect(`${prefix}_card`, x, cardY, panelW, panelH, C.gray300, C.white, 2, true));
  const panelHeader = buildAuthCardHeader(prefix, x, cardY, panelW, 'Create your organization');
  els.push(...panelHeader.els);

  let fy = cardY + AUTH_HEADER_H;
  if (serverBanner) {
    els.push(rect(`${prefix}_ban`, x + AUTH_CARD_PAD_X, fy, panelW - AUTH_CARD_PAD_X * 2, 40, C.dangerBorder, C.dangerBg, 1, true));
    els.push(text(`${prefix}_ban_t`, x + AUTH_CARD_PAD_X + 12, fy + 12, panelW - AUTH_CARD_PAD_X * 2 - 24, 16, serverBanner, 12, C.danger));
    fy += 52;
  }

  fy = paintRegisterOrgEntryFields(els, prefix, x, fy, panelW, fields);

  if (terms) {
    const { els: te, blockH: termsBlockH } = authTermsRow(`${prefix}_terms`, x, fy, panelW, terms);
    els.push(...te);
    fy += termsBlockH + AUTH_FIELD_STACK_GAP;
  }

  const btnY = cardY + panelH - 68;
  els.push(...buildAuthSubmitButton(prefix, x, btnY, panelW, 'Create organization'));
  return panelHeader.files;
}

const REGISTER_ORG_STATE_FIELDS = REGISTER_ORG_ENTRY_FIELDS.map((f) => ({
  ...f,
  value: f.kind === 'input' && f.label === 'Organization name'
    ? "O'Brien & Co."
    : f.kind === 'input' && f.label === 'Email address'
      ? 'alex@company.com'
      : f.value,
}));

/**
 * Register-Org states — inline validation, terms required, server error (5xx).
 */
function genRegisterOrgStates() {
  const els = [];
  let wireFiles = {};
  const panelW = 520;
  const panelH = 640;
  const gap = 40;
  const startX = Math.round((W - (panelW * 2 + gap)) / 2);
  const y0 = 40;

  els.push(rect('ros_bg', 0, 0, W, H, C.gray300, C.gray100, 1, false));
  els.push(text('ros_pg', 0, 16, W, 20, 'Registration form — validation & server errors', 13, C.gray500, 'center'));

  wireFiles = mergeExcalidrawFiles(wireFiles, registerOrgStatePanel(els, {
    id: 'val',
    x: startX,
    y0,
    panelW,
    panelH,
    lbl: 'Inline validation (field-level)',
    lblColor: C.danger,
    serverBanner: null,
    fields: [
      { kind: 'input', label: 'Organization name', value: 'A', err: 'Must be between 2 and 100 characters.', required: true },
      { kind: 'slug' },
      { kind: 'input', label: 'Admin full name', value: 'Alex Brown', err: null, required: true },
      { kind: 'input', label: 'Email address', value: 'not-an-email', err: 'Enter a valid email address.', required: true },
      {
        kind: 'password',
        label: 'Password',
        value: '•••',
        err: 'Must be at least 8 characters with a letter and a number.',
        required: true,
        passwordCriteria: PASSWORD_CRITERIA_PARTIAL,
      },
      { kind: 'input', label: 'Confirm password', value: '••••••••', err: 'Passwords do not match.', required: true },
    ],
    terms: { checked: true },
  }));

  wireFiles = mergeExcalidrawFiles(wireFiles, registerOrgStatePanel(els, {
    id: 'srv',
    x: startX + panelW + gap,
    y0,
    panelW,
    panelH,
    lbl: 'Server error (5xx)',
    lblColor: C.danger,
    serverBanner: 'Something went wrong, please try again.',
    fields: REGISTER_ORG_STATE_FIELDS,
    terms: { checked: true },
  }));

  write('platform-foundation/register-org-states.excalidraw', els, wireFiles);
}

/**
 * Register-Org provider states — external identity provider error paths (ADR-027).
 */
function genRegisterOrgProviderStates() {
  const els = [];
  let wireFiles = {};
  const panelW = 520;
  const panelH = 300;
  const gap = 40;
  const startX = Math.round((W - (panelW * 2 + gap)) / 2);
  const cardY = 72;

  els.push(rect('rops_bg', 0, 0, W, H, C.gray300, C.gray100, 1, false));
  els.push(text('rops_pg', 0, 24, W, 20, 'Registration — external provider errors', 13, C.gray500, 'center'));

  const panels = [
    {
      id: 'dup',
      x: startX,
      lbl: 'Provider email already registered',
      msg: 'An account with this email already exists. Sign in instead.',
      variant: 'warning',
    },
    {
      id: 'noemail',
      x: startX + panelW + gap,
      lbl: 'Provider returned no verified email',
      msg: 'Your GitHub account has no verified email; use email and password instead.',
      variant: 'danger',
    },
  ];

  panels.forEach(({ id, x, lbl, msg, variant }) => {
    const icon = variant === 'warning' ? '⚠' : '✕';
    const prefix = `rops_${id}`;
    els.push(text(`${prefix}_lbl`, x, 48, panelW, 16, lbl, 12, C.danger));
    els.push(rect(`${prefix}_card`, x, cardY, panelW, panelH, C.gray300, C.white, 2, true));
    const brand = buildAuthCardBrandBar(prefix, x, cardY, panelW);
    els.push(...brand.els);
    wireFiles = mergeExcalidrawFiles(wireFiles, brand.files);
    const ix = x + AUTH_CARD_PAD_X;
    const innerW = panelW - AUTH_CARD_PAD_X * 2;
    const headY = cardY + 76;
    els.push(...stateHeadline(prefix, ix, headY, innerW, icon, variant, 'Registration could not continue', 14));
    const bodyY = headY + AUTH_HEADLINE_H + AUTH_BODY_GAP;
    els.push(text(`${prefix}_body`, ix, bodyY, innerW, 36, msg, 13, C.gray700));
    els.push(text(`${prefix}_link`, ix, bodyY + 48, innerW, 16, 'Back to registration →', 12, C.primary, 'center'));
  });

  write('platform-foundation/register-org-provider-states.excalidraw', els, wireFiles);
}

/**
 * Email-Confirmation — registration success, email verification resend
 * Informational card (no form): compact icon, title, copy, resend link.
 */
function genEmailConfirmation() {
  const cardW = AUTH_CARD_W;
  const cardX = Math.round((W - cardW) / 2);
  const ecInnerW = cardW - AUTH_CARD_PAD_X * 2;
  const ecX = cardX + AUTH_CARD_PAD_X;
  const ecHeadY = 68;
  const ecBodyY = ecHeadY + AUTH_HEADLINE_H + AUTH_BODY_GAP;
  const bodyCopy =
    'If an account exists for this email, you will receive a verification link shortly.';
  const body1Size = wrappedTextBlock('ec_body1', 0, 0, ecInnerW, bodyCopy, 13, C.gray700, 0.78);
  const body2Size = wrappedTextBlock('ec_body2', 0, 0, ecInnerW, 'Check your inbox.', 13, C.gray700, 0.78);
  const resendY = ecBodyY + body1Size.blockH + 8 + body2Size.blockH + 14;
  const cardH = resendY + 24 + AUTH_CARD_FOOTER_ZONE;
  const cardY = Math.round((H - cardH) / 2);
  const screenH = authScreenCanvasHeight(cardY, cardH, H);
  const els = [];

  els.push(rect('ec_bg', 0, 0, W, screenH, C.gray300, C.gray100, 1, false));
  els.push(rect('ec_card', cardX, cardY, cardW, cardH, C.gray300, C.white, 2, true));

  const ecBrand = buildAuthCardBrandBar('ec', cardX, cardY, cardW);
  els.push(...ecBrand.els);
  const wireFiles = ecBrand.files;

  els.push(...stateHeadline('ec', ecX, cardY + ecHeadY, ecInnerW, '✉', 'info', 'Check your email', 16));
  const body1 = wrappedTextBlock('ec_body1', ecX, cardY + ecBodyY, ecInnerW, bodyCopy, 13, C.gray700, 0.78);
  els.push(...body1.els);
  const body2 = wrappedTextBlock(
    'ec_body2',
    ecX,
    cardY + ecBodyY + body1.blockH + 8,
    ecInnerW,
    'Check your inbox.',
    13,
    C.gray700,
    0.78,
  );
  els.push(...body2.els);
  els.push(...buildAuthCardCenteredInlineRow('ec_resend', cardX, cardW, cardY + resendY, [
    { text: "Didn't receive it?", color: C.gray700 },
    { text: 'Resend email →', color: C.primary, link: true },
  ]));
  els.push(...buildAuthCardBackFooter('ec', cardX, cardY, cardW, cardH, 'Back to sign in'));

  write('platform-foundation/email-confirmation.excalidraw', els, wireFiles);
}

/**
 * Verify-Email — email verification (all 4 outcome states)
 * 2×2 grid of state cards: success, expired, already-used, invalid.
 * Each card: 440×176. Grid centred at W=1200.
 */
function genVerifyEmail() {
  const els   = [];
  const cardW = AUTH_CARD_W;
  const cardH = 176;
  const gapX  = 60;
  const gapY  = 36;   // row gap; first 20px reserved for state label above card
  const col1X = Math.round((W - (cardW * 2 + gapX)) / 2);  // 130
  const col2X = col1X + cardW + gapX;
  const row1Y = 100;
  const row2Y = row1Y + cardH + gapY;

  els.push(rect('ve_bg', 0, 0, W, H, C.gray300, C.gray100, 1, false));
  els.push(text('ve_pg', 0, 48, W, 20, 'Email Verification — States', 13, C.gray500, 'center'));

  const states = [
    {
      id: 've_ok',   x: col1X, y: row1Y,
      stateLbl: '✓  Success',       lblColor: C.success, variant: 'success', icon: '✓',
      title: 'Email verified!',
      body:  'Your account is ready. Signing you in…',
      btnLabel: null, btnVariant: null,
    },
    {
      id: 've_exp',  x: col2X, y: row1Y,
      stateLbl: 'Expired link',     lblColor: C.warning, variant: 'warning', icon: '⏱',
      title: 'Verification link expired',
      body:  'This link was valid for 24 hours.',
      btnLabel: 'Resend verification email', btnVariant: 'secondary',
    },
    {
      id: 've_used', x: col1X, y: row2Y,
      stateLbl: 'Already verified', lblColor: C.gray500, variant: 'neutral', icon: '✓',
      title: 'Already verified',
      body:  'This link has already been used. Please sign in.',
      btnLabel: 'Sign in', btnVariant: 'ghost',
    },
    {
      id: 've_inv',  x: col2X, y: row2Y,
      stateLbl: 'Invalid token',    lblColor: C.danger, variant: 'danger', icon: '✕',
      title: 'Invalid verification link',
      body:  'This link is invalid or has been tampered with.',
      btnLabel: null, btnVariant: null,
    },
  ];

  states.forEach(({ id, x, y, stateLbl, lblColor, variant, icon, title, body, btnLabel, btnVariant }) => {
    els.push(text(`${id}_lbl`,   x,      y - 20,  cardW,      16, stateLbl, 12, lblColor));
    els.push(rect(`${id}_card`,  x,      y,       cardW,      cardH, C.gray300, C.white, 2, true));
    els.push(text(`${id}_logo`,  x,      y + 12,  cardW,      18, '⬡  Axis', 12, C.primary, 'center'));
    els.push(hline(`${id}_hdiv`, x, y + 34, cardW, C.gray300));
    const ix = x + AUTH_CARD_PAD;
    const innerW = cardW - AUTH_CARD_PAD * 2;
    const headY = y + 38;
    els.push(...stateHeadline(`${id}`, ix, headY, innerW, icon, variant, title, 14));
    els.push(text(`${id}_body`, ix, headY + AUTH_HEADLINE_H + AUTH_BODY_GAP, innerW, 36, body, 12, C.gray700));
    if (btnLabel) {
      els.push(...btn(`${id}_cta`, ix, y + cardH - 40, btnLabel, btnVariant));
    }
  });

  write('platform-foundation/verify-email.excalidraw', els);
}

function genVerifyEmailRateLimit() {
  const cardW = AUTH_CARD_W;
  const cardH = 228;
  const cardX = Math.round((W - cardW) / 2);
  const cardY = Math.round((H - cardH) / 2);
  const els = [];

  els.push(rect('vrl_bg', 0, 0, W, H, C.gray300, C.gray100, 1, false));
  els.push(text('vrl_lbl', 0, 76, W, 18, 'Resend rate limit state', 12, C.warning, 'center'));
  els.push(rect('vrl_card', cardX, cardY, cardW, cardH, C.gray300, C.white, 2, true));
  els.push(text('vrl_logo', cardX, cardY + 12, cardW, 18, '⬡  Axis', 12, C.primary, 'center'));
  els.push(hline('vrl_hdiv', cardX, cardY + 34, cardW, C.gray300));

  const vrlX = cardX + AUTH_CARD_PAD;
  const vrlInnerW = cardW - AUTH_CARD_PAD * 2;
  const vrlHeadY = cardY + 38;
  els.push(...stateHeadline('vrl', vrlX, vrlHeadY, vrlInnerW, '⏳', 'warning', 'Please wait before requesting another email.', 14));
  const vrlBodyY = vrlHeadY + AUTH_HEADLINE_H + AUTH_BODY_GAP;
  els.push(text('vrl_body', vrlX, vrlBodyY, vrlInnerW, 32, 'You reached the resend limit (3 requests per hour).', 12, C.gray700));
  els.push(text('vrl_cnt', vrlX, vrlBodyY + 34, vrlInnerW, 18, 'Try again in 37 minutes.', 13, semanticVariantColor('warning')));

  els.push(rect('vrl_btn', cardX + AUTH_CARD_PAD, cardY + cardH - 40, 220, 36, C.gray300, C.gray100, 1, true));
  els.push(text('vrl_btn_t', cardX + AUTH_CARD_PAD, cardY + cardH - 30, 220, 16, 'Resend verification email', 13, C.gray300, 'center'));
  els.push(text('vrl_hint', cardX + 252, cardY + cardH - 26, 168, 16, 'Button disabled until timer ends', 10, C.gray500, 'center'));

  write('platform-foundation/verify-email-rate-limit.excalidraw', els);
}

/**
 * Workspace-Provisioning — tenant provisioning (2 states side by side)
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

  write('platform-foundation/workspace-provisioning.excalidraw', els);
}

/**
 * Settings-Org Delete Modal — organization deletion
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

  // Confirm input (required — must match org name exactly)
  els.push(...fieldLabel('sdm_inp_l', mX + 20, mY + 130, 'Organization name', { required: true }));
  els.push(text('sdm_inp_hint', mX + 20, mY + 146, mW - 40, 14,
    "Type 'Acme Corp' to confirm (case-sensitive)", 10, C.gray500));
  els.push(...inputField('sdm_inp', mX + 20, mY + 162, mW - 40, 'Acme Corp'));

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

  write('platform-foundation/settings-org-delete-modal.excalidraw', els);
}

/**
 * Delete modal states — organization deletion enabled confirm + queue failure.
 */
function genSettingsOrgDeleteStates() {
  const els = [];
  const panelW = 520;
  const panelH = 320;
  const gap = 80;
  const startX = Math.round((W - (panelW * 2 + gap)) / 2);
  const y0 = 120;

  els.push(rect('sods_bg', 0, 0, W, H, C.gray300, C.gray100, 1, false));
  els.push(text('sods_pg', 0, 48, W, 20, 'Delete organization — modal states', 13, C.gray500, 'center'));

  const modals = [
    {
      id: 'en',
      x: startX,
      lbl: 'Name matched — delete enabled',
      lblColor: C.danger,
      inp: 'Acme Corp',
      delEnabled: true,
      err: null,
    },
    {
      id: 'err',
      x: startX + panelW + gap,
      lbl: 'Deletion queue failed',
      lblColor: C.danger,
      inp: 'Acme Corp',
      delEnabled: true,
      err: 'Could not schedule deletion. Please try again.',
    },
  ];

  modals.forEach(({ id, x, lbl, lblColor, inp, delEnabled, err }) => {
    const mY = y0 + 24;
    els.push(text(`sods_${id}_lbl`, x, y0, panelW, 16, lbl, 12, lblColor));
    els.push(rect(`sods_${id}_dim`, x, mY, panelW, panelH, C.gray300, C.gray50, 1, false));
    const mW = 440;
    const mH = 260;
    const mX = x + Math.round((panelW - mW) / 2);
    const mCardY = mY + 30;
    els.push(rect(`sods_${id}_card`, mX, mCardY, mW, mH, C.gray700, C.white, 2, true));
    els.push(text(`sods_${id}_ti`, mX + 20, mCardY + 18, mW - 40, 24, 'Delete organization', 16, C.danger));
    els.push(hline(`sods_${id}_hd`, mX, mCardY + 50, mW, C.gray300));
    if (err) {
      els.push(rect(`sods_${id}_ban`, mX + 20, mCardY + 62, mW - 40, 36, C.dangerBorder, C.dangerBg, 1, true));
      els.push(text(`sods_${id}_ban_t`, mX + 32, mCardY + 72, mW - 64, 16, err, 12, C.danger));
    }
    const inpY = err ? mCardY + 108 : mCardY + 68;
    els.push(...fieldLabel(`sods_${id}_il`, mX + 20, inpY, 'Organization name', { required: true }));
    els.push(text(`sods_${id}_ih`, mX + 20, inpY + 16, mW - 40, 14,
      "Type 'Acme Corp' to confirm (case-sensitive)", 10, C.gray500));
    els.push(...inputField(`sods_${id}_in`, mX + 20, inpY + 32, mW - 40, inp));
    const delY = mCardY + mH - 48;
    if (delEnabled) {
      els.push(...btn(`sods_${id}_del`, mX + mW - 20 - 184, delY, 'Delete organization', 'danger'));
    } else {
      els.push(rect(`sods_${id}_del`, mX + mW - 20 - 184, delY, 184, 36, C.gray300, C.gray100, 1, true));
      els.push(text(`sods_${id}_del_t`, mX + mW - 20 - 184, delY + 10, 184, 16, 'Delete organization', 13, C.gray300, 'center'));
    }
    els.push(...btn(`sods_${id}_can`, mX + 20, delY, 'Cancel', 'ghost'));
  });

  write('platform-foundation/settings-org-delete-states.excalidraw', els);
}

/**
 * Pricing — plan selection at registration, public pricing page
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

  write('platform-foundation/pricing.excalidraw', els);
}

/**
 * Settings-Org — profile, usage, organization deletion
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

  // Organization name (required); timezone / language optional
  els.push(...fieldLabel('so_nm_l', cx + 96, profY + 12, 'Organization name', { required: true }));
  els.push(...inputField('so_nm', cx + 96, profY + 30, 500, 'Acme Corp'));

  // Timezone + Language (row 2 — starts at profY+88 = 212)
  const row2Y = profY + 88;
  els.push(...fieldLabel('so_tz_l', cx + 96, row2Y, 'Timezone'));
  els.push(...selectField('so_tz', cx + 96, row2Y + 18, 280, 'UTC+0 · London'));
  els.push(...fieldLabel('so_ln_l', cx + 96 + 296, row2Y, 'Language'));
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

  write('platform-foundation/settings-org.excalidraw', els);
}

function genSettingsOrgUploadStates() {
  const els = [];
  els.push(rect('sou_bg', 0, 0, W, H, C.gray300, C.gray100, 1, false));
  els.push(text('sou_t', 0, 48, W, 20, 'Logo upload states', 13, C.gray500, 'center'));

  const cardW = 340;
  const cardH = 220;
  const gap = 30;
  const startX = Math.round((W - (cardW * 3 + gap * 2)) / 2);
  const y = 120;
  const states = [
    { id: 'inv', title: 'Invalid file type/size', msg: 'Only PNG/JPG/SVG up to 2 MB.', stroke: C.dangerBorder, bg: C.dangerBg, txt: C.danger },
    { id: 'up', title: 'Uploading logo...', msg: 'Uploading 68% · Please keep this tab open.', stroke: C.infoBorder, bg: C.infoBg, txt: C.primary },
    { id: 'nav', title: 'Changes in progress', msg: 'You have an upload in progress. Leave page?', stroke: C.warningBorder, bg: C.warningBg, txt: C.warning },
  ];

  states.forEach((s, i) => {
    const x = startX + i * (cardW + gap);
    els.push(rect(`sou_${s.id}_c`, x, y, cardW, cardH, C.gray300, C.white, 1, true));
    els.push(text(`sou_${s.id}_h`, x + 16, y + 16, cardW - 32, 20, s.title, 14, C.gray900));
    els.push(rect(`sou_${s.id}_a`, x + 16, y + 48, cardW - 32, 56, s.stroke, s.bg, 1, true));
    els.push(text(`sou_${s.id}_m`, x + 26, y + 68, cardW - 52, 20, s.msg, 12, s.txt));
    els.push(text(`sou_${s.id}_l`, x + 16, y + 122, cardW - 32, 16, 'Logo upload field', 11, C.gray500));
    els.push(rect(`sou_${s.id}_f`, x + 16, y + 140, cardW - 32, 40, C.gray300, C.white, 1, true));
    els.push(text(`sou_${s.id}_p`, x + 28, y + 151, cardW - 56, 16, i === 1 ? 'acme-logo.png' : 'Select a file…', 13, C.gray500));
  });

  write('platform-foundation/settings-org-upload-states.excalidraw', els);
}

/**
 * Settings profile save states — profile settings inline validation, API error, success toast.
 */
function genSettingsOrgProfileStates() {
  const els = [];
  els.push(rect('sops_bg', 0, 0, W, H, C.gray300, C.gray100, 1, false));
  els.push(text('sops_t', 0, 48, W, 20, 'Organization profile — save states', 13, C.gray500, 'center'));

  const cardW = 360;
  const cardH = 240;
  const gap = 30;
  const startX = Math.round((W - (cardW * 3 + gap * 2)) / 2);
  const y = 100;

  const states = [
    {
      id: 'val',
      title: 'Inline validation',
      stroke: C.dangerBorder,
      bg: C.dangerBg,
      msg: 'Organization name must be between 2 and 100 characters.',
      msgColor: C.danger,
      fieldErr: true,
      toast: null,
    },
    {
      id: 'api',
      title: 'Save failed (network/5xx)',
      stroke: C.dangerBorder,
      bg: C.dangerBg,
      msg: 'Could not save changes. Your edits are still in the form.',
      msgColor: C.danger,
      fieldErr: false,
      toast: null,
    },
    {
      id: 'ok',
      title: 'Save succeeded',
      stroke: C.successBorder,
      bg: C.successBg,
      msg: 'Changes saved — reflected in header without reload.',
      msgColor: C.success,
      fieldErr: false,
      toast: 'Organization profile saved',
    },
  ];

  states.forEach((s, i) => {
    const x = startX + i * (cardW + gap);
    els.push(rect(`sops_${s.id}_c`, x, y, cardW, cardH, C.gray300, C.white, 1, true));
    els.push(text(`sops_${s.id}_h`, x + 16, y + 16, cardW - 32, 20, s.title, 14, C.gray900));
    els.push(rect(`sops_${s.id}_a`, x + 16, y + 44, cardW - 32, 48, s.stroke, s.bg, 1, true));
    els.push(text(`sops_${s.id}_m`, x + 26, y + 58, cardW - 52, 32, s.msg, 12, s.msgColor));
    els.push(...fieldLabel(`sops_${s.id}_nl`, x + 16, y + 108, 'Organization name', { required: true }));
    const inpStroke = s.fieldErr ? C.dangerBorder : C.gray300;
    els.push(rect(`sops_${s.id}_inp`, x + 16, y + 126, cardW - 32, 40, inpStroke, C.white, 1, true));
    els.push(text(`sops_${s.id}_v`, x + 28, y + 137, cardW - 56, 18, 'Acme Corp', 13, C.gray900));
    if (s.fieldErr) {
      els.push(text(`sops_${s.id}_e`, x + 16, y + 170, cardW - 32, 14, s.msg, 11, C.danger));
    }
    els.push(...btn(`sops_${s.id}_save`, x + 16, y + cardH - 52, 'Save changes', 'primary'));
    if (s.toast) {
      els.push(rect(`sops_${s.id}_tst`, x + cardW - 200, y + 8, 184, 32, C.successBorder, C.successBg, 1, true));
      els.push(text(`sops_${s.id}_tst_t`, x + cardW - 192, y + 16, 168, 16, s.toast, 11, C.success, 'center'));
    }
  });

  write('platform-foundation/settings-org-profile-states.excalidraw', els);
}

function genSettingsOrgUsageError() {
  const els = [];
  els.push(...appShell('sue', W, H, NAV, 4, 'Settings — Organization'));
  els.push(text('sue_h', cx, cy, 300, 28, 'Usage', 20, C.gray900));
  els.push(text('sue_sub', cx, cy + 30, 420, 16, 'Usage stats are temporarily unavailable.', 12, C.gray700));

  const mY = cy + 62;
  const mW = Math.floor((cw - 40) / 3);
  ['Workflows', 'Executions this month', 'Team members'].forEach((label, i) => {
    const x = cx + i * (mW + 20);
    els.push(rect(`sue_c_${i}`, x, mY, mW, 100, C.gray300, C.white, 1, true));
    els.push(text(`sue_l_${i}`, x + 12, mY + 12, mW - 24, 16, label, 11, C.gray500));
    els.push(text(`sue_v_${i}`, x + 12, mY + 36, mW - 24, 24, '—', 20, C.gray500));
    els.push(...btn(`sue_r_${i}`, x + 12, mY + 60, 'Retry', 'ghost'));
  });

  write('platform-foundation/settings-org-usage-error.excalidraw', els);
}

/** usage settings edge case — free plan usage without denominator limits. */
function genSettingsOrgFreePlan() {
  const navIdx = 4;
  const els = [];
  els.push(...appShell('sofp', W, H, NAV, navIdx, 'Settings — Organization'));
  els.push(text('sofp_h', cx, cy, 400, 28, 'Usage (Free plan — no configured limits)', 18, C.gray900));

  const usageY = cy + 40;
  els.push(rect('sofp_pl_b', cx + 128, usageY, 56, 22, C.infoBorder, C.infoBg, 1, true));
  els.push(text('sofp_pl_t', cx + 128, usageY + 4, 56, 14, 'Free', 11, C.primary, 'center'));

  const mY = usageY + 36;
  const mW = Math.floor((cw - 40) / 3);
  [
    { label: 'Workflows', val: '12' },
    { label: 'Executions this month', val: '340' },
    { label: 'Team members', val: '3' },
  ].forEach(({ label, val }, i) => {
    const x = cx + i * (mW + 20);
    els.push(rect(`sofp_m_${i}`, x, mY, mW, 72, C.gray300, C.white, 1, true));
    els.push(text(`sofp_ml_${i}`, x + 12, mY + 10, mW - 24, 16, label, 11, C.gray500));
    els.push(text(`sofp_mv_${i}`, x + 12, mY + 28, mW - 24, 24, val, 20, C.gray900));
    els.push(text(`sofp_ms_${i}`, x + 12, mY + 54, mW - 24, 14, 'no limit configured', 11, C.gray300));
  });

  write('platform-foundation/settings-org-free-plan.excalidraw', els);
}

/** usage settings — non-admin receives 403 (redirect target shown as message). */
function genSettingsOrgAccessDenied() {
  const navIdx = 4;
  const els = [];
  els.push(...appShell('soad', W, H, NAV, navIdx, 'Settings'));
  const boxW = 480;
  const boxX = cx + Math.round((cw - boxW) / 2);
  const boxY = cy + 80;
  els.push(rect('soad_box', boxX, boxY, boxW, 160, C.gray300, C.white, 2, true));
  els.push(...stateHeadline('soad', boxX + 24, boxY + 24, boxW - 48, '✕', 'danger', 'Access denied', 16));
  els.push(text('soad_body', boxX + 24, boxY + 88, boxW - 48, 40,
    'You need the Admin role to view organization settings.\nRedirecting to dashboard…', 13, C.gray700));
  els.push(...btn('soad_home', boxX + 24, boxY + 116, 'Go to dashboard', 'secondary'));

  write('platform-foundation/settings-org-access-denied.excalidraw', els);
}

function genSettingsOrgDeletionScheduled() {
  const els = [];
  els.push(...appShell('sds', W, H, NAV, 4, 'Settings — Organization'));

  const by = cy;
  els.push(rect('sds_b', cx, by, cw, 88, C.warningBorder, C.warningBg, 1, true));
  els.push(text('sds_t', cx + 16, by + 14, cw - 220, 20, 'Your organization is scheduled for deletion', 14, C.warning));
  els.push(text('sds_d', cx + 16, by + 36, cw - 220, 32, 'All data will be permanently removed on Feb 14, 2026.\nYou can cancel deletion during the grace period.', 12, C.gray700));
  els.push(...btn('sds_cancel', cx + cw - 180, by + 26, 'Cancel deletion', 'secondary'));

  els.push(text('sds_stub', cx, by + 120, 400, 20, 'Organization Profile', 16, C.gray900));
  els.push(rect('sds_stub_card', cx, by + 150, cw, 170, C.gray300, C.white, 1, true));
  els.push(text('sds_stub_txt', cx + 20, by + 222, cw - 40, 16, 'Settings content continues below…', 12, C.gray500, 'center'));

  write('platform-foundation/settings-org-deletion-scheduled.excalidraw', els);
}

// ─── identity-access Identity & Access — Auth screens (no sidebar) ───────────────────────

function genLogin() {
  const { els, files } = authCard(W, H, 'li', {
    title: 'Sign in to Axis',
    items: [
      { label: 'Email address', placeholder: 'you@company.com', required: true },
      { label: 'Password',      placeholder: '••••••••', required: true },
    ],
    extraLink: 'Forgot password?',
  }, 'Sign in', { lead: "Don't have an account? ", link: 'Sign up' });
  write('identity-access/login.excalidraw', els, files);
}

/** email verification (tenant-registration) / unverified sign-in — unverified email blocks sign-in. */
function genLoginUnverified() {
  const cardW = AUTH_CARD_W;
  const cardH = 280;
  const cardX = Math.round((W - cardW) / 2);
  const cardY = Math.round((H - cardH) / 2);
  const els = [];
  els.push(rect('lu_bg', 0, 0, W, H, C.gray300, C.gray100, 1, false));
  els.push(rect('lu_card', cardX, cardY, cardW, cardH, C.gray300, C.white, 2, true));
  const luBrand = buildAxisLogo('lu', cardX, cardY + 16, cardW, 'auth');
  els.push(...luBrand.els);
  const wireFiles = luBrand.files;
  els.push(hline('lu_hdiv', cardX, cardY + 60, cardW, C.gray300));
  els.push(text('lu_title', cardX + 24, cardY + 76, cardW - 48, 24, 'Sign in to Axis', 17, C.gray900));

  const ix = cardX + AUTH_CARD_PAD;
  const innerW = cardW - AUTH_CARD_PAD * 2;
  let fy = cardY + 112;
  [
    { label: 'Email address', value: 'alex@company.com', required: true },
    { label: 'Password', value: '••••••••', required: true },
  ].forEach((f, i) => {
    const { els: fe, blockH } = authFormField(
      `lu_f${i}`, cardX, fy, cardW, f.label, f.value, null, f.required === true);
    els.push(...fe);
    fy += blockH;
  });

  const headY = fy + 8;
  els.push(...stateHeadline('lu_blk', ix, headY, innerW, '✉', 'warning', 'Please verify your email before signing in.', 14));
  els.push(text('lu_resend', ix, headY + AUTH_HEADLINE_H + 8, innerW, 16, 'Resend verification email →', 12, C.primary, 'center'));

  const btnY = cardY + cardH - 68;
  const btnW = cardW - 48;
  els.push(rect('lu_sbtn', cardX + 24, btnY, btnW, 36, C.gray300, C.gray100, 1, true));
  els.push(text('lu_sbtn_t', cardX + 24, btnY + 10, btnW, 16, 'Sign in', 13, C.gray300, 'center'));

  write('identity-access/login-unverified.excalidraw', els, wireFiles);
}

function genRegister() {
  const { els, files } = authCard(W, H, 'reg', {
    title: 'Create your account',
    subtitle: null,
    items: [
      { label: 'Full name',     placeholder: 'Alex Brown', required: true },
      { label: 'Email address', placeholder: 'you@company.com', required: true },
      { label: 'Password',      placeholder: '••••••••', required: true },
    ],
  }, 'Create account', { lead: 'Already have an account? ', link: 'Sign in' });
  write('identity-access/register.excalidraw', els, files);
}

function genForgotPassword() {
  const { els, files } = authCard(W, H, 'fp', {
    title: 'Reset your password',
    subtitle: 'Enter your email and we will send you a reset link.',
    items: [
      { label: 'Email address', placeholder: 'you@company.com', required: true },
    ],
  }, 'Send reset link', { lead: 'Remember your password? ', link: 'Sign in' });
  write('identity-access/forgot-password.excalidraw', els, files);
}

function genChangePassword() {
  const { els, files } = authCard(W, H, 'cp', {
    title: 'Choose a new password',
    subtitle: null,
    items: [
      { label: 'New password',     placeholder: '••••••••', required: true },
      { label: 'Confirm password', placeholder: '••••••••', required: true },
    ],
  }, 'Set new password', { link: 'Back to sign in' });
  write('identity-access/change-password.excalidraw', els, files);
}

function genAcceptInvitation() {
  const { els, files } = authCard(W, H, 'ai', {
    title: 'You have been invited',
    subtitle: 'Join Acme Corp on Axis',
    items: [
      { label: 'Organization',      placeholder: 'Acme Corp', required: false },
      { label: 'Choose a password', placeholder: '••••••••', required: true },
    ],
  }, 'Accept Invitation', { lead: 'Already have an account? ', link: 'Sign in' });
  write('identity-access/accept-invitation.excalidraw', els, files);
}

// ─── identity-access Identity & Access — Settings screens (with sidebar) ─────────────────

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
  write('identity-access/settings-users.excalidraw', els);
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
  write('identity-access/settings-roles.excalidraw', els);
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
  write('identity-access/settings-security.excalidraw', els);
}

// ─── data-modeling Data Modeling ────────────────────────────────────────────────────────

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
  write('data-modeling/data-models.excalidraw', els);
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
  write('data-modeling/data-classes.excalidraw', els);
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
  write('data-modeling/records.excalidraw', els);
}

// ─── workflow-builder Workflow Builder ─────────────────────────────────────────────────────

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
  write('workflow-builder/workflows.excalidraw', els);
}

function genWorkflowEditor() {
  const navIdx = 1;
  // component(buildWorkflowCanvas, cx, cy) places the 900×340 canvas at (cx, cy).
  // Canvas bg: x=cx=250, w=900 → ends at x=1150 < W=1200. ✓
  const els = [
    ...appShell('we', W, H, NAV, navIdx, 'Workflow Editor — Order Processing'),
    ...component(buildWorkflowCanvas, cx, cy),
  ];
  write('workflow-builder/workflow-editor.excalidraw', els);
}

// ─── form-builder Form Builder ─────────────────────────────────────────────────────────

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
  write('form-builder/forms.excalidraw', els);
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
    ...fieldLabel('fe_fn_lbl', fx + lW + 20, fy + toolbarH + 66, 'Full name', { required: true }),
    rect('fe_fn_inp', fx + lW + 20, fy + toolbarH + 82, cW - 40, 40, C.primary, C.infoBg, 2, true),
    text('fe_fn_ph',  fx + lW + 32, fy + toolbarH + 93, 200, 18, 'Enter your name…', 13, C.gray500),
    // Email (required)
    ...fieldLabel('fe_em_lbl', fx + lW + 20, fy + toolbarH + 134, 'Email address', { required: true }),
    rect('fe_em_inp', fx + lW + 20, fy + toolbarH + 150, cW - 40, 40, C.gray300, C.white, 1, true),
    text('fe_em_ph',  fx + lW + 32, fy + toolbarH + 161, 200, 18, 'you@company.com', 13, C.gray300),
    // Message (optional)
    ...fieldLabel('fe_msg_lbl', fx + lW + 20, fy + toolbarH + 202, 'Message'),
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
  write('form-builder/form-editor.excalidraw', els);
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
  write('form-builder/form-submission.excalidraw', els);
}

// ─── workflow-engine Workflow Engine ──────────────────────────────────────────────────────

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
  write('workflow-engine/executions.excalidraw', els);
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
  write('workflow-engine/execution-detail.excalidraw', els);
}

// ─── Main ─────────────────────────────────────────────────────────────────────

// Shared
runScreen('app-shell', genAppShell);

// platform-foundation — Platform Foundation
runScreen('platform-foundation/register-org', genRegisterOrg);
runScreen('platform-foundation/register-org-complete', genRegisterOrgComplete);
runScreen('platform-foundation/register-org-complete-states', genRegisterOrgCompleteStates);
runScreen('platform-foundation/register-org-states', genRegisterOrgStates);
runScreen('platform-foundation/register-org-provider-states', genRegisterOrgProviderStates);
runScreen('platform-foundation/email-confirmation', genEmailConfirmation);
runScreen('platform-foundation/verify-email', genVerifyEmail);
runScreen('platform-foundation/verify-email-rate-limit', genVerifyEmailRateLimit);
runScreen('platform-foundation/workspace-provisioning', genWorkspaceProvisioning);
runScreen('platform-foundation/pricing', genPricing);
runScreen('platform-foundation/settings-org', genSettingsOrg);
runScreen('platform-foundation/settings-org-upload-states', genSettingsOrgUploadStates);
runScreen('platform-foundation/settings-org-profile-states', genSettingsOrgProfileStates);
runScreen('platform-foundation/settings-org-usage-error', genSettingsOrgUsageError);
runScreen('platform-foundation/settings-org-free-plan', genSettingsOrgFreePlan);
runScreen('platform-foundation/settings-org-access-denied', genSettingsOrgAccessDenied);
runScreen('platform-foundation/settings-org-deletion-scheduled', genSettingsOrgDeletionScheduled);
runScreen('platform-foundation/settings-org-delete-modal', genSettingsOrgDeleteModal);
runScreen('platform-foundation/settings-org-delete-states', genSettingsOrgDeleteStates);

// identity-access — auth screens (no sidebar)
runScreen('identity-access/login', genLogin);
runScreen('identity-access/login-unverified', genLoginUnverified);
runScreen('identity-access/register', genRegister);
runScreen('identity-access/forgot-password', genForgotPassword);
runScreen('identity-access/change-password', genChangePassword);
runScreen('identity-access/accept-invitation', genAcceptInvitation);

// identity-access — settings screens (with sidebar)
runScreen('identity-access/settings-users', genSettingsUsers);
runScreen('identity-access/settings-roles', genSettingsRoles);
runScreen('identity-access/settings-security', genSettingsSecurity);

// data-modeling
runScreen('data-modeling/data-models', genDataModels);
runScreen('data-modeling/data-classes', genDataClasses);
runScreen('data-modeling/records', genRecords);

// workflow-builder
runScreen('workflow-builder/workflows', genWorkflows);
runScreen('workflow-builder/workflow-editor', genWorkflowEditor);

// form-builder
runScreen('form-builder/forms', genForms);
runScreen('form-builder/form-editor', genFormEditor);
runScreen('form-builder/form-submission', genFormSubmission);

// workflow-engine
runScreen('workflow-engine/executions', genExecutions);
runScreen('workflow-engine/execution-detail', genExecutionDetail);

console.log('\n✅  All screen wireframes generated.');
