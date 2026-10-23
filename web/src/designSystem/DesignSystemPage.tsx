import { Layout, Typography } from 'antd'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { FormWidget } from '../platform/FormWidget'
import { PageLayout } from '../platform/PageLayout'
import { ProcessInspectorWidget } from '../platform/ProcessInspectorWidget'
import type { FieldType } from '../platform/site'
import { TableWidget } from '../platform/TableWidget'
import { TaskInboxWidget } from '../platform/TaskInboxWidget'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { useDesignTokens, type ThemeMode } from '../platform/themeMode'
import {
  fieldWidget,
  formWidget,
  locale,
  processInspectorWidget,
  requestId,
  siteTexts,
  sitePath,
  tableWidget,
  taskInboxWidget,
} from './fixtures'

const modes: readonly ThemeMode[] = ['light', 'dark']

const fieldTypes: readonly FieldType[] = [
  'text',
  'integer',
  'decimal',
  'boolean',
  'date',
  'date-time',
  'enum',
  'reference',
  'child-collection',
]

/** What a preview draws: the shared widget component over fixture metadata. */
const previews: Record<string, () => ReactNode> = {
  table: () => <TableWidget sitePath={sitePath} widget={tableWidget} locale={locale} pageValues={{}} />,
  form: () => (
    <FormWidget
      widget={formWidget}
      sitePath={sitePath}
      pages={[]}
      recordId={requestId}
      returnTo="/ds"
      locale={locale}
    />
  ),
  taskInbox: () => (
    <TaskInboxWidget
      sitePath={sitePath}
      pageName="Tasks"
      widget={taskInboxWidget}
      locale={locale}
      fallbackLocale={locale}
    />
  ),
  processInspector: () => (
    <ProcessInspectorWidget sitePath={sitePath} pageName="Instances" widget={processInspectorWidget} locale={locale} />
  ),
  ...Object.fromEntries(
    fieldTypes.map((type) => [
      `field-${type}`,
      () => (
        <FormWidget
          widget={fieldWidget(type)}
          sitePath={sitePath}
          pages={[]}
          recordId={requestId}
          returnTo="/ds"
          locale={locale}
        />
      ),
    ]),
  ),
}

const previewNames = Object.keys(previews)

const catalogs = [
  { texts: siteTexts, fallbackTexts: siteTexts },
  { texts: platformEnglish, fallbackTexts: platformEnglish },
]

function Page({ title, children }: { title: string; children: ReactNode }) {
  const { colour, spacing } = useDesignTokens()
  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Layout.Content style={{ padding: spacing.lg, background: colour.canvas }}>
        <PageLayout title={title}>{children}</PageLayout>
      </Layout.Content>
    </Layout>
  )
}

function Index() {
  return (
    <Page title="Axis design system">
      <ul>
        {previewNames.flatMap((name) =>
          modes.map((mode) => (
            <li key={`${name}-${mode}`}>
              <Typography.Link href={`?preview=${name}&mode=${mode}`}>
                {name} ({mode})
              </Typography.Link>
            </li>
          )),
        )}
      </ul>
    </Page>
  )
}

/**
 * The design-system page: the index of previews, or one preview in light or dark mode, as the query
 * string says (`?preview=table&mode=dark`). Development builds and the design-system build serve it.
 */
export function DesignSystemPage({ search }: { search: string }) {
  const params = new URLSearchParams(search)
  const name = params.get('preview')
  const mode: ThemeMode = params.get('mode') === 'dark' ? 'dark' : 'light'
  const preview = name === null ? undefined : Object.hasOwn(previews, name) ? previews[name] : undefined

  return (
    <ThemeModeProvider initialMode={mode}>
      <MemoryRouter initialEntries={['/ds/page']}>
        <TextProvider catalogs={catalogs} development>
          {name === null ? (
            <Index />
          ) : (
            <Page title={name}>
              {preview ? preview() : <Typography.Text>{`No preview named '${name}'.`}</Typography.Text>}
            </Page>
          )}
        </TextProvider>
      </MemoryRouter>
    </ThemeModeProvider>
  )
}
