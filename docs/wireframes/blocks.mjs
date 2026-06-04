/**
 * Axis Wireframes — Reusable blocks (single source for screen composition).
 *
 * Screens import blocks here — not hand-drawn duplicates in generate-screens.mjs.
 * Primitives: components.mjs. Large kit sections (tables, canvas): generate-template.mjs (S01–S37).
 */

import {
  C,
  rect,
  text,
  hline,
  arrow,
  fieldLabelBlock,
  inputField,
  inlineTextRow,
  inlineTextRowWidth,
  labelTextWidth,
  wrappedTextBlock,
  semanticVariantColor,
  isPasswordLabel,
  PASSWORD_INPUT_PAD_RIGHT,
  passwordRevealToggle,
  componentContent,
} from './components.mjs';

import { buildAxisLogo, mergeExcalidrawFiles } from './logo.mjs';

export {
  buildAxisLogo,
  AXIS_LOGO_SVG_PATH,
  axisLogoExcalidrawFiles,
  mergeExcalidrawFiles,
} from './logo.mjs';

// ─── Auth layout constants ─────────────────────────────────────────────────────

export const AUTH_CARD_W = 440;
export const AUTH_CARD_PAD_X = 24;
export const AUTH_INNER_W = AUTH_CARD_W - AUTH_CARD_PAD_X * 2;
/** External IdP buttons (Microsoft / Google / GitHub). */
export const AUTH_PROVIDER_BTN_SIZE = 36;
export const AUTH_PROVIDER_GAP = 16;
export const AUTH_PROVIDER_ICON_FONT = 15;
/** Icon row + gap + “or” divider — use after placeAuthExternalSignIn(). */
export const AUTH_EXTERNAL_SIGN_IN_BLOCK_H = AUTH_PROVIDER_BTN_SIZE + 12 + 28;
/** Space below each auth field before the next control. */
export const AUTH_FIELD_STACK_GAP = 12;
/** Approx. block height for authCard layout (no help / with help). */
export const AUTH_FIELD_BLOCK_H = 90;
export const AUTH_FIELD_BLOCK_H_HELP = 110;
export const AUTH_HEADER_H = 112;
export const AUTH_HEADER_H_SUBTITLE = 136;
export const AUTH_CARD_FOOTER_ZONE = 44;
/** Gap below primary submit before footer divider. */
export const AUTH_SUBMIT_AFTER_GAP = 16;

/** Bottom edge of an Excalidraw element (for card height sync). */
export function elementBottom(el) {
  if (el.type === 'line') {
    const pts = el.points || [[0, 0]];
    return el.y + Math.max(...pts.map((p) => p[1]));
  }
  return el.y + (el.height || 0);
}

/**
 * Card height from layout cursor after submit + trailing gap, or from measured content.
 * Pass contentEls to grow the card when blockH math and painted geometry diverge.
 */
export function measureAuthCardHeight(cardY, yAfterSubmitTrailing, contentEls = []) {
  const fromCursor = yAfterSubmitTrailing - cardY + AUTH_CARD_FOOTER_ZONE;
  if (contentEls.length === 0) {
    return fromCursor;
  }
  let maxB = cardY;
  for (const el of contentEls) {
    maxB = Math.max(maxB, elementBottom(el));
  }
  const fromContent = maxB - cardY + AUTH_CARD_FOOTER_ZONE;
  return Math.max(fromCursor, fromContent);
}

/** Canvas height when auth card is taller than default screen H. */
export function authScreenCanvasHeight(cardY, cardH, minHeight = 700) {
  return Math.max(minHeight, cardY + cardH + 24);
}

// ─── External sign-in (Microsoft / Google / GitHub + or) ─────────────────────

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

  const iconFont = AUTH_PROVIDER_ICON_FONT;
  const iconY = yC + Math.round((btnSize - iconFont) / 2) - 1;

  providers.forEach(([id, icon, stroke, bg], i) => {
    const bx = startX + i * (btnSize + gap);
    els.push(rect(id, bx, yC, btnSize, btnSize, stroke, bg, 1, true));
    els.push(text(`${id}_ic`, bx, iconY, btnSize, iconFont + 4, icon, iconFont, stroke, 'center'));
  });

  const yOr = yC + btnSize + 12;
  const mid = 50 + innerW / 2;
  els.push(hline('ext_or_l', 50, yOr + 10, innerW / 2 - 28, C.gray300));
  els.push(text('ext_or_t', mid - 20, yOr, 40, 16, 'or', 11, C.gray500, 'center'));
  els.push(hline('ext_or_r', mid + 28, yOr + 10, innerW / 2 - 28, C.gray300));

  return els;
}

