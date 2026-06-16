const REGISTRATION_CONTEXT_KEY = 'axis.registration-context';

export interface RegistrationContext {
  email: string;
  WorkspaceName?: string;
}

export function saveRegistrationContext(context: RegistrationContext): void {
  sessionStorage.setItem(REGISTRATION_CONTEXT_KEY, JSON.stringify(context));
}

export function loadRegistrationContext(): RegistrationContext | null {
  const raw = sessionStorage.getItem(REGISTRATION_CONTEXT_KEY);
  if (!raw) return null;

  try {
    const parsed = JSON.parse(raw) as RegistrationContext;
    if (typeof parsed.email !== 'string') {
      return null;
    }
    if (parsed.WorkspaceName !== undefined && typeof parsed.WorkspaceName !== 'string') {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

export function clearRegistrationContext(): void {
  sessionStorage.removeItem(REGISTRATION_CONTEXT_KEY);
}
