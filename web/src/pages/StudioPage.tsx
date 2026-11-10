import { Button, Descriptions, Flex, Result, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { AxisActionRow } from '../platform/AxisButton'
import { fetchCurrentUser } from '../platform/currentUser'
import { forbidden } from '../platform/inspection'
import type { ModuleKind } from '../platform/moduleKinds'
import { ShellSidebar } from '../platform/shellSidebar'
import { SiteMark } from '../platform/SiteMark'
import {
  fetchStudioApps,
  fetchStudioDiagnostics,
  fetchStudioNames,
  fetchStudioResource,
  fetchStudioResources,
  groupByKind,
  moduleResources,
  resourceTitle,
  ServerUnreachableError,
  type StudioApplication,
  studioAppDescription,
  studioAppTitle,
  type StudioDiagnostic,
  type StudioNames,
  type StudioResource,
  type StudioResourceItem,
  waitForServer,
} from '../platform/studio'
import { StudioConnectionContext, useStudioConnection } from '../platform/studioConnection'
import { kindName, kindSingular } from '../platform/studioKinds'
import { studioResourceAddress } from '../platform/studioAddress'
import { starterKinds } from '../platform/studioStarters'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { leaveWithoutAsking } from '../platform/unsavedChanges'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { useHomeGrid } from './homeGrid'
import { HomeTileStyle } from './HomeTileStyle'
import { StudioEditor, type StudioEditorHandle } from './StudioEditor'
import { StudioHistory, StudioRevisionPage } from './StudioHistory'
import { ModuleResourceView } from './StudioModuleView'
import { StudioNewApplication } from './StudioNewApplication'
import { StudioNewResource } from './StudioNewResource'
import { StudioOverview } from './StudioOverview'
import { StudioPageHeading } from './StudioPageHeading'
import { StudioReleasePage } from './StudioReleasePage'
import { StudioPolicyMatrix } from './StudioPolicyMatrix'
import { StudioAskAi } from './StudioAskAi'
import { StudioProblems } from './StudioProblems'
import { StudioResourceMenu } from './StudioResourceMenu'
import { StudioPublishing, type StudioPublishingHandle } from './StudioPublishing'
import { studioActiveEntry, studioNavigation } from './studioNavigation'
import { StudioSidebar } from './StudioSidebar'

/** The titles and test ids of the module resource groups in the list. */
const moduleGroupTitles: Record<ModuleKind, string> = {
  entity: 'shell.studio.moduleEntities',
  dataSource: 'shell.studio.moduleDataSources',
  rule: 'shell.studio.moduleRules',
  sequence: 'shell.studio.moduleSequences',
  form: 'shell.studio.moduleForms',
  page: 'shell.studio.modulePages',
  process: 'shell.studio.moduleProcesses',
  event: 'shell.studio.moduleEvents',
  text: 'shell.studio.moduleTexts',
  seed: 'shell.studio.moduleSeeds',
  policy: 'shell.studio.modulePolicies',
  trigger: 'shell.studio.moduleTriggers',
  scenario: 'shell.studio.moduleScenarios',
  contract: 'shell.studio.moduleContracts',
}
const moduleGroupTestIds: Record<ModuleKind, string> = {
  entity: 'studio-group-module-entities',
  dataSource: 'studio-group-module-data-sources',
  rule: 'studio-group-module-rules',
  sequence: 'studio-group-module-sequences',
  form: 'studio-group-module-forms',
  page: 'studio-group-module-pages',
  process: 'studio-group-module-processes',
  event: 'studio-group-module-events',
  text: 'studio-group-module-texts',
  seed: 'studio-group-module-seeds',
  policy: 'studio-group-module-policies',
  trigger: 'studio-group-module-triggers',
  scenario: 'studio-group-module-scenarios',
  contract: 'studio-group-module-contracts',
}

/** The tiles of the applications Studio may edit, in the UI locale with `en` as the fallback. Each opens its application. */
function StudioApplicationTiles({ apps }: { apps: StudioApplication[] }) {
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour, radius, spacing } = useDesignTokens()
  const grid = useHomeGrid()
  return (
    <div data-testid="studio-apps" style={grid}>
      {apps.map((app) => {
        const title = studioAppTitle(app, locale)
        const description = studioAppDescription(app, locale)
        return (
          <Link
            key={app.name}
            to={`/studio?app=${encodeURIComponent(app.name)}`}
            data-home-tile
            data-testid={`studio-app-${app.name}`}
            style={{
              display: 'flex',
              alignItems: description === null ? 'center' : 'flex-start',
              gap: spacing.sm,
              minWidth: 0,
              padding: spacing.md,
              background: colour.surface,
              border: `1px solid ${colour.borderSubtle}`,
              borderRadius: radius.base,
            }}
          >
            <SiteMark title={title} size={32} />
            <Flex vertical style={{ minWidth: 0 }}>
              <Typography.Text strong style={{ color: colour.primaryText }}>
                {title}
              </Typography.Text>
              {description !== null && (
                <Typography.Paragraph
                  ellipsis={{ rows: 2, tooltip: description }}
                  style={{ margin: 0, color: colour.textMuted }}
                >
                  {description}
                </Typography.Paragraph>
              )}
            </Flex>
          </Link>
        )
      })}
    </div>
  )
}

