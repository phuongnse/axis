const MAX_AUTHORIZATION_REQUEST_LENGTH = 2048;

export function isAuthorizationRequestHandle(value: string | undefined): value is string {
  return Boolean(
    value &&
      value.length <= MAX_AUTHORIZATION_REQUEST_LENGTH &&
      !Array.from(value).some((character) => {
        const codePoint = character.codePointAt(0);
        return codePoint !== undefined && (codePoint <= 31 || codePoint === 127);
      }),
  );
}

export function buildAuthorizationRequestResumeUrl(requestUri: string): string {
  const params = new URLSearchParams({ request_uri: requestUri });
  return `${window.location.origin}/connect/authorize?${params.toString()}`;
}
