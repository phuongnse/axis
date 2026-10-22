import type { ConfigProviderProps } from 'antd'
import enUS from 'antd/locale/en_US'
import viVN from 'antd/locale/vi_VN'
import 'dayjs/locale/vi'

type AntLocale = NonNullable<ConfigProviderProps['locale']>

// Ant Design 5 ships `antd/locale/*` as CommonJS, so the production bundle can hand over `{ __esModule, default }`
// instead of the locale. Ant Design 6 ships ES modules, where the import is already the locale.
function unwrapLocale(imported: AntLocale): AntLocale {
  const wrapped = imported as { default?: AntLocale }
  return wrapped.default ?? imported
}

// The date pickers' clear button is named "Clear" by default, the same as the clear button of a reference field.
function withDateClearLabel(locale: AntLocale, clear: string): AntLocale {
  return { ...locale, DatePicker: { ...locale.DatePicker!, lang: { ...locale.DatePicker!.lang, clear } } }
}

const enLocale = withDateClearLabel(unwrapLocale(enUS), 'Clear date')
const viLocale = withDateClearLabel(unwrapLocale(viVN), 'Xóa ngày')

/** The Ant Design locale for a UI locale. A locale with no mapping gets `en_US`. */
export function antLocaleFor(locale: string): AntLocale {
  return locale.toLowerCase() === 'vi' ? viLocale : enLocale
}

/** The dayjs locale for a UI locale, which dates in Ant Design pickers follow. */
export function dayjsLocaleFor(locale: string): string {
  return locale.toLowerCase() === 'vi' ? 'vi' : 'en'
}
