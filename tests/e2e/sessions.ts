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

/** Swaps the browser's cookies for `user`'s saved session. Navigate afterwards so the SPA reads it. */
export async function useSession(page: Page, user: SampleUser) {
  const { cookies } = JSON.parse(await readFile(sessionFile(user), "utf8"));
  await page.context().clearCookies();
  await page.context().addCookies(cookies);
}
