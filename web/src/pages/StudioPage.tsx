import { Alert, Button, Flex, Input, Result } from 'antd'
import { useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router'
import { fetchCurrentUser } from '../platform/currentUser'
import { forbidden } from '../platform/inspection'
import {
  fetchStudioApps,
  fetchStudioDiagnostics,
  fetchStudioNames,
  fetchStudioResource,
  fetchStudioResources,
  moduleResources,
  resourceTitle,
  studioApplicationDescription,
  studioApplicationTitle,
  ServerUnreachableError,
  type StudioApplication,
  type StudioDiagnostic,
  type StudioNames,
  type StudioReleases,
  type StudioResource,
  type StudioResourceItem,
  waitForServer,
  releaseGroups,
} from '../platform/studio'
import { StudioConnectionContext, useStudioConnection } from '../platform/studioConnection'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { StudioEditor } from './StudioEditor'
import { StudioResourceTable } from './StudioResourceTable'
import { StudioNewApplication } from './StudioNewApplication'
import { StudioNewResource } from './StudioNewResource'
import { StudioOverview } from './StudioOverview'
import { StudioProblemsPanel } from './StudioProblems'
import { StudioStatus } from './StudioStatus'
import { ModuleResourceView } from './StudioModuleView'
import { StudioPolicyMatrix } from './StudioPolicyMatrix'
import { StudioPublishing, type StudioPublishingHandle } from './StudioPublishing'
import { ReleaseTags } from './StudioReleases'
import { StudioNavigation } from './StudioNavigation'
import type { StudioView } from './studioViews'
import { StudioReferences } from './StudioReferences'
import { StudioResourceTitle } from './studioResourceTitle'
import { PageLayout } from '../platform/PageLayout'
import { HomeTile } from '../platform/HomeTile'
import { SiteMark } from '../platform/SiteMark'
import { PlatformContext } from './context'
import { useDesignTokens } from '../platform/themeMode'

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
 * breadcrumb. The Overview's heading is the application's title, with its description under it, and it shows no
 * resources. A kind's sidebar entry, as `&kind=<kind>`, shows that kind's list under a heading with its name, then the module group of that kind when modules hold one.
 * A resource opens alone in the content, under a heading with its name and, for a scenario, Run scenario on its right:
 * its details, its editor, and its Uses and Used by lists. The band under the header holds the application's state and
 * its actions. Its errors and warnings are one button that opens the Problems panel beside the page, which lists
 * the folder's diagnostics, loaded when the application is picked. A diagnostic that names a resource selects it,
 * and opens it at the diagnostic's path, as a Used by entry does, and the panel stays open. A selected resource opens in an editor below its details, with its Uses and Used by
 * lists under the editor. A Used by entry opens its resource at the path of the reference, as `&path=`. After a
 * save, the Problems panel shows the diagnostics that the save answered. After a delete, the resource leaves the
 * folder, and Studio shows the Overview. A New resource… button in the band creates a resource of any kind from its starter. The new resource
 * joins its kind's list, the Problems panel shows the diagnostics of the create, and the editor opens the resource.
 * A New application… item ends the application row's menu in the sidebar, and a button also sits on the list. Both show
 * only when `GET /api/me` answers `studioCreate: true`. A created application joins the menu and is picked at once.
 * Validate and Publish sit in the band, and Publish shows only when `GET /api/me` answers
 * `studioPublisher: true`. Under the content, the release list shows the application's releases, with an Activate… button
 * for a publisher. A save, a text write or a delete loads the release list again, because the folder may now differ from the
 * active release. The Problems panel shows the diagnostics of a request that ended. When the server does not
 * answer, a reconnect message shows at the top. Studio checks the server with a
 * back-off, then loads its data again, and an unsaved draft stays on screen. The names that the reference pickers
 * and expression completion offer load with the application too, and again after a save, a text write, a delete and a
 * reconnect. A text write, which a label's Write texts button sends, updates the Problems panel and loads the names
 * and the release list again, as a save does. The sidebar's Policy matrix entry shows the policy matrix, kept in the address
 * as `&view=policies`. The matrix shows in place of the content under a Policy matrix heading, and
 * its Open policy link opens the policy in the Resources view. The band and the Problems panel show in every view.
 * `&view=releases` shows only the Releases heading and the release list.
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
 * application and again after a write. An Off-standard tag shows in the band after the state while it has a relax
 * entry. A module contract, which the application declares directly, has Relax and Undo relax, which write it.
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
      {reconnecting && <Alert data-testid="studio-reconnecting" type="info" title={t('shell.studio.reconnecting')} />}
      <StudioWorkspace />
    </StudioConnectionContext.Provider>
  )
}