/** The applications Studio may edit, or why they could not be listed. */
type AppsState = StudioApplication[] | typeof forbidden | 'failed'

/** What one application's folder holds, or `failed`. `application` says which one it was loaded for. */
type FolderState =
  | { application: string; resources: StudioResourceItem[]; diagnostics: StudioDiagnostic[] }
  | { application: string; failed: true }

/** The names of one application, or null when they did not load. `application` says which one they were loaded for. */
type NamesState = { application: string; names: StudioNames | null }

/** The `application.json` of one application, or null when it did not load. `application` says which one it was loaded for. */
type ManifestState = { application: string; resource: StudioResource | null }

/**
 * Studio, the authoring page. The address holds the picked application and the selected resource, as
 * `/studio?app=PurchaseRequests&resource=<id>`. A person without the author role sees a forbidden page.
 * With an application open, the shell's sidebar holds an application row, which lists the applications, and the
 * application's entries under it.
 * With no application open, it lists the applications as tiles in up to three columns, each with its mark, its title
 * and its description in the UI locale (`en` as the fallback, then the name), under the page heading "Studio", with a
 * solid New application… button on its right. A tile opens its application, and a single application is not opened by itself.
 * Every page of an open application heads its content with one level-4 `StudioPageHeading`, and no page has a
 * breadcrumb. The Overview's heading is the application's title, with its description under it, then the blocks of
 * `StudioOverview`. A kind's sidebar entry, as `&kind=<kind>`, shows that kind's list under a heading with its name, then the module group of that kind when modules hold one.
 * A resource opens alone in the content, under a heading with its name and, for a scenario, Run scenario on its right:
 * its details, its editor, and its Uses and Used by lists. The band under the header holds the application's state and
 * its actions. Its errors and warnings are one button that opens the Problems panel beside the page, which lists
 * the folder's diagnostics, loaded when the application is picked. A diagnostic that names a resource selects it,
 * and opens it at the diagnostic's path, as a Used by entry does, and the panel stays open. A selected resource opens in an editor below its details, with its Uses and Used by
 * lists under the editor. A Used by entry opens its resource at the path of the reference, as `&path=`. After a
 * save, the Problems panel shows the diagnostics that the save answered. After a delete, the resource leaves the
 * folder, and Studio shows the Overview. A New resource… button in the band creates a resource of any kind from its starter, and a kind's list has its own button named for the kind, such as New entity…, on the right of its heading, whose dialog asks for the name alone. The new resource
 * joins its kind's list, the Problems panel shows the diagnostics of the create, and the editor opens the resource.
 * A New application… item ends the application row's menu in the sidebar, and a button also sits on the list. Both show
 * only when `GET /api/me` answers `studioCreate: true`. A created application joins the menu and is picked at once.
 * Validate and Publish sit in the band, and Publish shows only when `GET /api/me` answers
 * `studioPublisher: true`. On the Releases view the release list shows the application's releases, with an Activate… button
 * for a publisher. A save, a text write or a delete loads the release history again, because the folder may now differ from the
 * active release. The Problems panel shows the diagnostics of a request that ended. When the server does not
 * answer, a reconnect message shows at the top. Studio checks the server with a
 * back-off, then loads its data again, and an unsaved draft stays on screen. The names that the reference pickers
 * and expression completion offer load with the application too, and again after a save, a text write, a delete and a
 * reconnect. A text write, which Save sends for a label's texts before the patch, updates the Problems panel and loads the names
 * and the release list again, as a save does. The sidebar's Policy matrix entry shows the policy matrix, kept in the address
 * as `&view=policies`. The matrix shows in place of the content under a Policy matrix heading, and
 * its Open policy link opens the policy in the Resources view. The band and the Problems panel show in every view.
 * `&view=releases` shows only the Releases heading and the release list. `&view=release&release=<number>` shows that
 * release's `StudioReleasePage` in place of the list, under the Releases entry, and its Activate… sends the same request as the list's.
 * Run tests sits beside Validate and shows only when `GET /api/me` answers `studioTest: true`. A selected scenario
 * has a Run button in its details, which runs only that scenario. Both are off while a request runs and while the
 * reconnect message shows. A failed scenario in the test results opens at its failing step, as a diagnostic does.
 * A process step that a diagnostic of the open resource names is marked on its graph.
 * A kind's list shows the module resources of that kind that the application uses, from the names, by their `<module>/<name>`. A module resource of every kind but a contract and a scenario opens in its editor with the application's layer applied
 * and every input on, with no JSON tab, Delete, migration questions, Uses or Used by lists: an entity in the entity
 * designer, a process in the process designer, and a data source, rule, trigger, sequence and event in the schema
 * form, and a seed in the seed grid. Save writes the layer, and a Customized tag with Reset all, which deletes
 * the layer, shows while it has one. A contract opens in its lifecycle view, and a scenario opens
 * read-only as JSON under a notice with the alert role. Its address is the same `&resource=<id>`, so a reload, a shared link and a Uses
 * entry open it too, when the id is not one of the folder's. The folder's `application.json` loads with the
 * application and again after a write. A module contract, which the application declares directly, has Relax and Undo relax, which write it.
 */
