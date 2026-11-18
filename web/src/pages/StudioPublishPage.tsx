import { Flex, Input } from 'antd'
import { useState } from 'react'
import { ActionBar } from '../platform/ActionBar'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { fillText } from '../platform/fillText'
import { nextReleaseNumber, type PublishDetails, type StudioReleases } from '../platform/studio'
import { useText } from '../platform/texts'
import { StudioPageHeading } from './StudioPageHeading'

/**
 * The publish page, `&view=publish`, see docs/reference/studio.md#publishing. Its heading has one line: the release
 * the publish replaces, the first release, or that everything is live, found in `history`, which is `undefined` while
 * it loads and `'failed'` when it did not. Without the history it shows no line and no release number, and Publish
 * stays on, as the old dialog did. "The release" holds the Label and the Note, and the action bar at the foot says which
 * number the publish gets, unless everything is live, and holds Cancel and Publish. Publish is off while `busy`, while the history loads and when
 * the folder already is the live release. Publish calls `onPublish` with the trimmed values, then `onLeave`, which
 * Cancel calls alone.
 */
export function StudioPublishPage({
  history,
  busy,
  onPublish,
  onLeave,
}: {
  history: StudioReleases | 'failed' | undefined
  busy: boolean
  onPublish: (details: PublishDetails) => void
  onLeave: () => void
}) {
  const t = useText()
  const [label, setLabel] = useState('')
  const [note, setNote] = useState('')
  const loaded = history !== undefined && history !== 'failed' ? history : null
  const allLive = loaded !== null && loaded.active !== null && loaded.folderContentHash === loaded.active.contentHash

  let line = ''
  if (loaded !== null) {
    if (allLive) {
      line = t('shell.studio.publish.allLive')
    } else if (loaded.active === null) {
      line = t('shell.studio.publish.first')
    } else {
      line = fillText(t('shell.studio.publish.replaces'), { release: loaded.active.number })
    }
  }

  return (
    <Flex vertical gap="middle">
      <StudioPageHeading title={t('shell.studio.publish.title')} line={line} />
      <AxisFieldGroup title={t('shell.studio.publish.release')} help="">
        <Flex vertical gap="middle">
          <AxisField label={t('shell.studio.publish.label')} help={t('shell.studio.publish.labelHelp')}>
            <Input maxLength={100} value={label} onChange={(event) => setLabel(event.target.value)} />
          </AxisField>
          <AxisField label={t('shell.studio.publish.note')} help={t('shell.studio.publish.noteHelp')}>
            <Input.TextArea
              maxLength={2000}
              autoSize={{ minRows: 3, maxRows: 8 }}
              value={note}
              onChange={(event) => setNote(event.target.value)}
            />
          </AxisField>
        </Flex>
      </AxisFieldGroup>
      <ActionBar
        start={
          loaded === null || allLive ? undefined : (
            <span>{fillText(t('shell.studio.publish.goesLive'), { number: nextReleaseNumber(loaded) })}</span>
          )
        }
        end={
          <>
            <AxisButton role="secondary" onClick={onLeave}>
              {t('shell.studio.cancel')}
            </AxisButton>
            <AxisButton
              role="finish"
              data-testid="studio-publish-confirm"
              disabled={busy || history === undefined || allLive}
              onClick={() => {
                onPublish({ label: label.trim(), note: note.trim() })
                onLeave()
              }}
            >
              {t('shell.studio.publish.confirm')}
            </AxisButton>
          </>
        }
      />
    </Flex>
  )
}
