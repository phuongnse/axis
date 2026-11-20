import { Button, Flex, Result, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { lazy, Suspense, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router'
import { AxisActionRow } from '../platform/AxisButton'
import { PageActions, PageLayout } from '../platform/PageLayout'
import { SectionPart, SectionParts } from '../platform/SectionParts'
import { TermList } from '../platform/TermList'
import { fetchCurrentUser } from '../platform/currentUser'
import { forbidden } from '../platform/inspection'
import { ShellSidebar } from '../platform/shellSidebar'
import { SiteMark } from '../platform/SiteMark'
import {
  fetchStudioApps,
  fetchStudioDiagnostics,
  fetchStudioNames,
  fetchStudioPackageFiles,
  fetchStudioResource,
  fetchStudioResourceActivity,
  fetchStudioResources,
  moduleResourceTitle,
  moduleResources,
  resourceTitle,
  ServerUnreachableError,
  type StudioApplication,
  studioAppDescription,
  studioAppTitle,
  type StudioDiagnostic,
  type StudioNames,
  type StudioPackageFileItem,
  type StudioResource,
  type StudioResourceActivityItem,
  type StudioResourceItem,
  waitForServer,
} from '../platform/studio'
import { StudioConnectionContext, useStudioConnection } from '../platform/studioConnection'
import { kindName, kindSingular } from '../platform/studioKinds'
import { studioResourceAddress } from '../platform/studioAddress'
import { starterKinds } from '../platform/studioStarters'
import { StudioApplicationContext, StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { Tile, TileFocusStyle } from '../platform/Tile'
import { leaveWithoutAsking } from '../platform/unsavedChanges'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { useHomeGrid } from './homeGrid'
import { StudioAuthoringRequestPage } from './StudioAuthoringRequestPage'
import { StudioEditor, type StudioEditorHandle } from './StudioEditor'
import { StudioHistory, StudioRevisionPage } from './StudioHistory'
import { StudioKindList } from './StudioKindList'
import { ModuleResourceView } from './StudioModuleView'
import { StudioNewApplication } from './StudioNewApplication'
import { StudioNewPackage } from './StudioNewPackage'
import { StudioNewResource } from './StudioNewResource'
import { StudioOverview } from './StudioOverview'
import { StudioPackages } from './StudioPackages'
import { StudioReleasePage } from './StudioReleasePage'
import { StudioPolicyMatrix } from './StudioPolicyMatrix'
import { StudioAskAi } from './StudioAskAi'
import { StudioProblems } from './StudioProblems'
import { StudioResourceMenu } from './StudioResourceMenu'
import { StudioPublishing, type StudioPublishingHandle } from './StudioPublishing'
import { StudioPublishPage } from './StudioPublishPage'
import { studioActiveEntry, studioNavigation } from './studioNavigation'
import { StudioSidebar } from './StudioSidebar'

// A package's page carries the highlighting of each language, so it loads only when a package opens.
const LazyStudioPackage = lazy(() => import('./StudioPackage'))

/** The address Publish… was opened from, which the publish page keeps in its navigation state. */
function publishOrigin(state: unknown): string | null {
  return typeof state === 'object' && state !== null && 'publishFrom' in state && typeof state.publishFrom === 'string'
    ? state.publishFrom
    : null
}

/** The tiles of the applications Studio may edit, in the UI locale with `en` as the fallback. Each opens its application. */
function StudioApplicationTiles({ apps }: { apps: StudioApplication[] }) {
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour } = useDesignTokens()
  const grid = useHomeGrid()
  return (
    <div data-testid="studio-apps" style={grid}>
      {apps.map((app) => {
        const title = studioAppTitle(app, locale)
        const description = studioAppDescription(app, locale)
        return (
          <Tile
            key={app.name}
            to={`/studio?app=${encodeURIComponent(app.name)}`}
            testId={`studio-app-${app.name}`}
            align={description === null ? 'center' : 'flex-start'}
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
          </Tile>
        )
      })}
    </div>
  )
}

/** The applications Studio may edit, or why they could not be listed. */
/** The side panel Studio shows, one at a time. */
type StudioPanel = 'problems' | 'askAi'

type AppsState = StudioApplication[] | typeof forbidden | 'failed'

/** What one application's folder holds, or `failed`. `application` says which one it was loaded for. */
type FolderState =
  | { application: string; resources: StudioResourceItem[]; diagnostics: StudioDiagnostic[] }
  | { application: string; failed: true }

/** The names of one application, or null when they did not load. `application` says which one they were loaded for. */
type NamesState = { application: string; names: StudioNames | null }

/** The resource activity of one application, or none when it did not load. `application` says which one it was loaded for. */
type ActivityState = { application: string; items: StudioResourceActivityItem[] }

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
 * Every page of an open application heads its content with one `PageLayout` heading, and no page has a
 * breadcrumb, see docs/reference/studio-workspace.md#page-heading. The Overview's heading is the application's title, with its description under it, then the blocks of
 * `StudioOverview`. A kind's sidebar entry, as `&kind=<kind>`, shows that kind's list as one table under a heading with its name.
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
 * Validate, Ask AI and Publish… sit in the band. Ask AI shows only when `GET /api/me` answers `studioAi: true`, and opens the
 * request panel, `StudioAskAi`, which shares the one side panel with the Problems panel: opening one closes the other. Publish… shows only when `GET /api/me` answers
 * `studioPublisher: true`, and opens `&view=publish`, the `StudioPublishPage`, in place of the content, with no sidebar entry marked. Under its release fields the page
 * lists the files that differ from the live release, each with who changed it, see docs/reference/studio.md#publishing. The page keeps the
 * address Publish… was opened from in the navigation state, and its Publish and Cancel replace its entry with that address, or with the Overview when it was opened from a link.
 * Without `studioPublisher`, `&view=publish` shows the Overview, see docs/reference/studio.md#publishing. On the Releases view the release list shows the application's releases, with an Activate… button
 * for a publisher. A save, a text write or a delete loads the release history again, because the folder may now differ from the
 * active release. The Problems panel shows the diagnostics of a request that ended. When the server does not
 * answer, a reconnect message shows at the top. Studio checks the server with a
 * back-off, then loads its data again, and an unsaved draft stays on screen. The names that the reference pickers
 * and expression completion offer load with the application too, and again after a save, a text write, a delete and a
 * reconnect. A text write, which Save sends for a label's texts before the patch, updates the Problems panel and loads the names
 * and the release list again, as a save does. The sidebar's Policy matrix entry shows the policy matrix, kept in the address
 * as `&view=policies`. The matrix shows in place of the content under a Policy matrix heading, and
 * its Open policy link opens the policy in the Resources view. The band and the Problems panel show in every view.
 * The sidebar's Packages entry, `&view=packages`, shows `StudioPackages` under a Packages heading, and `&package=<id>` shows that
 * package's `StudioPackagePage` in place of the list, under the same entry. New package… sits on the heading's right and shows only
 * when `GET /api/me` answers `studioDeveloper: true`, see docs/reference/studio-packages.md#the-packages-entry.
 * `&view=releases` shows only the Releases heading and the release list. `&view=release&release=<number>` shows that
 * release's `StudioReleasePage` in place of the list, under the Releases entry, and its Activate… sends the same request as the list's.
 * `&view=requests&request=<id>` shows that authoring request's page in place of the content, and no sidebar entry is marked for it.
 * Run tests sits beside Validate and shows only when `GET /api/me` answers `studioTest: true`. A selected scenario
 * has a Run button in its details, which runs only that scenario. Both are off while a request runs and while the
 * reconnect message shows. A failed scenario in the test results opens at its failing step, as a diagnostic does.
 * A process step that a diagnostic of the open resource names is marked on its graph.
 * A kind's list is `StudioKindList`: one table of the application's own resources of the kind and the module resources of that kind that
 * the application uses, with the columns Name, Description, Status, Last modified by and Last modified at, whose Name is a link. The server
 * builds and pages the rows, see docs/reference/studio.md#kind-lists-and-module-resources. The list loads again after a save, a text write,
 * a delete and a reconnect, as the names do. A module resource has a neutral tag beside its name with its module's label.
 * A module resource of every kind but a contract and a scenario opens in its editor with the application's layer applied
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
  const location = useLocation()
  const application = params.get('app')
  const resourceId = params.get('resource')
  const path = params.get('path')
  const kind = params.get('kind')
  const policiesView = params.get('view') === 'policies'
  const packagesView = params.get('view') === 'packages'
  const packageId = params.get('package')
  const releasesView = params.get('view') === 'releases'
  const releaseView = params.get('view') === 'release'
  const releaseNumber = params.get('release')
  const releasePage = releaseView && releaseNumber !== null
  const historyView = params.get('view') === 'history'
  const requestedPublish = params.get('view') === 'publish'
  const revisionId = params.get('revision')
  const requestsView = params.get('view') === 'requests'
  const requestId = params.get('request')
  const packageFile = params.get('file')
  const [apps, setApps] = useState<AppsState>()
  const [folder, setFolder] = useState<FolderState>()
  const [canPublish, setCanPublish] = useState(false)
  const [canCreate, setCanCreate] = useState(false)
  const [canTest, setCanTest] = useState(false)
  const [canAskAi, setCanAskAi] = useState(false)
  const [canDevelop, setCanDevelop] = useState(false)
  const [packageFilesState, setPackageFilesState] = useState<{
    application: string
    items: StudioPackageFileItem[]
  }>()
  const [previewHost, setPreviewHost] = useState<string | null>(null)
  const [userId, setUserId] = useState<string | null>(null)
  const [folderRevision, setFolderRevision] = useState(0)
  const [namesState, setNamesState] = useState<NamesState>()
  const [activityState, setActivityState] = useState<ActivityState>()
  const [manifestState, setManifestState] = useState<ManifestState>()
  const [manifestReload, setManifestReload] = useState(0)
  const publishing = useRef<StudioPublishingHandle>(null)
  const editor = useRef<StudioEditorHandle>(null)
  // Studio shows one side panel at a time, see docs/reference/shell.md#layout.
  const [openPanel, setOpenPanel] = useState<StudioPanel | null>(null)
  const [requestBusy, setRequestBusy] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchCurrentUser(controller.signal).then(
      (user) => {
        setCanPublish(user?.studioPublisher === true)
        setCanCreate(user?.studioCreate === true)
        setCanTest(user?.studioTest === true)
        setCanAskAi(user?.studioAi === true)
        setCanDevelop(user?.studioDeveloper === true)
        setPreviewHost(user?.studioPreviewHost ?? null)
        setUserId(user?.id ?? null)
      },
      () => {
        if (!controller.signal.aborted) {
          setCanPublish(false)
          setCanCreate(false)
          setCanTest(false)
          setCanAskAi(false)
          setCanDevelop(false)
          setPreviewHost(null)
          setUserId(null)
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

  useEffect(() => {
    if (application === null) {
      return
    }
    const controller = new AbortController()
    fetchStudioPackageFiles(application, controller.signal).then(
      (items) => setPackageFilesState({ application, items }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the package files of '${application}' failed`, error)
        setPackageFilesState({ application, items: [] })
      },
    )
    return () => controller.abort()
  }, [application, generation, folderRevision, lost])
  const packageFiles = useMemo(
    () => (packageFilesState?.application === application ? packageFilesState.items : []),
    [packageFilesState, application],
  )

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
  const allModuleGroups = useMemo(() => moduleResources(names, '', locale), [names, locale])
  const selected = resourceId === null ? undefined : resources.find((item) => item.id === resourceId.toLowerCase())
  const selectedModuleItem =
    resourceId === null || selected !== undefined
      ? undefined
      : allModuleGroups
          .flatMap((group) => group.items)
          .find((item) => item.id.toLowerCase() === resourceId.toLowerCase())

  // The activity of the open module resource holds its label, which its heading shows. Other resources need none.
  const selectedModuleId = selectedModuleItem?.id
  useEffect(() => {
    if (application === null || selectedModuleId === undefined) {
      return
    }
    const controller = new AbortController()
    fetchStudioResourceActivity(application, controller.signal).then(
      (items) => setActivityState({ application, items }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the resource activity of '${application}' failed`, error)
        setActivityState({ application, items: [] })
      },
    )
    return () => controller.abort()
  }, [application, selectedModuleId, generation, folderRevision, lost])
  const activity = activityState?.application === application ? activityState.items : undefined

  function openPublish() {
    navigate(`/studio?${new URLSearchParams({ app: application!, view: 'publish' })}`, {
      state: { publishFrom: location.pathname + location.search },
    })
  }

  /** Goes back to the page Publish… was opened from, or to the Overview, and replaces the publish page's entry. */
  function leavePublish() {
    navigate(publishOrigin(location.state) ?? `/studio?${new URLSearchParams({ app: application! })}`, {
      replace: true,
    })
  }

  function pickApplication(name: string) {
    setParams({ app: name })
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
      <PageLayout title={t('shell.studio.title')}>
        <TileFocusStyle />
        {canCreate && (
          <PageActions>
            <StudioNewApplication primary onCreated={onApplicationCreated} />
          </PageActions>
        )}
        {apps.length === 0 ? (
          <AxisAlert data-testid="studio-no-applications" kind="info" message={t('shell.studio.noApplications')} />
        ) : (
          <StudioApplicationTiles apps={apps} />
        )}
      </PageLayout>
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

  const publishView = requestedPublish && canPublish
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
          <>
            <StudioPublishing
              key={application}
              application={application}
              canPublish={canPublish}
              canTest={canTest}
              previewHost={previewHost}
              sites={resources.filter((item) => item.kind === 'site')}
              folderRevision={folderRevision}
              diagnostics={loaded.diagnostics}
              canAskAi={canAskAi}
              askAiOpen={openPanel === 'askAi'}
              onToggleAskAi={() => setOpenPanel((open) => (open === 'askAi' ? null : 'askAi'))}
              problemsOpen={openPanel === 'problems'}
              onToggleProblems={() => setOpenPanel((open) => (open === 'problems' ? null : 'problems'))}
              newResource={<StudioNewResource application={application!} resources={resources} onCreated={onCreated} />}
              releasesView={releasesView}
              publishView={publishView}
              onOpenPublish={openPublish}
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
                publishView ? (
                  <StudioPublishPage
                    history={history}
                    busy={requestBusy || reconnecting}
                    application={application!}
                    userId={userId}
                    resources={resources}
                    onPublish={(details) => publishing.current?.publish(details)}
                    onLeave={leavePublish}
                  />
                ) : releasePage ? (
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
                  <PageLayout title={t('shell.studio.releases')} />
                ) : requestsView && requestId !== null ? (
                  <StudioAuthoringRequestPage
                    application={application!}
                    requestId={requestId}
                    userId={userId}
                    resources={resources}
                  />
                ) : historyView ? (
                  revisionId !== null ? (
                    <StudioRevisionPage application={application!} revisionId={revisionId} resources={resources} />
                  ) : (
                    <PageLayout title={t('shell.studio.history')}>
                      <StudioHistory application={application!} folderRevision={folderRevision} onUndone={onUndone} />
                    </PageLayout>
                  )
                ) : packagesView ? (
                  packageId !== null ? (
                    // Proposed: an open package is a place to work, see #3978.
                    <Suspense fallback={null}>
                      <LazyStudioPackage
                        key={packageId}
                        application={application!}
                        packageId={packageId}
                        file={packageFile}
                        files={packageFiles.filter((item) => item.package === packageId)}
                        diagnostics={loaded.diagnostics}
                        developer={canDevelop}
                        busy={reconnecting}
                        onChanged={(diagnostics) => {
                          setFolder({ ...loaded, diagnostics })
                          setFolderRevision((value) => value + 1)
                        }}
                      />
                    </Suspense>
                  ) : (
                    <PageLayout title={t('shell.studio.nav.packages')}>
                      {canDevelop && (
                        <PageActions>
                          <StudioNewPackage application={application!} onCreated={onWritten} />
                        </PageActions>
                      )}
                      <StudioPackages application={application!} folderRevision={folderRevision} />
                    </PageLayout>
                  )
                ) : policiesView ? (
                  <PageLayout title={t('shell.studio.view.policies')}>
                    <StudioPolicyMatrix
                      application={application!}
                      folderRevision={folderRevision}
                      names={names}
                      onOpenPolicy={(id) => navigate(studioResourceAddress(application!, id))}
                      onWritten={onWritten}
                    />
                  </PageLayout>
                ) : selected ? (
                  <div data-testid="studio-resource-details" style={{ minWidth: 0 }}>
                    <PageLayout title={resourceTitle(selected, locale, t)} line={kindSingular(selected.kind, t)}>
                      {((canTest && selected.kind === 'scenario' && selected.name) ||
                        selected.kind !== 'application') && (
                        <PageActions>
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
                        </PageActions>
                      )}
                      <SectionParts>
                        <SectionPart testName="facts">
                          <TermList
                            terms={[
                              { key: 'id', term: t('shell.studio.id'), value: selected.id },
                              { key: 'file', term: t('shell.studio.file'), value: selected.file },
                            ]}
                          />
                        </SectionPart>
                        <SectionPart testName="editor">
                          <StudioApplicationContext.Provider value={application!}>
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
                          </StudioApplicationContext.Provider>
                        </SectionPart>
                      </SectionParts>
                    </PageLayout>
                  </div>
                ) : selectedModuleItem ? (
                  <div data-testid="studio-resource-details" style={{ minWidth: 0 }}>
                    <PageLayout
                      title={moduleResourceTitle(
                        selectedModuleItem,
                        activity?.find((item) => item.id.toLowerCase() === selectedModuleItem.id.toLowerCase())?.titles,
                        locale,
                      )}
                      line={kindSingular(selectedModuleItem.kind, t)}
                    >
                      <SectionParts>
                        <SectionPart testName="facts">
                          <TermList
                            terms={[
                              { key: 'id', term: t('shell.studio.id'), value: selectedModuleItem.id },
                              { key: 'module', term: t('shell.studio.module'), value: selectedModuleItem.module },
                            ]}
                          />
                        </SectionPart>
                        <SectionPart testName="view">
                          <StudioApplicationContext.Provider value={application!}>
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
                          </StudioApplicationContext.Provider>
                        </SectionPart>
                      </SectionParts>
                    </PageLayout>
                  </div>
                ) : resourceId !== null ? (
                  <div data-testid="studio-resource-details">
                    <Typography.Text type="secondary">{t('shell.studio.selectResource')}</Typography.Text>
                  </div>
                ) : kind !== null ? (
                  <PageLayout title={kindName(kind, t)}>
                    {starterKinds.includes(kind) && (
                      <PageActions>
                        <StudioNewResource
                          kind={kind}
                          application={application!}
                          resources={resources}
                          onCreated={onCreated}
                        />
                      </PageActions>
                    )}
                    <StudioKindList key={kind} application={application!} kind={kind} folderRevision={folderRevision} />
                  </PageLayout>
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
            {openPanel === 'problems' && (
              <StudioProblems
                diagnostics={loaded.diagnostics}
                resources={loaded.resources}
                onSelect={selectDiagnostic}
                onClose={() => setOpenPanel(null)}
              />
            )}
            {openPanel === 'askAi' && (
              <StudioAskAi key={application} application={application!} onClose={() => setOpenPanel(null)} />
            )}
          </>
        )}
        {loaded && 'failed' in loaded && (
          <AxisAlert data-testid="studio-load-error" kind="error" message={t('shell.studio.loadFailed')} />
        )}
      </Flex>
    </>
  )
}
