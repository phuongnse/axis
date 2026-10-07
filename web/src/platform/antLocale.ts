import type { Locale } from 'antd/es/locale'
import enUS from 'antd/locale/en_US'
import viVN from 'antd/locale/vi_VN'
import 'dayjs/locale/vi'

/** The Ant Design locale for a UI locale. A locale with no mapping gets `en_US`. */
export function antLocaleFor(locale: string): Locale {
  return locale.toLowerCase() === 'vi' ? viVN : enUS
}

/** The dayjs locale for a UI locale, which dates in Ant Design pickers follow. */
export function dayjsLocaleFor(locale: string): string {
  return locale.toLowerCase() === 'vi' ? 'vi' : 'en'
}
