import { cn } from '@/lib/utils';

type HeaderRuleTone = 'default' | 'inverted' | 'adaptive';

interface HeaderRuleProps {
  tone?: HeaderRuleTone;
  className?: string;
}

const toneClass: Record<HeaderRuleTone, string> = {
  default: 'from-transparent via-border to-transparent',
  inverted: 'from-white/0 via-white/15 to-white/0',
  adaptive: 'from-transparent via-border to-transparent dark:via-white/15',
};

export function HeaderRule({ tone = 'default', className }: HeaderRuleProps) {
  return (
    <div aria-hidden className={cn('h-px w-full bg-gradient-to-r', toneClass[tone], className)} />
  );
}
