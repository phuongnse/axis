import axios from 'axios';

// Create a configured axios instance
export const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL || '/api',
  withCredentials: true, // Necessary for HttpOnly cookies (refresh token)
  headers: {
    'Content-Type': 'application/json',
  },
});

// Add a request interceptor to attach correlation IDs or other headers if needed
api.interceptors.request.use((config) => {
  // If we had a Zustand store for the access token, we could inject it here.
  // E.g., const token = useAuthStore.getState().accessToken;
  // if (token) config.headers.Authorization = `Bearer ${token}`;

  return config;
});

// Add a response interceptor for global error handling
api.interceptors.response.use(
  (response) => response,
  async (error) => {
    // Check if error is 401 Unauthorized
    if (error.response?.status === 401) {
      // Logic to refresh token or redirect to login could go here
      // For now, we rely on TanStack Router loader to redirect on 401
    }

    return Promise.reject(error);
  }
);
