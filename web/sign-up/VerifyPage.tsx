import { Alert, Flex, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { apiFetch } from '../src/platform/api'
import { AxisButton } from '../src/platform/AxisButton'
import { FilledText } from '../src/platform/FilledText'
import { useText } from '../src/platform/texts'
import { useDesignTokens } from '../src/platform/themeMode'
import { useSignUpDocuments } from './signUpDocuments'
import { SignUpFrame } from './SignUpFrame'
import type { SignUpLocale } from './texts'

/** Proposed: the answer of `POST /api/sign-up/verification` for the link's token. */
type Verification =
  | { state: 'pending'; email: string; businessName: string; address: string }
  | { state: 'confirmed' }
  | { state: 'expired' }

/** What the page shows: the link being checked, its check failing, or the sign-up it names. */
type Shown = { state: 'checking' } | { state: 'checkFailed' } | Verification

/**
 * Proposed: the page the email's link opens, `/sign-up/verify#<token>`. The token is in the fragment, so no request,
 * log or referrer carries it. The page asks the person to confirm before anything is created, so a mail scanner that
 * opens the link verifies no one. It names what Confirm creates: the business, its address and the email to sign in
 * with. A link already used says the sign-up is confirmed, and an expired or unknown one asks to sign up again, both
 * without saying more about the sign-up. `onConfirmed` gets what the progress page needs.
 */
export function VerifyPage({
  token,
  locale,
  onLocaleChange,
  onConfirmed,
}: {
  token: string
  locale: SignUpLocale
  onLocaleChange: (locale: SignUpLocale) => void
  onConfirmed: (progress: { id: string; secret: string }) => void
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const [documents] = useSignUpDocuments()
  const [shown, setShown] = useState<Shown>({ state: 'checking' })
  const [check, setCheck] = useState(0)
  const [confirming, setConfirming] = useState(false)
  const [confirmFailed, setConfirmFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    apiFetch(
      '/api/sign-up/verification',
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token }),
        signal: controller.signal,
      },
      { promptSignInOnUnauthorized: false },
    )
      .then(async (response) => {
        setShown(response.ok ? ((await response.json()) as Verification) : { state: 'checkFailed' })
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setShown({ state: 'checkFailed' })
        }
      })
    return () => controller.abort()
  }, [token, check])

  const confirm = async () => {
    setConfirmFailed(false)
    setConfirming(true)
    try {
      const response = await apiFetch(
        '/api/sign-up/confirm',
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ token }) },
        { promptSignInOnUnauthorized: false },
      )
      if (response.ok) {
        onConfirmed((await response.json()) as { id: string; secret: string })
      } else if (response.status === 409) {
        // The link was used or expired while the page was open.
        setShown((await response.json()) as Verification)
      } else {
        setConfirmFailed(true)
      }
    } catch {
      setConfirmFailed(true)
    } finally {
      setConfirming(false)
    }
  }

  const title = {
    checking: t('verify.title'),
    checkFailed: t('verify.title'),
    pending: t('verify.title'),
    confirmed: t('verify.confirmed.title'),
    expired: t('verify.expired.title'),
  }[shown.state]

  return (
    <SignUpFrame title={title} locale={locale} onLocaleChange={onLocaleChange} documents={documents}>
      <Flex vertical gap={spacing.md}>
        {shown.state === 'checking' && (
          <Typography.Paragraph style={{ margin: 0 }}>{t('verify.checking')}</Typography.Paragraph>
        )}
        {shown.state === 'checkFailed' && (
          <>
            <Alert type="error" showIcon title={t('verify.checkFailed')} />
            <AxisButton
              role="secondary"
              block
              onClick={() => {
                setShown({ state: 'checking' })
                setCheck((count) => count + 1)
              }}
            >
              {t('verify.tryAgain')}
            </AxisButton>
          </>
        )}
        {shown.state === 'pending' && (
          <>
            {confirmFailed && <Alert type="error" showIcon title={t('verify.confirmFailed')} />}
            <Typography.Paragraph style={{ margin: 0 }}>
              <FilledText
                text={t('verify.intro')}
                parts={{
                  business: <strong>{shown.businessName}</strong>,
                  address: <strong>{shown.address}</strong>,
                  email: <strong>{shown.email}</strong>,
                }}
              />
            </Typography.Paragraph>
            <AxisButton role="finish" block loading={confirming} onClick={confirm}>
              {t('verify.confirm')}
            </AxisButton>
          </>
        )}
        {shown.state === 'confirmed' && (
          <Typography.Paragraph style={{ margin: 0 }}>{t('verify.confirmed.message')}</Typography.Paragraph>
        )}
        {shown.state === 'expired' && (
          <>
            <Typography.Paragraph style={{ margin: 0 }}>{t('verify.expired.message')}</Typography.Paragraph>
            <AxisButton role="finish" block href="/sign-up">
              {t('verify.signUpAgain')}
            </AxisButton>
          </>
        )}
      </Flex>
    </SignUpFrame>
  )
}
