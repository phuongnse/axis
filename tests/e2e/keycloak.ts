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
 * Types `code` into the six boxes of the Axis two-step page that `page` shows, and verifies it. Keycloak shows the page
 * in the user's own language, so the button is found by its type, not by its name.
 */
export async function submitCode(page: Page, code: string) {
  // The first box takes the whole code, and each box moves the focus to the next one.
  await page.locator("#root form input").first().pressSequentially(code);
  await page.locator('#root form button[type="submit"]').first().click();
}

/**
 * Adds an authenticator on the Axis add-authenticator page that `page` shows: reads the key that the page gives to
 * type by hand, types the code for it and the device name, and submits. Returns the Base32 secret, for `totp`.
 */
export async function addAuthenticator(page: Page): Promise<string> {
  const key = await page.locator("#root code").first().innerText();
  const secret = key.replace(/\s+/g, "");
  await page.locator("#root form input").first().pressSequentially(totp(secret));
  await page.getByLabel("Device name", { exact: true }).fill("Test");
  await page.locator('#root form button[type="submit"]').first().click();
  return secret;
}

/**
 * Signs `user` in on the Keycloak login page that `page` shows: the password `axis`, then the code that the user's
 * authenticator in the development realm gives.
 */
export async function logInAtKeycloak(page: Page, user: string) {
  await submitPassword(page, user, "axis");
  await submitCode(page, totp(totpSecret(user)));
}
