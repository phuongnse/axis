const CLIENT_ID = 'axis_spa';
const CONNECT_BASE_URL = (
  import.meta.env.VITE_CONNECT_URL || (import.meta.env.DEV ? 'https://localhost:7275' : '')
).replace(/\/+$/, '');
const REDIRECT_URI = `${window.location.origin}/callback`;
const SCOPES = 'openid email profile offline_access permissions';

function randomString(length: number): string {
  const bytes = new Uint8Array(length);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0'))
    .join('')
    .slice(0, length);
}

async function sha256Base64Url(input: string): Promise<string> {
  const data = new TextEncoder().encode(input);
  const hash = await crypto.subtle.digest('SHA-256', data);
  const bytes = new Uint8Array(hash);
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export interface PkceSession {
  verifier: string;
  state: string;
}

export function createPkceSession(): PkceSession {
  const verifier = randomString(64);
  const state = randomString(32);
  sessionStorage.setItem('pkce_verifier', verifier);
  sessionStorage.setItem('pkce_state', state);
  return { verifier, state };
}

export function loadPkceSession(): PkceSession | null {
  const verifier = sessionStorage.getItem('pkce_verifier');
  const state = sessionStorage.getItem('pkce_state');
  if (!verifier || !state) return null;
  return { verifier, state };
}

export function clearPkceSession(): void {
  sessionStorage.removeItem('pkce_verifier');
  sessionStorage.removeItem('pkce_state');
}

export async function buildAuthorizeUrl(state: string, verifier: string): Promise<string> {
  const challenge = await sha256Base64Url(verifier);
  const params = new URLSearchParams({
    response_type: 'code',
    client_id: CLIENT_ID,
    redirect_uri: REDIRECT_URI,
    code_challenge: challenge,
    code_challenge_method: 'S256',
    scope: SCOPES,
    state,
  });
  return `${CONNECT_BASE_URL}/connect/authorize?${params.toString()}`;
}

export function connectEndpoint(path: string): string {
  return `${CONNECT_BASE_URL}${path}`;
}

export { CLIENT_ID, REDIRECT_URI };
