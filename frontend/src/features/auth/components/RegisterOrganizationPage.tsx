import { Link } from '@tanstack/react-router';
import { Building2, Loader2 } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { Button } from '@/components/ui/button';
import { CheckboxField } from '@/components/ui/checkbox-field';
import { FormField } from '@/components/ui/form-field';
import { Input } from '@/components/ui/input';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { AuthNotice } from '@/features/auth/components/AuthNotice';
import { useRegisterOrganization } from '@/features/auth/hooks/useRegisterOrganization';
import { useSlugPreview } from '@/features/auth/hooks/useSlugPreview';

export function RegisterOrganizationPage() {
  const { t } = useTranslation();
  const { form, loading, submit } = useRegisterOrganization();
  const {
    register,
    handleSubmit,
    watch,
    formState: { errors },
  } = form;
  const orgName = watch('orgName');
  const slugPreview = useSlugPreview(orgName);
  const submitError = errors.root?.message;

  return (
    <AuthCard
      title={t('organizationRegistration.title')}
      footer={
        <>
          {t('organizationRegistration.footerPrompt')}{' '}
          <Link to="/register" className="font-medium hover:underline">
            {t('organizationRegistration.personalAccount')}
          </Link>
        </>
      }
    >
      <form className="space-y-4" onSubmit={handleSubmit(submit)} noValidate>
        <FormField
          id="orgName"
          label={t('organizationRegistration.orgName')}
          helpText={t('organizationRegistration.orgNameHelp')}
          error={errors.orgName?.message}
        >
          {({ describedBy }) => (
            <Input
              id="orgName"
              autoComplete="organization"
              aria-describedby={describedBy}
              aria-invalid={errors.orgName ? true : undefined}
              {...register('orgName')}
            />
          )}
        </FormField>

        <div className="rounded-md border border-border bg-muted/35 px-3 py-2 text-sm">
          <p className="text-xs font-medium uppercase text-muted-foreground">
            {t('organizationRegistration.slugPreviewLabel')}
          </p>
          <p className="mt-1 font-medium text-foreground" aria-live="polite">
            {slugPreview.loading
              ? t('organizationRegistration.slugPreviewLoading')
              : slugPreview.slug
                ? t('organizationRegistration.slugPreviewValue', { slug: slugPreview.slug })
                : t('organizationRegistration.slugPreviewHelp')}
          </p>
        </div>

        <FormField
          id="organizationContactEmail"
          label={t('organizationRegistration.contactEmail')}
          helpText={t('organizationRegistration.contactEmailHelp')}
          error={errors.organizationContactEmail?.message}
        >
          {({ describedBy }) => (
            <Input
              id="organizationContactEmail"
              type="email"
              autoComplete="email"
              aria-describedby={describedBy}
              aria-invalid={errors.organizationContactEmail ? true : undefined}
              {...register('organizationContactEmail')}
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
          {loading
            ? t('organizationRegistration.registering')
            : t('organizationRegistration.submit')}
        </Button>
      </form>
    </AuthCard>
  );
}
