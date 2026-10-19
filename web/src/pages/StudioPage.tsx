import { Alert, Button, Descriptions, Flex, Input, Result, Select, Tag, Typography } from 'antd'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import { forbidden } from '../platform/inspection'
import {
  fetchStudioApps,
  fetchStudioDiagnostics,
  fetchStudioResources,
  filterResources,
  groupByKind,
  resourceTitle,
  ServerUnreachableError,
  type StudioApplication,
  type StudioDiagnostic,
  type StudioResourceItem,
  waitForServer,
} from '../platform/studio'
import { StudioConnectionContext, useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { StudioEditor } from './StudioEditor'
import { StudioReferences } from './StudioReferences'

/** The applications Studio may edit, or why they could not be listed. */
type AppsState = StudioApplication[] | typeof forbidden | 'failed'

/** What one application's folder holds, or `failed`. `application` says which one it was loaded for. */
type FolderState =
  | { application: string; resources: StudioResourceItem[]; diagnostics: StudioDiagnostic[] }
  | { application: string; failed: true }

/**
 * Studio, the authoring page. The address holds the picked application and the selected resource, as
 * `/studio?app=PurchaseRequests&resource=<id>`. A person without the author role sees a forbidden page.
 * With one application, it is picked at once. The resources are grouped by kind, and a search narrows them
 * by name or id. The diagnostics panel lists the folder's diagnostics, loaded when the application is
 * picked. A diagnostic that names a resource selects it, clears the search, and opens it at the diagnostic's path,
 * as a Used by entry does. A selected resource opens in an editor below its details, with its Uses and Used by
 * lists under the editor. A Used by entry opens its resource at the path of the reference, as `&path=`. After a
 * save, the diagnostics panel shows the diagnostics that the save answered. After a delete, the resource leaves the
 * list. When the server does not answer, a reconnect message shows at the top. Studio checks the server with a
 * back-off, then loads its data again, and an unsaved draft stays on screen.
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
  const [apps, setApps] = useState<AppsState>()
  const [folder, setFolder] = useState<FolderState>()
  const [search, setSearch] = useState('')

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

  const loaded = folder?.application === application ? folder : undefined
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
    return <Alert data-testid="studio-no-applications" type="info" message={t('shell.studio.noApplications')} />
  }

  return (
    <Flex vertical gap="middle">
      <Typography.Title level={2}>{t('shell.studio.title')}</Typography.Title>
      <Select
        aria-label={t('shell.studio.application')}
        placeholder={t('shell.studio.application')}
        style={{ maxWidth: 360 }}
        value={application}
        options={apps.map((app) => ({ value: app.name, label: app.name }))}
        onChange={pickApplication}
      />
      {application !== null && loaded && 'failed' in loaded && (
        <Alert data-testid="studio-load-error" type="error" message={t('shell.studio.loadFailed')} />
      )}
      {loaded && 'resources' in loaded && (
        <>
          <Flex gap="large" align="flex-start">
            <Flex vertical gap="small" style={{ width: 320, flex: 'none' }}>
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
                  <StudioEditor
                    key={`editor-${selected.id}`}
                    application={application!}
                    item={selected}
                    path={path}
                    onSaved={(diagnostics) => setFolder({ ...loaded, diagnostics })}
                    onDeleted={(diagnostics) => {
                      setFolder({
                        ...loaded,
                        resources: loaded.resources.filter((item) => item.id !== selected.id),
                        diagnostics,
                      })
                      setParams({ app: application! })
                    }}
                  />
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
