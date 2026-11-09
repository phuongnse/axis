import assert from "node:assert/strict";
import { test } from "node:test";
import {
  backgroundOf,
  blend,
  contrastRatio,
  isLargeText,
  parseColour,
  requiredRatio,
} from "./contrast.ts";

const white = parseColour("rgb(255, 255, 255)");
const black = parseColour("rgb(0, 0, 0)");

test("black on white is 21:1", () => {
  assert.equal(contrastRatio(black, white), 21);
});

test("the grey #767676 on white is about 4.54:1", () => {
  const ratio = contrastRatio(parseColour("rgb(118, 118, 118)"), white);
  assert.ok(Math.abs(ratio - 4.54) < 0.01, `got ${ratio}`);
});

test("a 50% black text colour on white blends to the grey it makes", () => {
  const translucent = contrastRatio(parseColour("rgba(0, 0, 0, 0.5)"), white);
  const blended = contrastRatio(parseColour("rgb(127.5, 127.5, 127.5)"), white);
  assert.equal(translucent, blended);
  assert.ok(Math.abs(translucent - 3.98) < 0.01, `got ${translucent}`);
});

test("a translucent background layer over an opaque one composites to a grey", () => {
  assert.deepEqual(backgroundOf(["rgba(0, 0, 0, 0.5)", "rgb(255, 255, 255)"]), {
    red: 127.5,
    green: 127.5,
    blue: 127.5,
    alpha: 1,
  });
});

test("a translucent text colour over a translucent background layer uses the blended background", () => {
  const surface = backgroundOf(["rgba(255, 255, 255, 0.5)", "rgb(0, 0, 0)"]);
  assert.deepEqual(surface, {
    red: 127.5,
    green: 127.5,
    blue: 127.5,
    alpha: 1,
  });
  const ratio = contrastRatio(parseColour("rgba(0, 0, 0, 0.5)"), surface);
  const expected = contrastRatio(
    parseColour("rgb(63.75, 63.75, 63.75)"),
    surface,
  );
  assert.equal(ratio, expected);
});

test("with no opaque layer, the stack is composed over white", () => {
  assert.deepEqual(
    backgroundOf(["rgba(0, 0, 0, 0.5)"]),
    backgroundOf(["rgba(0, 0, 0, 0.5)", "rgb(255, 255, 255)"]),
  );
});

test("blending a fully transparent top leaves the bottom unchanged", () => {
  assert.deepEqual(blend(parseColour("rgba(9, 9, 9, 0)"), white), white);
});

test("text at 24 px is large, and 18.66 px is large only when bold", () => {
  assert.equal(isLargeText(24, 400), true);
  assert.equal(isLargeText(18.66, 700), true);
  assert.equal(isLargeText(18.66, 400), false);
  assert.equal(isLargeText(18, 700), false);
});

test("text needs 4.5:1, or 3:1 when large, and a border or an icon needs 3:1", () => {
  assert.equal(requiredRatio("text", isLargeText(24, 400)), 3);
  assert.equal(requiredRatio("text", isLargeText(18.66, 700)), 3);
  assert.equal(requiredRatio("text", isLargeText(18.66, 400)), 4.5);
  assert.equal(requiredRatio("text", isLargeText(18, 700)), 4.5);
  assert.equal(requiredRatio("border", false), 3);
  assert.equal(requiredRatio("icon", true), 3);
});

test("a colour in a format the helper does not know throws, so no miss passes silently", () => {
  assert.throws(() => parseColour("hsl(0 0% 0%)"), /Unknown colour format/);
  assert.throws(
    () => parseColour("color(srgb 0 0 0)"),
    /Unknown colour format/,
  );
});
