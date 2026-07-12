import { Avatar, AvatarFallback } from '@/components/ui/avatar';

interface AccountAvatarProps {
  initials: string | null | undefined;
  size?: 'sm' | 'md';
}

export function AccountAvatar({ initials, size = 'sm' }: AccountAvatarProps) {
  const label = initials?.trim() || '?';

  return (
    <Avatar size={size === 'sm' ? 'sm' : 'default'} aria-hidden>
      <AvatarFallback>{label}</AvatarFallback>
    </Avatar>
  );
}
