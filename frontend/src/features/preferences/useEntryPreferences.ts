import { useTranslation } from 'react-i18next';
import type { EntryPreferencesModel } from '@/components/shared/EntrySurface';
import { useEntryLanguagePreferenceModel } from './LanguageControl';
import { useEntryThemePreferenceModel } from './ThemeControl';

export function useEntryPreferencesModel(): EntryPreferencesModel {
  const { t } = useTranslation();
  const language = useEntryLanguagePreferenceModel();
  const theme = useEntryThemePreferenceModel();

  return {
    label: t('app.preferences'),
    language,
    theme,
  };
}
