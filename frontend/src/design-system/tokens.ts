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
  '--state-info',
  '--state-info-foreground',
  '--state-info-background',
  '--state-info-border',
  '--state-success',
  '--state-success-foreground',
  '--state-success-background',
  '--state-success-border',
  '--state-warning',
  '--state-warning-foreground',
  '--state-warning-background',
  '--state-warning-border',
  '--action-accent-border',
  '--action-accent-shadow',
  '--action-primary-shadow',
  '--action-inverse-foreground',
  '--inverse',
  '--inverse-foreground',
  '--inverse-muted',
  '--inverse-border',
  '--sidebar',
  '--sidebar-foreground',
  '--sidebar-muted',
  '--sidebar-border',
  '--sidebar-accent',
  '--sidebar-accent-foreground',
] as const;

export const axisRadiusTokens = ['--radius'] as const;

export const axisTypographyTokens = [
  '--font-heading',
  '--font-sans',
  '--type-body-sm',
  '--type-label-xs',
  '--type-heading-md',
] as const;

export const axisSpacingTokens = [
  '--space-form-gap',
  '--space-section-gap',
  '--space-page-padding',
] as const;

export const axisSizingTokens = [
  '--size-control-xs',
  '--size-control-sm',
  '--size-control-md',
  '--size-control-lg',
  '--size-icon-sm',
  '--size-icon-md',
  '--size-icon-lg',
  '--size-sidebar',
] as const;

export const axisShadowTokens = [
  '--shadow-control',
  '--shadow-panel',
  '--shadow-feature-panel',
] as const;

export const axisMotionTokens = [
  '--motion-duration-fast',
  '--motion-duration-standard',
  '--motion-easing-standard',
] as const;

export const axisBreakpointTokens = [
  '--breakpoint-mobile',
  '--breakpoint-tablet',
  '--breakpoint-desktop',
] as const;

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
  state: {
    info: {
      DEFAULT: '--state-info',
      foreground: '--state-info-foreground',
      background: '--state-info-background',
      border: '--state-info-border',
    },
    success: {
      DEFAULT: '--state-success',
      foreground: '--state-success-foreground',
      background: '--state-success-background',
      border: '--state-success-border',
    },
    warning: {
      DEFAULT: '--state-warning',
      foreground: '--state-warning-foreground',
      background: '--state-warning-background',
      border: '--state-warning-border',
    },
  },
  inverse: {
    DEFAULT: '--inverse',
    foreground: '--inverse-foreground',
    muted: '--inverse-muted',
    border: '--inverse-border',
  },
  sidebar: {
    DEFAULT: '--sidebar',
    foreground: '--sidebar-foreground',
    muted: '--sidebar-muted',
    border: '--sidebar-border',
    accent: '--sidebar-accent',
    'accent-foreground': '--sidebar-accent-foreground',
  },
} as const;

export const axisTailwindRadiusTokens = {
  lg: 'var(--radius)',
  md: 'calc(var(--radius) - 2px)',
  sm: 'calc(var(--radius) - 4px)',
} as const;
