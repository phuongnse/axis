/**
 * Proposed: the style sheet that gives the box of a checkbox or radio in a field with an error a border in
 * `dangerText`, the colour of the field's message, in every form. Ant Design marks the field but draws the box as
 * usual, so only the message showed the error. `dangerText` keeps the border at 3:1 on `surface` in both modes.
 */
export function choiceErrorStyles(dangerText: string): string {
  const field = '.ant-form-item-has-error'
  return [`${field} .ant-checkbox:not(.ant-checkbox-checked)`, `${field} .ant-radio:not(.ant-radio-checked)`]
    .map((selector) => `${selector} { border-color: ${dangerText}; }`)
    .join('\n')
}
