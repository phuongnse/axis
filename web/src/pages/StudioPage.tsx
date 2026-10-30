import { Alert, Button, Descriptions, Flex, Input, Result, Tag, Typography } from 'antd'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router'
import { fetchCurrentUser } from '../platform/currentUser'
import { forbidden } from '../platform/inspection'
import {
  fetchStudioApps,
  fetchStudioDiagnostics,
  fetchStudioNames,
  fetchStudioResources,
  filterResources,
  type ModuleResourceItem,
  moduleResources,
  resourceTitle,
  ServerUnreachableError,
  type StudioApplication,
  type StudioDiagnostic,
  type StudioNames,
  type StudioResourceItem,
  waitForServer,
} from '../platform/studio'
import { StudioConnectionContext, useStudioConnection } from '../platform/studioConnection'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { StudioEditor } from './StudioEditor'
import { StudioModuleEntity } from './StudioModuleEntity'
import { StudioModulePage } from './StudioModulePage'
import { StudioModulePolicy } from './StudioModulePolicy'
import { StudioModuleProcess } from './StudioModuleProcess'
import { StudioNewApplication } from './StudioNewApplication'
import { StudioNewResource } from './StudioNewResource'
import { StudioPolicyMatrix } from './StudioPolicyMatrix'
import { StudioPublishing, type StudioPublishingHandle } from './StudioPublishing'
import { StudioNavigation } from './StudioNavigation'
import type { StudioView } from './studioViews'
import { StudioReferences } from './StudioReferences'
import { PageLayout, type BreadcrumbStep } from '../platform/PageLayout'
import { SiteMark } from '../platform/SiteMark'
import { useDesignTokens } from '../platform/themeMode'

/** The read-only view of a module resource, by its kind. */
function ModuleResourceView({ application, item }: { application: string; item: ModuleResourceItem }) {
  switch (item.kind) {
    case 'entity':
      return <StudioModuleEntity application={application} module={item.module} id={item.id} />
    case 'process':
      return <StudioModuleProcess application={application} module={item.module} id={item.id} />
    case 'policy':
      return <StudioModulePolicy application={application} module={item.module} id={item.id} />
    case 'page':
      return <StudioModulePage application={application} module={item.module} id={item.id} />
  }
}

/** The applications Studio may edit, or why they could not be listed. */
type AppsState = StudioApplication[] | typeof forbidden | 'failed'

/** What one application's folder holds, or `failed`. `application` says which one it was loaded for. */
type FolderState =
  | { application: string; resources: StudioResourceItem[]; diagnostics: StudioDiagnostic[] }
  | { application: string; failed: true }

/** The names of one application, or null when they did not load. `application` says which one they were loaded for. */
type NamesState = { application: string; names: StudioNames | null }

