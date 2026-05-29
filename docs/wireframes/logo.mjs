/**
 * Axis brand logo for wireframes — mark SVG + "Axis" wordmark in Excalidraw.
 */

import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

import { nextSeed, BASE, C, text } from './components.mjs';

const __dir = dirname(fileURLToPath(import.meta.url));

/** Canonical logo asset (commit changes here; sync frontend copy when mark changes). */
export const AXIS_LOGO_SVG_PATH = join(__dir, 'assets', 'axis-logo.svg');

export const AXIS_MARK_AUTH_W = 32;
export const AXIS_MARK_AUTH_H = 32;
export const AXIS_MARK_COMPACT_W = 24;
export const AXIS_MARK_COMPACT_H = 24;

export const AXIS_WORD_GAP = 10;
export const AXIS_WORD_AUTH_W = 42;
export const AXIS_WORD_COMPACT_W = 34;

/** Auth lockup: mark + word (viewBox mark 36x36). */
export const AXIS_LOGO_AUTH_W = AXIS_MARK_AUTH_W + AXIS_WORD_GAP + AXIS_WORD_AUTH_W;
export const AXIS_LOGO_AUTH_H = AXIS_MARK_AUTH_H;

export const AXIS_LOGO_COMPACT_W = AXIS_MARK_COMPACT_W + 8 + AXIS_WORD_COMPACT_W;
export const AXIS_LOGO_COMPACT_H = AXIS_MARK_COMPACT_H;

const LOGO_FILE_ID = 'axis_brand_logo_svg';

let logoFileBundle = null;

export function axisLogoExcalidrawFiles() {
  if (logoFileBundle === null) {
    const svg = readFileSync(AXIS_LOGO_SVG_PATH, 'utf-8');
    const dataURL = `data:image/svg+xml;base64,${Buffer.from(svg, 'utf-8').toString('base64')}`;
    logoFileBundle = {
      [LOGO_FILE_ID]: {
        mimeType: 'image/svg+xml',
        id: LOGO_FILE_ID,
        dataURL,
        created: Date.now(),
      },
    };
  }
  return logoFileBundle;
}

export function imageElement(id, fileId, x, y, w, h) {
  const s = nextSeed();
  return {
    ...BASE,
    id,
    type: 'image',
    x,
    y,
    width: w,
    height: h,
    angle: 0,
    strokeColor: 'transparent',
    backgroundColor: 'transparent',
    fillStyle: 'solid',
    strokeWidth: 1,
    strokeStyle: 'solid',
    roughness: 0,
    roundness: null,
    seed: s,
    versionNonce: s + 1,
    opacity: 100,
    groupIds: [],
    frameId: null,
    index: null,
    isDeleted: false,
    boundElements: null,
    updated: Date.now(),
    link: null,
    locked: false,
    status: 'saved',
    fileId,
    scale: [1, 1],
  };
}

/**
 * Centered brand lockup: SVG mark + "Axis" label.
 * @param {'auth' | 'compact'} variant
 */
export function buildAxisLogo(prefix, slotX, y, slotW, variant = 'auth') {
  const markW = variant === 'compact' ? AXIS_MARK_COMPACT_W : AXIS_MARK_AUTH_W;
  const markH = variant === 'compact' ? AXIS_MARK_COMPACT_H : AXIS_MARK_AUTH_H;
  const wordGap = variant === 'compact' ? 8 : AXIS_WORD_GAP;
  const wordW = variant === 'compact' ? AXIS_WORD_COMPACT_W : AXIS_WORD_AUTH_W;
  const lockupW = markW + wordGap + wordW;
  const lockupH = markH;
  const fontSize = variant === 'compact' ? 14 : 18;
  const lockupX = slotX + Math.round((slotW - lockupW) / 2);
  const textY = y + Math.round((markH - fontSize * 1.2) / 2);

  const files = axisLogoExcalidrawFiles();
  return {
    els: [
      imageElement(`${prefix}_brand_mark`, LOGO_FILE_ID, lockupX, y, markW, markH),
      text(
        `${prefix}_brand_word`,
        lockupX + markW + wordGap,
        textY,
        wordW,
        fontSize + 4,
        'Axis',
        fontSize,
        C.gray900,
      ),
    ],
    files,
    width: lockupW,
    height: lockupH,
  };
}

export function mergeExcalidrawFiles(...maps) {
  return Object.assign({}, ...maps);
}
