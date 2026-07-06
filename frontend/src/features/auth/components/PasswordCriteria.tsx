import { CheckCircle2, Circle, XCircle } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { isPasswordHardToGuess, PASSWORD_MIN_LENGTH } from '@/features/auth/password-policy';
import { cn } from '@/lib/utils';

interface PasswordCriteriaProps {
  id: string;
  password: string;
}

function PasswordCriteria({ id, password }: PasswordCriteriaProps) {
  const { t } = useTranslation();
  const hasValue = password.length > 0;
  const items = [
    {
      key: 'length',
      met: hasValue && password.length >= PASSWORD_MIN_LENGTH,
      label: t('password.criteriaLength', { count: PASSWORD_MIN_LENGTH }),
    },
    {
      key: 'hard-to-guess',
      met: hasValue && isPasswordHardToGuess(password),
      label: t('password.criteriaHard'),
    },
  ];

  return (
    <ul id={id} className="space-y-1 pt-1" aria-live="polite">
      {items.map((item) => {
        const Icon = item.met ? CheckCircle2 : hasValue ? XCircle : Circle;
        const status = item.met ? t('password.criteriaMet') : t('password.criteriaMissing');
        return (
          <li
            key={item.key}
            aria-label={`${status}: ${item.label}`}
            className={cn(
              'flex items-center gap-2 text-xs leading-5',
              item.met
                ? 'text-emerald-700 dark:text-emerald-300'
                : hasValue
                  ? 'text-destructive'
                  : 'text-muted-foreground',
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
