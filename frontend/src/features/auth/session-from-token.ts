function decodeJwtPayload(token: string): Record<string, unknown> | null {
  const parts = token.split('.');
  if (parts.length < 2) {
    return null;
  }

  try {
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const json = atob(base64);
    return JSON.parse(json) as Record<string, unknown>;
  } catch {
    return null;
  }
}

export function sessionDisplayFromAccessToken(token: string): {
  userLabel: string;
  userInitials: string;
} {
  const payload = decodeJwtPayload(token);
  const email = typeof payload?.email === 'string' ? payload.email : null;
  const name = typeof payload?.name === 'string' ? payload.name : null;
  const label = name ?? email ?? 'User';

  const parts = label.includes('@')
    ? [label.split('@')[0] ?? '']
    : label.split(/\s+/).filter((part) => part.length > 0);

  const initials = parts
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('');

  return {
    userLabel: label,
    userInitials: initials.length > 0 ? initials : '?',
  };
}
