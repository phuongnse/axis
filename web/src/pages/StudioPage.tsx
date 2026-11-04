import { Alert, Button, Descriptions, Flex, Input, Result, Tag, Typography } from 'antd'
import { useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { fetchCurrentUser } from '../platform/currentUser'
import { forbidden } from '../platform/inspection'
import { ShellSidebar } from '../platform/shellSidebar'
import { SiteMark } from '../platform/SiteMark'
import {
  fetchStudioApps,
  fetchStudioDiagnostics,
  fetchStudioNames,
  fetchStudioResource,
  fetchStudioResources,
  filterResources,
  groupByKind,
  type ModuleResourceItem,
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
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { useHomeGrid } from './homeGrid'
import { HomeTileStyle } from './HomeTileStyle'
import { StudioEditor } from './StudioEditor'
import { StudioModuleContract } from './StudioModuleContract'
import { StudioModuleEditor } from './StudioModuleEditor'
import { StudioModulePage } from './StudioModulePage'
import { StudioModulePolicy } from './StudioModulePolicy'
import { StudioNewApplication } from './StudioNewApplication'
import { StudioNewResource } from './StudioNewResource'
import { StudioPolicyMatrix } from './StudioPolicyMatrix'
import { StudioPublishing, type StudioPublishingHandle } from './StudioPublishing'
import { hasRelaxEntry } from './relaxEntries'
import { studioActiveEntry, studioNavigation } from './studioNavigation'
import { StudioReferences } from './StudioReferences'
import { StudioSidebar } from './StudioSidebar'

/** The titles and test ids of the module resource groups in the list. */
const moduleGroupTitles = {
  entity: 'shell.studio.moduleEntities',
  process: 'shell.studio.moduleProcesses',
  policy: 'shell.studio.modulePolicies',
  contract: 'shell.studio.moduleContracts',
  page: 'shell.studio.modulePages',
} as const
const moduleGroupTestIds = {
  entity: 'studio-group-module-entities',
  process: 'studio-group-module-processes',
  policy: 'studio-group-module-policies',
  contract: 'studio-group-module-contracts',
  page: 'studio-group-module-pages',
} as const

/** The view of a module resource, by its kind. An entity and a process are edited as a layer, and the other kinds are read-only. */
function ModuleResourceView({
  application,
  item,
  manifest,
  onModuleWritten,
  onManifestSaved,
  onManifestStale,
}: {
  application: string
  item: ModuleResourceItem
  manifest?: StudioResource
  onModuleWritten: (diagnostics: StudioDiagnostic[], layer: { added?: StudioResourceItem; removedId?: string }) => void
  onManifestSaved: (manifest: StudioResource, diagnostics: StudioDiagnostic[]) => void
  onManifestStale: () => void
}) {
  switch (item.kind) {
    case 'entity':
    case 'process':
      return <StudioModuleEditor application={application} item={item} onWritten={onModuleWritten} />
    case 'policy':
      return <StudioModulePolicy application={application} module={item.module} id={item.id} />
    case 'contract':
      return (
        <StudioModuleContract
          application={application}
          module={item.module}
          id={item.id}
          manifest={manifest}
          onManifestSaved={onManifestSaved}
          onManifestStale={onManifestStale}
        />
      )
    case 'page':
      return <StudioModulePage application={application} module={item.module} id={item.id} />
  }
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
 * and its description in the UI locale (`en` as the fallback, then the name), and a solid New application… button
 * beside the heading. A tile opens its application, and a single application is not opened by itself. The resources are grouped by kind, and a search narrows them
 * by name or id. The diagnostics panel lists the folder's diagnostics, loaded when the application is
 * picked. A diagnostic that names a resource selects it, clears the search, and opens it at the diagnostic's path,
 * as a Used by entry does. A selected resource opens in an editor below its details, with its Uses and Used by
 * lists under the editor. A Used by entry opens its resource at the path of the reference, as `&path=`. After a
 * save, the diagnostics panel shows the diagnostics that the save answered. After a delete, the resource leaves the
 * list. A New resource button above the search box creates a resource of any kind from its starter. The new resource
 * joins the list, the diagnostics panel shows the diagnostics of the create, and the editor opens the resource.
 * A New application… item ends the application row's menu in the sidebar, and a button also sits on the list. Both show
 * only when `GET /api/me` answers `studioCreate: true`. A created application joins the menu and is picked at once.
 * Validate and Publish sit at the top of the open application's page, and Publish shows only when `GET /api/me` answers
 * `studioPublisher: true`. Under them, the release list shows the application's releases, with a Roll back button
 * for a publisher. A save, a text write or a delete loads the release list again, because the folder may now differ from the
 * active release. The diagnostics panel shows the diagnostics of a request that ended. When the server does not
 * answer, a reconnect message shows at the top. Studio checks the server with a
 * back-off, then loads its data again, and an unsaved draft stays on screen. The names that the reference pickers
 * and expression completion offer load with the application too, and again after a save, a text write, a delete and a
 * reconnect. A text write, which a label's Write texts button sends, updates the diagnostics panel and loads the names
 * and the release list again, as a save does. The sidebar's Policy matrix entry shows the policy matrix, kept in the address
 * as `&view=policies`. The matrix shows in place of the resource list and the details, and
 * its Open policy link opens the policy in the Resources view. The diagnostics panel shows in the resource view and in the matrix view.
 * `&view=releases` shows only the publishing block, and `&kind=<kind>` limits the resource list to that kind.
 * Run tests sits beside Validate and shows only when `GET /api/me` answers `studioTest: true`. A selected scenario
 * has a Run button in its details, which runs only that scenario. Both are off while a request runs and while the
 * reconnect message shows. A failed scenario in the test results opens at its failing step, as a diagnostic does.
 * A process step that a diagnostic of the open resource names is marked on its graph.
 * Module entities, Module processes, Module policies, Module contracts and Module pages groups under the kinds list those of the modules the application
 * uses, from the names, by their `<module>/<name>`. A module entity opens in the entity designer, and a module process
 * in the process designer, each with the application's layer applied and every input on, with no JSON tab, Delete,
 * migration questions, Uses or Used by lists. Save writes the layer, and a Customized tag with Reset all, which deletes
 * the layer, shows while it has one. The other kinds open read-only, with no editor, no Uses and no Used by lists: a
 * policy as a table of its grants, a contract as its lifecycle in a state diagram with tables of its named points and a page on the page
 * canvas with no palette, data panel, widget tree or moves, each under a read-only notice with the alert role. Its address is the same `&resource=<id>`, so a reload, a shared link and a Uses
 * entry open it too, when the id is not one of the folder's. The folder's `application.json` loads with the
 * application and again after a write. An Off-standard tag shows above Validate and Publish while it has a relax
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
  const { generation, reconnecting, lost } = useStudioConnection()
  const [params, setParams] = useSearchParams()
  const application = params.get('app')
  const resourceId = params.get('resource')
  const path = params.get('path')
  const kind = params.get('kind')
  const policiesView = params.get('view') === 'policies'
  const releasesView = params.get('view') === 'releases'
  const [apps, setApps] = useState<AppsState>()
  const [folder, setFolder] = useState<FolderState>()
  const [search, setSearch] = useState('')
  const [canPublish, setCanPublish] = useState(false)
  const [canCreate, setCanCreate] = useState(false)
  const [canTest, setCanTest] = useState(false)
  const [previewHost, setPreviewHost] = useState<string | null>(null)
  const [folderRevision, setFolderRevision] = useState(0)
  const [namesState, setNamesState] = useState<NamesState>()
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
    () => groupByKind(filterResources(resources, search)).filter((group) => kind === null || group.kind === kind),
    [resources, search, kind],
  )
  const allModuleGroups = useMemo(() => moduleResources(names, ''), [names])
  const moduleGroups = useMemo(
    () => moduleResources(names, search).filter((group) => kind === null || group.kind === kind),
    [names, search, kind],
  )
  const selected = resourceId === null ? undefined : resources.find((item) => item.id === resourceId.toLowerCase())
  const selectedModuleItem =
    resourceId === null || selected !== undefined
      ? undefined
      : allModuleGroups
          .flatMap((group) => group.items)
          .find((item) => item.id.toLowerCase() === resourceId.toLowerCase())

  function pickApplication(name: string) {
    setSearch('')
    setParams({ app: name })
  }

  function selectResource(id: string) {
    setParams({ app: application!, ...(kind === null ? {} : { kind }), resource: id })
  }

  function onCreated(item: StudioResourceItem, diagnostics: StudioDiagnostic[]) {
    setFolder((current) =>
      current && current.application === application && 'resources' in current
        ? { ...current, resources: [...current.resources, item], diagnostics }
        : current,
    )
    setFolderRevision((value) => value + 1)
    setSearch('')
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
    setApps((current) =>
      Array.isArray(current)
        ? [...current, { name, titles: {}, descriptions: {} }]
        : [{ name, titles: {}, descriptions: {} }],
    )
    pickApplication(name)
  }

  function openReference(id: string, referencePath: string | null) {
    setParams({ app: application!, resource: id, ...(referencePath ? { path: referencePath } : {}) })
  }

  function selectDiagnostic(id: string, diagnosticPath: string) {
    setSearch('')
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
  if (application === null) {
    return (
      <Flex vertical gap="middle">
        <HomeTileStyle />
        <Flex justify="space-between" align="center">
          <Typography.Title level={4} style={{ margin: 0 }}>
            {t('shell.studio.title')}
          </Typography.Title>
          {canCreate && <StudioNewApplication primary onCreated={onApplicationCreated} />}
        </Flex>
        {apps.length === 0 ? (
          <Alert data-testid="studio-no-applications" type="info" title={t('shell.studio.noApplications')} />
        ) : (
          <StudioApplicationTiles apps={apps} />
        )}
      </Flex>
    )
  }
  if (apps.length === 0) {
    return (
      <Flex vertical gap="middle">
        <Alert data-testid="studio-no-applications" type="info" title={t('shell.studio.noApplications')} />
        {canCreate && <StudioNewApplication onCreated={onApplicationCreated} />}
      </Flex>
    )
  }

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
        <Typography.Title level={2}>{t('shell.studio.title')}</Typography.Title>
        {loaded && 'resources' in loaded && (
          <Flex vertical gap="small">
            {manifest && hasRelaxEntry(manifest.content) && (
              <Flex>
                <Tag data-testid="studio-off-standard" color="warning">
                  {t('shell.studio.offStandard')}
                </Tag>
              </Flex>
            )}
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
              ref={publishing}
            />
          </Flex>
        )}
        {loaded && 'failed' in loaded && (
          <Alert data-testid="studio-load-error" type="error" title={t('shell.studio.loadFailed')} />
        )}
        {loaded && 'resources' in loaded && !releasesView && (
          <>
            {policiesView ? (
              <StudioPolicyMatrix
                application={application!}
                folderRevision={folderRevision}
                names={names}
                onOpenPolicy={(id) => setParams({ app: application!, resource: id })}
                onWritten={onWritten}
              />
            ) : (
              <Flex gap="large" align="flex-start">
                <Flex vertical gap="small" style={{ width: 320, flex: 'none' }}>
                  <StudioNewResource application={application!} resources={resources} onCreated={onCreated} />
                  <Input.Search
                    aria-label={t('shell.studio.search')}
                    placeholder={t('shell.studio.search')}
                    allowClear
                    value={search}
                    onChange={(event) => setSearch(event.target.value)}
                  />
                  {groups.length === 0 && moduleGroups.length === 0 && (
                    <Typography.Text type="secondary">{t('shell.studio.noResources')}</Typography.Text>
                  )}
                  {groups.map((group) => (
                    <section key={group.kind} data-testid={`studio-group-${group.kind}`}>
                      <Typography.Title level={5}>{group.kind}</Typography.Title>
                      <Flex vertical align="flex-start">
                        {group.items.map((item) => (
                          <Button
                            key={item.id}
                            type={item.id === selected?.id ? 'primary' : 'text'}
                            data-testid={`studio-resource-${item.id}`}
                            aria-current={item.id === selected?.id ? 'true' : undefined}
                            onClick={() => selectResource(item.id)}
                          >
                            {resourceTitle(item)}
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
                          <Button
                            key={item.id}
                            type={item.id === selectedModuleItem?.id ? 'primary' : 'text'}
                            data-testid={`studio-module-resource-${item.id}`}
                            aria-current={item.id === selectedModuleItem?.id ? 'true' : undefined}
                            onClick={() => selectResource(item.id)}
                          >
                            {item.name}
                          </Button>
                        ))}
                      </Flex>
                    </section>
                  ))}
                </Flex>
                <div data-testid="studio-resource-details" style={{ flex: 1 }}>
                  {selected ? (
                    <Flex vertical gap="middle">
                      <Descriptions
                        title={resourceTitle(selected)}
                        column={1}
                        extra={
                          canTest && selected.kind === 'scenario' && selected.name ? (
                            <Button
                              data-testid="studio-run-scenario"
                              disabled={requestBusy || reconnecting}
                              onClick={() => publishing.current?.runTests([selected.name!])}
                            >
                              {t('shell.studio.runScenario')}
                            </Button>
                          ) : undefined
                        }
                        items={[
                          { key: 'kind', label: t('shell.studio.kind'), children: selected.kind },
                          { key: 'id', label: t('shell.studio.id'), children: selected.id },
                          { key: 'file', label: t('shell.studio.file'), children: selected.file },
                          ...(path ? [{ key: 'path', label: t('shell.studio.path'), children: path }] : []),
                        ]}
                      />
                      <StudioNamesContext.Provider value={names ?? undefined}>
                        <StudioEditor
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
                      <StudioReferences
                        key={`references-${selected.id}`}
                        application={application!}
                        resourceId={selected.id}
                        onOpen={openReference}
                      />
                    </Flex>
                  ) : selectedModuleItem ? (
                    <Flex vertical gap="middle">
                      <Descriptions
                        title={selectedModuleItem.name}
                        column={1}
                        items={[
                          { key: 'kind', label: t('shell.studio.kind'), children: selectedModuleItem.kind },
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
                          onModuleWritten={onModuleWritten}
                          onManifestSaved={onManifestSaved}
                          onManifestStale={() => setManifestReload((value) => value + 1)}
                        />
                      </StudioNamesContext.Provider>
                    </Flex>
                  ) : (
                    <Typography.Text type="secondary">{t('shell.studio.selectResource')}</Typography.Text>
                  )}
                </div>
              </Flex>
            )}
            <section data-testid="studio-diagnostics">
              <Typography.Title level={4}>{t('shell.studio.diagnostics')}</Typography.Title>
              {loaded.diagnostics.length === 0 ? (
                <Typography.Text type="secondary">{t('shell.studio.noDiagnostics')}</Typography.Text>
              ) : (
                <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
                  {loaded.diagnostics.map((diagnostic, index) => (
                    <li key={index}>
                      <DiagnosticRow diagnostic={diagnostic} onSelect={selectDiagnostic} />
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </>
        )}
      </Flex>
    </>
  )
}

/** One diagnostic: severity, code, message, file and path. It is a button when it names a resource. */
function DiagnosticRow({
  diagnostic,
  onSelect,
}: {
  diagnostic: StudioDiagnostic
  onSelect: (resourceId: string, path: string) => void
}) {
  const t = useText()
  const content = (
    <Flex vertical align="flex-start">
      <span>
        <Tag color={diagnostic.severity === 'error' ? 'error' : 'warning'}>{diagnostic.severity}</Tag>
        <strong>{diagnostic.code}</strong> {diagnostic.message}
      </span>
      <Typography.Text type="secondary">
        {diagnostic.file}
        {diagnostic.path !== '' && ` · ${t('shell.studio.path')}: ${diagnostic.path}`}
      </Typography.Text>
    </Flex>
  )
  const resourceId = diagnostic.resourceId
  return resourceId === null ? (
    <div style={{ padding: '4px 15px' }}>{content}</div>
  ) : (
    <Button
      type="text"
      style={{ height: 'auto', textAlign: 'left' }}
      onClick={() => onSelect(resourceId, diagnostic.path)}
    >
      {content}
    </Button>
  )
}
