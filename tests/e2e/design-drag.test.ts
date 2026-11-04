import assert from "node:assert/strict";
import { after, before, test } from "node:test";
import { chromium, type Browser, type Page } from "@playwright/test";
import { dragTo } from "./design-drag.ts";

// The target moves 80 px down on the first pointer move of the drag, as a redraw or an auto-scroll would move it. When
// `marks` is set, the target has `data-drop-over` while the pointer is inside it, as the canvas marks its drop target.
// On pointer up, `window.dropped` says whether the pointer is inside the target.
function draggablePage({ moves, marks }: { moves: boolean; marks: boolean }) {
  return `
<div data-testid="studio-page-design" style="position: relative; width: 800px; height: 900px">
  <div id="source" style="position: absolute; left: 20px; top: 20px; width: 120px; height: 40px; background: #ccc"></div>
  <div id="target" style="position: absolute; left: 300px; top: 500px; width: 200px; height: 32px; background: #eee"></div>
</div>
<script>
  const moves = ${moves};
  const marks = ${marks};
  const source = document.getElementById("source");
  const target = document.getElementById("target");
  const inside = (event, box) =>
    event.clientX >= box.left && event.clientX <= box.right && event.clientY >= box.top && event.clientY <= box.bottom;
  let dragging = false;
  let moved = false;
  source.addEventListener("pointerdown", () => {
    dragging = true;
  });
  window.addEventListener("pointermove", (event) => {
    if (!dragging) return;
    if (moves && !moved) {
      moved = true;
      target.style.top = "580px";
    }
    if (marks && inside(event, target.getBoundingClientRect())) {
      target.setAttribute("data-drop-over", "true");
    } else {
      target.removeAttribute("data-drop-over");
    }
  });
  window.addEventListener("pointerup", (event) => {
    dragging = false;
    window.dropped = inside(event, target.getBoundingClientRect()) ? "target" : "elsewhere";
  });
</script>
`;
}

let browser: Browser;

before(async () => {
  browser = await chromium.launch();
});

after(async () => {
  await browser.close();
});

async function openPage(options: { moves: boolean; marks: boolean }): Promise<Page> {
  const page = await browser.newPage();
  await page.setContent(draggablePage(options));
  return page;
}

test("dragTo releases the mouse over the target after it moved during the drag", async () => {
  const page = await openPage({ moves: true, marks: true });
  try {
    await dragTo(page, "studio-page-design", page.locator("#source"), page.locator("#target"));
    const dropped = await page.evaluate(() => (window as unknown as { dropped: string }).dropped);
    assert.equal(dropped, "target");
  } finally {
    await page.close();
  }
});

test("dragTo rejects when the target never gets data-drop-over", async () => {
  const page = await openPage({ moves: false, marks: false });
  try {
    await assert.rejects(dragTo(page, "studio-page-design", page.locator("#source"), page.locator("#target")));
  } finally {
    await page.close();
  }
});

// The window is 3000 px tall and the target sits in its bottom fifth. A few ms after a pointer move in that fifth, the
// window scrolls 3 px, as the drag library's auto-scroll does, and the target leaves the pointer. The marks only follow
// the pointer's moves, so the mark can be stale when the mouse is released.
const scrollingPage = `
<div data-testid="studio-page-design" style="position: relative; width: 800px; height: 3000px">
  <div id="source" style="position: absolute; left: 20px; top: 20px; width: 120px; height: 40px; background: #ccc"></div>
  <div id="target" style="position: absolute; left: 300px; top: 620px; width: 200px; height: 20px; background: #eee"></div>
</div>
<script>
  const source = document.getElementById("source");
  const target = document.getElementById("target");
  const inside = (event, box) =>
    event.clientX >= box.left && event.clientX <= box.right && event.clientY >= box.top && event.clientY <= box.bottom;
  let dragging = false;
  source.addEventListener("pointerdown", () => {
    dragging = true;
  });
  window.addEventListener("pointermove", (event) => {
    if (!dragging) return;
    if (event.clientY > window.innerHeight * 0.8) setTimeout(() => window.scrollBy(0, 3), 3);
    if (inside(event, target.getBoundingClientRect())) {
      target.setAttribute("data-drop-over", "true");
    } else {
      target.removeAttribute("data-drop-over");
    }
  });
  window.addEventListener("pointerup", (event) => {
    dragging = false;
    window.dropped = inside(event, target.getBoundingClientRect()) ? "target" : "elsewhere";
  });
</script>
`;

test("dragTo scrolls a target in the bottom fifth away from the edge before it aims", async () => {
  const page = await browser.newPage();
  try {
    await page.setContent(scrollingPage);
    await dragTo(page, "studio-page-design", page.locator("#source"), page.locator("#target"), 0.9);
    const dropped = await page.evaluate(() => (window as unknown as { dropped: string }).dropped);
    assert.equal(dropped, "target");
  } finally {
    await page.close();
  }
});
