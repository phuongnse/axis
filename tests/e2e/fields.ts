/** Matches a form field's label, with or without the required mark `*` that follows it. */
export function fieldLabel(label: string): RegExp {
  return new RegExp(`^\\s*${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*\\*?\\s*$`);
}
