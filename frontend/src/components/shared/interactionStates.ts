import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';

export const transientItemHighlight = cn(
  'hover:bg-accent hover:text-accent-foreground focus-visible:bg-accent focus-visible:text-accent-foreground dark:hover:bg-accent dark:hover:text-accent-foreground transition-colors motion-reduce:transition-none',
  axisStyles.motion.duration.state,
  axisStyles.motion.easing.state,
);

export const persistentItemHighlight =
  'bg-secondary text-secondary-foreground hover:bg-secondary hover:text-secondary-foreground dark:hover:bg-secondary dark:hover:text-secondary-foreground';

export const selectedItemHighlight =
  'aria-selected:bg-secondary aria-selected:text-secondary-foreground';

export const sectionTabLabelState = 'text-muted-foreground data-active:text-foreground';

export const keyboardFocusRing = 'focus-visible:ring-2 focus-visible:ring-ring';

export const searchMatchHighlight =
  'rounded-xs bg-primary px-0.5 font-semibold text-primary-foreground';

export const opaquePopoverTriggerSurface = 'bg-popover dark:bg-popover dark:hover:bg-muted';

export const toggledItemHighlight =
  'aria-pressed:bg-secondary aria-pressed:text-secondary-foreground aria-pressed:hover:bg-secondary aria-pressed:hover:text-secondary-foreground dark:aria-pressed:hover:bg-secondary dark:aria-pressed:hover:text-secondary-foreground';