/** Place SSO row at card inner left edge (cardX + AUTH_CARD_PAD_X). */
export function placeAuthExternalSignIn(cardInnerLeftX, y) {
  return componentContent((y0) => buildAuthExternalSignInBlock(y0 + 48), cardInnerLeftX, y, 48);
}

// ─── Auth card chrome ──────────────────────────────────────────────────────────

/** Logo row + divider only (informational / error cards without a form title). */
export function buildAuthCardBrandBar(prefix, cardX, cardY, cardW) {
  const logo = buildAxisLogo(prefix, cardX, cardY + 16, cardW, 'auth');
  return {
    els: [
      ...logo.els,
      hline(`${prefix}_hdiv`, cardX, cardY + 60, cardW, C.gray300),
    ],
    files: logo.files,
  };
}

export function buildAuthCardHeader(prefix, cardX, cardY, cardW, title, subtitle = null) {
  const logo = buildAxisLogo(prefix, cardX, cardY + 16, cardW, 'auth');
  const els = [
    ...logo.els,
    hline(`${prefix}_hdiv`, cardX, cardY + 60, cardW, C.gray300),
    text(`${prefix}_title`, cardX + AUTH_CARD_PAD_X, cardY + 76, cardW - 48, 24, title, 17, C.gray900),
  ];
  if (subtitle) {
    els.push(text(`${prefix}_sub`, cardX + AUTH_CARD_PAD_X, cardY + 104, cardW - 48, 18, subtitle, 13, C.gray700));
  }
  return { els, files: logo.files };
}

/**
 * Auth card footer: gray lead-in + primary link (centered), or whole line as link.
 * @param {string | { lead?: string, link: string, forwardArrow?: boolean }} footer
 *   `forwardArrow` — draw a forward chevron to the right of the link (not a back arrow).
 */
export function buildAuthCardFooter(prefix, cardX, cardY, cardW, cardH, footer) {
  const footerY = cardY + cardH - 32;
  const lineY = footerY + 10;
  const lineH = 16;
  const fontSize = 12;
  const forwardArrow = typeof footer === 'object' && footer.forwardArrow === true;
  const segments = typeof footer === 'string'
    ? [{ text: footer, color: C.primary, link: true }]
    : [
        ...(footer.lead ? [{ text: footer.lead, color: C.gray700 }] : []),
        { text: footer.link, color: C.primary, link: true },
      ];
  const textW = inlineTextRowWidth(segments, fontSize);
  const arrowSlot = forwardArrow ? 12 : 0;
  const totalW = textW + arrowSlot;
  const startX = cardX + Math.round((cardW - totalW) / 2);
  const { els: rowEls } = inlineTextRow(`${prefix}_footer`, startX, lineY, lineH, fontSize, segments);
  const row = [hline(`${prefix}_fdiv`, cardX, footerY, cardW, C.gray300), ...rowEls];
  if (forwardArrow) {
    row.push(arrow(`${prefix}_fwd`, startX + textW + 2, lineY + 5, 8, 0, C.primary, 1.5));
  }
  return row;
}

/** Left-aligned inline row inside auth card (aligns with body copy padding). */
export function buildAuthCardInlineRow(prefix, cardX, cardW, y, segments, fontSize = 12) {
  const startX = cardX + AUTH_CARD_PAD_X;
  const lineH = 16;
  const { els } = inlineTextRow(`${prefix}_inline`, startX, y, lineH, fontSize, segments);
  return els;
}

/** Centered inline row inside auth card. */
export function buildAuthCardCenteredInlineRow(prefix, cardX, cardW, y, segments, fontSize = 12) {
  const innerX = cardX + AUTH_CARD_PAD_X;
  const innerW = cardW - AUTH_CARD_PAD_X * 2;
  const lineH = 16;
  const totalW = inlineTextRowWidth(segments, fontSize);
  const startX = innerX + Math.round((innerW - totalW) / 2);
  const { els } = inlineTextRow(`${prefix}_inline`, startX, y, lineH, fontSize, segments);
  return els;
}

