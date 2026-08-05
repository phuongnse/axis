import { create } from 'zustand';

import type { AxisBrowserSessionDto } from '@/lib/api-generated';
import { sessionDisplayFromLabel } from './session-display';

export interface BrowserSessionUser {
  userId: string;
  workspaceId: string | null;
  email: string;
  name: string;
}

interface AuthState {
  browserSessionStatus: BrowserSessionStatus;
  csrfToken: string | null;
  user: BrowserSessionUser | null;
  userLabel: string | null;
  userInitials: string | null;
  clearSession: () => void;
  setBrowserSession: (session: AxisBrowserSessionDto) => boolean;
  markBrowserSessionGuest: (csrfToken?: string | null) => void;
}

export type BrowserSessionStatus = 'unknown' | 'guest' | 'authenticated';

export const useAuthStore = create<AuthState>((set) => ({
  browserSessionStatus: 'unknown',
  csrfToken: null,
  user: null,
  userLabel: null,
  userInitials: null,
  setBrowserSession: (session) => {
    const csrfToken = requireString(session.csrfToken, 'csrfToken');
    if (!session.authenticated) {
      set({
        browserSessionStatus: 'guest',
        csrfToken,
        user: null,
        userLabel: null,
        userInitials: null,
      });
      return false;
    }

    const user: BrowserSessionUser = {
      userId: requireString(session.user?.userId, 'user.userId'),
      workspaceId: session.user?.workspaceId ?? null,
      email: requireString(session.user?.email, 'user.email'),
      name: requireString(session.user?.name, 'user.name'),
    };
    const { userLabel, userInitials } = sessionDisplayFromLabel(user.name || user.email);
    set({
      browserSessionStatus: 'authenticated',
      csrfToken,
      user,
      userLabel,
      userInitials,
    });
    return true;
  },
  clearSession: () =>
    set({
      browserSessionStatus: 'unknown',
      csrfToken: null,
      user: null,
      userLabel: null,
      userInitials: null,
    }),
  markBrowserSessionGuest: (csrfToken = null) =>
    set({
      browserSessionStatus: 'guest',
      csrfToken,
      user: null,
      userLabel: null,
      userInitials: null,
    }),
}));

export function getBrowserSessionStatus(): BrowserSessionStatus {
  return useAuthStore.getState().browserSessionStatus;
}

export function getCsrfToken(): string | null {
  return useAuthStore.getState().csrfToken;
}

export function applyBrowserSessionResponse(session: AxisBrowserSessionDto): boolean {
  return useAuthStore.getState().setBrowserSession(session);
}

function requireString(value: string | undefined, field: string): string {
  if (!value) {
    throw new Error(`Browser session response is missing ${field}.`);
  }
  return value;
}
