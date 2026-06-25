import { CheckCircle2, Circle, XCircle } from 'lucide-react';

import { isPasswordHardToGuess, PASSWORD_MIN_LENGTH } from '@/features/auth/password-policy';
import { cn } from '@/lib/utils';

interface PasswordCriteriaProps {
  id: string;
  password: string;
}

function PasswordCriteria({ id, password }: PasswordCriteriaProps) {
  const hasValue = password.length > 0;
  const items = [
    {
      key: 'length',
      met: hasValue && password.length >= PASSWORD_MIN_LENGTH,
      label: `At least ${PASSWORD_MIN_LENGTH} characters`,
    },
    {
      key: 'hard-to-guess',
      met: hasValue && isPasswordHardToGuess(password),
      label: 'Hard to guess',
    },
  ];

  return (
    <ul id={id} className="space-y-1 pt-1" aria-live="polite">
      {items.map((item) => {
        const Icon = item.met ? CheckCircle2 : hasValue ? XCircle : Circle;
        const status = item.met ? 'Met' : 'Missing';
        return (
          <li
            key={item.key}
            aria-label={`${status}: ${item.label}`}
            className={cn(
              'flex items-center gap-2 text-xs leading-5',
              item.met ? 'text-primary' : hasValue ? 'text-destructive' : 'text-muted-foreground',
            )}
          >
            <Icon className="size-3.5 shrink-0" aria-hidden />
            <span>{item.label}</span>
          </li>
        );
      })}
    </ul>
  );
}

export { PasswordCriteria };
