import { cn } from '@/lib/utils';

export function TopologyBackdrop({ className }: { className?: string }) {
  return (
    <div
      aria-hidden
      className={cn('pointer-events-none absolute inset-0 overflow-hidden', className)}
    >
      <div className="absolute inset-0 bg-background" />
      <div className="absolute inset-0 bg-[linear-gradient(180deg,hsl(var(--foreground)/0.024),transparent_24%,hsl(var(--background)/0.66)_100%)]" />
      <div className="absolute inset-0 bg-[linear-gradient(90deg,hsl(var(--background)/0.52),transparent_20%,transparent_80%,hsl(var(--background)/0.52))]" />
    </div>
  );
}
