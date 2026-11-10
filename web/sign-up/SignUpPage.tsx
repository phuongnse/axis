import { Flex, Form, Input, Select, Typography } from 'antd'
import { useEffect, useRef, useState } from 'react'
import { apiFetch } from '../src/platform/api'
import { AcceptTermsField } from '../src/platform/AcceptTermsField'
import { AxisAlert } from '../src/platform/AxisAlert'
import { AxisButton } from '../src/platform/AxisButton'
import { AxisField, type AxisFieldStatus } from '../src/platform/AxisField'
import { FilledText } from '../src/platform/FilledText'
import { placeProblemErrors } from '../src/platform/problemErrors'
import { requiredMark } from '../src/platform/requiredMark'
import { StartFromField } from '../src/platform/StartFromField'
import { useText } from '../src/platform/texts'
import { useDesignTokens } from '../src/platform/themeMode'
import type { SignUpDocuments } from './documents'
import { SignUpFrame } from './SignUpFrame'
import { suggestSubdomain } from './suggestSubdomain'
import { signUpLocales, type SignUpLocale } from './texts'
import { useProofOfWork } from './useProofOfWork'

interface SignUpApplication {
  id: string
  titles: Record<SignUpLocale, string>
  descriptions: Record<SignUpLocale, string>
}

type SubdomainStatus =
  | { state: 'idle' }
  | { state: 'checking' }
  | { state: 'free' }
  | { state: 'refused'; reason: 'invalid' | 'reserved' | 'taken' | 'held' }
  | { state: 'unavailable' }

/** The answer of the check for one name. */
interface SubdomainAnswer {
  name: string
  status: SubdomainStatus
}

/** The fields of the form in the order the page shows them, which is the order a click checks them in. */
const formFields = ['fullName', 'email', 'businessName', 'subdomain', 'language', 'application'] as const
type FormField = (typeof formFields)[number]

interface SignUpValues {
  fullName: string
  email: string
  businessName: string
  subdomain: string
  application?: string
  language: SignUpLocale
}

const checkDelay = 300

function fieldOf(segments: string[]): FormField | null {
  return formFields.find((field) => segments.length === 1 && segments[0] === field) ?? null
}

function subdomainStatusOf(status: SubdomainStatus, t: (key: string) => string): AxisFieldStatus {
  switch (status.state) {
    case 'idle':
      return { state: 'idle' }
    case 'checking':
      return { state: 'running' }
    case 'free':
      return { state: 'passed', message: t('signUp.subdomain.free') }
    case 'refused':
      return { state: 'refused', message: t(`signUp.subdomain.${status.reason}`) }
    case 'unavailable':
      return { state: 'failed', message: t('signUp.subdomain.unavailable') }
  }
}

/**
 * The sign-up page of the control plane: who the person is, the business, its subdomain, the application to start from,
 * the language and the terms box, sent by the Sign up button. Typing a business name fills in a subdomain until the person
 * edits it, and the page checks the subdomain as they type. Once the sign-up is accepted, the card asks the person to
 * check their inbox.
 */
