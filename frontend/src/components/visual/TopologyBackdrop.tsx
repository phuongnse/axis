import { cn } from '@/lib/utils';

export function TopologyBackdrop({ className }: { className?: string }) {
  return (
    <div
      aria-hidden
      className={cn('pointer-events-none absolute inset-0 overflow-hidden', className)}
    >
      <div className="absolute inset-0 bg-[linear-gradient(135deg,hsl(var(--secondary)/0.54)_0%,hsl(var(--background)/0)_48%,hsl(var(--muted)/0.42)_100%)] dark:bg-transparent" />

      <div className="absolute -left-[14%] top-[16%] h-[54%] w-[56%] -skew-x-12 bg-gradient-to-r from-primary/[0.17] via-primary/[0.07] to-transparent blur-2xl dark:from-primary/[0.12] dark:via-primary/5" />
      <div className="absolute right-[-12%] top-[12%] h-[44%] w-[52%] skew-x-12 bg-gradient-to-l from-[hsl(202_53%_43%/0.14)] via-[hsl(202_53%_43%/0.06)] to-transparent blur-2xl dark:from-[hsl(202_53%_43%/0.11)] dark:via-[hsl(202_53%_43%/0.04)]" />
      <div className="absolute bottom-[-20%] left-[26%] h-[40%] w-[58%] -skew-x-6 bg-gradient-to-t from-accent/[0.13] via-accent/5 to-transparent blur-2xl dark:from-accent/10 dark:via-accent/[0.04]" />

      <div className="absolute inset-0 bg-[linear-gradient(115deg,transparent_0%,hsl(var(--background)/0.34)_42%,hsl(var(--background)/0.54)_58%,transparent_100%)] dark:bg-[linear-gradient(115deg,transparent_0%,hsl(var(--background)/0.46)_42%,hsl(var(--background)/0.72)_58%,transparent_100%)]" />
      <div className="absolute inset-0 bg-[hsl(var(--background)/0.04)] dark:bg-background/[0.24]" />
    </div>
  );
}
