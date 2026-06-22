export const axisSemanticColorTokens = [
  '--background',
  '--foreground',
  '--card',
  '--card-foreground',
  '--popover',
  '--popover-foreground',
  '--primary',
  '--primary-foreground',
  '--secondary',
  '--secondary-foreground',
  '--muted',
  '--muted-foreground',
  '--accent',
  '--accent-foreground',
  '--destructive',
  '--destructive-foreground',
  '--border',
  '--input',
  '--ring',
  '--chart-1',
  '--chart-2',
  '--chart-3',
  '--chart-4',
  '--chart-5',
  '--action-accent-border',
  '--action-accent-shadow',
  '--action-primary-shadow',
  '--action-inverse-foreground',
] as const;

export const axisRadiusTokens = ['--radius'] as const;

export const axisTypographyTokens = ['--font-heading', '--font-sans'] as const;

export const axisTailwindColorTokens = {
  background: '--background',
  foreground: '--foreground',
  border: '--border',
  input: '--input',
  ring: '--ring',
  primary: {
    DEFAULT: '--primary',
    foreground: '--primary-foreground',
  },
  secondary: {
    DEFAULT: '--secondary',
    foreground: '--secondary-foreground',
  },
  destructive: {
    DEFAULT: '--destructive',
    foreground: '--destructive-foreground',
  },
  muted: {
    DEFAULT: '--muted',
    foreground: '--muted-foreground',
  },
  accent: {
    DEFAULT: '--accent',
    foreground: '--accent-foreground',
  },
  popover: {
    DEFAULT: '--popover',
    foreground: '--popover-foreground',
  },
  card: {
    DEFAULT: '--card',
    foreground: '--card-foreground',
  },
  chart: {
    '1': '--chart-1',
    '2': '--chart-2',
    '3': '--chart-3',
    '4': '--chart-4',
    '5': '--chart-5',
  },
} as const;

export const axisTailwindRadiusTokens = {
  lg: 'var(--radius)',
  md: 'calc(var(--radius) - 2px)',
  sm: 'calc(var(--radius) - 4px)',
} as const;
