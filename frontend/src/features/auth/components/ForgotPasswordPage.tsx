import { Link } from '@tanstack/react-router';
import { Send } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { Button } from '@/components/ui/button';
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field';
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
        <Field>
          <FieldLabel htmlFor="fp-email">{t('common.emailAddress')}</FieldLabel>
          <Input
            id="fp-email"
            type="email"
            autoComplete="username"
            disabled
            aria-describedby="fp-email-help"
          />
          <FieldDescription id="fp-email-help">{t('forgotPassword.emailHelp')}</FieldDescription>
        </Field>
        <Button className="h-9 w-full" disabled>
          <Send className="size-4" aria-hidden />
          {t('forgotPassword.sendResetLink')}
        </Button>
      </form>
    </AuthCard>
  );
}
