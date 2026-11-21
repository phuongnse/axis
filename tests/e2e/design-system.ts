import { expect, type FrameLocator, type Page } from "@playwright/test";
import { spawn } from "node:child_process";
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

/** Picks a state in the State control of a preview, and waits until its figure draws. */
export async function pickState(page: Page | FrameLocator, caption: string) {
  await page
    .getByRole("radiogroup", { name: "State", exact: true })
    .getByText(caption, { exact: true })
    .click();
  await expect(figure(page, caption)).toBeVisible();
}

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

/** The dev server of the design-system harness, see docs/development.md#widget-previews. */
export interface DesignSystemDevServer {
  /** The harness, which the dev server serves from source. */
  url: string;
  /** Ends the server with SIGTERM, and with SIGKILL after 15 seconds. */
  stop(): Promise<void>;
  /** What the server printed. */
  output(): string;
}

/**
 * Starts `vite` with `vite.design-system.config.ts` in `web/`, and returns once the harness answers 200 on port 5174.
 * The port is fixed, so a server that already holds it makes this throw. It throws with the server's output when the
 * server exits, or when the harness does not answer within 60 seconds. The first request compiles the previews, so
 * a spec that opens the harness should give itself a longer timeout.
 */
export async function startDesignSystemDevServer(): Promise<DesignSystemDevServer> {
  const webFolder = path.join(repositoryRoot, "web");
  const url = "http://localhost:5174/design-system.html";
  const child = spawn(
    process.execPath,
    [
      path.join(webFolder, "node_modules", "vite", "bin", "vite.js"),
      "--config",
      "vite.design-system.config.ts",
    ],
    { cwd: webFolder, stdio: ["ignore", "pipe", "pipe"] },
  );
  let output = "";
  child.stdout
    .setEncoding("utf8")
    .on("data", (chunk: string) => (output += chunk));
  child.stderr
    .setEncoding("utf8")
    .on("data", (chunk: string) => (output += chunk));
  let exitCode: number | null | undefined;
  const exited = new Promise<void>((resolve) =>
    child.once("close", (code) => {
      exitCode = code ?? -1;
      resolve();
    }),
  );
  const stop = async () => {
    if (exitCode === undefined) {
      child.kill("SIGTERM");
      // A server that ignores SIGTERM must not hang the spec.
      const killer = setTimeout(() => child.kill("SIGKILL"), 15_000);
      await exited;
      clearTimeout(killer);
    }
  };

  const deadline = Date.now() + 60_000;
  while (exitCode === undefined && Date.now() < deadline) {
    if (await answersOk(url)) return { url, stop, output: () => output };
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  const reason =
    exitCode !== undefined
      ? `The design-system dev server exited with code ${exitCode}.`
      : "The design-system dev server never answered the harness with 200 within 60 seconds.";
  await stop();
  throw new Error(`${reason} Dev server output:\n${output}`);
}

async function answersOk(url: string): Promise<boolean> {
  try {
    return (
      (await fetch(url, { signal: AbortSignal.timeout(5_000) })).status === 200
    );
  } catch {
    return false;
  }
}
