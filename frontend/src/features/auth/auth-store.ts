import { create } from 'zustand';

import { sessionDisplayFromAccessToken } from './session-from-token';

interface AuthState {
  accessToken: string | null;
  browserSessionStatus: BrowserSessionStatus;
  userLabel: string | null;
  userInitials: string | null;
  setSession: (token: string) => void;
  clearSession: () => void;
  markBrowserSessionGuest: () => void;
}

export type BrowserSessionStatus = 'unknown' | 'guest' | 'authenticated';

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  browserSessionStatus: 'unknown',
  userLabel: null,
  userInitials: null,
  setSession: (token) => {
    const { userLabel, userInitials } = sessionDisplayFromAccessToken(token);
    set({ accessToken: token, browserSessionStatus: 'authenticated', userLabel, userInitials });
  },
  clearSession: () =>
    set({
      accessToken: null,
      browserSessionStatus: 'unknown',
      userLabel: null,
      userInitials: null,
    }),
  markBrowserSessionGuest: () =>
    set({
      accessToken: null,
      browserSessionStatus: 'guest',
      userLabel: null,
      userInitials: null,
    }),
}));

export function getAccessToken(): string | null {
  return useAuthStore.getState().accessToken;
}

export function getBrowserSessionStatus(): BrowserSessionStatus {
  return useAuthStore.getState().browserSessionStatus;
}
