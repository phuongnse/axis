const en = {
  'shell.preferences.label': 'Language and theme',
  'shell.locale.label': 'Language',
  'shell.locale.en': 'English',
  'shell.locale.vi': 'Tiếng Việt',
  'shell.theme.label': 'Theme',
  'shell.theme.system': 'System',
  'shell.theme.light': 'Light',
  'shell.theme.dark': 'Dark',
  'signUp.title': 'Sign up',
  'signUp.fullName': 'Full name',
  'signUp.email': 'Work email',
  'signUp.businessName': 'Business name',
  'signUp.subdomain': 'Subdomain',
  'signUp.application': 'Application',
  'signUp.language': 'Language',
  'signUp.terms': 'I accept the terms of service and the data processing agreement.',
  'signUp.subdomain.checking': 'Checking the subdomain.',
  'signUp.subdomain.free': 'This subdomain is free.',
  'signUp.subdomain.invalid': 'Use lower-case letters, digits and hyphens, up to 40 characters.',
  'signUp.subdomain.reserved': 'This subdomain is reserved. Choose another.',
  'signUp.subdomain.taken': 'This subdomain is taken. Choose another.',
  'signUp.subdomain.held': 'This subdomain is held for a short time. Choose another.',
  'signUp.subdomain.unavailable': 'The subdomain could not be checked. Try again later.',
}

const vi: Record<keyof typeof en, string> = {
  'shell.preferences.label': 'Ngôn ngữ và giao diện',
  'shell.locale.label': 'Ngôn ngữ',
  'shell.locale.en': 'English',
  'shell.locale.vi': 'Tiếng Việt',
  'shell.theme.label': 'Giao diện',
  'shell.theme.system': 'Theo hệ thống',
  'shell.theme.light': 'Sáng',
  'shell.theme.dark': 'Tối',
  'signUp.title': 'Đăng ký',
  'signUp.fullName': 'Họ và tên',
  'signUp.email': 'Email công việc',
  'signUp.businessName': 'Tên doanh nghiệp',
  'signUp.subdomain': 'Tên miền phụ',
  'signUp.application': 'Ứng dụng',
  'signUp.language': 'Ngôn ngữ',
  'signUp.terms': 'Tôi đồng ý với điều khoản dịch vụ và thỏa thuận xử lý dữ liệu.',
  'signUp.subdomain.checking': 'Đang kiểm tra tên miền phụ.',
  'signUp.subdomain.free': 'Tên miền phụ này còn trống.',
  'signUp.subdomain.invalid': 'Chỉ dùng chữ thường, chữ số và dấu gạch ngang, tối đa 40 ký tự.',
  'signUp.subdomain.reserved': 'Tên miền phụ này đã được giữ trước. Chọn tên khác.',
  'signUp.subdomain.taken': 'Tên miền phụ này đã có người dùng. Chọn tên khác.',
  'signUp.subdomain.held': 'Tên miền phụ này đang được giữ tạm. Chọn tên khác.',
  'signUp.subdomain.unavailable': 'Không kiểm tra được tên miền phụ. Thử lại sau.',
}

/** The languages of the sign-up page. */
export const signUpLocales = ['en', 'vi'] as const

export type SignUpLocale = (typeof signUpLocales)[number]

/**
 * The texts of the sign-up page. The control plane has no `/api/texts`, and the platform's catalogue holds about 1,300
 * keys for each language, so the page keeps its own small one. `vi` is typed as `en`'s keys, so a key cannot miss a language.
 */
export const signUpTexts: Record<SignUpLocale, Record<keyof typeof en, string>> = { en, vi }
