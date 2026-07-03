import { fetchApi } from '@/lib/api';
import type { components } from '@/lib/api-types';

export type LanguagePreference = components['schemas']['LanguagePreferenceDto'];
export type UpdateLanguagePreferenceRequest =
  components['schemas']['UpdateUserLanguagePreferenceRequest'];

export async function updateLanguagePreference(
  request: UpdateLanguagePreferenceRequest,
): Promise<LanguagePreference> {
  return fetchApi<LanguagePreference>('/users/me/preferences/language', {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}
