/** sessionStorage key: verification token to resume provisioning after PKCE callback */
export const POST_VERIFY_PROVISIONING_TOKEN_KEY = 'axis_post_verify_provisioning_token';

export function storePostVerifyProvisioningToken(token: string): void {
  sessionStorage.setItem(POST_VERIFY_PROVISIONING_TOKEN_KEY, token);
}

export function consumePostVerifyProvisioningToken(): string | null {
  const token = sessionStorage.getItem(POST_VERIFY_PROVISIONING_TOKEN_KEY);
  if (token) {
    sessionStorage.removeItem(POST_VERIFY_PROVISIONING_TOKEN_KEY);
  }
  return token;
}
