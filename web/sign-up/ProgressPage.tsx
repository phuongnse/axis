import { Flex, Typography } from 'antd'
import { useRef, useState } from 'react'
import { apiFetch } from '../src/platform/api'
import { AxisAlert } from '../src/platform/AxisAlert'
import { FilledText } from '../src/platform/FilledText'
import { StepsBar } from '../src/platform/StepsBar'
import { useText } from '../src/platform/texts'
import { useDesignTokens } from '../src/platform/themeMode'
import { useFollowJob } from '../src/platform/useFollowJob'
import type { SignUpDocuments } from './documents'
import { SignUpFrame } from './SignUpFrame'
import type { SignUpLocale } from './texts'

type ProgressState = { kind: 'settingUp'; delayed: boolean } | { kind: 'ready'; address: string } | { kind: 'unknown' }

type ProgressAnswer =
  { kind: 'unknown' } | { kind: 'step'; step: 'verified' | 'creating' | 'ready' | 'delayed'; address: string }

const interval = 2000

/** The id and the secret after `#`, or `null` when the fragment is not `<id>.<secret>`. */
function parseFragment(fragment: string): { id: string; secret: string } | null {
  const dot = fragment.indexOf('.')
  if (dot <= 0 || dot === fragment.length - 1) {
    return null
  }
  return { id: fragment.slice(0, dot), secret: fragment.slice(dot + 1) }
}

/**
 * The page that follows the trial from Setting up to Ready, see docs/reference/sign-up.md#provisioning. It reads the id and
 * the secret after `#`, asks the progress route every 2 seconds with the secret in a header, and needs no account. An
 * unknown id or a wrong secret shows only that an email follows.
 */
export function ProgressPage({
  locale,
  onLocaleChange,
  documents,
}: {
  locale: SignUpLocale
  onLocaleChange: (locale: SignUpLocale) => void
  documents: SignUpDocuments | undefined
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const [fragment] = useState(() => parseFragment(window.location.hash.slice(1)))
  const [state, setState] = useState<ProgressState>(
    fragment === null ? { kind: 'unknown' } : { kind: 'settingUp', delayed: false },
  )
  const titleRef = useRef<HTMLElement>(null)

  useFollowJob<ProgressAnswer>(fragment?.id ?? null, {
    interval,
    read: async (signal) => {
      const response = await apiFetch(
        `/api/sign-up/${encodeURIComponent(fragment!.id)}`,
        { headers: { 'Sign-Up-Secret': fragment!.secret }, signal },
        { promptSignInOnUnauthorized: false },
      )
      if (response.status === 404) {
        return { kind: 'unknown' }
      }
      if (!response.ok) {
        throw new Error(`The progress answered ${response.status}`)
      }
      return {
        kind: 'step',
        ...((await response.json()) as { step: 'verified' | 'creating' | 'ready' | 'delayed'; address: string }),
      }
    },
    ends: (answer) => answer.kind === 'unknown' || answer.step === 'ready',
    onAnswer: (answer) => setState({ kind: 'settingUp', delayed: answer.kind === 'step' && answer.step === 'delayed' }),
    onEnd: (answer) =>
      setState(answer.kind === 'step' ? { kind: 'ready', address: answer.address } : { kind: 'unknown' }),
    onError: () => 'retry',
  })

  const title =
    state.kind === 'ready'
      ? t('signUp.progress.ready.title')
      : state.kind === 'unknown'
        ? t('signUp.progress.unknown.title')
        : t('signUp.progress.title')

  return (
    <SignUpFrame
      locale={locale}
      onLocaleChange={onLocaleChange}
      title={title}
      titleRef={titleRef}
      documents={documents}
    >
      {state.kind === 'unknown' ? (
        <Typography.Paragraph style={{ margin: 0 }}>{t('signUp.progress.unknown')}</Typography.Paragraph>
      ) : (
        <Flex vertical gap={spacing.md}>
          <StepsBar
            vertical
            titles={[
              t('signUp.progress.step.confirmed'),
              t('signUp.progress.step.settingUp'),
              t('signUp.progress.step.ready'),
            ]}
            current={state.kind === 'ready' ? 3 : 1}
          />
          {state.kind === 'settingUp' && state.delayed && (
            <AxisAlert kind="info" message={t('signUp.progress.delayed')} />
          )}
          <Typography.Paragraph role="status" style={{ margin: 0 }}>
            {state.kind === 'ready' ? (
              <FilledText
                text={t('signUp.progress.ready')}
                parts={{ address: <Typography.Link href={state.address}>{state.address}</Typography.Link> }}
              />
            ) : (
              t('signUp.progress.settingUp')
            )}
          </Typography.Paragraph>
        </Flex>
      )}
    </SignUpFrame>
  )
}
