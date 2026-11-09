import type { FrameLocator, Page } from "@playwright/test";
import path from "node:path";

// The design-system bundle is a separate build, which scripts/e2e.sh makes with scripts/design-system.sh. The page
// web/design-system.html is its harness. The bundle answers every API call of the fixture application from fixtures,
// so the specs serve both from disk and need no server.
export const origin = "http://design-system.test";
export const repositoryRoot = path.resolve(import.meta.dirname, "..", "..");
export const harnessFile = path.join(
  repositoryRoot,
  "web",
  "design-system.html",
);
export const bundleFolder = path.join(
  repositoryRoot,
  "artifacts",
  "design-system",
);
export const bundleFile = path.join(bundleFolder, "axis-design-system.js");
export const harnessUrl = `${origin}/design-system.html`;
export const bundleUrl = `${origin}/artifacts/design-system/axis-design-system.js`;

/** The figure of one state of a preview, found by its caption. */
export const figure = (page: Page | FrameLocator, caption: string) =>
  page.getByRole("figure", { name: caption, exact: true });

/** What a page records while it loads the harness: console errors, page errors and every request. */
export interface Recording {
  errors: string[];
  requests: string[];
}

/**
 * Opens the harness from disk with the bundle next to it, and records errors and requests. When `sandboxed`, it opens
 * a page that holds the harness in an iframe sandboxed as the Design System artifact's card frames are, without
 * `allow-forms`. The iframe is `frameWidth` pixels wide, or the width of the page when none is given.
 */
export async function openHarness(
  page: Page,
  query: string,
  options: { sandboxed?: boolean; frameWidth?: number } = {},
): Promise<Recording> {
  const width =
    options.frameWidth === undefined ? "100%" : `${options.frameWidth}px`;
  const recording: Recording = { errors: [], requests: [] };
  page.on("console", (message) => {
    if (message.type() === "error") {
      recording.errors.push(message.text());
    }
  });
  page.on("pageerror", (error) => recording.errors.push(error.message));
  page.on("request", (request) => recording.requests.push(request.url()));
  await page.route(`${origin}/**`, async (route) => {
    const { pathname } = new URL(route.request().url());
    if (pathname === "/design-system.html") {
      await route.fulfill({ path: harnessFile });
    } else if (pathname === "/sandboxed.html") {
      await route.fulfill({
        contentType: "text/html",
        body: `<iframe sandbox="allow-scripts allow-same-origin" src="/design-system.html${query}" style="width:${width};height:100vh;border:0"></iframe>`,
      });
    } else if (pathname.startsWith("/artifacts/design-system/")) {
      const file = path.join(bundleFolder, path.basename(pathname));
      await route
        .fulfill({ path: file })
        .catch(() => route.fulfill({ status: 404 }));
    } else {
      await route.fulfill({ status: 404 });
    }
  });
  await page.goto(
    options.sandboxed ? `${origin}/sandboxed.html` : `${harnessUrl}${query}`,
  );
  return recording;
}
