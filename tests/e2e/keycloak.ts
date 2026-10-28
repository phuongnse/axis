import type { Page } from "@playwright/test";
import { totp, totpSecret } from "./totp";

/**
 * Signs `user` in on the Keycloak login page that `page` shows: the password `axis`, then the code that the user's
 * authenticator in the development realm gives.
 */
export async function logInAtKeycloak(page: Page, user: string) {
  await page.locator("#username").fill(user);
  await page.locator("#password").fill("axis");
  await page.locator("#kc-login").click();
  await page.locator("#otp").fill(totp(totpSecret(user)));
  await page.locator("#kc-login").click();
}
