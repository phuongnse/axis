import { Alert, Button, Descriptions, Flex, Input, Result, Select, Tag, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import { forbidden } from '../platform/inspection'
import {
  fetchStudioApps,
  fetchStudioDiagnostics,
  fetchStudioResources,
  filterResources,
  groupByKind,
  resourceTitle,
  type StudioApplication,
  type StudioDiagnostic,
  type StudioResourceItem,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { StudioEditor } from './StudioEditor'

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
 * picked. A diagnostic that names a resource selects it, and clears the search so the resource is listed.
 * A selected resource opens in an editor below its details. After a save, the diagnostics panel shows
 * the diagnostics that the save answered.
 */
export function StudioPage() {
  const t = useText()
  const [params, setParams] = useSearchParams()
  const application = params.get('app')
  const resourceId = params.get('resource')
  const [apps, setApps] = useState<AppsState>()
  const [folder, setFolder] = useState<FolderState>()
  const [search, setSearch] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioApps(controller.signal).then(setApps, (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the Studio applications failed', error)
        setApps('failed')
      }
    })
    return () => controller.abort()
  }, [])

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
        if (!controller.signal.aborted) {
          console.warn(`Loading the Studio folder of '${application}' failed`, error)
          setFolder({ application, failed: true })
        }
      },
    )
    return () => controller.abort()
  }, [application])

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

  function selectDiagnostic(id: string) {
    setSearch('')
    selectResource(id)
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
                    ]}
                  />
                  <StudioEditor
                    key={selected.id}
                    application={application!}
                    item={selected}
                    onSaved={(diagnostics) => setFolder({ ...loaded, diagnostics })}
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
  onSelect: (resourceId: string) => void
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
    <Button type="text" style={{ height: 'auto', textAlign: 'left' }} onClick={() => onSelect(resourceId)}>
      {content}
    </Button>
  )
}
