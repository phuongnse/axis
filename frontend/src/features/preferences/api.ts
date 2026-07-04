import { fetchApi } from '@/lib/api';
import type { components } from '@/lib/api-types';

export type LanguagePreference = components['schemas']['LanguagePreferenceDto'];
export type UpdateLanguagePreferenceRequest =
  components['schemas']['UpdateUserLanguagePreferenceRequest'];
export type ThemePreference = components['schemas']['ThemePreferenceDto'];
export type UpdateThemePreferenceRequest =
  components['schemas']['UpdateUserThemePreferenceRequest'];

export async function updateLanguagePreference(
  request: UpdateLanguagePreferenceRequest,
): Promise<LanguagePreference> {
  return fetchApi<LanguagePreference>('/users/me/preferences/language', {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export async function updateThemePreference(
  request: UpdateThemePreferenceRequest,
): Promise<ThemePreference> {
  return fetchApi<ThemePreference>('/users/me/preferences/theme', {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}
