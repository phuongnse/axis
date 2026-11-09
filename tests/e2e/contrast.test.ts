import assert from "node:assert/strict";
import { test } from "node:test";
import {
  backgroundOf,
  contrast,
  isLargeText,
  requiredRatio,
} from "../../web/src/test/contrast.ts";

const white = "rgb(255, 255, 255)";

test("black on white is 21:1", () => {
  assert.equal(contrast("rgb(0, 0, 0)", white), 21);
});

test("the grey #767676 on white is about 4.54:1", () => {
  const ratio = contrast("#767676", white);
  assert.ok(Math.abs(ratio - 4.54) < 0.01, `got ${ratio}`);
});

test("a 50% black text colour on white blends to the grey it makes", () => {
  const translucent = contrast("rgba(0, 0, 0, 0.5)", white);
  const blended = contrast("rgb(127.5, 127.5, 127.5)", white);
  assert.equal(translucent, blended);
  assert.ok(Math.abs(translucent - 3.98) < 0.01, `got ${translucent}`);
});

test("a translucent background layer over an opaque one composites to a grey", () => {
  assert.equal(backgroundOf(["rgba(0, 0, 0, 0.5)", white]), "#808080");
});

test("a translucent text colour over a translucent background layer uses the blended background", () => {
  const surface = backgroundOf(["rgba(255, 255, 255, 0.5)", "rgb(0, 0, 0)"]);
  assert.equal(surface, "#808080");
  assert.equal(
    contrast("rgba(0, 0, 0, 0.5)", surface),
    contrast("rgb(64, 64, 64)", surface),
  );
});

test("with no opaque layer, the stack is composed over white", () => {
  assert.equal(
    backgroundOf(["rgba(0, 0, 0, 0.5)"]),
    backgroundOf(["rgba(0, 0, 0, 0.5)", white]),
  );
});

test("a fully transparent text colour leaves the background unchanged", () => {
  assert.equal(contrast("rgba(9, 9, 9, 0)", white), 1);
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
  assert.throws(
    () => contrast("hsl(0 0% 0%)", white),
    /Cannot parse the colour/,
  );
  assert.throws(
    () => backgroundOf(["color(srgb 0 0 0)"]),
    /Cannot parse the colour/,
  );
});
