/**
 * Axis brand logo for wireframes — embeds docs/wireframes/assets/axis-logo.svg in Excalidraw.
 */

import { readFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

import { nextSeed, BASE } from './components.mjs';

const __dir = dirname(fileURLToPath(import.meta.url));

/** Canonical logo asset (commit changes here; sync frontend copy when mark changes). */
export const AXIS_LOGO_SVG_PATH = join(__dir, 'assets', 'axis-logo.svg');

/** Auth card header: tri-node mark only (viewBox 36×36). */
export const AXIS_LOGO_AUTH_W = 32;
export const AXIS_LOGO_AUTH_H = 32;

/** Compact mark for sidebars / small slots. */
export const AXIS_LOGO_COMPACT_W = 24;
export const AXIS_LOGO_COMPACT_H = 24;

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
 * Centered Axis logo (SVG image). Returns elements + Excalidraw files map.
 * @param {'auth' | 'compact'} variant
 */
export function buildAxisLogo(prefix, slotX, y, slotW, variant = 'auth') {
  const w = variant === 'compact' ? AXIS_LOGO_COMPACT_W : AXIS_LOGO_AUTH_W;
  const h = variant === 'compact' ? AXIS_LOGO_COMPACT_H : AXIS_LOGO_AUTH_H;
  const x = slotX + Math.round((slotW - w) / 2);
  const files = axisLogoExcalidrawFiles();
  return {
    els: [imageElement(`${prefix}_brand_logo`, LOGO_FILE_ID, x, y, w, h)],
    files,
    width: w,
    height: h,
  };
}

export function mergeExcalidrawFiles(...maps) {
  return Object.assign({}, ...maps);
}