export function SignUpPage({
  locale,
  onLocaleChange,
  documents,
}: {
  locale: SignUpLocale
  onLocaleChange: (locale: SignUpLocale) => void
  documents: SignUpDocuments | undefined
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [form] = Form.useForm()
  const subdomain = Form.useWatch<string | undefined>('subdomain', form)
  const subdomainEdited = useRef(false)
  const languageEdited = useRef(false)
  const [applications, setApplications] = useState<SignUpApplication[] | 'failed'>()
  const [tenantDomain, setTenantDomain] = useState<string>()
  const [answer, setAnswer] = useState<SubdomainAnswer | null>(null)
  const [sending, setSending] = useState(false)
  const [formErrors, setFormErrors] = useState<string[]>([])
  const [sent, setSent] = useState<{ email: string } | null>(null)
  const formErrorRef = useRef<HTMLDivElement>(null)
  const titleRef = useRef<HTMLElement>(null)
  const focusEmail = useRef(false)
  const proofOfWork = useProofOfWork()

  useEffect(() => {
    const controller = new AbortController()
    apiFetch('/api/sign-up/applications', { signal: controller.signal }, { promptSignInOnUnauthorized: false })
      .then(async (response) => {
        setApplications(
          response.ok ? ((await response.json()) as { applications: SignUpApplication[] }).applications : 'failed',
        )
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setApplications('failed')
        }
      })
    return () => controller.abort()
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    apiFetch('/api/sign-up/region', { signal: controller.signal }, { promptSignInOnUnauthorized: false })
      .then(async (response) => {
        if (response.ok) {
          setTenantDomain(((await response.json()) as { tenantDomain: string }).tenantDomain)
        }
      })
      .catch(() => {
        // The subdomain shows alone when the region does not answer.
      })
    return () => controller.abort()
  }, [])

  useEffect(() => {
    if (!languageEdited.current) {
      form.setFieldValue('language', locale)
    }
  }, [form, locale])

  useEffect(() => {
    if (!subdomain) {
      return
    }
    const controller = new AbortController()
    const timer = setTimeout(() => {
      apiFetch(
        `/api/sign-up/subdomains/${encodeURIComponent(subdomain)}`,
        { signal: controller.signal },
        { promptSignInOnUnauthorized: false },
      )
        .then(async (response) => {
          if (!response.ok) {
            setAnswer({ name: subdomain, status: { state: 'unavailable' } })
            return
          }
          const body = (await response.json()) as { free: boolean; reason: string | null }
          setAnswer({
            name: subdomain,
            status: body.free
              ? { state: 'free' }
              : { state: 'refused', reason: body.reason as 'invalid' | 'reserved' | 'taken' | 'held' },
          })
        })
        .catch(() => {
          if (!controller.signal.aborted) {
            setAnswer({ name: subdomain, status: { state: 'unavailable' } })
          }
        })
    }, checkDelay)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [subdomain])

  useEffect(() => {
    if (formErrors.length === 0) {
      return
    }
    formErrorRef.current?.scrollIntoView?.({ block: 'center' })
    formErrorRef.current?.focus({ preventScroll: true })
  }, [formErrors])

  useEffect(() => {
    if (sent) {
      titleRef.current?.focus()
    }
  }, [sent])

  useEffect(() => {
    if (sent === null && focusEmail.current) {
      focusEmail.current = false
      form.scrollToField('email', { focus: true })
    }
  }, [form, sent])

  // Until the check for the name in the field answers, the page shows that it is checking.
  const status: SubdomainStatus = !subdomain
    ? { state: 'idle' }
    : answer?.name === subdomain
      ? answer.status
      : { state: 'checking' }
  const subdomainStatus = subdomainStatusOf(status, t)
  const choices = Array.isArray(applications)
    ? applications.map((application) => ({
        id: application.id,
        title: application.titles[locale],
        description: application.descriptions[locale],
      }))
    : applications

  const showRefusal = (errors: Record<string, string[]>) => {
    const placed = placeProblemErrors<FormField>(errors, fieldOf)
    form.setFields(
      placed.fields.map(({ field, messages }) => ({ name: field, errors: messages.map((message) => t(message)) })),
    )
    const first = formFields.find((field) => placed.fields.some((refused) => refused.field === field))
    if (first) {
      form.scrollToField(first, { focus: true })
    }
    const shown = placed.form.map((message) => t(message))
    if (shown.length === 0 && first === undefined) {
      shown.push(t('signUp.failed'))
    }
    setFormErrors(shown)
  }

  // The message of a 429: the wait in minutes from Retry-After for the address limit, or tomorrow for the domain limit.
  // See docs/reference/sign-up.md#sending-the-form. An answer without a usable wait or a known limit gets the failed text.
  const limitMessage = async (response: Response) => {
    const limit = ((await response.json().catch(() => ({}))) as { limit?: string }).limit
    if (limit === 'domain') {
      return t('signUp.limit.domain')
    }
    const header = response.headers.get('Retry-After')
    const seconds = header !== null && /^\d+$/.test(header) ? Number(header) : 0
    if (limit !== 'address' || seconds <= 0) {
      return t('signUp.failed')
    }
    const minutes = Math.ceil(seconds / 60)
    return minutes === 1
      ? t('signUp.limit.address.one')
      : t('signUp.limit.address').replace('{minutes}', new Intl.NumberFormat(locale).format(minutes))
  }

  const send = async (values: SignUpValues) => {
    if (status.state === 'checking' || status.state === 'refused') {
      form.scrollToField('subdomain', { focus: true })
      return
    }
    setSending(true)
    setFormErrors([])
    try {
      const altcha = await proofOfWork.solution()
      const response = await apiFetch(
        '/api/sign-up',
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            fullName: values.fullName.trim(),
            email: values.email.trim(),
            businessName: values.businessName.trim(),
            subdomain: values.subdomain,
            language: values.language,
            application: values.application ?? '',
            ...(altcha ? { altcha } : {}),
          }),
        },
        { promptSignInOnUnauthorized: false },
      )
      if (response.status === 202) {
        setSent({ email: values.email.trim() })
      } else {
        // A solved challenge is for one try, so the next try solves a new one.
        proofOfWork.renew()
        if (response.status === 400) {
          showRefusal(((await response.json()) as { errors?: Record<string, string[]> }).errors ?? {})
        } else if (response.status === 429) {
          setFormErrors([await limitMessage(response)])
        } else {
          setFormErrors([t('signUp.failed')])
        }
      }
    } catch {
      proofOfWork.renew()
      setFormErrors([t('signUp.failed')])
    } finally {
      setSending(false)
    }
  }

  const changeEmail = () => {
    focusEmail.current = true
    setSent(null)
  }

  return (
    <SignUpFrame
      locale={locale}
      onLocaleChange={onLocaleChange}
      title={sent ? t('signUp.inbox.title') : t('signUp.title')}
      titleRef={titleRef}
      documents={documents}
    >
      {formErrors.length > 0 && (
        <div ref={formErrorRef} tabIndex={-1}>
          <AxisAlert kind="error" message={formErrors.join(' ')} />
        </div>
      )}
      {sent && (
        <Flex vertical gap={spacing.md} align="flex-start">
          <Typography.Paragraph style={{ margin: 0 }}>
            {t('signUp.inbox.sent').replace('{email}', sent.email)}
          </Typography.Paragraph>
          <Typography.Paragraph style={{ margin: 0 }}>{t('signUp.inbox.notThere')}</Typography.Paragraph>
          <AxisButton role="plain" onClick={changeEmail}>
            {t('signUp.inbox.changeEmail')}
          </AxisButton>
        </Flex>
      )}
      <div hidden={sent !== null}>
        <Form
          form={form}
          name="sign-up"
          layout="vertical"
          requiredMark={requiredMark}
          noValidate
          initialValues={{ language: locale }}
          scrollToFirstError={{ focus: true }}
          onFinish={send}
        >
          <AxisField
            label={t('signUp.fullName')}
            help={t('signUp.fullName.help')}
            name="fullName"
            requiredMessage={t('signUp.fullName.required')}
          >
            <Input autoComplete="name" />
          </AxisField>
          <AxisField
            label={t('signUp.email')}
            help={t('signUp.email.help')}
            name="email"
            requiredMessage={t('signUp.email.required')}
            rules={[{ type: 'email', message: t('signUp.email.invalid') }]}
          >
            <Input type="email" autoComplete="email" />
          </AxisField>
          <AxisField
            label={t('signUp.businessName')}
            help={t('signUp.businessName.help')}
            name="businessName"
            requiredMessage={t('signUp.businessName.required')}
          >
            <Input
              autoComplete="organization"
              onChange={(event) => {
                if (!subdomainEdited.current) {
                  form.setFieldValue('subdomain', suggestSubdomain(event.target.value))
                }
              }}
            />
          </AxisField>
          <AxisField
            label={t('signUp.subdomain')}
            help={t('signUp.subdomain.help')}
            name="subdomain"
            requiredMessage={t('signUp.subdomain.required')}
            status={subdomainStatus}
          >
            <Input
              autoComplete="off"
              spellCheck={false}
              suffix={
                tenantDomain === undefined ? undefined : (
                  <span style={{ color: colour.textMuted }}>.{tenantDomain}</span>
                )
              }
              onChange={() => {
                subdomainEdited.current = true
              }}
            />
          </AxisField>
          <AxisField
            label={t('signUp.language')}
            help={t('signUp.language.help')}
            name="language"
            requiredMessage={t('signUp.language.required')}
          >
            <Select
              options={signUpLocales.map((tag) => ({ value: tag, label: t(`shell.locale.${tag}`) }))}
              onChange={() => {
                languageEdited.current = true
              }}
            />
          </AxisField>
          <StartFromField
            name="application"
            label={t('signUp.startFrom')}
            help={t('signUp.startFrom.help')}
            blank={t('signUp.startFrom.blank')}
            choices={choices}
            failedMessage={t('signUp.startFrom.failed')}
          />
          <AcceptTermsField
            name="terms"
            text={t('signUp.terms')}
            documents={{
              terms: { label: t('signUp.terms.termsOfService'), href: documents?.terms.urls[locale] },
              dpa: { label: t('signUp.terms.dpa'), href: documents?.dpa.urls[locale] },
            }}
            requiredMessage={t('signUp.terms.required')}
          />
          <Typography.Paragraph style={{ margin: 0, color: colour.textMuted }}>
            <FilledText
              text={t('signUp.privacyNotice')}
              parts={{
                privacy: documents ? (
                  <Typography.Link
                    href={documents.privacy.urls[locale]}
                    target="_blank"
                    rel="noopener noreferrer"
                    style={{ color: colour.primaryText }}
                  >
                    {t('signUp.privacyNotice.privacyPolicy')}
                  </Typography.Link>
                ) : (
                  t('signUp.privacyNotice.privacyPolicy')
                ),
              }}
            />
          </Typography.Paragraph>
          <AxisButton role="finish" htmlType="submit" block loading={sending} style={{ marginTop: spacing.lg }}>
            {t('signUp.submit')}
          </AxisButton>
        </Form>
      </div>
    </SignUpFrame>
  )
}
