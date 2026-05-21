import { create } from 'zustand';

interface AuthState {
  accessToken: string | null;
  setAccessToken: (token: string | null) => void;
  clearSession: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  setAccessToken: (token) => set({ accessToken: token }),
  clearSession: () => set({ accessToken: null }),
}));

export function getAccessToken(): string | null {
  return useAuthStore.getState().accessToken;
}
