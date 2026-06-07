import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';

import {
  DEFAULT_LANGUAGE,
  resources,
  SUPPORTED_LANGUAGES,
} from '@/features/preferences/i18n-resources';
import { readInitialLanguage } from '@/features/preferences/preferences-store';

void i18n.use(initReactI18next).init({
  resources,
  lng: readInitialLanguage(),
  fallbackLng: DEFAULT_LANGUAGE,
  supportedLngs: SUPPORTED_LANGUAGES,
  interpolation: {
    escapeValue: false,
  },
  returnNull: false,
});

export { i18n };
