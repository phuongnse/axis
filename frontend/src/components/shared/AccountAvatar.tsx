import { cn } from '@/lib/utils';

interface AccountAvatarProps {
  initials: string | null | undefined;
  size?: 'sm' | 'md';
  className?: string;
}

const sizeClasses = {
  sm: 'size-[1.625rem] text-[0.72rem]',
  md: 'size-9 text-xs',
} satisfies Record<NonNullable<AccountAvatarProps['size']>, string>;

export function AccountAvatar({ initials, size = 'sm', className }: AccountAvatarProps) {
  const label = initials?.trim() || '?';

  return (
    <span
      className={cn(
        'flex shrink-0 items-center justify-center rounded-full bg-accent font-semibold text-accent-foreground',
        sizeClasses[size],
        className,
      )}
      aria-hidden
    >
      {label}
    </span>
  );
}