/**
 * Studio, the authoring page. The address holds the picked application and the selected resource, as
 * `/studio?app=PurchaseRequests&resource=<id>`. A person without the author role sees a forbidden page.
 * With one application, it is picked at once. The resources are grouped by kind, and a search narrows them
 * by name or id. The diagnostics panel lists the folder's diagnostics, loaded when the application is
 * picked. A diagnostic that names a resource selects it, clears the search, and opens it at the diagnostic's path,
 * as a Used by entry does. A selected resource opens in an editor below its details, with its Uses and Used by
 * lists under the editor. A Used by entry opens its resource at the path of the reference, as `&path=`. After a
 * save, the diagnostics panel shows the diagnostics that the save answered. After a delete, the resource leaves the
 * list. A New resource button above the search box creates a resource of any kind from its starter. The new resource
 * joins the list, the diagnostics panel shows the diagnostics of the create, and the editor opens the resource.
 * A New application button sits beside the application picker, and also shows when Studio lists no application. It shows
 * only when `GET /api/me` answers `studioCreate: true`. A created application joins the picker and is picked at once.
 * Validate and Publish sit under the application picker, and Publish shows only when `GET /api/me` answers
 * `studioPublisher: true`. Under them, the release list shows the application's releases, with a Roll back button
 * for a publisher. A save, a text write or a delete loads the release list again, because the folder may now differ from the
 * active release. The diagnostics panel shows the diagnostics of a request that ended. When the server does not
 * answer, a reconnect message shows at the top. Studio checks the server with a
 * back-off, then loads its data again, and an unsaved draft stays on screen. The names that the reference pickers
 * and expression completion offer load with the application too, and again after a save, a text write, a delete and a
 * reconnect. A text write, which a label's Write texts button sends, updates the diagnostics panel and loads the names
 * and the release list again, as a save does. A Resources / Policy matrix switch under the application picker picks the view, kept
 * in the address as `&view=policies`. The policy matrix shows in place of the resource list and the details, and
 * its Open policy link opens the policy in the Resources view. The diagnostics panel shows in both views.
 * Run tests sits beside Validate and shows only when `GET /api/me` answers `studioTest: true`. A selected scenario
 * has a Run button in its details, which runs only that scenario. Both are off while a request runs and while the
 * reconnect message shows. A failed scenario in the test results opens at its failing step, as a diagnostic does.
 * A process step that a diagnostic of the open resource names is marked on its graph.
 * A kind's list also holds the entities, processes, policies or pages of the modules the application uses, from the
 * names, by their `<module>/<name>`, with the module beside each. One opens read-only, with no editor, no Uses and no Used by lists: an
 * entity in the entity designer, a process as its graph alone, a policy as a table of its grants and a page on the page
 * canvas with no palette, data panel, widget tree or moves, each under a read-only notice with the alert role. Its address is the same `&resource=<id>`, so a reload, a shared link and a Uses
 * entry open it too, when the id is not one of the folder's.
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
  // Proposed: `view` and `kind` pick the part of the workspace the sidebar opened; an open resource wins.
  const view: StudioView = parseView(params.get('view'), params.get('kind'))
  const [actionsSlot, setActionsSlot] = useState<HTMLDivElement | null>(null)
  const [apps, setApps] = useState<AppsState>()
  const [folder, setFolder] = useState<FolderState>()
  const [search, setSearch] = useState('')
  const [canPublish, setCanPublish] = useState(false)
  const [canCreate, setCanCreate] = useState(false)
  const [canTest, setCanTest] = useState(false)
  const [previewHost, setPreviewHost] = useState<string | null>(null)
  const [folderRevision, setFolderRevision] = useState(0)
  const [namesState, setNamesState] = useState<NamesState>()
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

  const onlyApplication = Array.isArray(apps) && apps.length === 1 ? apps[0].name : null
  useEffect(() => {
    if (onlyApplication !== null && application === null) {
      setParams({ app: onlyApplication }, { replace: true })
    }
  }, [onlyApplication, application, setParams])

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

  const loaded = folder?.application === application ? folder : undefined
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
  const moduleGroups = useMemo(() => moduleResources(names, search), [names, search])
  const selected = resourceId === null ? undefined : resources.find((item) => item.id === resourceId.toLowerCase())
  const selectedModuleItem =
    resourceId === null || selected !== undefined
      ? undefined
      : moduleResources(names, '')
          .flatMap((group) => group.items)
          .find((item) => item.id.toLowerCase() === resourceId.toLowerCase())

  function pickApplication(name: string) {
    setSearch('')
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

  function onApplicationCreated(name: string) {
    setApps((current) => (Array.isArray(current) ? [...current, { name }] : [{ name }]))
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
        title={t('shell.studio.title')}
        breadcrumb={[{ label: 'Axis', to: '/' }, { label: t('shell.studio.title') }]}
        actions={canCreate ? <StudioNewApplication primary onCreated={onApplicationCreated} /> : undefined}
      >
        <StudioApplicationTiles applications={apps} onPick={pickApplication} />
      </PageLayout>
    )
  }

  const kindView = view.name === 'kind' ? view.kind : (selected?.kind ?? selectedModuleItem?.kind)
  const steps: BreadcrumbStep[] = [
    { label: t('shell.studio.title'), to: '/studio' },
    { label: application, to: `/studio?app=${encodeURIComponent(application)}` },
  ]
  let title = application
  if (selected) {
    if (selected.kind !== 'application') {
      steps.push({
        label: kindLabel(selected.kind),
        to: studioAddress(application, { name: 'kind', kind: selected.kind }),
      })
    }
    title = resourceTitle(selected)
  } else if (selectedModuleItem) {
    steps.push({
      label: kindLabel(selectedModuleItem.kind),
      to: studioAddress(application, { name: 'kind', kind: selectedModuleItem.kind }),
    })
    title = selectedModuleItem.name
  } else if (view.name === 'kind') {
    title = kindLabel(view.kind)
  } else if (view.name === 'policies') {
    title = t('shell.studio.view.policies')
  } else if (view.name === 'releases') {
    title = t('shell.studio.nav.releases')
  }
  if (steps.length > 2 || title !== application) {
    steps.push({ label: title })
  } else {
    steps.pop()
    steps.push({ label: application })
  }

  function kindLabel(kind: string) {
    const key = `shell.studio.kind.${kind}`
    const label = t(key)
    return label === key ? kind : label
  }

  const ofKind = kindView ? resources.filter((item) => item.kind === kindView) : []
  const listed = filterResources(ofKind, search)
  const navigationView: StudioView =
    selected?.kind === 'application'
      ? { name: 'settings' }
      : kindView && (selected || selectedModuleItem || view.name === 'kind')
        ? { name: 'kind', kind: kindView }
        : view

  return (
    <>
      {loaded && 'resources' in loaded && (
        <StudioNavigation
          application={application}
          applications={apps}
          resources={resources}
          view={navigationView}
          canCreate={canCreate}
          onPickApplication={pickApplication}
          onApplicationCreated={onApplicationCreated}
          onView={(next) => {
            setSearch('')
            if (next.name === 'settings') {
              const settings = resources.find((item) => item.kind === 'application')
              setParams(settings ? { app: application, resource: settings.id } : { app: application })
            } else {
              setParams(new URLSearchParams(studioAddress(application, next).split('?')[1]))
            }
          }}
        />
      )}
      <PageLayout title={title} breadcrumb={steps} actions={<div ref={setActionsSlot} style={{ display: 'flex' }} />}>
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
            showHistory={view.name === 'releases' && !selected}
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
                    application={application}
                    item={selectedModuleItem}
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
              <Flex vertical gap="middle">
                <Flex gap="small" align="center">
                  <Input.Search
                    aria-label={t('shell.studio.search')}
                    placeholder={t('shell.studio.search')}
                    allowClear
                    style={{ maxWidth: 360 }}
                    value={search}
                    onChange={(event) => setSearch(event.target.value)}
                  />
                  <div style={{ flex: 1 }} />
                  <StudioNewResource application={application} resources={resources} onCreated={onCreated} />
                </Flex>
                <StudioResourceRows
                  items={listed}
                  extra={moduleGroups.find((group) => group.kind === view.kind)?.items ?? []}
                  onOpen={selectResource}
                />
              </Flex>
            ) : view.name === 'overview' ? (
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
            ) : null}
          </div>
        )}
      </PageLayout>
    </>
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
    default:
      return `/studio?${app}`
  }
}

function parseView(view: string | null, kind: string | null): StudioView {
  if (view === 'kind' && kind) {
    return { name: 'kind', kind }
  }
  if (view === 'policies' || view === 'releases') {
    return { name: view }
  }
  return { name: 'overview' }
}

/** Proposed: Studio's applications as tiles in a three-column grid, each its mark and name, which opens it. */
function StudioApplicationTiles({
  applications,
  onPick,
}: {
  applications: readonly StudioApplication[]
  onPick: (name: string) => void
}) {
  const { colour, spacing, radius } = useDesignTokens()
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: `repeat(auto-fit, minmax(max(0px, calc((100% - ${2 * spacing.md}px) / 3)), 1fr))`,
        gap: spacing.md,
      }}
    >
      {applications.map((app) => (
        <button
          key={app.name}
          type="button"
          data-testid={`studio-application-${app.name}`}
          onClick={() => onPick(app.name)}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: spacing.sm,
            padding: spacing.md,
            background: colour.surface,
            border: `1px solid ${colour.borderSubtle}`,
            borderRadius: radius.base,
            cursor: 'pointer',
            font: 'inherit',
            textAlign: 'start',
          }}
        >
          <SiteMark title={app.name} size={32} />
          <Typography.Text strong style={{ color: colour.primaryText }}>
            {app.name}
          </Typography.Text>
        </button>
      ))}
    </div>
  )
}

