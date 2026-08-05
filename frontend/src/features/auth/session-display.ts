export function sessionDisplayFromLabel(label: string): {
  userLabel: string;
  userInitials: string;
} {
  const parts = label.includes('@')
    ? [label.split('@')[0] ?? '']
    : label.split(/\s+/).filter((part) => part.length > 0);

  const initials = parts
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('');

  return {
    userLabel: label,
    userInitials: initials.length > 0 ? initials : '?',
  };
}
