import { Flex, Typography } from 'antd'
import { useCallback, useEffect, useRef, useState } from 'react'
import { apiFetch } from '../src/platform/api'
import { AxisAlert } from '../src/platform/AxisAlert'
import { AxisButton } from '../src/platform/AxisButton'
import { FilledText } from '../src/platform/FilledText'
import { useText } from '../src/platform/texts'
import { useDesignTokens } from '../src/platform/themeMode'
import { SignUpFrame } from './SignUpFrame'
import type { SignUpLocale } from './texts'

type VerifyState =
  | { state: 'checking' }
  | { state: 'checkFailed' }
  | { state: 'pending'; email: string; businessName: string; address: string }
  | { state: 'confirmed' }
  | { state: 'expired' }

/** Posts the token in a request body, because the fragment it came from reaches no request line, server log or referrer. */
function postToken(url: string, token: string, signal?: AbortSignal) {
  return apiFetch(
    url,
    { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ token }), signal },
    { promptSignInOnUnauthorized: false },
  )
}

/**
 * The page that the link of the confirm email opens, see docs/reference/sign-up.md#the-verify-page. It reads the token after
 * `#`, checks the link, says what will start and confirms when the person presses Confirm. Until the progress page exists, a
 * confirm that succeeds shows the already confirmed state.
 */
export function VerifyPage({
  locale,
  onLocaleChange,
}: {
  locale: SignUpLocale
  onLocaleChange: (locale: SignUpLocale) => void
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const [token] = useState(() => window.location.hash.slice(1))
  const [page, setPage] = useState<VerifyState>({ state: 'checking' })
  const [confirming, setConfirming] = useState(false)
  const [confirmFailed, setConfirmFailed] = useState(false)
  const [attempt, setAttempt] = useState(0)
  const titleRef = useRef<HTMLElement>(null)
  const moveFocus = useRef(false)

  useEffect(() => {
    const controller = new AbortController()
    postToken('/api/sign-up/verification', token, controller.signal)
      .then(async (response) => {
        if (!response.ok) {
          setPage({ state: 'checkFailed' })
          return
        }
        const body = (await response.json()) as {
          state: 'pending' | 'confirmed' | 'expired'
          email: string
          businessName: string
          address: string
        }
        setPage(
          body.state === 'pending'
            ? { state: 'pending', email: body.email, businessName: body.businessName, address: body.address }
            : { state: body.state },
        )
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setPage({ state: 'checkFailed' })
        }
      })
    return () => controller.abort()
  }, [token, attempt])

  useEffect(() => {
    if (moveFocus.current && page.state !== 'checking') {
      moveFocus.current = false
      titleRef.current?.focus()
    }
  }, [page])

  const confirm = useCallback(async () => {
    setConfirming(true)
    setConfirmFailed(false)
    try {
      const response = await postToken('/api/sign-up/confirm', token)
      if (response.ok) {
        moveFocus.current = true
        setPage({ state: 'confirmed' })
      } else if (response.status === 409) {
        // The link was used or expired while the page was open, so the page shows the state that the answer names.
        const { state } = (await response.json()) as { state: 'confirmed' | 'expired' }
        moveFocus.current = true
        setPage({ state: state === 'expired' ? 'expired' : 'confirmed' })
      } else {
        setConfirmFailed(true)
      }
    } catch {
      setConfirmFailed(true)
    } finally {
      setConfirming(false)
    }
  }, [token])

  const title =
    page.state === 'confirmed'
      ? t('signUp.verify.confirmed.title')
      : page.state === 'expired'
        ? t('signUp.verify.expired.title')
        : t('signUp.verify.title')

  return (
    <SignUpFrame locale={locale} onLocaleChange={onLocaleChange} title={title} titleRef={titleRef}>
      {page.state === 'checking' && (
        <Typography.Paragraph style={{ margin: 0 }}>{t('signUp.verify.checking')}</Typography.Paragraph>
      )}
      {page.state === 'checkFailed' && (
        <Flex vertical gap={spacing.md} align="flex-start">
          <AxisAlert kind="error" message={t('signUp.verify.checkFailed')} />
          <AxisButton
            role="secondary"
            onClick={() => {
              moveFocus.current = true
              setPage({ state: 'checking' })
              setAttempt((count) => count + 1)
            }}
          >
            {t('signUp.verify.tryAgain')}
          </AxisButton>
        </Flex>
      )}
      {page.state === 'pending' && (
        <>
          {confirmFailed && <AxisAlert kind="error" message={t('signUp.verify.confirmFailed')} />}
          <Typography.Paragraph style={{ margin: 0 }}>
            <FilledText
              text={t('signUp.verify.sentence')}
              parts={{
                business: <Typography.Text strong>{page.businessName}</Typography.Text>,
                address: <Typography.Text strong>{page.address}</Typography.Text>,
                email: <Typography.Text strong>{page.email}</Typography.Text>,
              }}
            />
          </Typography.Paragraph>
          <AxisButton role="finish" block loading={confirming} onClick={confirm}>
            {t('signUp.verify.confirm')}
          </AxisButton>
        </>
      )}
      {page.state === 'confirmed' && (
        <Typography.Paragraph style={{ margin: 0 }}>{t('signUp.verify.confirmed.body')}</Typography.Paragraph>
      )}
      {page.state === 'expired' && (
        <>
          <Typography.Paragraph style={{ margin: 0 }}>{t('signUp.verify.expired.body')}</Typography.Paragraph>
          <AxisButton role="finish" block href="/sign-up">
            {t('signUp.verify.signUpAgain')}
          </AxisButton>
        </>
      )}
    </SignUpFrame>
  )
}