/** Proposed: the resources of one kind as rows, each its name and file, which opens it in the editor. */
function StudioResourceRows({
  items,
  extra,
  onOpen,
}: {
  items: readonly StudioResourceItem[]
  extra: readonly { id: string; name: string; module: string | null }[]
  onOpen: (id: string) => void
}) {
  const t = useText()
  const { colour, spacing, radius } = useDesignTokens()
  const rows = [
    ...items.map((item) => ({ id: item.id, name: resourceTitle(item), detail: item.file })),
    ...extra.map((entity) => ({ id: entity.id, name: entity.name, detail: entity.module ?? '' })),
  ]
  if (rows.length === 0) {
    return <Typography.Text type="secondary">{t('shell.studio.noResources')}</Typography.Text>
  }
  return (
    <div style={{ background: colour.surface, border: `1px solid ${colour.borderSubtle}`, borderRadius: radius.base }}>
      {rows.map((row, index) => (
        <button
          key={row.id}
          type="button"
          data-testid={`studio-resource-${row.id}`}
          onClick={() => onOpen(row.id)}
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            gap: spacing.md,
            width: '100%',
            padding: `${spacing.sm}px ${spacing.md}px`,
            background: 'transparent',
            border: 'none',
            borderTop: index === 0 ? 'none' : `1px solid ${colour.divider}`,
            cursor: 'pointer',
            font: 'inherit',
            textAlign: 'start',
          }}
        >
          <Typography.Text style={{ color: colour.primaryText }}>{row.name}</Typography.Text>
          <Typography.Text style={{ color: colour.textMuted }}>{row.detail}</Typography.Text>
        </button>
      ))}
    </div>
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
