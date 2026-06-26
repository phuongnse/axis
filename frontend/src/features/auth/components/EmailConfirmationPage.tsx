import { Link } from '@tanstack/react-router';
import { Mail, UserPlus } from 'lucide-react';

import { ActionLink } from '@/components/shared/ActionLink';
import { Button } from '@/components/ui/button';
import { AuthCard } from '@/features/auth/components/AuthCard';
import { AuthNotice } from '@/features/auth/components/AuthNotice';
import { useResendVerification } from '@/features/auth/hooks/useResendVerification';
import { loadRegistrationContext } from '@/features/auth/registration-context';

export function EmailConfirmationPage() {
  const context = loadRegistrationContext();
  const { resend, state, rateLimitMessage, reset } = useResendVerification();

  async function handleResend() {
    if (!context?.email || state === 'sending' || state === 'rate_limited') return;
    reset();
    await resend(context.email).catch(() => undefined);
  }

  return (
    <AuthCard
      title="Check your email"
      footer={
        <>
          Need to use another address?{' '}
          <Link to="/register" className="font-medium hover:underline">
            Back to registration
          </Link>
        </>
      }
    >
      <div className="space-y-4">
        <div className="flex items-start gap-3">
          <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-blue-100 text-blue-800 dark:bg-blue-500/15 dark:text-blue-300">
            <Mail className="h-4 w-4" aria-hidden />
          </div>
          <div className="space-y-2 text-sm text-muted-foreground">
            <p>
              If an account exists for this email, you will receive a verification link shortly.
            </p>
            <p>Open that link to finish registration and enter the dashboard.</p>
            {context?.email ? (
              <p className="text-xs text-muted-foreground/80">Sent to {context.email}</p>
            ) : null}
          </div>
        </div>

        {state === 'sending' ? (
          <AuthNotice title="Sending verification email">Please wait a moment.</AuthNotice>
        ) : null}

        {state === 'success' ? (
          <AuthNotice title="Verification email sent">
            Check your inbox for the latest link.
          </AuthNotice>
        ) : null}

        {state === 'rate_limited' ? (
          <AuthNotice title="Please wait">
            {rateLimitMessage ?? 'Too many requests. Try again shortly.'}
          </AuthNotice>
        ) : null}

        {state === 'error' ? (
          <AuthNotice variant="destructive" title="Unable to resend email">
            Please try again.
          </AuthNotice>
        ) : null}

        <div className="text-sm">
          <span className="text-muted-foreground">Didn't receive it? </span>
          {context?.email ? (
            <Button
              type="button"
              variant="link"
              className="h-auto p-0 text-sm font-medium disabled:text-muted-foreground disabled:no-underline"
              disabled={state === 'sending' || state === 'rate_limited'}
              onClick={() => void handleResend()}
            >
              <Mail className="size-3.5" aria-hidden />
              Resend email
            </Button>
          ) : (
            <Link to="/register" className="font-medium text-primary hover:underline">
              Back to registration
            </Link>
          )}
        </div>

        <ActionLink to="/register" icon={UserPlus} variant="secondary" className="w-full">
          Register another account
        </ActionLink>
      </div>
    </AuthCard>
  );
}
