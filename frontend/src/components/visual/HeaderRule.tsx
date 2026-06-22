import { cn } from '@/lib/utils';

type HeaderRuleTone = 'default' | 'inverted' | 'adaptive';

interface HeaderRuleProps {
  tone?: HeaderRuleTone;
  className?: string;
}

const toneClass: Record<HeaderRuleTone, string> = {
  default: 'from-transparent via-border to-transparent',
  inverted: 'from-transparent via-inverse-border to-transparent',
  adaptive: 'from-transparent via-border to-transparent dark:via-inverse-border',
};

export function HeaderRule({ tone = 'default', className }: HeaderRuleProps) {
  return (
    <div aria-hidden className={cn('h-px w-full bg-gradient-to-r', toneClass[tone], className)} />
  );
}
