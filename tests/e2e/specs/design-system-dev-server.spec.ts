import { expect, test } from "../test";
import {
  figure,
  startDesignSystemDevServer,
  type DesignSystemDevServer,
} from "../design-system";
import { fieldLabel } from "../fields";

// This spec starts the harness's dev server itself, on port 5174. Its first request compiles the previews, which takes
// longer than a usual test.
const firstCompile = 120_000;
let server: DesignSystemDevServer | undefined;

test.beforeAll(async () => {
  test.setTimeout(firstCompile);
  server = await startDesignSystemDevServer();
});

test.afterAll(async () => {
  await server?.stop();
});

test("the preview server draws a preview in the app's IBM Plex Sans", async ({
  page,
}) => {
  test.setTimeout(firstCompile);
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(error.message));

  await page.goto(`${server!.url}?preview=field-text&mode=light`);
  await expect(
    figure(page, "Editable").getByLabel(fieldLabel("Title")),
  ).toBeVisible();
  await page.evaluate(() => document.fonts.ready);
  await expect
    .poll(() =>
      page.evaluate(() =>
        [...document.fonts].some(
          (face) =>
            face.family.replaceAll('"', "") === "IBM Plex Sans" &&
            face.status === "loaded",
        ),
      ),
    )
    .toBe(true);
  expect(errors).toEqual([]);
});
