import { Checkbox, ConfigProvider, Form, Input, Select } from 'antd'
import { useEffect, useRef, useState } from 'react'
import { antLocaleFor } from '../src/platform/antLocale'
import { apiFetch } from '../src/platform/api'
import { AxisField, type FieldCheck } from '../src/platform/AxisField'
import { CardPage } from '../src/platform/CardPage'
import { PreferencesMenu } from '../src/platform/PreferencesMenu'
import { requiredMark } from '../src/platform/requiredMark'
import { useText } from '../src/platform/texts'
import { suggestSubdomain } from './suggestSubdomain'
import { signUpLocales, type SignUpLocale } from './texts'

interface SignUpApplication {
  id: string
  titles: Record<SignUpLocale, string>
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
  const [form] = Form.useForm()
  const subdomain = Form.useWatch<string | undefined>('subdomain', form)
  const subdomainEdited = useRef(false)
  const languageEdited = useRef(false)
  const [applications, setApplications] = useState<SignUpApplication[]>([])
  const [answer, setAnswer] = useState<SubdomainAnswer | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    apiFetch('/api/sign-up/applications', { signal: controller.signal }, { promptSignInOnUnauthorized: false })
      .then(async (response) => {
        if (response.ok) {
          setApplications(((await response.json()) as { applications: SignUpApplication[] }).applications)
        }
      })
      .catch(() => {
        // The list stays empty. The person can still fill in the rest of the form.
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
  const subdomainCheck: FieldCheck | undefined = {
    idle: undefined,
    checking: { state: 'running' as const, message: t('signUp.subdomain.checking') },
    free: { state: 'passed' as const, message: t('signUp.subdomain.free') },
    refused: {
      state: 'refused' as const,
      message: status.state === 'refused' ? t(`signUp.subdomain.${status.reason}`) : '',
    },
    unavailable: { state: 'failed' as const, message: t('signUp.subdomain.unavailable') },
  }[status.state]

  return (
    <ConfigProvider locale={antLocaleFor(locale)}>
      <CardPage
        title={t('signUp.title')}
        legal={{
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
        {/* Proposed: every field but the terms box is required, marked and helped, as every platform form's are. */}
        <Form
          form={form}
          name="sign-up"
          layout="vertical"
          requiredMark={requiredMark}
          initialValues={{ language: locale }}
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
            check={subdomainCheck}
          >
            <Input
              autoComplete="off"
              spellCheck={false}
              onChange={() => {
                subdomainEdited.current = true
              }}
            />
          </AxisField>
          <AxisField
            name="application"
            label={t('signUp.application')}
            help={t('signUp.application.help')}
            requiredMessage={t('signUp.application.required')}
          >
            <Select
              options={applications.map((application) => ({
                value: application.id,
                label: application.titles[locale],
              }))}
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
          <Form.Item name="terms" valuePropName="checked" style={{ marginBottom: 0 }}>
            <Checkbox>{t('signUp.terms')}</Checkbox>
          </Form.Item>
        </Form>
      </CardPage>
    </ConfigProvider>
  )
}
