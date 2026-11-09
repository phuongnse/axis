import { Alert, ConfigProvider, Flex, Form, Input, Select, Typography } from 'antd'
import { useEffect, useRef, useState } from 'react'
import { AcceptTermsField, TextWithLinks } from '../src/platform/AcceptTermsField'
import { antLocaleFor } from '../src/platform/antLocale'
import { apiFetch } from '../src/platform/api'
import { AxisButton } from '../src/platform/AxisButton'
import { AxisField, type FieldStatus } from '../src/platform/AxisField'
import { CardPage } from '../src/platform/CardPage'
import { PreferencesMenu } from '../src/platform/PreferencesMenu'
import { requiredMark } from '../src/platform/requiredMark'
import { StartFromField } from '../src/platform/StartFromField'
import { useText } from '../src/platform/texts'
import { useDesignTokens } from '../src/platform/themeMode'
import { suggestSubdomain } from './suggestSubdomain'
import { signUpLocales, type SignUpLocale } from './texts'

/** Proposed: a built-in application the region offers to start from, with its title and description per language. */
interface SignUpApplication {
  id: string
  titles: Record<SignUpLocale, string>
  descriptions: Record<SignUpLocale, string>
}

/** Proposed: a document the person accepts or is told of, at its current version, with its address in each language. */
interface SignUpDocument {
  version: string
  urls: Record<SignUpLocale, string>
}

/** Proposed: the answer of `GET /api/sign-up/documents`, the region's current legal documents. */
interface SignUpDocuments {
  terms: SignUpDocument
  dpa: SignUpDocument
  privacy: SignUpDocument
  licencesUrl: string
}

/** The fields of the form, as `POST /api/sign-up` takes them with the versions of the documents shown. */
interface SignUpValues {
  fullName: string
  email: string
  businessName: string
  subdomain: string
  language: SignUpLocale
  application: string
  terms: boolean
}

/** Proposed: the problem of a refused sign-up: text keys by the JSON Pointer of the field they are about. */
interface SignUpProblem {
  errors?: Record<string, string[]>
  /** On a `429`: whether the client address or the email's domain reached its limit. */
  limit?: 'address' | 'domain'
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

const checkDelay = 300

/**
 * The sign-up page of the control plane: who the person is, the business, its subdomain, the application to start from,
 * the language and the terms box. It has no button to send the form yet. Typing a business name fills in a subdomain
 * until the person edits it, and the page checks the subdomain as they type.
 */
export function SignUpPage({
  locale,
  onLocaleChange,
}: {
  locale: SignUpLocale
  onLocaleChange: (locale: SignUpLocale) => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const [form] = Form.useForm()
  const subdomain = Form.useWatch<string | undefined>('subdomain', form)
  const subdomainEdited = useRef(false)
  const languageEdited = useRef(false)
  const [applications, setApplications] = useState<SignUpApplication[] | 'failed'>()
  const [tenantDomain, setTenantDomain] = useState<string>()
  const [documents, setDocuments] = useState<SignUpDocuments | null>(null)
  // Counts the loads of the documents, so a sign-up refused for a changed version loads them again.
  const [documentsLoad, setDocumentsLoad] = useState(0)
  const [sending, setSending] = useState(false)
  const [formError, setFormError] = useState<string>()
  const formErrorRef = useRef<HTMLDivElement>(null)
  const [sent, setSent] = useState<{ email: string; resend: string }>()
  const [resendState, setResendState] = useState<'idle' | 'sending' | 'sent' | 'failed'>('idle')
  const inboxRef = useRef<HTMLDivElement>(null)
  // Whether the person came back from the inbox step to change the email, so the email field takes the focus.
  const changingEmail = useRef(false)
  const [answer, setAnswer] = useState<SubdomainAnswer | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    apiFetch('/api/sign-up/applications', { signal: controller.signal }, { promptSignInOnUnauthorized: false })
      .then(async (response) => {
        if (!response.ok) {
          setApplications('failed')
          return
        }
        setApplications(((await response.json()) as { applications: SignUpApplication[] }).applications)
      })
      .catch(() => {
        // Blank is still offered, and a warning says so.
        if (!controller.signal.aborted) {
          setApplications('failed')
        }
      })
    return () => controller.abort()
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    apiFetch('/api/sign-up/documents', { signal: controller.signal }, { promptSignInOnUnauthorized: false })
      .then(async (response) => {
        if (response.ok) {
          setDocuments((await response.json()) as SignUpDocuments)
        }
      })
      .catch(() => {
        // Without the addresses the box names the documents as plain text, and sending is refused.
      })
    return () => controller.abort()
  }, [documentsLoad])