/** Bordered notice inside auth card (success / warning / info). */
export function authNoticeBanner(prefix, x, y, innerW, { title, body }, variant = 'info') {
  const stroke =
    variant === 'success' ? C.successBorder :
      variant === 'warning' ? C.warningBorder :
        variant === 'danger' ? C.dangerBorder :
          C.infoBorder;
  const bg =
    variant === 'success' ? C.successBg :
      variant === 'warning' ? C.warningBg :
        variant === 'danger' ? C.dangerBg :
          C.infoBg;
  const titleColor = semanticVariantColor(variant);
  const pad = 12;
  const content = [];
  let innerH = pad;

  if (title) {
    content.push(text(`${prefix}_nbt`, x + pad, y + innerH, innerW - pad * 2, 18, title, 13, titleColor));
    innerH += 20;
  }
  if (body) {
    const wrapped = wrappedTextBlock(`${prefix}_nbb`, x + pad, y + innerH, innerW - pad * 2, body, 12, C.gray700, 0.85);
    content.push(...wrapped.els);
    innerH += wrapped.blockH + 4;
  }
  innerH += pad;
  return {
    els: [rect(`${prefix}_nb`, x, y, innerW, innerH, stroke, bg, 1, true), ...content],
    blockH: innerH,
  };
}

/** Centered footer: chevron icon + underlined link (icon separate from label text). */
export function buildAuthCardBackFooter(prefix, cardX, cardY, cardW, cardH, linkLabel) {
  const footerY = cardY + cardH - 32;
  const lineY = footerY + 10;
  const lineH = 16;
  const fontSize = 12;
  const label = linkLabel.replace(/^←\s*/, '').trim();
  const iconSlot = 14;
  const iconGap = 6;
  const textW = Math.ceil(labelTextWidth(label, fontSize) + 8);
  const totalW = iconSlot + iconGap + textW;
  const startX = cardX + Math.round((cardW - totalW) / 2);
  const textX = startX + iconSlot + iconGap;
  const segments = [{ text: label, color: C.primary, link: true }];
  const { els: rowEls } = inlineTextRow(`${prefix}_footer`, textX, lineY, lineH, fontSize, segments);
  return [
    hline(`${prefix}_fdiv`, cardX, footerY, cardW, C.gray300),
    arrow(`${prefix}_back_ic`, startX + 10, lineY + 5, -8, 0, C.primary, 1.5),
    ...rowEls,
  ];
}

/** Primary submit button inside auth card (full inner width). */
export function buildAuthSubmitButton(prefix, cardX, y, cardW, label) {
  const btnW = cardW - AUTH_CARD_PAD_X * 2;
  return [
    rect(`${prefix}_sbtn`, cardX + AUTH_CARD_PAD_X, y, btnW, 36, C.accentDark, C.accent, 2, true),
    text(`${prefix}_sbtn_t`, cardX + AUTH_CARD_PAD_X, y + 10, btnW, 16, label, 13, C.white, 'center'),
  ];
}

// ─── Auth form field blocks ────────────────────────────────────────────────────

export const PASSWORD_CRITERIA_ROW_H = 15;
export const PASSWORD_CRITERIA_GAP = 8;

/** Default checklist (happy path — all rules satisfied while typing). */
export const PASSWORD_CRITERIA_MET = [
  { label: 'At least 8 characters', met: true },
  { label: 'Contains a letter', met: true },
  { label: 'Contains a number', met: true },
  { label: 'Contains a special character', met: true },
];

/** Checklist with some rules still unmet (validation / weak password states). */
export const PASSWORD_CRITERIA_PARTIAL = [
  { label: 'At least 8 characters', met: false },
  { label: 'Contains a letter', met: true },
  { label: 'Contains a number', met: false },
  { label: 'Contains a special character', met: false },
];

export function passwordCriteriaBlock(prefix, x, y, innerW, criteria) {
  const els = [];
  criteria.forEach((row, i) => {
    const ry = y + i * PASSWORD_CRITERIA_ROW_H;
    const mark = row.met ? '✓' : '○';
    const color = row.met ? C.success : C.gray500;
    els.push(text(`${prefix}_pc_m${i}`, x, ry, 14, PASSWORD_CRITERIA_ROW_H, mark, 10, color));
    els.push(text(`${prefix}_pc_t${i}`, x + 18, ry, innerW - 18, PASSWORD_CRITERIA_ROW_H, row.label, 10, color));
  });
  return { els, blockH: criteria.length * PASSWORD_CRITERIA_ROW_H };
}

