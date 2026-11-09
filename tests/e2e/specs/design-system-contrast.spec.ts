import { expect, test } from "../test";
import type { Locator, Page } from "@playwright/test";
import { readFileSync } from "node:fs";
import path from "node:path";
import { cardGroups } from "../../../web/src/designSystem/cardOrder";
import { openHarness } from "../design-system";
import {
  backgroundOf,
  contrastRatio,
  isLargeText,
  parseColour,
  requiredRatio,
} from "../contrast";

// One sample is a piece of a preview that draws: a text colour, a control's border or an icon's colour, with the
// layers behind it, nearest first, up to the first opaque one. The browser collects samples, see collectSamples.
interface Sample {
  kind: "text" | "border" | "icon";
  element: string;
  foreground: string;
  layers: string[];
  fontSize: number;
  fontWeight: number;
}

// An entry of tests/e2e/contrast-baseline.json: a miss that is known and listed. The same four fields name a miss the
// spec finds.
interface Miss {
  preview: string;
  state: string;
  mode: string;
  element: string;
}

interface Finding extends Miss {
  ratio: number;
  required: number;
  foreground: string;
  background: string;
}

const modes = ["light", "dark"] as const;
const previews = cardGroups.flatMap((group) => group.previews);
const baseline: Miss[] = JSON.parse(
  readFileSync(
    path.join(import.meta.dirname, "..", "contrast-baseline.json"),
    "utf8",
  ),
);

const keyOf = (miss: Miss) =>
  JSON.stringify([miss.preview, miss.state, miss.mode, miss.element]);

const describeFinding = (finding: Finding) =>
  `${finding.state} · ${finding.element}: ${finding.ratio.toFixed(2)}:1, ${finding.required}:1 needed, ${finding.foreground} on ${finding.background}`;

const describeMiss = (miss: Miss) => `${miss.state} · ${miss.element}`;

/**
 * Runs in the browser on a figure, so it holds every helper it needs and returns plain data. It skips what WCAG
 * leaves out: disabled controls, aria-hidden subtrees, hidden or visually hidden boxes and fully transparent colours.
 */