export function StudioPage() {
  const t = useText()
  const [reconnecting, setReconnecting] = useState(false)
  const [generation, setGeneration] = useState(0)
  const lost = useCallback(() => setReconnecting(true), [])
  const connection = useMemo(() => ({ generation, reconnecting, lost }), [generation, reconnecting, lost])

  useEffect(() => {
    if (!reconnecting) {
      return
    }
    const controller = new AbortController()
    waitForServer(controller.signal).then(
      () => {
        setReconnecting(false)
        setGeneration((value) => value + 1)
      },
      () => {},
    )
    return () => controller.abort()
  }, [reconnecting])

  return (
    <StudioConnectionContext.Provider value={connection}>
      {reconnecting && (
        <AxisAlert data-testid="studio-reconnecting" kind="info" message={t('shell.studio.reconnecting')} />
      )}
      <StudioWorkspace />
    </StudioConnectionContext.Provider>
  )
}

function StudioWorkspace() {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { generation, reconnecting, lost } = useStudioConnection()
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const application = params.get('app')
  const resourceId = params.get('resource')
  const path = params.get('path')
  const kind = params.get('kind')
  const policiesView = params.get('view') === 'policies'
  const releasesView = params.get('view') === 'releases'
  const releaseView = params.get('view') === 'release'
  const releaseNumber = params.get('release')
  const releasePage = releaseView && releaseNumber !== null
  const historyView = params.get('view') === 'history'
  const requestsView = params.get('view') === 'requests'
  const revisionId = params.get('revision')
  const [apps, setApps] = useState<AppsState>()
  const [folder, setFolder] = useState<FolderState>()
  const [canPublish, setCanPublish] = useState(false)
  const [canCreate, setCanCreate] = useState(false)
  const [canTest, setCanTest] = useState(false)
  const [previewHost, setPreviewHost] = useState<string | null>(null)
  const [folderRevision, setFolderRevision] = useState(0)
  const [namesState, setNamesState] = useState<NamesState>()
  const [manifestState, setManifestState] = useState<ManifestState>()
  const [manifestReload, setManifestReload] = useState(0)
  const publishing = useRef<StudioPublishingHandle>(null)
  const editor = useRef<StudioEditorHandle>(null)
  // Proposed: one side panel at a time, the Problems panel or the request panel.
  const [sidePanel, setSidePanel] = useState<'problems' | 'askAi' | null>(null)
  const [askAi, setAskAi] = useState(false)
  const [userId, setUserId] = useState<string | null>(null)
  const [requestBusy, setRequestBusy] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchCurrentUser(controller.signal).then(
      (user) => {
        setCanPublish(user?.studioPublisher === true)
        setCanCreate(user?.studioCreate === true)
        setCanTest(user?.studioTest === true)
        setPreviewHost(user?.studioPreviewHost ?? null)
        setAskAi(user?.studioAi === true)
        setUserId(user?.id ?? null)
      },
      () => {
        if (!controller.signal.aborted) {
          setCanPublish(false)
          setCanCreate(false)
          setCanTest(false)
          setPreviewHost(null)
        }
      },
    )
    return () => controller.abort()
  }, [generation])

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioApps(controller.signal).then(setApps, (error: unknown) => {
      if (controller.signal.aborted) {
        return
      }
      if (error instanceof ServerUnreachableError) {
        lost()
        return
      }
      console.warn('Loading the Studio applications failed', error)
      setApps('failed')
    })
    return () => controller.abort()
  }, [generation, lost])

  useEffect(() => {
    if (application === null) {
      return
    }
    const controller = new AbortController()
    Promise.all([
      fetchStudioResources(application, controller.signal),
      fetchStudioDiagnostics(application, controller.signal),
    ]).then(
      ([resources, diagnostics]) => setFolder({ application, resources, diagnostics }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the Studio folder of '${application}' failed`, error)
        setFolder({ application, failed: true })
      },
    )
    return () => controller.abort()
  }, [application, generation, lost])

  useEffect(() => {
    if (application === null) {
      return
    }
    const controller = new AbortController()
    fetchStudioNames(application, controller.signal).then(
      (names) => setNamesState({ application, names }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the Studio names of '${application}' failed`, error)
        setNamesState({ application, names: null })
      },
    )
    return () => controller.abort()
  }, [application, generation, folderRevision, lost])

  const manifestId = useMemo(
    () =>
      (folder?.application === application && 'resources' in folder ? folder.resources : []).find(
        (item) => item.kind === 'application',
      )?.id,
    [folder, application],
  )
  useEffect(() => {
    if (application === null || manifestId === undefined) {
      return
    }
    const controller = new AbortController()
    fetchStudioResource(application, manifestId, controller.signal).then(
      (resource) => setManifestState({ application, resource }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the application.json of '${application}' failed`, error)
        setManifestState({ application, resource: null })
      },
    )
    return () => controller.abort()
  }, [application, manifestId, generation, folderRevision, manifestReload, lost])

  const loaded = folder?.application === application ? folder : undefined
  const manifest = manifestState?.application === application ? (manifestState.resource ?? undefined) : undefined
  const names = namesState?.application === application ? namesState.names : null
  const resources = useMemo(() => (loaded && 'resources' in loaded ? loaded.resources : []), [loaded])
  const diagnosticPaths = useMemo(
    () =>
      loaded && 'resources' in loaded && resourceId !== null
        ? loaded.diagnostics
            .filter((diagnostic) => diagnostic.resourceId?.toLowerCase() === resourceId.toLowerCase())
            .map((diagnostic) => diagnostic.path)
        : [],
    [loaded, resourceId],
  )
  const groups = useMemo(
    () =>
      kind === null
        ? []
        : groupByKind(resources, locale, (item) => resourceTitle(item, locale, t)).filter(
            (group) => group.kind === kind,
          ),
    [resources, kind, locale, t],
  )
  const allModuleGroups = useMemo(() => moduleResources(names, '', locale), [names, locale])
  const moduleGroups = useMemo(
    () => (kind === null ? [] : allModuleGroups.filter((group) => group.kind === kind)),
    [allModuleGroups, kind],
  )
  const selected = resourceId === null ? undefined : resources.find((item) => item.id === resourceId.toLowerCase())
  const selectedModuleItem =
    resourceId === null || selected !== undefined
      ? undefined
      : allModuleGroups
          .flatMap((group) => group.items)
          .find((item) => item.id.toLowerCase() === resourceId.toLowerCase())

  function pickApplication(name: string) {
    setParams({ app: name })
  }

  function selectResource(id: string) {
    navigate(studioResourceAddress(application!, id))
  }

  function onCreated(item: StudioResourceItem, diagnostics: StudioDiagnostic[]) {
    setFolder((current) =>
      current && current.application === application && 'resources' in current
        ? { ...current, resources: [...current.resources, item], diagnostics }
        : current,
    )
    setFolderRevision((value) => value + 1)
    navigate(studioResourceAddress(application!, item.id))
  }

  function onWritten(diagnostics: StudioDiagnostic[], created?: StudioResourceItem) {
    setFolder((current) =>
      current && current.application === application && 'resources' in current
        ? { ...current, resources: created ? [...current.resources, created] : current.resources, diagnostics }
        : current,
    )
    setFolderRevision((value) => value + 1)
  }

  /** A save or Reset all of a module resource: the layer file joins the folder's list or leaves it. */
  function onModuleWritten(diagnostics: StudioDiagnostic[], layer: { added?: StudioResourceItem; removedId?: string }) {
    setFolder((current) =>
      current && current.application === application && 'resources' in current
        ? {
            ...current,
            resources: [
              ...current.resources.filter((resource) => resource.id !== layer.removedId),
              ...(layer.added ? [layer.added] : []),
            ],
            diagnostics,
          }
        : current,
    )
    setFolderRevision((value) => value + 1)
  }

  /** An undo can add or remove files, so the folder's resources load again with the diagnostics it answered. */
  function onUndone(diagnostics: StudioDiagnostic[]) {
    fetchStudioResources(application!).then(
      (items) => {
        setFolder({ application: application!, resources: items, diagnostics })
        setFolderRevision((value) => value + 1)
      },
      (error: unknown) => {
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the Studio folder of '${application}' failed`, error)
        setFolder({ application: application!, failed: true })
      },
    )
  }

  function onManifestSaved(saved: StudioResource, diagnostics: StudioDiagnostic[]) {
    setManifestState({ application: application!, resource: saved })
    onWritten(diagnostics)
  }

  function onApplicationCreated(name: string) {
    setApps((current) =>
      Array.isArray(current)
        ? [...current, { name, titles: {}, descriptions: {} }]
        : [{ name, titles: {}, descriptions: {} }],
    )
    pickApplication(name)
  }

  function openReference(id: string, referencePath: string | null) {
    navigate(studioResourceAddress(application!, id, referencePath))
  }

  function selectDiagnostic(id: string, diagnosticPath: string) {
    openReference(id, diagnosticPath)
  }

  if (apps === undefined) {
    return null
  }
  if (apps === forbidden) {
    return (
      <div data-testid="studio-forbidden">
        <Result status="403" title={t('shell.studio.forbidden.title')} subTitle={t('shell.studio.forbidden.message')} />
      </div>
    )
  }
  if (apps === 'failed') {
    return <AxisAlert data-testid="studio-load-error" kind="error" message={t('shell.studio.loadFailed')} />
  }
  if (application === null) {
    return (
      <Flex vertical gap="middle">
        <HomeTileStyle />
        <StudioPageHeading
          title={t('shell.studio.title')}
          actions={canCreate && <StudioNewApplication primary onCreated={onApplicationCreated} />}
        />
        {apps.length === 0 ? (
          <AxisAlert data-testid="studio-no-applications" kind="info" message={t('shell.studio.noApplications')} />
        ) : (
          <StudioApplicationTiles apps={apps} />
        )}
      </Flex>
    )
  }
  if (apps.length === 0) {
    return (
      <Flex vertical gap="middle">
        <AxisAlert data-testid="studio-no-applications" kind="info" message={t('shell.studio.noApplications')} />
        {canCreate && <StudioNewApplication onCreated={onApplicationCreated} />}
      </Flex>
    )
  }

  const overviewApp = apps.find((item) => item.name === application)
  const entries = studioNavigation(application, resources, allModuleGroups, t)
  const activeKey = studioActiveEntry(
    params,
    resources,
    allModuleGroups.flatMap((group) => group.items),
  )

  return (
    <>
      <ShellSidebar>
        <StudioSidebar
          apps={apps}
          application={application}
          canCreate={canCreate}
          entries={entries}
          activeKey={activeKey}
          onPick={pickApplication}
          onCreated={onApplicationCreated}
        />
      </ShellSidebar>
      <Flex vertical gap="middle">
        {loaded && 'resources' in loaded && (
          <StudioPublishing
            key={application}
            application={application}
            canPublish={canPublish}
            canTest={canTest}
            previewHost={previewHost}
            sites={resources.filter((item) => item.kind === 'site')}
            folderRevision={folderRevision}
            diagnostics={loaded.diagnostics}
            problemsOpen={sidePanel === 'problems'}
            onToggleProblems={() => setSidePanel((open) => (open === 'problems' ? null : 'problems'))}
            newResource={<StudioNewResource application={application!} resources={resources} onCreated={onCreated} />}
            askAi={
              askAi
                ? {
                    open: sidePanel === 'askAi',
                    onToggle: () => setSidePanel((open) => (open === 'askAi' ? null : 'askAi')),
                  }
                : null
            }
            sidePanel={
              sidePanel === 'askAi' ? (
                <StudioAskAi
                  application={application!}
                  userId={userId}
                  openRequest={requestsView ? params.get('request') : null}
                  onClose={() => setSidePanel(null)}
                />
              ) : (
                sidePanel === 'problems' && (
                  <StudioProblems
                    diagnostics={loaded.diagnostics}
                    onSelect={selectDiagnostic}
                    onClose={() => setSidePanel(null)}
                  />
                )
              )
            }
            releasesView={releasesView}
            onDiagnostics={(diagnostics) =>
              setFolder((current) =>
                current && current.application === application && 'resources' in current
                  ? { ...current, diagnostics }
                  : current,
              )
            }
            onOpenScenario={selectDiagnostic}
            onBusyChange={setRequestBusy}
            ref={publishing}
          >
            {(history) =>
              releasePage ? (
                <StudioReleasePage
                  application={application!}
                  number={releaseNumber}
                  history={history}
                  resources={resources}
                  canActivate={canPublish}
                  busy={requestBusy || reconnecting}
                  onActivate={(releaseId) => publishing.current?.activate(releaseId)}
                />
              ) : releasesView ? (
                <StudioPageHeading title={t('shell.studio.releases')} />
              ) : historyView ? (
                revisionId !== null ? (
                  <StudioRevisionPage application={application!} revisionId={revisionId} resources={resources} />
                ) : (
                  <Flex vertical gap="middle">
                    <StudioPageHeading title={t('shell.studio.history')} />
                    <StudioHistory application={application!} folderRevision={folderRevision} onUndone={onUndone} />
                  </Flex>
                )
              ) : policiesView ? (
                <Flex vertical gap="middle">
                  <StudioPageHeading title={t('shell.studio.view.policies')} />
                  <StudioPolicyMatrix
                    application={application!}
                    folderRevision={folderRevision}
                    names={names}
                    onOpenPolicy={(id) => navigate(studioResourceAddress(application!, id))}
                    onWritten={onWritten}
                  />
                </Flex>
              ) : selected ? (
                <div data-testid="studio-resource-details" style={{ minWidth: 0 }}>
                  <Flex vertical gap="middle">
                    <StudioPageHeading
                      title={resourceTitle(selected, locale, t)}
                      actions={
                        (canTest && selected.kind === 'scenario' && selected.name) ||
                        selected.kind !== 'application' ? (
                          <AxisActionRow align="center">
                            {canTest && selected.kind === 'scenario' && selected.name && (
                              // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
                              <Button
                                data-testid="studio-run-scenario"
                                disabled={requestBusy || reconnecting}
                                onClick={() => publishing.current?.runTests([selected.name!])}
                              >
                                {t('shell.studio.runScenario')}
                              </Button>
                            )}
                            {selected.kind !== 'application' && (
                              <StudioResourceMenu
                                disabled={reconnecting}
                                onDelete={() => editor.current?.askDelete()}
                              />
                            )}
                          </AxisActionRow>
                        ) : undefined
                      }
                    />
                    <Descriptions
                      column={1}
                      items={[
                        { key: 'kind', label: t('shell.studio.kind'), children: kindSingular(selected.kind, t) },
                        { key: 'id', label: t('shell.studio.id'), children: selected.id },
                        { key: 'file', label: t('shell.studio.file'), children: selected.file },
                        ...(path ? [{ key: 'path', label: t('shell.studio.path'), children: path }] : []),
                      ]}
                    />
                    <StudioNamesContext.Provider value={names ?? undefined}>
                      <StudioEditor
                        key={`editor-${selected.id}`}
                        ref={editor}
                        application={application!}
                        item={selected}
                        publisher={canPublish}
                        resources={loaded.resources}
                        path={path}
                        diagnosticPaths={diagnosticPaths}
                        onOpen={openReference}
                        onSaved={(diagnostics, created) => {
                          setFolder({
                            ...loaded,
                            resources: created ? [...loaded.resources, ...created] : loaded.resources,
                            diagnostics,
                          })
                          setFolderRevision((value) => value + 1)
                        }}
                        onTextsWritten={(diagnostics) => {
                          setFolder({ ...loaded, diagnostics })
                          setFolderRevision((value) => value + 1)
                        }}
                        onDeleted={(diagnostics) => {
                          setFolder({
                            ...loaded,
                            resources: loaded.resources.filter((item) => item.id !== selected.id),
                            diagnostics,
                          })
                          setFolderRevision((value) => value + 1)
                          navigate(`/studio?${new URLSearchParams({ app: application! })}`, {
                            state: leaveWithoutAsking,
                          })
                        }}
                      />
                    </StudioNamesContext.Provider>
                  </Flex>
                </div>
              ) : selectedModuleItem ? (
                <div data-testid="studio-resource-details" style={{ minWidth: 0 }}>
                  <Flex vertical gap="middle">
                    <StudioPageHeading title={selectedModuleItem.name} line={selectedModuleItem.module} />
                    <Descriptions
                      column={1}
                      items={[
                        {
                          key: 'kind',
                          label: t('shell.studio.kind'),
                          children: kindSingular(selectedModuleItem.kind, t),
                        },
                        { key: 'id', label: t('shell.studio.id'), children: selectedModuleItem.id },
                        { key: 'module', label: t('shell.studio.module'), children: selectedModuleItem.module },
                      ]}
                    />
                    <StudioNamesContext.Provider value={names ?? undefined}>
                      <ModuleResourceView
                        key={`module-${selectedModuleItem.kind}-${selectedModuleItem.id}`}
                        application={application!}
                        item={selectedModuleItem}
                        manifest={manifest}
                        diagnostics={loaded.diagnostics}
                        onModuleWritten={onModuleWritten}
                        onManifestSaved={onManifestSaved}
                        onManifestStale={() => setManifestReload((value) => value + 1)}
                      />
                    </StudioNamesContext.Provider>
                  </Flex>
                </div>
              ) : resourceId !== null ? (
                <div data-testid="studio-resource-details">
                  <Typography.Text type="secondary">{t('shell.studio.selectResource')}</Typography.Text>
                </div>
              ) : kind !== null ? (
                <Flex vertical gap="small">
                  <StudioPageHeading
                    title={kindName(kind, t)}
                    actions={
                      starterKinds.includes(kind) ? (
                        <StudioNewResource
                          kind={kind}
                          application={application!}
                          resources={resources}
                          onCreated={onCreated}
                        />
                      ) : undefined
                    }
                  />
                  {groups.map((group) => (
                    <section key={group.kind} data-testid={`studio-group-${group.kind}`}>
                      <Flex vertical align="flex-start">
                        {group.items.map((item) => (
                          // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
                          <Button
                            key={item.id}
                            type="text"
                            data-testid={`studio-resource-${item.id}`}
                            onClick={() => selectResource(item.id)}
                          >
                            {resourceTitle(item, locale, t)}
                          </Button>
                        ))}
                      </Flex>
                    </section>
                  ))}
                  {moduleGroups.map((group) => (
                    <section key={group.kind} data-testid={moduleGroupTestIds[group.kind]}>
                      <Typography.Title level={5}>{t(moduleGroupTitles[group.kind])}</Typography.Title>
                      <Flex vertical align="flex-start">
                        {group.items.map((item) => (
                          // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
                          <Button
                            key={item.id}
                            type="text"
                            data-testid={`studio-module-resource-${item.id}`}
                            onClick={() => selectResource(item.id)}
                          >
                            {item.name}
                          </Button>
                        ))}
                      </Flex>
                    </section>
                  ))}
                </Flex>
              ) : (
                <StudioOverview
                  application={application}
                  title={overviewApp === undefined ? application : studioAppTitle(overviewApp, locale)}
                  description={overviewApp === undefined ? null : studioAppDescription(overviewApp, locale)}
                  resources={resources}
                  moduleKinds={new Set(allModuleGroups.map((group) => group.kind))}
                  history={history}
                />
              )
            }
          </StudioPublishing>
        )}
        {loaded && 'failed' in loaded && (
          <AxisAlert data-testid="studio-load-error" kind="error" message={t('shell.studio.loadFailed')} />
        )}
      </Flex>
    </>
  )
}
