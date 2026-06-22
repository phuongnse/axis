import { cn } from '@/lib/utils';

export function TopologyBackdrop({ className }: { className?: string }) {
  return (
    <div
      aria-hidden
      className={cn('pointer-events-none absolute inset-0 overflow-hidden', className)}
    >
      <div className="absolute inset-0 bg-gradient-topology-wash dark:bg-transparent" />

      <div className="absolute -left-[14%] top-[16%] h-[54%] w-[56%] -skew-x-12 bg-gradient-to-r from-primary/15 via-primary/5 to-transparent blur-2xl dark:from-primary/10 dark:via-primary/5" />
      <div className="absolute right-[-12%] top-[12%] h-[44%] w-[52%] skew-x-12 bg-gradient-to-l from-chart-2/15 via-chart-2/5 to-transparent blur-2xl dark:from-chart-2/10 dark:via-chart-2/5" />
      <div className="absolute bottom-[-20%] left-[26%] h-[40%] w-[58%] -skew-x-6 bg-gradient-to-t from-accent/15 via-accent/5 to-transparent blur-2xl dark:from-accent/10 dark:via-accent/5" />

      <div className="absolute inset-0 bg-gradient-topology-sheen dark:bg-gradient-topology-sheen-strong" />
      <div className="absolute inset-0 bg-background/5 dark:bg-background/25" />
    </div>
  );
}
