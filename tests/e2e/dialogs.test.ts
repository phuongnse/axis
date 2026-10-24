import assert from "node:assert/strict";
import { after, before, test } from "node:test";
import { chromium, expect, type Browser, type Page } from "@playwright/test";
import { confirmDialog, waitForDialog } from "./dialogs.ts";

// The dialog is shown at opacity 0 and zooms in for 1.5 seconds, starting 150 ms after it is shown, as Ant Design does.
const pageHTML = `
<button type="button" id="open">Open</button>
<script>
  document.getElementById("open").addEventListener("click", () => {
    const dialog = document.createElement("div");
    dialog.setAttribute("role", "dialog");
    dialog.setAttribute("aria-label", "Pick");
    dialog.style.opacity = "0";
    dialog.innerHTML = '<button type="button">Choose</button><button type="button">OK</button>';
    const [choose, ok] = dialog.querySelectorAll("button");
    choose.addEventListener("click", () => {
      window.clicked = {
        running: document.getAnimations().some((animation) => animation.playState === "running"),
        opacity: getComputedStyle(dialog).opacity,
      };
    });
    ok.addEventListener("click", () => dialog.remove());
    document.body.append(dialog);
    setTimeout(() => {
      dialog.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 1500 }).onfinish = () => {
        dialog.style.opacity = "1";
      };
    }, 150);
  });
</script>
`;

type Clicked = { running: boolean; opacity: string };

let browser: Browser;

before(async () => {
  browser = await chromium.launch();
});

after(async () => {
  await browser.close();
});

async function openPage(): Promise<Page> {
  const page = await browser.newPage();
  await page.setContent(pageHTML);
  await page.getByRole("button", { name: "Open", exact: true }).click();
  return page;
}

function chosen(page: Page): Promise<Clicked> {
  return page.evaluate(() => (window as unknown as { clicked: Clicked }).clicked);
}

test("a click through waitForDialog lands after the dialog has finished opening", async () => {
  const page = await openPage();
  try {
    const dialog = await waitForDialog(page, "Pick");
    await dialog.getByRole("button", { name: "Choose", exact: true }).click();

    assert.deepEqual(await chosen(page), { running: false, opacity: "1" });
  } finally {
    await page.close();
  }
});

test("a click without waiting lands while the dialog is still opening", async () => {
  const page = await openPage();
  try {
    await page.getByRole("dialog").getByRole("button", { name: "Choose", exact: true }).click();

    const clicked = await chosen(page);
    assert.ok(clicked.running || clicked.opacity !== "1", "the click landed before the dialog finished opening");
  } finally {
    await page.close();
  }
});

test("confirmDialog closes the dialog once its button has been clicked", async () => {
  const page = await openPage();
  try {
    await confirmDialog(page, "OK");

    await expect(page.getByRole("dialog")).toHaveCount(0);
  } finally {
    await page.close();
  }
});
