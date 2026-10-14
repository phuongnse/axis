import { readFile } from "node:fs/promises";
import path from "node:path";
import type { Page } from "@playwright/test";

export type SampleUser = "anna" | "lan" | "binh" | "chau" | "dung";

/** The sample users and the roles the development realm gives them. */
export const sampleUsers: Record<SampleUser, string[]> = {
  anna: ["employee"],
  lan: ["employee"],
  binh: ["department-manager"],
  chau: ["department-manager"],
  dung: ["finance-reviewer"],
};

/** The file where the setup step saves `user`'s browser session. */
export function sessionFile(user: SampleUser) {
  return path.resolve(import.meta.dirname, ".auth", `${user}.json`);
}

/** Checks that `GET /api/me` answers as `user`, and throws an error naming both users when it does not. */
export async function expectSessionUser(page: Page, user: SampleUser) {
  const response = await page.request.get("/api/me");
  if (response.status() !== 200) {
    throw new Error(`Expected the session of ${user}, but GET /api/me answers ${response.status()}.`);
  }
  const { id } = (await response.json()) as { id: string };
  if (id !== user) {
    throw new Error(`Expected the session of ${user}, but GET /api/me answers as ${id}.`);
  }
}

/** Leaves the application, swaps the cookies for `user`'s saved session and checks `/api/me`. */
export async function useSession(page: Page, user: SampleUser) {
  const { cookies } = JSON.parse(await readFile(sessionFile(user), "utf8"));
  // Leaving the page cancels the previous user's requests, so none can set a cookie after the swap.
  await page.goto("about:blank");
  await page.context().clearCookies();
  await page.context().addCookies(cookies);
  await expectSessionUser(page, user);
}