function collectSamples(root: Element): Sample[] {
  const alphaOf = (colour: string) => Number(colour.match(/[\d.]+/g)?.[3] ?? 1);
  const channelsOf = (colour: string) =>
    (colour.match(/[\d.]+/g) ?? []).slice(0, 3).join(", ");
  const opacityOf = (element: Element) => {
    let opacity = 1;
    for (let node: Element | null = element; node; node = node.parentElement) {
      opacity *= Number(getComputedStyle(node).opacity);
    }
    return opacity;
  };
  const skipped = (element: Element) => {
    if (
      element.closest(
        '[aria-hidden="true"], fieldset:disabled, [aria-disabled="true"]',
      ) ||
      element.matches(":disabled")
    ) {
      return true;
    }
    if (!element.checkVisibility({ visibilityProperty: true })) {
      return true;
    }
    const box = element.getBoundingClientRect();
    return box.width <= 1 || box.height <= 1;
  };
  const layersOf = (element: Element) => {
    const layers: string[] = [];
    for (let node: Element | null = element; node; node = node.parentElement) {
      const colour = getComputedStyle(node).backgroundColor;
      layers.push(colour);
      if (alphaOf(colour) === 1) {
        break;
      }
    }
    return layers;
  };
  const drawsBorder = (element: Element) => {
    const style = getComputedStyle(element);
    return (
      style.borderTopStyle !== "none" &&
      parseFloat(style.borderTopWidth) > 0 &&
      alphaOf(style.borderTopColor) > 0
    );
  };
  const borderOf = (control: Element): Element | null => {
    if (drawsBorder(control)) {
      return control;
    }
    let ancestor = control.parentElement;
    for (let depth = 0; ancestor && depth < 3; depth++) {
      if (drawsBorder(ancestor)) {
        return ancestor;
      }
      ancestor = ancestor.parentElement;
    }
    const siblings = control.parentElement
      ? [...control.parentElement.children]
      : [];
    return (
      siblings.find((sibling) => sibling !== control && drawsBorder(sibling)) ??
      null
    );
  };
  const roleOf = (control: Element) => {
    const role = control.getAttribute("role");
    if (role) {
      return role;
    }
    const type = control.getAttribute("type") ?? "";
    return (
      (
        {
          checkbox: "checkbox",
          radio: "radio",
          number: "spinbutton",
        } as Record<string, string>
      )[type] ?? "textbox"
    );
  };
  const nameOf = (control: Element) =>
    control.getAttribute("aria-label") ||
    (control as HTMLInputElement).labels?.[0]?.textContent?.trim() ||
    control.id ||
    control.getAttribute("placeholder") ||
    "";

  const samples: Sample[] = [];
  const texts = new Set<Element>();
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const text = (node.textContent ?? "").trim();
    const element = node.parentElement;
    if (!text || !element || texts.has(element) || skipped(element)) {
      continue;
    }
    texts.add(element);
    const style = getComputedStyle(element);
    const alpha = alphaOf(style.color) * opacityOf(element);
    if (alpha === 0) {
      continue;
    }
    samples.push({
      kind: "text",
      element: `text "${text.slice(0, 40)}"`,
      foreground: `rgba(${channelsOf(style.color)}, ${alpha})`,
      layers: layersOf(element),
      fontSize: parseFloat(style.fontSize),
      fontWeight: Number(style.fontWeight),
    });
  }

  const controls = root.querySelectorAll(
    "input:not([type=hidden]), textarea, [role=combobox], [role=checkbox], [role=radio], [role=spinbutton]",
  );
  for (const control of controls) {
    if (skipped(control)) {
      continue;
    }
    const border = borderOf(control);
    if (!border) {
      continue;
    }
    const style = getComputedStyle(border);
    const alpha = alphaOf(style.borderTopColor) * opacityOf(border);
    if (alpha === 0) {
      continue;
    }
    samples.push({
      kind: "border",
      element: `border ${roleOf(control)} "${nameOf(control)}"`,
      foreground: `rgba(${channelsOf(style.borderTopColor)}, ${alpha})`,
      layers: layersOf(border.parentElement ?? border),
      fontSize: 0,
      fontWeight: 0,
    });
  }

  for (const icon of root.querySelectorAll(".anticon")) {
    if (skipped(icon)) {
      continue;
    }
    const style = getComputedStyle(icon);
    const alpha = alphaOf(style.color) * opacityOf(icon);
    if (alpha === 0) {
      continue;
    }
    const name =
      icon.getAttribute("aria-label") ??
      [...icon.classList]
        .find((className) => className.startsWith("anticon-"))
        ?.slice("anticon-".length) ??
      "";
    samples.push({
      kind: "icon",
      element: `icon "${name}"`,
      foreground: `rgba(${channelsOf(style.color)}, ${alpha})`,
      layers: layersOf(icon),
      fontSize: 0,
      fontWeight: 0,
    });
  }
  return samples;
}

/** Waits until every figure of a card is visible and every finite animation has ended. */
async function settle(page: Page, card: Locator) {
  const figures = card.getByRole("figure");
  const count = await figures.count();
  for (let index = 0; index < count; index++) {
    await expect(figures.nth(index)).toBeVisible();
  }
  await page.evaluate(() =>
    Promise.all(
      document
        .getAnimations()
        .filter(
          (animation) => animation.effect?.getTiming().iterations !== Infinity,
        )
        .map((animation) => animation.finished.catch(() => undefined)),
    ),
  );
}

