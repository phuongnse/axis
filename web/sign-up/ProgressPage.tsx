import { Alert, Flex, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { apiFetch } from '../src/platform/api'
import { FilledText } from '../src/platform/FilledText'
import { FormStepsBar } from '../src/platform/FormStepsBar'
import { useText } from '../src/platform/texts'
import { useDesignTokens } from '../src/platform/themeMode'
import { useSignUpDocuments } from './signUpDocuments'
import { SignUpFrame } from './SignUpFrame'
import type { SignUpLocale } from './texts'

/** Proposed: the answer of `GET /api/sign-up/{id}` with the `Sign-Up-Secret` header. */
type Progress = { step: 'verified' | 'creating' | 'ready' | 'delayed'; address: string }

/** How often the page asks for the progress, as docs/reference/sign-up.md#provisioning says. */
const pollEvery = 2000

/**
 * Proposed: the page after Confirm, `/sign-up/progress#<id>.<secret>`, which follows the trial being set up: confirmed,
 * setting up, then ready, on the wizard's steps bar. It asks every two seconds until the trial is ready, and the
 * sentence under the bar is a status region, so a screen reader hears each change. Ready names the address and says to
 * set the password from the invitation email. A set-up that needs more time says so in a notice and keeps asking. An
 * unknown id or a wrong secret says only that an email follows. The person may close the page at any time.
 */
export function ProgressPage({
  id,
  secret,
  locale,
  onLocaleChange,
}: {
  id: string
  secret: string
  locale: SignUpLocale
  onLocaleChange: (locale: SignUpLocale) => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [documents] = useSignUpDocuments()
  const [progress, setProgress] = useState<Progress | 'unknown'>()

  useEffect(() => {
    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout> | undefined
    const ask = async () => {
      let done = false
      try {
        const response = await apiFetch(
          `/api/sign-up/${encodeURIComponent(id)}`,
          { headers: { 'Sign-Up-Secret': secret }, signal: controller.signal },
          { promptSignInOnUnauthorized: false },
        )
        if (response.status === 404) {
          setProgress('unknown')
          done = true
        } else if (response.ok) {
          const answer = (await response.json()) as Progress
          setProgress(answer)
          done = answer.step === 'ready'
        }
        // Any other answer keeps what the page shows, and the next ask tries again.
      } catch {
        // The next ask tries again.
      }
      if (!done && !controller.signal.aborted) {
        timer = setTimeout(() => void ask(), pollEvery)
      }
    }
    void ask()
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [id, secret])

  if (progress === 'unknown') {
    return (
      <SignUpFrame
        title={t('progress.unknown.title')}
        locale={locale}
        onLocaleChange={onLocaleChange}
        documents={documents}
      >
        <Typography.Paragraph style={{ margin: 0 }}>{t('progress.unknown.message')}</Typography.Paragraph>
      </SignUpFrame>
    )
  }

  const step = progress?.step ?? 'verified'
  const address = progress?.address ?? ''
  const current = { verified: 0, creating: 1, delayed: 1, ready: 2 }[step]
  return (
    <SignUpFrame
      title={step === 'ready' ? t('progress.ready.title') : t('progress.title')}
      locale={locale}
      onLocaleChange={onLocaleChange}
      documents={documents}
    >
      <Flex vertical gap={spacing.md}>
        {/* Proposed: the wizard's steps bar shows a set-up's steps too. */}
        <FormStepsBar
          vertical
          titles={[t('progress.step.verified'), t('progress.step.creating'), t('progress.step.ready')]}
          current={current}
        />
        {step === 'delayed' && <Alert type="info" showIcon title={t('progress.delayed')} />}
        <Typography.Paragraph role="status" style={{ margin: 0 }}>
          {step === 'ready' ? (
            <FilledText
              text={t('progress.ready.message')}
              parts={{
                address: (
                  <Typography.Link href={`https://${address}`} style={{ color: colour.primaryText }}>
                    {address}
                  </Typography.Link>
                ),
              }}
            />
          ) : (
            // Proposed: the page opens right after Confirm, so it says the trial is being set up before the first answer.
            t('progress.settingUp')
          )}
        </Typography.Paragraph>
      </Flex>
    </SignUpFrame>
  )
}