function StudioWorkspace() {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { generation, reconnecting, lost } = useStudioConnection()
  const [params, setParams] = useSearchParams()
  const application = params.get('app')
  const resourceId = params.get('resource')
  const path = params.get('path')
  // Proposed: `view` and `kind` pick the part of the workspace the sidebar opened; an open resource wins.
  const view: StudioView = parseView(params.get('view'), params.get('kind'), params.get('release'))
  const [actionsSlot, setActionsSlot] = useState<HTMLDivElement | null>(null)
  // Proposed: the right side of a release page's heading, where its Activate… shows.
  const [headingSlot, setHeadingSlot] = useState<HTMLDivElement | null>(null)
  const { spacing } = useDesignTokens()
  const [apps, setApps] = useState<AppsState>()
  const [folder, setFolder] = useState<FolderState>()
  // Proposed: a kind's list keeps its search in the address, as its sort and page, so a reload and a shared link keep them.
  const search = params.get('search') ?? ''
  const setSearch = (value: string) =>
    setParams(
      (current) => {
        const next = new URLSearchParams(current)
        next.delete('page')
        if (value === '') {
          next.delete('search')
        } else {
          next.set('search', value)
        }
        return next
      },
      { replace: true },
    )
  const [listReloads, setListReloads] = useState(0)
  const [canPublish, setCanPublish] = useState(false)
  const [canCreate, setCanCreate] = useState(false)
  const [canTest, setCanTest] = useState(false)
  const [previewHost, setPreviewHost] = useState<string | null>(null)
  const [folderRevision, setFolderRevision] = useState(0)
  const [namesState, setNamesState] = useState<NamesState>()
  const [releases, setReleases] = useState<{ application: string; history: StudioReleases | 'failed' }>()
  // Proposed: whether the problems panel is open beside the page.
  const [problemsOpen, setProblemsOpen] = useState(false)
  const [manifestState, setManifestState] = useState<ManifestState>()
  const [manifestReload, setManifestReload] = useState(0)
  const publishing = useRef<StudioPublishingHandle>(null)
  const [requestBusy, setRequestBusy] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchCurrentUser(controller.signal).then(
      (user) => {
        setCanPublish(user?.studioPublisher === true)
        setCanCreate(user?.studioCreate === true)
        setCanTest(user?.studioTest === true)
        setPreviewHost(user?.studioPreviewHost ?? null)
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
  }, [application, generation, listReloads, lost])

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
  const moduleGroups = useMemo(() => moduleResources(names, ''), [names])
  const selected = resourceId === null ? undefined : resources.find((item) => item.id === resourceId.toLowerCase())
  const selectedModuleItem =
    resourceId === null || selected !== undefined
      ? undefined
      : moduleGroups.flatMap((group) => group.items).find((item) => item.id.toLowerCase() === resourceId.toLowerCase())

  function pickApplication(name: string) {
    setParams({ app: name })
  }

  function selectResource(id: string) {
    setParams({ app: application!, resource: id })
  }

  function onCreated(item: StudioResourceItem, diagnostics: StudioDiagnostic[]) {
    setFolder((current) =>
      current && current.application === application && 'resources' in current
        ? { ...current, resources: [...current.resources, item], diagnostics }
        : current,
    )
    setFolderRevision((value) => value + 1)
    setParams({ app: application!, resource: item.id })
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

  function onManifestSaved(saved: StudioResource, diagnostics: StudioDiagnostic[]) {
    setManifestState({ application: application!, resource: saved })
    onWritten(diagnostics)
  }

  function onApplicationCreated(name: string) {
    setApps((current) => (Array.isArray(current) ? [...current, { name }] : [{ name }]))
    pickApplication(name)
  }

  function openReference(id: string, referencePath: string | null) {
    setParams({ app: application!, resource: id, ...(referencePath ? { path: referencePath } : {}) })
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
    return <Alert data-testid="studio-load-error" type="error" title={t('shell.studio.loadFailed')} />
  }
  if (apps.length === 0) {
    return (
      <Flex vertical gap="middle">
        <Alert data-testid="studio-no-applications" type="info" title={t('shell.studio.noApplications')} />
        {canCreate && <StudioNewApplication onCreated={onApplicationCreated} />}
      </Flex>
    )
  }

  // Proposed: with several applications and none open, Studio lists them as tiles, with New application as its main
  // action. An application opens in a workspace with Studio's navigation in the shell's sidebar.
  if (application === null) {
    return (
      <PageLayout
        compact
        title={t('shell.studio.title')}
        headingExtra={canCreate ? <StudioNewApplication primary onCreated={onApplicationCreated} /> : undefined}
      >
        <StudioApplicationTiles applications={apps} />
      </PageLayout>
    )
  }

  const kindView = view.name === 'kind' ? view.kind : (selected?.kind ?? selectedModuleItem?.kind)
  const openApp = apps.find((app) => app.name === application)
  // Proposed: every page heads its content with what it is about: the Overview with the application's title, as it is
  // the application's own page, another sidebar entry with its name, such as Settings or Entities, and an open resource
  // with its label, with its file or its module on a muted line under it. The band holds no title.
  let title = openApp ? studioApplicationTitle(openApp, locale) : application
  if (selected?.kind === 'application') {
    title = t('shell.studio.nav.settings')
  } else if (selected) {
    title = resourceTitle(selected, locale, t)
  } else if (selectedModuleItem) {
    title = selectedModuleItem.name
  } else if (view.name === 'kind') {
    title = kindLabel(view.kind)
  } else if (view.name === 'policies') {
    title = t('shell.studio.view.policies')
  } else if (view.name === 'releases') {
    title = t('shell.studio.nav.releases')
  } else if (view.name === 'release') {
    title = t('shell.studio.release.title').replace('{number}', String(view.number))
  }
  // Proposed: a release's page heads with "Release 6", its tags beside it and its label under it.
  const releaseHistory =
    releases?.application === application && releases.history !== 'failed' ? releases.history : undefined
  const openRelease =
    view.name === 'release' && !selected && releaseHistory
      ? releaseGroups(releaseHistory).find((group) => group.number === view.number)
      : undefined

  function kindLabel(kind: string) {
    const key = `shell.studio.kind.${kind}`
    const label = t(key)
    return label === key ? kind : label
  }

  const ofKind = kindView ? resources.filter((item) => item.kind === kindView) : []
  const navigationView: StudioView =
    selected?.kind === 'application'
      ? { name: 'settings' }
      : kindView && (selected || selectedModuleItem || view.name === 'kind')
        ? { name: 'kind', kind: kindView }
        : view

  // Proposed: every place in the workspace names a resource by its label, through this lookup.
  const titleOf = (id: string) => {
    const item = resources.find((resource) => resource.id === id)
    return item ? resourceTitle(item, locale, t) : undefined
  }

  return (
    <StudioResourceTitle.Provider value={titleOf}>
      {loaded && 'resources' in loaded && (
        <StudioNavigation
          application={application}
          applications={apps}
          resources={resources}
          moduleKinds={moduleGroups.map((group) => group.kind)}
          view={navigationView}
          canCreate={canCreate}
          onPickApplication={pickApplication}
          onApplicationCreated={onApplicationCreated}
          onView={(next) => {
            if (next.name === 'settings') {
              const settings = resources.find((item) => item.kind === 'application')
              setParams(settings ? { app: application, resource: settings.id } : { app: application })
            } else {
              setParams(new URLSearchParams(studioAddress(application, next).split('?')[1]))
            }
          }}
        />
      )}
      <PageLayout
        compact
        title={title}
        subtitle={
          // Proposed: an open resource's heading has no muted line: its file and path are technical names, which the
          // JSON view shows to whoever needs them.
          selected
            ? undefined
            : selectedModuleItem
              ? `${t('shell.studio.module')}: ${selectedModuleItem.module}`
              : view.name === 'overview' && openApp
                ? studioApplicationDescription(openApp, locale)
                : undefined
        }
        titleExtra={openRelease ? <ReleaseTags release={openRelease} /> : undefined}
        headingExtra={
          selected && canTest && selected.kind === 'scenario' && selected.name ? (
            <Button
              data-testid="studio-run-scenario"
              disabled={requestBusy || reconnecting}
              onClick={() => publishing.current?.runTests([selected.name!])}
            >
              {t('shell.studio.runScenario')}
            </Button>
          ) : view.name === 'kind' && !selected && loaded && 'resources' in loaded ? (
            // Proposed: New entity… acts on the application, not on the table, so it sits on the page's heading.
            <StudioNewResource application={application} resources={resources} kind={view.kind} onCreated={onCreated} />
          ) : view.name === 'release' && !selected ? (
            <div ref={setHeadingSlot} style={{ display: 'contents' }} />
          ) : undefined
        }
        status={
          loaded && 'resources' in loaded ? (
            <StudioStatus
              diagnostics={loaded.diagnostics}
              releases={releases?.application === application ? releases.history : undefined}
              problemsOpen={problemsOpen}
              onProblems={() => setProblemsOpen((current) => !current)}
            />
          ) : undefined
        }
        actions={
          // Proposed: New resource… heads the band's actions, the same gap apart as the others, as every one acts on the
          // application; Publish… stands out as the one solid button and is never beside it.
          <Flex align="center" gap={spacing.xs}>
            {loaded && 'resources' in loaded && (
              <StudioNewResource application={application} resources={resources} inBand onCreated={onCreated} />
            )}
            <div ref={setActionsSlot} style={{ display: 'flex' }} />
          </Flex>
        }
      >
        {loaded && 'resources' in loaded && (
          <StudioPublishing
            key={application}
            application={application}
            canPublish={canPublish}
            canTest={canTest}
            previewHost={previewHost}
            sites={resources.filter((item) => item.kind === 'site')}
            folderRevision={folderRevision}
            onDiagnostics={(diagnostics) =>
              setFolder((current) =>
                current && current.application === application && 'resources' in current
                  ? { ...current, diagnostics }
                  : current,
              )
            }
            onOpenScenario={selectDiagnostic}
            onBusyChange={setRequestBusy}
            actionsSlot={actionsSlot}
            showHistory={(view.name === 'releases' || view.name === 'release') && !selected}
            release={view.name === 'release' && !selected ? view.number : undefined}
            releaseAddress={(number) => studioAddress(application, { name: 'release', number })}
            historyAddress={studioAddress(application, { name: 'releases' })}
            headingSlot={headingSlot}
            onHistory={(history) => setReleases({ application, history })}
            ref={publishing}
          />
        )}
        {loaded && 'failed' in loaded && (
          <Alert data-testid="studio-load-error" type="error" title={t('shell.studio.loadFailed')} />
        )}
        {loaded && 'resources' in loaded && (
          <div data-testid="studio-resource-details">
            {selected ? (
              <Flex vertical gap="middle">
                <StudioNamesContext.Provider value={names ?? undefined}>
                  <StudioEditor
                    references={
                      <StudioReferences
                        key={`references-${selected.id}`}
                        application={application!}
                        resourceId={selected.id}
                        kind={selected.kind}
                      />
                    }
                    key={`editor-${selected.id}`}
                    application={application!}
                    item={selected}
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
                      setParams({ app: application! })
                    }}
                  />
                </StudioNamesContext.Provider>
              </Flex>
            ) : selectedModuleItem ? (
              <Flex vertical gap="middle">
                <StudioNamesContext.Provider value={names ?? undefined}>
                  <ModuleResourceView
                    key={`module-${selectedModuleItem.kind}-${selectedModuleItem.id}`}
                    application={application}
                    item={selectedModuleItem}
                    manifest={manifest}
                    diagnostics={loaded.diagnostics}
                    onModuleWritten={onModuleWritten}
                    onManifestSaved={onManifestSaved}
                    onManifestStale={() => setManifestReload((value) => value + 1)}
                  />
                </StudioNamesContext.Provider>
              </Flex>
            ) : view.name === 'policies' ? (
              <StudioPolicyMatrix
                application={application}
                folderRevision={folderRevision}
                names={names}
                onOpenPolicy={(id) => setParams({ app: application, resource: id })}
                onWritten={onWritten}
              />
            ) : view.name === 'kind' ? (
              <StudioResourceTable
                application={application}
                folderRevision={folderRevision}
                items={ofKind}
                searchText={search}
                extra={moduleGroups.find((group) => group.kind === view.kind)?.items ?? []}
                search={
                  <Input.Search
                    aria-label={t('shell.studio.search')}
                    placeholder={t('shell.studio.search')}
                    allowClear
                    style={{ maxWidth: 360 }}
                    value={search}
                    onChange={(event) => setSearch(event.target.value)}
                  />
                }
                addressOf={(id) => `/studio?${new URLSearchParams({ app: application, resource: id })}`}
                onRefresh={() => {
                  setListReloads((value) => value + 1)
                  setFolderRevision((value) => value + 1)
                }}
                onOpen={selectResource}
              />
            ) : view.name === 'overview' ? (
              <StudioOverview
                application={application}
                locale={locale}
                resources={loaded.resources}
                releases={releases?.application === application ? releases.history : undefined}
                addressOf={(target) =>
                  'resource' in target
                    ? `/studio?${new URLSearchParams({ app: application, resource: target.resource, ...(target.path ? { path: target.path } : {}) })}`
                    : studioAddress(application, target)
                }
              />
            ) : null}
          </div>
        )}
      </PageLayout>
      {loaded && 'resources' in loaded && (
        <StudioProblemsPanel
          open={problemsOpen}
          diagnostics={loaded.diagnostics}
          addressOf={(diagnostic) =>
            diagnostic.resourceId === null
              ? null
              : `/studio?${new URLSearchParams({ app: application, resource: diagnostic.resourceId, ...(diagnostic.path ? { path: diagnostic.path } : {}) })}`
          }
          onClose={() => setProblemsOpen(false)}
        />
      )}
    </StudioResourceTitle.Provider>
  )
}

