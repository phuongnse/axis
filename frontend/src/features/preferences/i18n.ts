import i18n from 'i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import { initReactI18next } from 'react-i18next';

import {
  applyDocumentLanguage,
  DEFAULT_LANGUAGE,
  isSupportedLanguage,
  LANGUAGE_STORAGE_KEY,
  normalizeSupportedLanguage,
  persistLanguage,
  type SupportedLanguage,
  supportedLanguages,
} from './language-store';
import { translations } from './translations';

const supportedLanguageValues = supportedLanguages.map((language) => language.value);
const resources = Object.fromEntries(
  Object.entries(translations).map(([language, translation]) => [language, { translation }]),
);

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources,
    fallbackLng: DEFAULT_LANGUAGE,
    supportedLngs: supportedLanguageValues,
    load: 'languageOnly',
    initAsync: false,
    interpolation: {
      escapeValue: false,
      prefix: '{',
      suffix: '}',
    },
    detection: {
      order: ['localStorage', 'navigator'],
      lookupLocalStorage: LANGUAGE_STORAGE_KEY,
      caches: [],
    },
    react: {
      useSuspense: false,
    },
  });

export function currentSiteLanguage(): SupportedLanguage {
  return normalizeSupportedLanguage(i18n.resolvedLanguage ?? i18n.language) ?? DEFAULT_LANGUAGE;
}

export async function changeSiteLanguage(
  language: SupportedLanguage,
  options: { persist?: boolean } = {},
): Promise<void> {
  if (!isSupportedLanguage(language)) return;
  await i18n.changeLanguage(language);
  applyDocumentLanguage(language);
  if (options.persist !== false) {
    persistLanguage(language);
  }
}

i18n.on('languageChanged', (language) => {
  applyDocumentLanguage(normalizeSupportedLanguage(language) ?? DEFAULT_LANGUAGE);
});

applyDocumentLanguage(currentSiteLanguage());

export { i18n };
