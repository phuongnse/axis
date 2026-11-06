import { Card, Checkbox, ConfigProvider, Flex, Form, Input, Select, Typography } from 'antd'
import { useEffect, useRef, useState } from 'react'
import { antLocaleFor } from '../src/platform/antLocale'
import { apiFetch } from '../src/platform/api'
import { OrganizationHeading } from '../src/platform/OrganizationHeading'
import { PreferencesMenu } from '../src/platform/PreferencesMenu'
import { useText } from '../src/platform/texts'
import { useDesignTokens } from '../src/platform/themeMode'
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
  const { colour, spacing, radius } = useDesignTokens()
  const [form] = Form.useForm()
  const subdomain = Form.useWatch<string | undefined>('subdomain', form)
  const subdomainEdited = useRef(false)
  const languageEdited = useRef(false)
  const [applications, setApplications] = useState<SignUpApplication[]>([])
  const [answer, setAnswer] = useState<SubdomainAnswer | null>(null)
  const labelStyle = { fontWeight: 500, color: colour.textMuted }

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
  const subdomainField = {
    idle: {},
    checking: { validateStatus: 'validating' as const, help: t('signUp.subdomain.checking') },
    free: { validateStatus: 'success' as const, help: t('signUp.subdomain.free') },
    refused: {
      validateStatus: 'error' as const,
      help: status.state === 'refused' ? t(`signUp.subdomain.${status.reason}`) : undefined,
    },
    unavailable: { validateStatus: 'warning' as const, help: t('signUp.subdomain.unavailable') },
  }[status.state]

  return (
    <ConfigProvider locale={antLocaleFor(locale)}>
      <Flex
        vertical
        align="center"
        gap={spacing.md}
        style={{
          position: 'relative',
          paddingBlock: spacing.lg,
          background: colour.canvas,
          minHeight: '100vh',
          boxSizing: 'border-box',
        }}
      >
        <div style={{ position: 'absolute', top: spacing.lg, right: spacing.lg }}>
          <PreferencesMenu
            locales={signUpLocales}
            locale={locale}
            onLocaleChange={(next) => onLocaleChange(next as SignUpLocale)}
          />
        </div>
        <OrganizationHeading />
        <Card
          style={{
            width: '100%',
            maxWidth: 480,
            background: colour.surface,
            borderColor: colour.borderSubtle,
            borderRadius: radius.base,
          }}
          styles={{ body: { padding: spacing.xl } }}
        >
          <Flex vertical gap={spacing.md}>
            <Typography.Title level={4} style={{ margin: 0 }}>
              {t('signUp.title')}
            </Typography.Title>
            <Form
              form={form}
              name="sign-up"
              layout="vertical"
              requiredMark={false}
              initialValues={{ language: locale }}
            >
              <Form.Item name="fullName" label={<span style={labelStyle}>{t('signUp.fullName')}</span>}>
                <Input autoComplete="name" />
              </Form.Item>
              <Form.Item name="email" label={<span style={labelStyle}>{t('signUp.email')}</span>}>
                <Input type="email" autoComplete="email" />
              </Form.Item>
              <Form.Item name="businessName" label={<span style={labelStyle}>{t('signUp.businessName')}</span>}>
                <Input
                  autoComplete="organization"
                  onChange={(event) => {
                    if (!subdomainEdited.current) {
                      form.setFieldValue('subdomain', suggestSubdomain(event.target.value))
                    }
                  }}
                />
              </Form.Item>
              <Form.Item
                name="subdomain"
                label={<span style={labelStyle}>{t('signUp.subdomain')}</span>}
                {...subdomainField}
              >
                <Input
                  autoComplete="off"
                  spellCheck={false}
                  onChange={() => {
                    subdomainEdited.current = true
                  }}
                />
              </Form.Item>
              <Form.Item name="application" label={<span style={labelStyle}>{t('signUp.application')}</span>}>
                <Select
                  options={applications.map((application) => ({
                    value: application.id,
                    label: application.titles[locale],
                  }))}
                />
              </Form.Item>
              <Form.Item name="language" label={<span style={labelStyle}>{t('signUp.language')}</span>}>
                <Select
                  options={signUpLocales.map((tag) => ({ value: tag, label: t(`shell.locale.${tag}`) }))}
                  onChange={() => {
                    languageEdited.current = true
                  }}
                />
              </Form.Item>
              <Form.Item name="terms" valuePropName="checked" style={{ marginBottom: 0 }}>
                <Checkbox>{t('signUp.terms')}</Checkbox>
              </Form.Item>
            </Form>
          </Flex>
        </Card>
      </Flex>
    </ConfigProvider>
  )
}
