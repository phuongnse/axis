import { create } from 'zustand';

import { sessionDisplayFromAccessToken } from './session-from-token';

interface AuthState {
  accessToken: string | null;
  userLabel: string | null;
  userInitials: string | null;
  setSession: (token: string) => void;
  clearSession: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  userLabel: null,
  userInitials: null,
  setSession: (token) => {
    const { userLabel, userInitials } = sessionDisplayFromAccessToken(token);
    set({ accessToken: token, userLabel, userInitials });
  },
  clearSession: () => set({ accessToken: null, userLabel: null, userInitials: null }),
}));

export function getAccessToken(): string | null {
  return useAuthStore.getState().accessToken;
}
