import { cn } from '@/lib/utils';

export function TopologyBackdrop({ className }: { className?: string }) {
  return (
    <div
      aria-hidden
      className={cn('pointer-events-none absolute inset-0 overflow-hidden', className)}
    >
      <div className="absolute inset-0 bg-background" />
      <div className="absolute inset-0 bg-[linear-gradient(180deg,color-mix(in_oklch,var(--foreground),transparent_97.6%),transparent_24%,color-mix(in_oklch,var(--background),transparent_34%)_100%)]" />
      <div className="absolute inset-0 bg-[linear-gradient(90deg,color-mix(in_oklch,var(--background),transparent_48%),transparent_20%,transparent_80%,color-mix(in_oklch,var(--background),transparent_48%))]" />
    </div>
  );
}
