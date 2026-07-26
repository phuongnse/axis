import { fetchApi } from '@/lib/api';
import type * as ApiTypes from '@/lib/api-generated';

export type LanguagePreference = ApiTypes.LanguagePreferenceDto;
export type UpdateLanguagePreferenceRequest = ApiTypes.UpdateUserLanguagePreferenceRequest;
export type ThemePreference = ApiTypes.ThemePreferenceDto;
export type UpdateThemePreferenceRequest = ApiTypes.UpdateUserThemePreferenceRequest;

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
