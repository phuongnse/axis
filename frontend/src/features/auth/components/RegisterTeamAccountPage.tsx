import { Link } from '@tanstack/react-router';
import { Building2, CheckCircle2, Globe2, Loader2, Mail, UserPlus } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { Button } from '@/components/ui/button';
import { CheckboxField } from '@/components/ui/checkbox-field';
import { FormField } from '@/components/ui/form-field';
import { Input } from '@/components/ui/input';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { AuthNotice } from '@/features/auth/components/AuthNotice';
import { useRegisterTeamAccount } from '@/features/auth/hooks/useRegisterTeamAccount';
import { useSlugPreview } from '@/features/auth/hooks/useSlugPreview';

export function RegisterTeamAccountPage() {
  const { t } = useTranslation();
  const { form, loading, submit } = useRegisterTeamAccount();
  const onboardingSteps = [
    {
      icon: Building2,
      title: t('teamAccountRegistration.stepTeamAccountTitle'),
      body: t('teamAccountRegistration.stepTeamAccountBody'),
    },
    {
      icon: Mail,
      title: t('teamAccountRegistration.stepVerificationTitle'),
      body: t('teamAccountRegistration.stepVerificationBody'),
    },
    {
      icon: UserPlus,
      title: t('teamAccountRegistration.stepOwnerTitle'),
      body: t('teamAccountRegistration.stepOwnerBody'),
    },
  ];
  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
  } = form;
  const teamAccountName = watch('teamAccountName');
  const slugPreview = useSlugPreview(teamAccountName);
  const submitError = errors.root?.message;

  return (
    <AuthCard
      title={t('teamAccountRegistration.title')}
      footer={
        <>
          {t('teamAccountRegistration.footerPrompt')}{' '}
          <Link to="/register" className="font-medium hover:underline">
            {t('teamAccountRegistration.personalAccount')}
          </Link>
        </>
      }
    >
      <div className="space-y-4">
        <AuthNotice variant="info" title={t('teamAccountRegistration.bannerTitle')}>
          {t('teamAccountRegistration.bannerBody')}
        </AuthNotice>

        <ol className="space-y-2" aria-label={t('teamAccountRegistration.stepsLabel')}>
          {onboardingSteps.map((step) => {
            const StepIcon = step.icon;
            return (
              <li key={step.title} className="grid grid-cols-[1.5rem_1fr] gap-2 text-sm">
                <span className="mt-0.5 flex size-5 items-center justify-center rounded-full bg-primary/10 text-primary">
                  <StepIcon className="size-3.5" aria-hidden />
                </span>
                <span>
                  <span className="block font-medium text-foreground">{step.title}</span>
                  <span className="block text-xs text-muted-foreground">{step.body}</span>
                </span>
              </li>
            );
          })}
        </ol>
      </div>

      <form className="mt-5 space-y-4" onSubmit={handleSubmit(submit)} noValidate>
        <FormField
          id="teamAccountName"
          label={t('teamAccountRegistration.teamAccountName')}
          helpText={t('teamAccountRegistration.teamAccountNameHelp')}
          error={errors.teamAccountName?.message}
        >
          {({ describedBy }) => (
            <Input
              id="teamAccountName"
              autoComplete="teamAccount"
              aria-describedby={describedBy}
              aria-invalid={errors.teamAccountName ? true : undefined}
              {...register('teamAccountName')}
            />
          )}
        </FormField>

        <div className="border-l-2 border-primary/45 pl-3 text-sm">
          <p className="inline-flex items-center gap-1 text-xs font-medium uppercase text-muted-foreground">
            <Globe2 className="size-3.5" aria-hidden />
            {t('teamAccountRegistration.slugPreviewLabel')}
          </p>
          <p
            className="mt-1 break-all rounded-md bg-muted/50 px-3 py-2 font-mono text-[13px] font-medium text-foreground"
            aria-live="polite"
          >
            {slugPreview.loading
              ? t('teamAccountRegistration.slugPreviewLoading')
              : slugPreview.slug
                ? t('teamAccountRegistration.slugPreviewValue', { slug: slugPreview.slug })
                : t('teamAccountRegistration.slugPreviewHelp')}
          </p>
          {slugPreview.slug ? (
            <p className="mt-1 inline-flex items-center gap-1 text-xs text-emerald-700 dark:text-emerald-400">
              <CheckCircle2 className="size-3.5" aria-hidden />
              {t('teamAccountRegistration.slugPreviewReady')}
            </p>
          ) : null}
        </div>

        <FormField
          id="teamContactEmail"
          label={t('teamAccountRegistration.contactEmail')}
          helpText={t('teamAccountRegistration.contactEmailHelp')}
          error={errors.teamContactEmail?.message}
        >
          {({ describedBy }) => (
            <Input
              id="teamContactEmail"
              type="email"
              autoComplete="email"
              aria-describedby={describedBy}
              aria-invalid={errors.teamContactEmail ? true : undefined}
              {...register('teamContactEmail')}
            />
          )}
        </FormField>

        <CheckboxField
          id="acceptedTerms"
          error={errors.acceptedTerms?.message}
          {...register('acceptedTerms')}
        >
          {t('register.agreePrefix')}{' '}
          <a
            href="/legal/terms"
            target="_blank"
            rel="noopener noreferrer"
            className="font-medium text-primary hover:underline"
          >
            {t('register.termsOfService')}
          </a>{' '}
          {t('register.agreeMiddle')}{' '}
          <a
            href="/legal/privacy"
            target="_blank"
            rel="noopener noreferrer"
            className="font-medium text-primary hover:underline"
          >
            {t('register.privacyPolicy')}
          </a>
        </CheckboxField>

        {submitError ? <AuthNotice variant="error">{submitError}</AuthNotice> : null}

        <Button type="submit" variant="cta" className="w-full h-9" disabled={loading}>
          {loading ? (
            <Loader2 className="size-4 animate-spin" aria-hidden />
          ) : (
            <Building2 className="size-4" aria-hidden />
          )}
          {loading ? t('teamAccountRegistration.registering') : t('teamAccountRegistration.submit')}
        </Button>
      </form>
    </AuthCard>
  );
}