/** Proposed: the workspace address of a view of `application`. */
function studioAddress(application: string, view: StudioView): string {
  const app = `app=${encodeURIComponent(application)}`
  switch (view.name) {
    case 'kind':
      return `/studio?${app}&view=kind&kind=${encodeURIComponent(view.kind)}`
    case 'policies':
    case 'releases':
      return `/studio?${app}&view=${view.name}`
    case 'release':
      return `/studio?${app}&view=release&release=${view.number}`
    default:
      return `/studio?${app}`
  }
}

function parseView(view: string | null, kind: string | null, release: string | null): StudioView {
  if (view === 'kind' && kind) {
    return { name: 'kind', kind }
  }
  if (view === 'release' && release && /^[1-9][0-9]*$/.test(release)) {
    return { name: 'release', number: Number(release) }
  }
  if (view === 'policies' || view === 'releases') {
    return { name: view }
  }
  return { name: 'overview' }
}

/**
 * Proposed: Studio's applications as tiles of the home page, its mark, its title and its description, in the home
 * page's grid of up to three columns, as an application in Studio has no sites under it. The whole tile opens it.
 */
function StudioApplicationTiles({ applications }: { applications: readonly StudioApplication[] }) {
  const { spacing } = useDesignTokens()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const sorted = [...applications].sort((a, b) =>
    studioApplicationTitle(a, locale).localeCompare(studioApplicationTitle(b, locale), locale),
  )
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: `repeat(auto-fit, minmax(max(0px, calc((100% - ${2 * spacing.md}px) / 3)), 1fr))`,
        gridAutoRows: '1fr',
        gap: spacing.md,
      }}
    >
      {sorted.map((app) => {
        const title = studioApplicationTitle(app, locale)
        return (
          <HomeTile
            key={app.name}
            to={`/studio?app=${encodeURIComponent(app.name)}`}
            mark={<SiteMark title={title} size={32} />}
            title={title}
            description={studioApplicationDescription(app, locale)}
          />
        )
      })}
    </div>
  )
}