/** The findings of one state: each sample under its requirement, with a miss collapsed to one entry per element. */
function findingsOf(
  preview: string,
  state: string,
  mode: string,
  samples: readonly Sample[],
): Finding[] {
  const findings = new Map<string, Finding>();
  for (const sample of samples) {
    const background = backgroundOf(sample.layers);
    const ratio = contrastRatio(parseColour(sample.foreground), background);
    const required = requiredRatio(
      sample.kind,
      isLargeText(sample.fontSize, sample.fontWeight),
    );
    if (ratio >= required) {
      continue;
    }
    const finding: Finding = {
      preview,
      state,
      mode,
      element: sample.element,
      ratio,
      required,
      foreground: sample.foreground,
      background: `rgb(${[background.red, background.green, background.blue].map(Math.round).join(", ")})`,
    };
    findings.set(keyOf(finding), finding);
  }
  return [...findings.values()];
}

/** Sweeps every state of a card: each Fixtures option in turn, or else each figure in the order it draws. */
async function sweepCard(
  page: Page,
  card: Locator,
  preview: string,
  mode: string,
): Promise<Finding[]> {
  await expect(card.getByRole("figure").first()).toBeVisible();
  const chooser = card.getByRole("radiogroup", {
    name: "Fixtures",
    exact: true,
  });
  const findings: Finding[] = [];
  const sweep = async (state: string, figure: Locator) => {
    await settle(page, card);
    const samples = await figure.evaluate(collectSamples);
    findings.push(...findingsOf(preview, state, mode, samples));
  };

  if ((await chooser.count()) > 0) {
    const captions = (await chooser.locator("label").allTextContents()).map(
      (caption) => caption.trim(),
    );
    for (const caption of captions) {
      await chooser.getByText(caption, { exact: true }).click();
      await expect(
        card.getByRole("figure", { name: caption, exact: true }),
      ).toBeVisible();
      await sweep(caption, card.getByRole("figure"));
    }
  } else {
    const figures = card.getByRole("figure");
    const count = await figures.count();
    for (let index = 0; index < count; index++) {
      const caption =
        (await figures.nth(index).locator("figcaption").textContent()) ?? "";
      await sweep(caption.trim(), figures.nth(index));
    }
  }
  return findings;
}

test("the contrast baseline names only previews of the cards and the light or dark mode", () => {
  const names = new Set<string>(previews);
  const unknown = baseline.filter(
    (entry) =>
      !names.has(entry.preview) ||
      !(modes as readonly string[]).includes(entry.mode),
  );
  expect(
    unknown.map(keyOf),
    "A contrast baseline entry names a preview or mode that does not exist. Fix or remove it in tests/e2e/contrast-baseline.json.",
  ).toEqual([]);
});

for (const preview of previews) {
  for (const mode of modes) {
    test(`the ${preview} preview draws its text, control borders and icons at their contrast in ${mode} mode`, async ({
      page,
    }) => {
      test.setTimeout(120_000);
      await page.setViewportSize({ width: 992, height: 720 });
      const recording = await openHarness(
        page,
        `?preview=${preview}&mode=${mode}`,
      );
      const findings = await sweepCard(
        page,
        page.locator(`[data-preview="${preview}"]`),
        preview,
        mode,
      );

      const listed = baseline.filter(
        (entry) => entry.preview === preview && entry.mode === mode,
      );
      const listedKeys = new Set(listed.map(keyOf));
      const foundKeys = new Set(findings.map(keyOf));
      expect(
        findings
          .filter((finding) => !listedKeys.has(keyOf(finding)))
          .map(describeFinding)
          .sort(),
        "A contrast miss is not listed in tests/e2e/contrast-baseline.json. Fix the colour, or list the miss there.",
      ).toEqual([]);
      expect(
        listed
          .filter((entry) => !foundKeys.has(keyOf(entry)))
          .map(describeMiss)
          .sort(),
        "A listed contrast miss no longer misses. Remove its entry from tests/e2e/contrast-baseline.json.",
      ).toEqual([]);
      expect(recording.errors).toEqual([]);
    });
  }
}
