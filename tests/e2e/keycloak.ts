import type { Locator, Page } from "@playwright/test";
import { totp, totpSecret } from "./totp";

/** The field for the username on the Axis sign-in page of the development realm. */
export function signInUsername(page: Page): Locator {
  return page.getByLabel("Email or username", { exact: true });
}

/** Submits `user` and `password` on the Axis sign-in page that `page` shows, and stops at what Keycloak answers. */
export async function submitPassword(page: Page, user: string, password: string) {
  await signInUsername(page).fill(user);
  await page.getByLabel("Password", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
}

/**
 * Signs `user` in on the Keycloak login page that `page` shows: the password `axis`, then the code that the user's
 * authenticator in the development realm gives.
 */
export async function logInAtKeycloak(page: Page, user: string) {
  await submitPassword(page, user, "axis");
  // Keycloak's own code page is not themed yet, so it keeps Keycloak's ids.
  await page.locator("#otp").fill(totp(totpSecret(user)));
  await page.locator("#kc-login").click();
}