export function authFormField(
  prefix, cardX, y, cardW, label, value, errorMsg = null, required = false, helpText = null,
  isPassword = null, passwordCriteria = null) {
  const x = cardX + AUTH_CARD_PAD_X;
  const innerW = cardW - AUTH_CARD_PAD_X * 2;
  const password = isPassword ?? isPasswordLabel(label);
  const valuePadR = password ? PASSWORD_INPUT_PAD_RIGHT : 24;
  const { els: labelEls, inputY } = fieldLabelBlock(prefix, x, y, innerW, label, { required, helpText });
  const els = [
    ...labelEls,
    rect(`${prefix}_inp`, x, inputY, innerW, 40, errorMsg ? C.dangerBorder : C.gray300, C.white, 1, true),
    text(`${prefix}_val`, x + 12, inputY + 11, innerW - 12 - valuePadR, 18, value, 13, C.gray900),
  ];
  if (password) {
    els.push(...passwordRevealToggle(`${prefix}_pw`, x, inputY, innerW));
  }
  let afterInputY = inputY + 40;
  if (passwordCriteria) {
    const criteria = Array.isArray(passwordCriteria) ? passwordCriteria : PASSWORD_CRITERIA_MET;
    const critY = afterInputY + PASSWORD_CRITERIA_GAP;
    const { els: critEls, blockH: critH } = passwordCriteriaBlock(`${prefix}_pc`, x, critY, innerW, criteria);
    els.push(...critEls);
    afterInputY = critY + critH;
  }
  if (errorMsg) {
    els.push(text(`${prefix}_err`, x, afterInputY + 4, innerW, 14, errorMsg, 11, C.danger));
    afterInputY += 18;
  }
  const blockH = afterInputY - y + AUTH_FIELD_STACK_GAP;
  return { els, blockH };
}

export function authReadOnlyValueField(
  prefix, cardX, y, cardW, label, value, required = false, helpText = null) {
  const x = cardX + AUTH_CARD_PAD_X;
  const innerW = cardW - AUTH_CARD_PAD_X * 2;
  const { els: labelEls, inputY } = fieldLabelBlock(prefix, x, y, innerW, label, { required, helpText });
  const els = [
    ...labelEls,
    rect(`${prefix}_inp`, x, inputY, innerW, 40, C.gray300, C.gray50, 1, true),
    text(`${prefix}_val`, x + 12, inputY + 11, innerW - 24, 18, value, 13, C.gray700),
  ];
  const blockH = inputY + 48 - y + AUTH_FIELD_STACK_GAP;
  return { els, blockH };
}

export function authSlugPreviewField(
  prefix, cardX, y, cardW, errorMsg = null, required = false, helpText = null) {
  const x = cardX + AUTH_CARD_PAD_X;
  const innerW = cardW - AUTH_CARD_PAD_X * 2;
  const slugHelp = helpText ?? 'Unique URL path; auto-generated from organization name.';
  const { els: labelEls, inputY } = fieldLabelBlock(
    prefix, x, y, innerW, 'Organization URL slug', { required, helpText: slugHelp });
  const previewY = inputY + 44;
  const els = [
    ...labelEls,
    rect(`${prefix}_inp`, x, inputY, innerW, 40, errorMsg ? C.dangerBorder : C.gray300, C.gray50, 1, true),
    text(`${prefix}_val`, x + 12, inputY + 11, innerW - 24, 18, 'acme-corp', 13, C.gray700),
    text(`${prefix}_hint`, x, previewY, innerW, 14, 'Preview: axis.app/acme-corp', 10, C.gray500),
  ];
  if (errorMsg) {
    els.push(text(`${prefix}_err`, x, previewY + 16, innerW, 14, errorMsg, 11, C.danger));
  }
  const blockH = (errorMsg ? previewY + 30 : previewY + 14) - y + AUTH_FIELD_STACK_GAP;
  return { els, blockH };
}

export function authTermsRow(prefix, cardX, y, cardW, { checked = true, errorMsg = null } = {}) {
  const x = cardX + AUTH_CARD_PAD_X;
  const innerW = cardW - AUTH_CARD_PAD_X * 2;
  const chkSize = 18;
  const lineH = 16;
  const fontSize = 11;
  const textY = y + Math.round((chkSize - lineH) / 2);
  const textX = x + chkSize + 8;
  const blockH = errorMsg ? 50 : 28;
  const { els: rowEls } = inlineTextRow(`${prefix}_terms`, textX, textY, lineH, fontSize, [
    { text: 'I agree to the ', color: C.gray700 },
    { text: 'Terms of Service', color: C.primary, link: true },
    { text: ' and ', color: C.gray700 },
    { text: 'Privacy Policy', color: C.primary, link: true },
  ]);
  const els = [
    rect(`${prefix}_chk`, x, y, chkSize, chkSize, checked ? C.primary : C.dangerBorder, C.white, 1, true),
    ...rowEls,
  ];
  if (checked) {
    els.push(text(`${prefix}_mark`, x + 4, y + 4, chkSize - 8, 12, '✓', 11, C.primary, 'center'));
  }
  if (errorMsg) {
    els.push(text(`${prefix}_err`, x, y + chkSize + 8, innerW, 14, errorMsg, 11, C.danger));
  }
  return { els, blockH };
}