  useEffect(() => {
    const controller = new AbortController()
    apiFetch('/api/sign-up/region', { signal: controller.signal }, { promptSignInOnUnauthorized: false })
      .then(async (response) => {
        if (response.ok) {
          setTenantDomain(((await response.json()) as { tenantDomain: string }).tenantDomain)
        }
      })
      .catch(() => {
        // Without the domain the input shows the subdomain alone, and its help still says it starts the address.
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

  // Until the check for the name in the field answers, the page shows that it is checking.
  const status: SubdomainStatus = !subdomain
    ? { state: 'idle' }
    : answer?.name === subdomain
      ? answer.status
      : { state: 'checking' }
  const subdomainStatus: FieldStatus | undefined = {
    idle: undefined,
    checking: { state: 'running' as const, message: t('signUp.subdomain.checking') },
    free: { state: 'passed' as const, message: t('signUp.subdomain.free') },
    refused: {
      state: 'refused' as const,
      message: status.state === 'refused' ? t(`signUp.subdomain.${status.reason}`) : '',
    },
    unavailable: { state: 'failed' as const, message: t('signUp.subdomain.unavailable') },
  }[status.state]

  useEffect(() => {
    if (sent) {
      inboxRef.current?.focus()
    } else if (changingEmail.current) {
      changingEmail.current = false
      form.scrollToField('email', { focus: true })
    }
  }, [form, sent])

  useEffect(() => {
    if (formError !== undefined) {
      formErrorRef.current?.scrollIntoView({ block: 'center' })
      formErrorRef.current?.focus({ preventScroll: true })
    }
  }, [formError])

  // Proposed: Sign up checks every field on click and sends nothing while one is wrong. A refusal shows under the field
  // its JSON Pointer names, which it scrolls to, and any other one above the form, which it scrolls to too.
  const send = async (values: SignUpValues) => {
    if (status.state === 'refused' || status.state === 'checking') {
      form.scrollToField('subdomain', { focus: true })
      return
    }
    setFormError(undefined)
    setSending(true)
    try {
      const response = await apiFetch(
        '/api/sign-up',
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            fullName: values.fullName,
            email: values.email,
            businessName: values.businessName,
            subdomain: values.subdomain,
            language: values.language,
            application: values.application,
            termsVersion: documents?.terms.version,
            dpaVersion: documents?.dpa.version,
            privacyVersion: documents?.privacy.version,
          }),
        },
        { promptSignInOnUnauthorized: false },
      )
      if (response.ok) {
        // The answer is the same for a new email and for one that already had a trial, so the page tells neither.
        const { resend } = (await response.json()) as { resend: string }
        setSent({ email: values.email, resend })
        setResendState('idle')
        return
      }
      const problem = ((await response.json().catch(() => ({}))) ?? {}) as SignUpProblem
      if (response.status === 429) {
        const minutes = Math.max(1, Math.ceil(Number(response.headers.get('Retry-After') ?? '60') / 60))
        setFormError(
          problem.limit === 'domain'
            ? t('signUp.tooMany.domain')
            : t('signUp.tooMany.address').replace('{minutes}', String(minutes)),
        )
        return
      }
      const onForm: string[] = []
      const onFields: { name: string; errors: string[] }[] = []
      for (const [pointer, messages] of Object.entries(problem.errors ?? {})) {
        const name = pointer.replace(/^\//, '')
        const texts = messages.map((message) => t(message))
        if (name === 'termsVersion' || name === 'dpaVersion' || name === 'privacyVersion') {
          // A document changed while the person filled in the form: they read it again and accept it again.
          setDocumentsLoad((load) => load + 1)
          form.setFieldValue('terms', false)
          onFields.push({ name: 'terms', errors: texts })
        } else if (['fullName', 'email', 'businessName', 'subdomain', 'language', 'application'].includes(name)) {
          onFields.push({ name, errors: texts })
        } else {
          onForm.push(...texts)
        }
      }
      if (onFields.length === 0 && onForm.length === 0) {
        onForm.push(t('signUp.sendFailed'))
      }
      form.setFields(onFields)
      setFormError(onForm.length === 0 ? undefined : onForm.join(' '))
      if (onFields.length > 0) {
        form.scrollToField(onFields[0].name, { focus: true })
      }
    } catch {
      setFormError(t('signUp.sendFailed'))
    } finally {
      setSending(false)
    }
  }

  // Proposed: Send again asks for the same email once more, which counts toward an address's three a day.
  const resend = async () => {
    if (!sent) {
      return
    }
    setResendState('sending')
    try {
      const response = await apiFetch(
        '/api/sign-up/resend',
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ resend: sent.resend }),
        },
        { promptSignInOnUnauthorized: false },
      )
      setResendState(response.ok ? 'sent' : 'failed')
    } catch {
      setResendState('failed')
    }
  }

  return (
    <ConfigProvider locale={antLocaleFor(locale)}>
      <CardPage
        title={sent ? t('signUp.inbox.title') : t('signUp.title')}
        legal={{
          privacyUrl: documents?.privacy.urls[locale],
          termsUrl: documents?.terms.urls[locale],
          licencesUrl: documents?.licencesUrl,
          texts: {
            legal: t('shell.legal.label'),
            privacy: t('shell.legal.privacy'),
            terms: t('shell.legal.terms'),
            licences: t('shell.legal.licences'),
          },
        }}
        preferences={
          <PreferencesMenu
            locales={signUpLocales}
            locale={locale}
            onLocaleChange={(next) => onLocaleChange(next as SignUpLocale)}
          />
        }
      >
        {sent ? (
          // Proposed: after Sign up the card says where the email went, and offers to send it again or to change the
          // email, which brings the form back as it was.
          <Flex vertical gap={spacing.md} ref={inboxRef} tabIndex={-1} style={{ outline: 'none' }}>
            <Typography.Paragraph style={{ margin: 0 }}>
              {t('signUp.inbox.sent')
                .split(/(\{email\})/)
                .map((part, index) => (part === '{email}' ? <strong key={index}>{sent.email}</strong> : part))}
            </Typography.Paragraph>
            <Typography.Paragraph style={{ margin: 0, color: colour.textMuted }}>
              {t('signUp.inbox.notThere')}
            </Typography.Paragraph>
            {/* Proposed: a notice after an action is an info Alert, as the identity provider's card pages give one. */}
            {resendState === 'sent' && <Alert type="info" showIcon title={t('signUp.inbox.resent')} />}
            {resendState === 'failed' && <Alert type="error" showIcon title={t('signUp.inbox.resendFailed')} />}
            <AxisButton id="sign-up-resend" role="secondary" block loading={resendState === 'sending'} onClick={resend}>
              {t('signUp.inbox.resend')}
            </AxisButton>
            <AxisButton
              role="plain"
              block
              onClick={() => {
                changingEmail.current = true
                setSent(undefined)
              }}
            >
              {t('signUp.inbox.changeEmail')}
            </AxisButton>
          </Flex>
        ) : (
          <>
            {/* Proposed: a refusal that names no field shows above the form, which scrolls to it and focuses it, so a
                person who pressed Sign up at the foot of the long form sees it. */}
            {formError !== undefined && (
              <div ref={formErrorRef} tabIndex={-1} style={{ outline: 'none' }}>
                <Alert type="error" showIcon title={formError} />
              </div>
            )}
            {/* Proposed: every field but Start from and the terms box is required, with its mark and its help. */}
            <Form
              form={form}
              name="sign-up"
              layout="vertical"
              requiredMark={requiredMark}
              initialValues={{ language: locale, application: '' }}
              scrollToFirstError={{ focus: true }}
              onFinish={send}
            >
              <AxisField
                name="fullName"
                label={t('signUp.fullName')}
                help={t('signUp.fullName.help')}
                requiredMessage={t('signUp.fullName.required')}
              >
                <Input autoComplete="name" />
              </AxisField>
              <AxisField
                name="email"
                label={t('signUp.email')}
                help={t('signUp.email.help')}
                requiredMessage={t('signUp.email.required')}
                rules={[{ type: 'email', message: t('signUp.email.invalid') }]}
              >
                <Input type="email" autoComplete="email" />
              </AxisField>
              <AxisField
                name="businessName"
                label={t('signUp.businessName')}
                help={t('signUp.businessName.help')}
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
                name="subdomain"
                label={t('signUp.subdomain')}
                help={t('signUp.subdomain.help')}
                requiredMessage={t('signUp.subdomain.required')}
                status={subdomainStatus}
              >
                {/* Proposed: the region's tenant domain ends the input, so the person reads their whole address. */}
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
                name="language"
                label={t('signUp.language')}
                help={t('signUp.language.help')}
                requiredMessage={t('signUp.language.required')}
              >
                <Select
                  options={signUpLocales.map((tag) => ({ value: tag, label: t(`shell.locale.${tag}`) }))}
                  onChange={() => {
                    languageEdited.current = true
                  }}
                />
              </AxisField>
              {/* Proposed: a person starts from Blank or a built-in application, as Studio's New application does. */}
              <StartFromField
                name="application"
                texts={{
                  label: t('signUp.startFrom'),
                  help: t('signUp.startFrom.help'),
                  blank: t('signUp.startFrom.blank'),
                  failed: t('signUp.startFrom.failed'),
                }}
                applications={
                  Array.isArray(applications)
                    ? applications.map((application) => ({
                        id: application.id,
                        title: application.titles[locale],
                        description: application.descriptions[locale],
                      }))
                    : applications
                }
              />
              {/* Proposed: the business accepts its two agreements; the privacy policy is told, not accepted. */}
              <AcceptTermsField
                name="terms"
                label={t('signUp.terms')}
                links={{
                  terms: { label: t('signUp.terms.termsOfService'), href: documents?.terms.urls[locale] },
                  dpa: { label: t('signUp.terms.dpa'), href: documents?.dpa.urls[locale] },
                }}
                requiredMessage={t('signUp.terms.required')}
              />
              <Typography.Paragraph style={{ margin: 0, color: colour.textMuted }}>
                <TextWithLinks
                  text={t('signUp.privacyNotice')}
                  links={{
                    privacy: { label: t('signUp.privacyNotice.privacyPolicy'), href: documents?.privacy.urls[locale] },
                  }}
                />
              </Typography.Paragraph>
              <AxisButton role="finish" htmlType="submit" block loading={sending} style={{ marginTop: spacing.lg }}>
                {t('signUp.send')}
              </AxisButton>
            </Form>
          </>
        )}
      </CardPage>
    </ConfigProvider>
  )
}
