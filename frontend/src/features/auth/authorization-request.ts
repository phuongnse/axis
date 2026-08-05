const MAX_AUTHORIZATION_REQUEST_LENGTH = 2048;
const MAX_AUTHORIZATION_CLIENT_ID_LENGTH = 255;

export interface AuthorizationRequestContinuation {
  clientId: string;
  requestUri: string;
}

function isSafeOAuthParameter(value: string | undefined, maxLength: number): value is string {
  return Boolean(
    value &&
      value.length <= maxLength &&
      !Array.from(value).some((character) => {
        const codePoint = character.codePointAt(0);
        return codePoint !== undefined && (codePoint <= 31 || codePoint === 127);
      }),
  );
}

export function isAuthorizationRequestHandle(value: string | undefined): value is string {
  return isSafeOAuthParameter(value, MAX_AUTHORIZATION_REQUEST_LENGTH);
}

export function isAuthorizationClientId(value: string | undefined): value is string {
  return isSafeOAuthParameter(value, MAX_AUTHORIZATION_CLIENT_ID_LENGTH);
}

export function getAuthorizationRequestContinuation(
  requestUri: string | undefined,
  clientId: string | undefined,
): AuthorizationRequestContinuation | undefined {
  if (!isAuthorizationRequestHandle(requestUri) || !isAuthorizationClientId(clientId)) {
    return undefined;
  }

  return { clientId, requestUri };
}

export function buildAuthorizationRequestResumeUrl({
  clientId,
  requestUri,
}: AuthorizationRequestContinuation): string {
  const params = new URLSearchParams({ client_id: clientId, request_uri: requestUri });
  return `${window.location.origin}/connect/authorize?${params.toString()}`;
}
