import { describe, expect, it } from 'vitest'
import { antLocaleFor } from './antLocale'

describe('antLocaleFor', () => {
  it('names the date pickers clear button apart from the Clear button of a reference', () => {
    expect(antLocaleFor('en').DatePicker?.lang.clear).toBe('Clear date')
    expect(antLocaleFor('vi').DatePicker?.lang.clear).toBe('Xóa ngày')
  })
})
