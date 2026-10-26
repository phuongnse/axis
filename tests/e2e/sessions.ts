import { readFile } from "node:fs/promises";
import path from "node:path";
import type { Page } from "@playwright/test";
import { journeyURL } from "./server-settings";

export type SampleUser = "anna" | "lan" | "binh" | "chau" | "dung" | "giang" | "hoa" | "khoa" | "linh" | "minh" | "nam" | "quan";

/** The sample users and the roles the development realm gives them. */
export const sampleUsers: Record<SampleUser, string[]> = {
  anna: ["employee", "requester"],
  lan: ["employee"],
  binh: ["department-manager"],
  chau: ["department-manager"],
  dung: ["finance-reviewer"],
  giang: ["operator"],
  hoa: ["author", "publisher"],
  khoa: ["agent"],
  linh: ["service-desk-lead"],
  minh: ["approver"],
  nam: ["service-desk-lead"],
  quan: ["asset-manager"],
};

const journeyRoles = ["author", "requester", "service-desk-lead", "agent", "approver"];

/** The users who sign in on the journey host: those who hold the author, requester, service desk lead, agent or approver role. */
export const journeyUsers = (Object.keys(sampleUsers) as SampleUser[]).filter((user) =>
  sampleUsers[user].some((role) => journeyRoles.includes(role)),
);

/** The file where the setup step saves `user`'s browser session. */
export function sessionFile(user: SampleUser) {
  return path.resolve(import.meta.dirname, ".auth", `${user}.json`);
}

/** The file where the setup step saves `user`'s browser session on the journey host. */
export function journeySessionFile(user: SampleUser) {
  return path.resolve(import.meta.dirname, ".auth", `journey-${user}.json`);
}

/**
 * Checks that `GET /api/me` answers as `user`, and throws an error naming both users when it does not. `origin`
 * is the server to ask, and empty for the main E2E server.
 */
export async function expectSessionUser(page: Page, user: SampleUser, origin = "") {
  const response = await page.request.get(`${origin}/api/me`);
  if (response.status() !== 200) {
    throw new Error(`Expected the session of ${user}, but GET /api/me answers ${response.status()}.`);
  }
  const { id } = (await response.json()) as { id: string };
  if (id !== user) {
    throw new Error(`Expected the session of ${user}, but GET /api/me answers as ${id}.`);
  }
}

/**
 * Opens a blank page. Leaving the page cancels the previous user's requests, so none can set a cookie
 * after the swap. A navigation the previous user started can still commit and interrupt this one, so the
 * blank page is opened again once that navigation has settled.
 */
async function leavePage(page: Page) {
  for (let attempt = 1; ; attempt++) {
    try {
      await page.goto("about:blank");
      return;
    } catch (error) {
      if (attempt === 3) throw error;
      await page.waitForLoadState("load").catch(() => null);
    }
  }
}

/** Leaves the application, swaps the cookies for `user`'s saved session and checks `/api/me`. */
export async function useSession(page: Page, user: SampleUser) {
  const { cookies } = JSON.parse(await readFile(sessionFile(user), "utf8"));
  await leavePage(page);
  await page.context().clearCookies();
  await page.context().addCookies(cookies);
  await expectSessionUser(page, user);
}

/** Does what `useSession` does with `user`'s session on the journey host, and checks `/api/me` there. */
export async function useJourneySession(page: Page, user: SampleUser) {
  const { cookies } = JSON.parse(await readFile(journeySessionFile(user), "utf8"));
  await leavePage(page);
  await page.context().clearCookies();
  await page.context().addCookies(cookies);
  await expectSessionUser(page, user, journeyURL);
}
