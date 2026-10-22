import type { ConfigProviderProps } from 'antd'
import enUS from 'antd/es/locale/en_US'
import viVN from 'antd/es/locale/vi_VN'
import 'dayjs/locale/vi'

type AntLocale = NonNullable<ConfigProviderProps['locale']>

// The date pickers' clear button is named "Clear" by default, the same as the clear button of a reference field.
function withDateClearLabel(locale: AntLocale, clear: string): AntLocale {
  return { ...locale, DatePicker: { ...locale.DatePicker!, lang: { ...locale.DatePicker!.lang, clear } } }
}

const enLocale = withDateClearLabel(enUS, 'Clear date')
const viLocale = withDateClearLabel(viVN, 'Xóa ngày')

/** The Ant Design locale for a UI locale. A locale with no mapping gets `en_US`. */
export function antLocaleFor(locale: string): AntLocale {
  return locale.toLowerCase() === 'vi' ? viLocale : enLocale
}

/** The dayjs locale for a UI locale, which dates in Ant Design pickers follow. */
export function dayjsLocaleFor(locale: string): string {
  return locale.toLowerCase() === 'vi' ? 'vi' : 'en'
}
