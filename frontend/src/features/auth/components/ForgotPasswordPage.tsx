import { Link } from '@tanstack/react-router';
import { Send } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { Button } from '@/components/ui/button';
import { FormField } from '@/components/ui/form-field';
import { Input } from '@/components/ui/input';
import { AuthCard } from '@/features/auth/components/AuthCard';

export function ForgotPasswordPage() {
  const { t } = useTranslation();

  return (
    <AuthCard
      title={t('forgotPassword.title')}
      footer={
        <>
          {t('forgotPassword.footerPrompt')}{' '}
          <Link to="/login" className="font-medium hover:underline">
            {t('common.signIn')}
          </Link>
        </>
      }
    >
      <p className="text-sm text-muted-foreground">{t('forgotPassword.body')}</p>
      <form className="space-y-4" onSubmit={(event) => event.preventDefault()}>
        <FormField
          id="fp-email"
          label={t('common.emailAddress')}
          helpText={t('forgotPassword.emailHelp')}
        >
          {({ describedBy }) => (
            <Input
              id="fp-email"
              type="email"
              autoComplete="username"
              disabled
              aria-describedby={describedBy}
            />
          )}
        </FormField>
        <Button variant="cta" className="h-9 w-full" disabled>
          <Send className="size-4" aria-hidden />
          {t('forgotPassword.sendResetLink')}
        </Button>
      </form>
    </AuthCard>
  );
}
