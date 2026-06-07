import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';

import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
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
        <div className="space-y-1.5">
          <Label htmlFor="fp-email">{t('common.emailAddress')}</Label>
          <Input id="fp-email" type="email" autoComplete="username" disabled />
        </div>
        <Button variant="cta" className="h-9 w-full" disabled>
          {t('forgotPassword.sendResetLink')}
        </Button>
      </form>
    </AuthCard>
  );
}
