import { Flex, Input } from 'antd'
import { useEffect, useState } from 'react'
import { ActionBar } from '../platform/ActionBar'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { fillText } from '../platform/fillText'
import { PageLayout } from '../platform/PageLayout'
import { SectionPart, SectionParts } from '../platform/SectionParts'
import {
  changeAuthors,
  fetchAuthoringRequest,
  fetchStudioLiveDiff,
  nextReleaseNumber,
  type PublishDetails,
  type StudioLiveDiff,
  type StudioReleases,
  type StudioResourceItem,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { requestName } from './studioAuthoringRequest'
import { StudioChanges } from './StudioChanges'

/** The files that differ from the live release, with the names of the AI requests among the revisions that made them. */
interface PublishChanges {
  diff: StudioLiveDiff
  requestNames: ReadonlyMap<string, string>
}

/**
 * Loads the live diff, then the AI requests its revisions name, each read once. A diff that fails or has no live release
 * is null, which shows no list. A request that cannot be read has no name, so its change names the person who accepted it.
 */
async function loadPublishChanges(application: string, signal: AbortSignal): Promise<PublishChanges | null> {
  const diff = await fetchStudioLiveDiff(application, signal).catch(() => null)
  if (diff === null) {
    return null
  }
  const ids = [
    ...new Set(
      diff.resources.flatMap((file) =>
        file.by.flatMap((revision) => (revision.authoringRequest === null ? [] : [revision.authoringRequest])),
      ),
    ),
  ]
  const requests = await Promise.allSettled(ids.map((id) => fetchAuthoringRequest(application, id, signal)))
  const requestNames = new Map<string, string>()
  requests.forEach((request, index) => {
    if (request.status === 'fulfilled' && request.value !== null) {
      requestNames.set(ids[index], requestName(request.value.text))
    }
  })
  return { diff, requestNames }
}

/**
 * The publish page, `&view=publish`, see docs/reference/studio.md#publishing. Its heading has one line: the release
 * the publish replaces, the first release, or that everything is live, found in `history`, which is `undefined` while
 * it loads and `'failed'` when it did not. Without the history it shows no line and no release number, and Publish
 * stays on, as the old dialog did. "The release" holds the Label and the Note. Under it, unless everything is live or
 * nothing is live yet, the page lists the files that differ from the live release, each with "Changed by", as
 * docs/reference/ai-authoring-in-studio.md#revisions describes. The action bar at the foot says which
 * number the publish gets, unless everything is live, and holds Cancel and Publish. Publish is off while `busy`, while
 * the history or the changed files load and when the folder already is the live release. Publish calls `onPublish` with the trimmed values, then `onLeave`, which
 * Cancel calls alone.
 */
export function StudioPublishPage({
  history,
  busy,
  application,
  userId,
  resources,
  onPublish,
  onLeave,
}: {
  history: StudioReleases | 'failed' | undefined
  busy: boolean
  application: string
  userId: string | null
  resources: StudioResourceItem[]
  onPublish: (details: PublishDetails) => void
  onLeave: () => void
}) {
  const t = useText()
  const [label, setLabel] = useState('')
  const [note, setNote] = useState('')
  const [loadedChanges, setLoadedChanges] = useState<{ history: StudioReleases; changes: PublishChanges | null }>()
  const loaded = history !== undefined && history !== 'failed' ? history : null
  const allLive = loaded !== null && loaded.active !== null && loaded.folderContentHash === loaded.active.contentHash
  const live = loaded !== null && loaded.active !== null && !allLive
  // The list follows the history, so a save or a publish that reloads it loads the list again. The last list stays until then.
  useEffect(() => {
    if (!live) {
      return
    }
    const controller = new AbortController()
    loadPublishChanges(application, controller.signal).then((next) => {
      if (!controller.signal.aborted) {
        setLoadedChanges({ history: loaded, changes: next })
      }
    })
    return () => controller.abort()
  }, [application, live, loaded])
  const changes = live ? (loadedChanges?.changes ?? null) : null
  // The list appears above the action bar and pushes Publish down, so a click while it loads can miss the button.
  const changesLoading = live && loadedChanges?.history !== loaded

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
    <PageLayout title={t('shell.studio.publish.title')} line={line}>
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
      {changes !== null && changes.diff.resources.length > 0 && (
        <SectionParts>
          <SectionPart
            testName="publish-changes"
            heading={{
              title:
                changes.diff.resources.length === 1
                  ? t('shell.studio.publish.changes.one')
                  : fillText(t('shell.studio.publish.changes.many'), { count: changes.diff.resources.length }),
            }}
          >
            {/* Proposed: the files fold at first, as the page lists every file since the live release, see #4623. */}
            <StudioChanges
              files={changes.diff.resources}
              resources={resources}
              folded
              changedBy={{
                application,
                authors: (file) => changeAuthors(file.by, userId, changes.requestNames),
              }}
            />
          </SectionPart>
        </SectionParts>
      )}
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
              disabled={busy || history === undefined || allLive || changesLoading}
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
    </PageLayout>
  )
}
