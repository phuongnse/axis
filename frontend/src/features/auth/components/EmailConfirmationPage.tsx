import { Link } from '@tanstack/react-router';
import { Mail } from 'lucide-react';

import { AuthCard } from '@/features/auth/components/AuthCard';
import { useResendVerification } from '@/features/auth/hooks/useResendVerification';
import { loadRegistrationContext } from '@/features/auth/registration-context';

function NoticeBanner({
  variant,
  title,
  body,
}: {
  variant: 'info' | 'success' | 'warning';
  title: string;
  body?: string;
}) {
  const styles =
    variant === 'success'
      ? 'border-emerald-500/30 bg-emerald-500/5 text-emerald-700 dark:text-emerald-400'
      : variant === 'warning'
        ? 'border-amber-500/30 bg-amber-500/5 text-amber-800 dark:text-amber-300'
        : 'border-sky-500/30 bg-sky-500/5 text-sky-800 dark:text-sky-300';

  return (
    <div className={`rounded-lg border px-3 py-2 text-sm ${styles}`} role="status">
      <p className="font-medium">{title}</p>
      {body ? <p className="mt-1 text-xs opacity-90">{body}</p> : null}
    </div>
  );
}

export function EmailConfirmationPage() {
  const context = loadRegistrationContext();
  const { resend, state, rateLimitMessage, reset } = useResendVerification();

  async function handleResend() {
    if (!context?.email || state === 'sending' || state === 'rate_limited') return;
    reset();
    try {
      await resend(context.email);
    } catch {
      // Resend state is derived from mutation error.
    }
  }

  return (
    <AuthCard
      title="Check your email"
      footer={
        <>
          Already verified?{' '}
          <Link to="/login" className="font-medium hover:underline">
            Go to sign in →
          </Link>
        </>
      }
    >
      <div className="space-y-4">
        <div className="flex items-start gap-3">
          <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-sky-500/10 text-sky-600">
            <Mail className="h-4 w-4" aria-hidden />
          </div>
          <div className="space-y-2 text-sm text-muted-foreground">
            <p>
              If an account exists for this email, you will receive a verification link shortly.
            </p>
            <p>Check your inbox.</p>
            {context?.email ? (
              <p className="text-xs text-muted-foreground/80">Sent to {context.email}</p>
            ) : null}
          </div>
        </div>

        {state === 'sending' ? (
          <NoticeBanner
            variant="info"
            title="Sending…"
            body="Stay on this screen; the resend link is disabled until the request completes."
          />
        ) : null}

        {state === 'success' ? (
          <NoticeBanner
            variant="success"
            title="Verification email sent"
            body="If an account exists for this email, check your inbox for a new link."
          />
        ) : null}

        {state === 'rate_limited' ? (
          <NoticeBanner
            variant="warning"
            title="Please wait before requesting another email."
            body={rateLimitMessage ?? 'You reached the resend limit (3 per hour).'}
          />
        ) : null}

        {state === 'error' ? (
          <NoticeBanner
            variant="warning"
            title="Couldn't send the email"
            body="Something went wrong sending the verification email. Please try again."
          />
        ) : null}

        <div className="text-sm">
          <span className="text-muted-foreground">Didn&apos;t receive it? </span>
          {context?.email ? (
            <button
              type="button"
              className="font-medium text-primary hover:underline disabled:text-muted-foreground disabled:no-underline"
              disabled={state === 'sending' || state === 'rate_limited'}
              onClick={() => void handleResend()}
            >
              Resend email →
            </button>
          ) : (
            <Link to="/register" className="font-medium text-primary hover:underline">
              Back to registration →
            </Link>
          )}
        </div>

        <Link
          to="/register"
          className="inline-flex w-full h-9 items-center justify-center rounded-md border border-input bg-background px-4 text-sm font-medium hover:bg-accent hover:text-accent-foreground"
        >
          Register another organization
        </Link>
      </div>
    </AuthCard>
  );
}
