import { Alert, Collapse, Flex, Input, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useNarrowWidth } from '../platform/useNarrowWidth'

/** One library that Axis ships, as scripts/licences.mjs writes it. */
interface ShippedPackage {
  name: string
  version: string
  licence: string
  url: string | null
  text: string | null
}

/** The data of the page, as scripts/licences.mjs writes it into /licences.json at build time. */
interface Licences {
  axis: { name: string; licence: string; url: string; licenceUrl: string }
  npm: ShippedPackage[]
  nuget: ShippedPackage[]
}

/** A licence written as one SPDX identifier, such as `MIT`, whose standard text spdx.org holds. */
const spdxIdentifier = /^[A-Za-z0-9.+-]+$/

/**
 * Proposed: the Open source licences page at /licences, open to everyone without signing in. It names Axis's licence
 * and its source, then every library the web app and the server ship with its version and licence. A library opens to
 * its licence text, or links the standard text on spdx.org when it carries none. The build writes the list, so the page
 * always matches what runs. A search narrows both lists by name or licence. The two lists sit side by side across the
 * page, and one above the other below the narrow width.
 */
export function LicencesPage() {
  const t = useText()
  const { colour, spacing, radius } = useDesignTokens()
  const [data, setData] = useState<Licences | 'failed'>()
  const [search, setSearch] = useState('')
  const narrow = useNarrowWidth()

  useEffect(() => {
    const controller = new AbortController()
    fetch('/licences.json', { signal: controller.signal })
      .then((response) => {
        if (!response.ok) {
          throw new Error(`Loading the licences failed with status ${response.status}.`)
        }
        return response.json() as Promise<Licences>
      })
      .then(setData, (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the licences failed', error)
          setData('failed')
        }
      })
    return () => controller.abort()
  }, [])

  const filter = useMemo(() => {
    const needle = search.trim().toLowerCase()
    return (items: ShippedPackage[]) =>
      needle === ''
        ? items
        : items.filter(
            (item) => item.name.toLowerCase().includes(needle) || item.licence.toLowerCase().includes(needle),
          )
  }, [search])

  function section(title: string, items: ShippedPackage[], testId: string) {
    const shown = filter(items)
    return (
      <section data-testid={testId} style={{ display: 'flex', flexDirection: 'column', gap: spacing.sm, minWidth: 0 }}>
        <Typography.Title level={4} style={{ margin: 0 }}>{`${title} (${shown.length})`}</Typography.Title>
        <Collapse
          size="small"
          items={shown.map((item) => ({
            key: `${item.name}@${item.version}`,
            label: (
              <Flex justify="space-between" gap={spacing.md} wrap>
                <Typography.Text>{`${item.name} ${item.version}`}</Typography.Text>
                <Typography.Text style={{ color: colour.textMuted }}>{item.licence}</Typography.Text>
              </Flex>
            ),
            children: (
              <Flex vertical gap={spacing.sm}>
                {item.url && (
                  <Typography.Link href={item.url} target="_blank" rel="noreferrer">
                    {item.url}
                  </Typography.Link>
                )}
                {item.text ? (
                  <pre
                    style={{
                      margin: 0,
                      padding: spacing.sm,
                      maxHeight: 320,
                      overflow: 'auto',
                      whiteSpace: 'pre-wrap',
                      background: colour.canvas,
                      borderRadius: radius.base,
                      fontSize: 12,
                    }}
                  >
                    {item.text}
                  </pre>
                ) : spdxIdentifier.test(item.licence) ? (
                  <Typography.Link
                    href={`https://spdx.org/licenses/${item.licence}.html`}
                    target="_blank"
                    rel="noreferrer"
                  >
                    {t('shell.licences.standardText').replace('{licence}', item.licence)}
                  </Typography.Link>
                ) : (
                  <Typography.Text type="secondary">{t('shell.licences.noText')}</Typography.Text>
                )}
              </Flex>
            ),
          }))}
        />
      </section>
    )
  }

  return (
    <PageLayout title={t('shell.licences.title')}>
      {data === 'failed' && <Alert type="error" title={t('shell.licences.loadFailed')} />}
      {data && data !== 'failed' && (
        <Flex vertical gap={spacing.lg}>
          <Typography.Paragraph style={{ margin: 0 }}>
            {t('shell.licences.axis').replace('{licence}', data.axis.licence)}{' '}
            <Typography.Link href={data.axis.licenceUrl} target="_blank" rel="noreferrer">
              {t('shell.licences.axisLicence')}
            </Typography.Link>
            {' · '}
            <Typography.Link href={data.axis.url} target="_blank" rel="noreferrer">
              {t('shell.legal.source')}
            </Typography.Link>
          </Typography.Paragraph>
          <Input.Search
            aria-label={t('shell.licences.search')}
            placeholder={t('shell.licences.search')}
            allowClear
            style={{ maxWidth: 360 }}
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
          {/* Proposed: the two lists side by side across the page, one column each below the narrow width. */}
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: narrow ? 'minmax(0, 1fr)' : 'repeat(2, minmax(0, 1fr))',
              gap: spacing.lg,
              alignItems: 'start',
            }}
          >
            {section(t('shell.licences.web'), data.npm, 'licences-npm')}
            {section(t('shell.licences.server'), data.nuget, 'licences-nuget')}
          </div>
        </Flex>
      )}
    </PageLayout>
  )
}