/**
 * Standalone centered auth card (login, register, forgot password, …).
 * Composes blocks above — use for simple { label, placeholder, required }[] forms.
 */
export function authCard(screenW, screenH, prefix, { title, subtitle = null, items = [], extraLink = null }, submitLabel, footerText) {
  const cardW = AUTH_CARD_W;
  const headerH = subtitle ? AUTH_HEADER_H_SUBTITLE : AUTH_HEADER_H;
  let fieldH = 0;
  items.forEach((item) => {
    fieldH += item.helpText ? AUTH_FIELD_BLOCK_H_HELP : AUTH_FIELD_BLOCK_H;
  });
  const cardH = headerH + fieldH + (extraLink ? 22 : 4) + 36 + 12 + AUTH_CARD_FOOTER_ZONE;
  const cardX = Math.round((screenW - cardW) / 2);
  const cardY = Math.round((screenH - cardH) / 2);
  const els = [];

  const header = buildAuthCardHeader(prefix, cardX, cardY, cardW, title, subtitle);
  const files = { ...header.files };

  els.push(rect(`${prefix}_bg`, 0, 0, screenW, screenH, C.gray300, C.gray100, 1, false));
  els.push(rect(`${prefix}_card`, cardX, cardY, cardW, cardH, C.gray300, C.white, 2, true));
  els.push(...header.els);

  const fieldStartY = cardY + headerH;
  let fy = fieldStartY;
  items.forEach(({ label, placeholder, required = false, helpText = null, password = null }, i) => {
    const x = cardX + AUTH_CARD_PAD_X;
    const innerW = cardW - AUTH_CARD_PAD_X * 2;
    const isPw = password ?? isPasswordLabel(label);
    const { els: labelEls, inputY } = fieldLabelBlock(`${prefix}_fl_${i}`, x, fy, innerW, label, { required, helpText });
    els.push(...labelEls);
    els.push(...inputField(`${prefix}_fi_${i}`, x, inputY, innerW, placeholder, { password: isPw }));
    fy += helpText ? AUTH_FIELD_BLOCK_H_HELP : AUTH_FIELD_BLOCK_H;
  });

  const afterFieldsY = fy;
  if (extraLink) {
    els.push(text(`${prefix}_xl`, cardX + AUTH_CARD_PAD_X, afterFieldsY + 6, cardW - 48, 16, extraLink, 12, C.primary, 'right'));
  }

  const btnY = afterFieldsY + (extraLink ? 22 : 4);
  els.push(...buildAuthSubmitButton(prefix, cardX, btnY, cardW, submitLabel));
  els.push(...buildAuthCardFooter(prefix, cardX, cardY, cardW, cardH, footerText));

  return { els, files };
}

/** Default register-org entry fields (organization onboarding only). */
export const REGISTER_ORG_ENTRY_FIELDS = [
  {
    kind: 'input',
    label: 'Organization name',
    value: 'Acme Corp',
    err: null,
    required: true,
    helpText: '2–100 characters. Shown on invoices and in your workspace.',
  },
  { kind: 'slug', err: null },
  {
    kind: 'input',
    label: 'Organization contact email',
    value: 'admin@company.com',
    err: null,
    required: true,
    helpText: 'Use an official organization mailbox. User sign-in is set up later.',
  },
];

export const REGISTER_ORG_ORG_NAME_HELP = '2–100 characters. Shown on invoices and in your workspace.';

/**
 * Paint register-org entry fields; returns y after last field block.
 * @param {object[]} fields — like REGISTER_ORG_ENTRY_FIELDS
 */
export function paintRegisterOrgEntryFields(els, idPrefix, cardX, y, cardW, fields) {
  let fy = y;
  fields.forEach((f, i) => {
    if (f.kind === 'slug') {
      const { els: slugEls, blockH } = authSlugPreviewField(
        `${idPrefix}_slug`, cardX, fy, cardW, f.err ?? null, true, f.helpText ?? null);
      els.push(...slugEls);
      fy += blockH;
      return;
    }
    const password = f.kind === 'password' || (f.kind === 'input' && isPasswordLabel(f.label));
    const criteria = f.passwordCriteria ?? (f.label === 'Password' ? PASSWORD_CRITERIA_MET : null);
    const { els: fieldEls, blockH } = authFormField(
      `${idPrefix}_f${i}`, cardX, fy, cardW, f.label, f.value, f.err ?? null, f.required !== false, f.helpText ?? null,
      password, criteria);
    els.push(...fieldEls);
    fy += blockH;
  });
  return fy;
}
