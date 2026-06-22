import type { LucideIcon } from 'lucide-react';
import { LoaderCircle } from 'lucide-react';
import type { ComponentProps } from 'react';

import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

type IconButtonSize = 'icon' | 'icon-xs' | 'icon-sm' | 'icon-lg';

interface IconButtonProps
  extends Omit<ComponentProps<typeof Button>, 'aria-label' | 'children' | 'isLoading' | 'size'> {
  icon: LucideIcon;
  label: string;
  size?: IconButtonSize;
  isLoading?: boolean;
  loadingLabel?: string;
}

function IconButton({
  icon: Icon,
  label,
  size = 'icon',
  variant = 'outline',
  isLoading = false,
  loadingLabel,
  disabled,
  className,
  ...props
}: IconButtonProps) {
  const accessibleLabel = isLoading ? (loadingLabel ?? `${label} loading`) : label;

  return (
    <Button
      aria-label={accessibleLabel}
      aria-busy={isLoading ? true : undefined}
      data-slot="icon-button"
      disabled={disabled || isLoading}
      size={size}
      variant={variant}
      className={cn('shrink-0', className)}
      {...props}
    >
      {isLoading ? (
        <LoaderCircle className="size-4 animate-spin" aria-hidden />
      ) : (
        <Icon className="size-4" aria-hidden />
      )}
    </Button>
  );
}

export { IconButton };
