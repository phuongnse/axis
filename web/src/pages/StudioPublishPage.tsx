import { Flex, Form, Input, Spin, Typography } from 'antd'
import { useEffect, useState, type ReactNode } from 'react'
import { Link, useNavigate } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import {
  fetchLiveDiff,
  ServerUnreachableError,
  type StudioChangeAuthor,
  type StudioLiveChange,
  type StudioResourceItem,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { authoringRequestAddress } from './authoringRequests'
import { StudioChanges } from './StudioHistory'
import { SectionPart, SectionParts } from './ResourceFormSection'
import { ActionBar } from './SaveBar'
import { StudioPageHeading } from './StudioPageHeading'

/**
 * Proposed: the publish page, which Publish… on the band opens in place of a dialog, as a whole diff needs a page, see
 * docs/reference/actions-and-controls.md#opening-things and the review on #3263. Under its heading it says what goes
 * live, then lists every file that differs from the live release as `StudioChanges` does, each folded, with who changed
 * it since: a person by name, "you" for the reader, or an AI request by its words as a link to its page. Then come the
 * optional label and note and Publish, which sends the publish and goes back to the page Publish… was opened from,
 * where the status line follows it. With nothing to publish it says so and Publish is off. Below the heading come the
 * changes, then the release's label and note, a rule apart. Cancel and Publish sit in the `ActionBar`
 * at the foot of the window, beside the number of the release that goes live.
 */
export function StudioPublishPage({
  application,
  resources,
  userId,
  liveNumber,
  busy,
  onPublish,
}: {
  application: string
  resources: StudioResourceItem[]
  userId: string | null
  /** The number of the live release, or `null` when none is live. */
  liveNumber: number | null
  busy: boolean
  onPublish: (label: string, note: string) => void
}) {
  const t = useText()
  const navigate = useNavigate()
  const { spacing } = useDesignTokens()
  const { generation, lost } = useStudioConnection()
  const [form] = Form.useForm<{ label: string; note: string }>()
  const [changes, setChanges] = useState<StudioLiveChange[] | null | 'failed'>()

  useEffect(() => {
    const controller = new AbortController()
    fetchLiveDiff(application, controller.signal).then(setChanges, (error: unknown) => {
      if (controller.signal.aborted) {
        return
      }
      if (error instanceof ServerUnreachableError) {
        lost()
        return
      }
      setChanges('failed')
    })
    return () => controller.abort()
  }, [application, generation, lost])

  const author = (by: StudioChangeAuthor): ReactNode =>
    by.kind === 'request' ? (
      <Link to={authoringRequestAddress(application, by.request)}>
        {t('shell.studio.publish.byRequest').replace('{text}', by.text)}
      </Link>
    ) : by.id === userId ? (
      t('shell.studio.publish.byYou')
    ) : (
      (by.name ?? t('shell.studio.ai.someone'))
    )
  const authors =
    Array.isArray(changes) &&
    new Map(
      changes.map((change) => [
        change.file,
        <>
          {t('shell.studio.publish.changedBy')}{' '}
          {change.by.map((by, index) => (
            <span key={index}>
              {index > 0 && ', '}
              {author(by)}
            </span>
          ))}
        </>,
      ]),
    )
  const nothing = Array.isArray(changes) && changes.length === 0

  return (
    <Flex vertical gap="middle" data-testid="studio-publish-page">
      <StudioPageHeading
        title={t('shell.studio.publish.title')}
        line={
          liveNumber === null
            ? t('shell.studio.publish.firstRelease')
            : t('shell.studio.publish.replaces').replace('{number}', String(liveNumber))
        }
      />
      <Form
        form={form}
        layout="vertical"
        initialValues={{ label: '', note: '' }}
        onFinish={(values) => {
          onPublish(values.label, values.note)
          navigate(-1)
          // The status line under the band follows the publish, so the page it goes back to starts at its top.
          window.scrollTo({ top: 0 })
        }}
      >
        <SectionParts>
          {changes !== null && (
            <SectionPart
              testName="publish-changes"
              title={
                !Array.isArray(changes)
                  ? t('shell.studio.overview.changes')
                  : changes.length === 1
                    ? t('shell.studio.publish.oneFile')
                    : t('shell.studio.publish.files').replace('{count}', String(changes.length))
              }
            >
              {changes === undefined ? (
                <Spin />
              ) : changes === 'failed' ? (
                <AxisAlert kind="error" message={t('shell.studio.publish.diffFailed')} />
              ) : nothing ? (
                <Typography.Text>{t('shell.studio.overview.changes.allLive')}</Typography.Text>
              ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: spacing.sm }}>
                  <StudioChanges changes={changes} resources={resources} authors={authors || undefined} folded />
                </div>
              )}
            </SectionPart>
          )}
          <SectionPart testName="publish-release" title={t('shell.studio.publish.release')}>
            <div>
              <AxisField
                name="label"
                label={t('shell.studio.publish.label')}
                help={t('shell.studio.publish.label.help')}
              >
                <Input maxLength={100} aria-label={t('shell.studio.publish.label')} />
              </AxisField>
              <AxisField name="note" label={t('shell.studio.publish.note')} help={t('shell.studio.publish.note.help')}>
                <Input.TextArea
                  maxLength={2000}
                  autoSize={{ minRows: 3, maxRows: 8 }}
                  aria-label={t('shell.studio.publish.note')}
                />
              </AxisField>
            </div>
          </SectionPart>
        </SectionParts>
        {/* What finishes the page's task, publishing, sits in the bar at the foot of the window. */}
        <ActionBar testId="studio-publish-bar">
          <span>
            {Array.isArray(changes) && changes.length > 0
              ? t('shell.studio.publish.goesLive').replace('{number}', String((liveNumber ?? 0) + 1))
              : ''}
          </span>
          <AxisActionRow align="center">
            <AxisButton role="secondary" onClick={() => navigate(-1)}>
              {t('shell.studio.cancel')}
            </AxisButton>
            <AxisButton role="finish" htmlType="submit" disabled={busy || nothing} data-testid="studio-publish-confirm">
              {t('shell.studio.publish.confirm')}
            </AxisButton>
          </AxisActionRow>
        </ActionBar>
      </Form>
    </Flex>
  )
}
