import { Alert, Button, Descriptions, Flex, Input, Result, Segmented, Select, Tag, Typography } from 'antd'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import { fetchCurrentUser } from '../platform/currentUser'
import { forbidden } from '../platform/inspection'
import {
  fetchStudioApps,
  fetchStudioDiagnostics,
  fetchStudioNames,
  fetchStudioResources,
  filterResources,
  groupByKind,
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
import { StudioNewApplication } from './StudioNewApplication'
import { StudioNewResource } from './StudioNewResource'
import { StudioPolicyMatrix } from './StudioPolicyMatrix'
import { StudioPublishing } from './StudioPublishing'
import { StudioReferences } from './StudioReferences'

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
      {reconnecting && <Alert data-testid="studio-reconnecting" type="info" message={t('shell.studio.reconnecting')} />}
      <StudioWorkspace />
    </StudioConnectionContext.Provider>
  )
}

function StudioWorkspace() {
  const t = useText()
  const { generation, lost } = useStudioConnection()
  const [params, setParams] = useSearchParams()
  const application = params.get('app')
  const resourceId = params.get('resource')
  const path = params.get('path')
  const policiesView = params.get('view') === 'policies'
  const [apps, setApps] = useState<AppsState>()
  const [folder, setFolder] = useState<FolderState>()
  const [search, setSearch] = useState('')
  const [canPublish, setCanPublish] = useState(false)
  const [canCreate, setCanCreate] = useState(false)
  const [previewHost, setPreviewHost] = useState<string | null>(null)
  const [folderRevision, setFolderRevision] = useState(0)
  const [namesState, setNamesState] = useState<NamesState>()

  useEffect(() => {
    const controller = new AbortController()
    fetchCurrentUser(controller.signal).then(
      (user) => {
        setCanPublish(user?.studioPublisher === true)
        setCanCreate(user?.studioCreate === true)
        setPreviewHost(user?.studioPreviewHost ?? null)
      },
      () => {
        if (!controller.signal.aborted) {
          setCanPublish(false)
          setCanCreate(false)
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
  const groups = useMemo(() => groupByKind(filterResources(resources, search)), [resources, search])
  const selected = resourceId === null ? undefined : resources.find((item) => item.id === resourceId.toLowerCase())

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
    return <Alert data-testid="studio-load-error" type="error" message={t('shell.studio.loadFailed')} />
  }
  if (apps.length === 0) {
    return (
      <Flex vertical gap="middle">
        <Alert data-testid="studio-no-applications" type="info" message={t('shell.studio.noApplications')} />
        {canCreate && <StudioNewApplication onCreated={onApplicationCreated} />}
      </Flex>
    )
  }

  return (
    <Flex vertical gap="middle">
      <Typography.Title level={2}>{t('shell.studio.title')}</Typography.Title>
      <Flex gap="small">
        <Select
          aria-label={t('shell.studio.application')}
          placeholder={t('shell.studio.application')}
          style={{ maxWidth: 360 }}
          value={application}
          options={apps.map((app) => ({ value: app.name, label: app.name }))}
          onChange={pickApplication}
        />
        {canCreate && <StudioNewApplication onCreated={onApplicationCreated} />}
      </Flex>
      {application !== null && loaded && 'resources' in loaded && (
        <StudioPublishing
          key={application}
          application={application}
          canPublish={canPublish}
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
        />
      )}
      {application !== null && loaded && 'resources' in loaded && (
        <Segmented
          aria-label={t('shell.studio.view')}
          value={policiesView ? 'policies' : 'resources'}
          options={[
            { value: 'resources', label: t('shell.studio.view.resources') },
            { value: 'policies', label: t('shell.studio.view.policies') },
          ]}
          onChange={(value) =>
            setParams(value === 'policies' ? { app: application, view: 'policies' } : { app: application })
          }
        />
      )}
      {application !== null && loaded && 'failed' in loaded && (
        <Alert data-testid="studio-load-error" type="error" message={t('shell.studio.loadFailed')} />
      )}
      {loaded && 'resources' in loaded && (
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
                {groups.length === 0 && (
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
              </Flex>
              <div data-testid="studio-resource-details" style={{ flex: 1 }}>
                {selected ? (
                  <Flex vertical gap="middle">
                    <Descriptions
                      title={resourceTitle(selected)}
                      column={1}
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
                        onSaved={(diagnostics) => {
                          setFolder({ ...loaded, diagnostics })
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
