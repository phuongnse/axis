import { Flex, Input, Modal, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { DialogFooter } from '../platform/DialogFooter'
import { AxisField } from '../platform/AxisField'
import { useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { CellJobTable } from '../platform/CellJobTable'
import {
  cellTenantStatusTones,
  downloadCellTenantExport,
  fetchCellDeletedTenant,
  fetchCellJobs,
  fetchCellTenant,
  queueCellTenantJob,
  requestCellTenantHost,
  type CellDeletedTenant,
  type CellJob,
  type CellQuota,
  type CellTenantAction,
  type CellTenantDetail,
} from '../platform/cell'
import { EmptyDash } from '../platform/EmptyValue'
import { saveFile } from '../platform/exports'
import { HostRequestDialog } from '../platform/HostRequestDialog'
import { HostRequestTable } from '../platform/HostRequestTable'
import { PageActions, PageLayout } from '../platform/PageLayout'
import { newIdempotencyKey } from '../platform/processes'
import { recordPageSize } from '../platform/tableQuery'
import { SectionPart, SectionParts } from '../platform/SectionParts'
import { StatusTag } from '../platform/StatusTag'
import { TermList } from '../platform/TermList'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useFollowCellJob } from '../platform/useFollowCellJob'
import { useTableView } from '../platform/useTableView'
import { usePlatform } from './context'
import { useCellConsole } from './cellConsoleContext'
import { CellTenantQuotasDialog } from './CellTenantQuotasDialog'
import { CellTenantUsage } from './CellTenantUsage'
import { NotFoundPage } from './NotFoundPage'

interface QuotaRow extends CellQuota {
  key: string
}

/** The columns the Host verification view's filter can name, see docs/reference/cell-console.md#views. */
const hostRequestFields = [
  { name: 'host', type: 'text' },
  { name: 'state', type: 'enum' },
  { name: 'expiresAt', type: 'date' },
] as const
/** The columns the Quotas view's filter can name, see docs/reference/cell-console.md#views. */
const quotaFields = [
  { name: 'key', type: 'enum' },
  { name: 'value', type: 'integer' },
  { name: 'source', type: 'enum' },
] as const
// The two tables have no parameters of their own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

/** The texts of each action, by their full keys. */
const actionTexts = {
  suspend: {
    button: 'shell.cell.tenant.suspend',
    confirm: 'shell.cell.tenant.suspendConfirm',
    body: 'shell.cell.tenant.suspendBody',
    ok: 'shell.cell.tenant.suspendOk',
  },
  resume: {
    button: 'shell.cell.tenant.resume',
    confirm: 'shell.cell.tenant.resumeConfirm',
    body: 'shell.cell.tenant.resumeBody',
    ok: 'shell.cell.tenant.resumeOk',
  },
  export: {
    button: 'shell.cell.tenant.export',
    confirm: 'shell.cell.tenant.exportConfirm',
    body: 'shell.cell.tenant.exportBody',
    ok: 'shell.cell.tenant.exportOk',
  },
  delete: {
    button: 'shell.cell.tenant.delete',
    confirm: 'shell.cell.tenant.deleteConfirm',
    body: 'shell.cell.tenant.deleteBody',
    ok: 'shell.cell.tenant.deleteOk',
  },
  'cancel-deletion': {
    button: 'shell.cell.tenant.cancelDeletion',
    confirm: 'shell.cell.tenant.cancelDeletionConfirm',
    body: 'shell.cell.tenant.cancelDeletionBody',
    ok: 'shell.cell.tenant.cancelDeletionOk',
  },
} as const

/**
 * The Tenant view of the cell console, headed by the tenant's id with its status as a tag, its kind as the line and its
 * actions on the right, see docs/reference/shell.md#page-header. Its parts are a rule apart, see
 * docs/reference/shell.md#layout: the facts of the application and the hosts, then Host verification with the state of each host that was asked for,
 * Quotas with the ones it overrides, Usage of the last 13 months by day in the table of the users page's "Usage" section, paged on the server from the tenant's own usage days route,
 * and Jobs in the shared job table. A `deleting` tenant shows its purge date after its status. An id that is no tenant
 * but a purged one shows only Deleted, the date of the purge, the date its deletion is complete and its jobs, with no
 * action, see docs/reference/cell-console.md#views. An id that is neither shows the not-found page. An `active` or `readOnly` tenant offers
 * Suspend and a `suspended` or `readOnly` one offers Resume. Any tenant but a `provisioning` one offers Export. An `active`,
 * `readOnly` or `suspended` tenant offers Delete, which stays off until the operator types the tenant id exactly, and a
 * `deleting` one offers Cancel deletion in place of Suspend, Resume and Delete. Each action is behind a confirmation. An
 * action queues a job with an `Idempotency-Key`, which stays for a retry after a network error or a 5xx and goes with a
 * final answer. The page then follows the job every 2 seconds until it ends and reloads the tenant and its jobs. With a
 * succeeded `export` job among the jobs, Download opens a warning that the ZIP is restricted data, and only accepting it
 * fetches the ZIP. Set quotas and Add a host open their own dialogs, show for every status and queue no job: the quota
 * table shows the quotas that the cell answered. Once Add a host is answered, the page loads the tenant again, so the
 * new host shows as pending. Host verification and Quotas each filter their own rows on every column. Their filters are
 * kept in the address under `hostRequests.` and `quotas.`, see docs/reference/cell-console.md#views.
 */
export function CellTenantPage() {
  const t = useText()
  const { size } = useDesignTokens()
  const { locale } = usePlatform().current
  const { onRefused } = useCellConsole()
  const { id = '' } = useParams()
  const [tenant, setTenant] = useState<CellTenantDetail | null>()
  // The purged tenant, read only when the id is no tenant. `null` is an id that was never a tenant.
  const [deleted, setDeleted] = useState<CellDeletedTenant | null>()
  const [tenantFailed, setTenantFailed] = useState(false)
  const [jobs, setJobs] = useState<CellJob[]>()
  const [jobsFailed, setJobsFailed] = useState(false)
  const [openDialog, setOpenDialog] = useState<'quotas' | 'host' | null>(null)
  const [confirming, setConfirming] = useState<CellTenantAction | null>(null)
  // The text typed to confirm a deletion. It starts empty whenever a confirmation opens.
  const [typed, setTyped] = useState('')
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [downloading, setDownloading] = useState(false)
  const [downloadError, setDownloadError] = useState<string | null>(null)
  // The job to follow. Setting it again to the same id does not restart the check, so a queued job sets it once.
  const [following, setFollowing] = useState<string | null>(null)
  // Counts the jobs that ended, so the tenant and its jobs load again.
  const [reload, setReload] = useState(0)
  // The Idempotency-Key of each action's request that got no final answer, by tenant and action, so a retry cannot
  // queue a second job.
  const actionKeys = useRef(new Map<string, string>())
  // The two tables keep their filters under prefixes of their own, so each link opens the same rows.
  const hostRequestView = useTableView(
    hostRequestFields,
    recordPageSize,
    noParameters,
    false,
    true,
    false,
    'hostRequests.',
  )
  const quotaView = useTableView(quotaFields, recordPageSize, noParameters, false, true, false, 'quotas.')

  useEffect(() => {
    const controller = new AbortController()
    fetchCellTenant(id, controller.signal)
      .then((result) => {
        if (result.kind !== 'ok') {
          onRefused(result.kind)
          return
        }
        setTenant(result.value)
        if (result.value !== null) {
          setDeleted(undefined)
          return
        }
        return fetchCellDeletedTenant(id, controller.signal).then((purged) => {
          if (purged.kind === 'ok') {
            setDeleted(purged.value)
          } else {
            onRefused(purged.kind)
          }
        })
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the cell tenant failed', error)
          setTenantFailed(true)
        }
      })
    // The tenant's 50 newest jobs, see docs/reference/cell-console.md#views.
    fetchCellJobs(new URLSearchParams({ tenant: id, pageSize: '50' }).toString(), controller.signal).then(
      (result) => {
        if (result.kind === 'ok') {
          setJobs(result.value.items)
        } else {
          onRefused(result.kind)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the cell tenant jobs failed', error)
          setJobsFailed(true)
        }
      },
    )
    return () => controller.abort()
  }, [id, onRefused, reload])

  useFollowCellJob(following, {
    onJob: (job) => setJobs((current) => current?.map((item) => (item.id === job.id ? job : item))),
    onEnd: () => {
      setFollowing(null)
      setReload((count) => count + 1)
    },
    onRefused,
  })

  if (tenant === null && deleted === null) {
    return <NotFoundPage />
  }

  async function runAction(action: CellTenantAction) {
    const key = `${id}/${action}`
    setBusy(true)
    setActionError(null)
    try {
      // A key is kept only while its request has no final answer: a network error or a 5xx.
      const idempotencyKey = actionKeys.current.get(key) ?? newIdempotencyKey()
      actionKeys.current.set(key, idempotencyKey)
      let result
      try {
        result = await queueCellTenantJob(id, action, idempotencyKey)
      } catch (error: unknown) {
        console.warn(`Queuing the ${action} job of the cell tenant '${id}' failed`, error)
        setActionError(t('shell.cell.tenant.actionFailed'))
        return
      }
      actionKeys.current.delete(key)
      if (result.kind === 'ok') {
        setConfirming(null)
        setJobs((current) => [result.value, ...(current ?? []).filter((item) => item.id !== result.value.id)])
        setFollowing(result.value.id)
      } else if (result.kind === 'problem') {
        setActionError(t('shell.cell.tenant.actionRefused').replace('{title}', result.title))
      } else {
        onRefused(result.kind)
      }
    } finally {
      setBusy(false)
    }
  }

  async function runDownload(exportId: string) {
    setBusy(true)
    setDownloadError(null)
    try {
      let result
      try {
        result = await downloadCellTenantExport(id, exportId)
      } catch (error: unknown) {
        console.warn(`Downloading the export of the cell tenant '${id}' failed`, error)
        setDownloadError(t('shell.cell.tenant.actionFailed'))
        return
      }
      if (result.kind === 'ok') {
        saveFile(result.value.blob, result.value.fileName)
        setDownloading(false)
      } else if (result.kind === 'problem') {
        setDownloadError(t('shell.cell.tenant.actionRefused').replace('{title}', result.title))
      } else {
        onRefused(result.kind)
      }
    } finally {
      setBusy(false)
    }
  }

  const canSuspend = tenant?.status === 'active' || tenant?.status === 'readOnly'
  const canResume = tenant?.status === 'suspended' || tenant?.status === 'readOnly'
  const canExport = tenant !== undefined && tenant !== null && tenant.status !== 'provisioning'
  // The newest succeeded export job, as the jobs come newest first. Its id is the export id.
  const ready = jobs?.find((job) => job.kind === 'export' && job.state === 'succeeded')
  const canDelete = tenant?.status === 'active' || tenant?.status === 'readOnly' || tenant?.status === 'suspended'
  const canCancelDeletion = tenant?.status === 'deleting'

  function openConfirmation(action: CellTenantAction) {
    setActionError(null)
    setTyped('')
    setConfirming(action)
  }

  const when = (time: string) => new Date(time).toLocaleString(locale)
  const quotas: QuotaRow[] = tenant ? Object.entries(tenant.quotas).map(([key, quota]) => ({ key, ...quota })) : []
  // A quota with no limit is empty for the number filter, as the table shows no number for it.
  const quotaColumns: AxisColumn<QuotaRow>[] = [
    {
      key: 'key',
      kind: 'fixed',
      title: t('shell.cell.tenant.quota.key'),
      render: (_value, row) => t(`shell.cell.tenant.quota.${row.key}`),
      filter: {
        kind: 'enum',
        value: (row) => row.key,
        options: quotas.map((row) => ({ value: row.key, label: t(`shell.cell.tenant.quota.${row.key}`) })),
      },
    },
    {
      key: 'value',
      kind: 'fixed',
      title: t('shell.cell.tenant.quota.value'),
      render: (_value, row) =>
        row.value === null ? t('shell.cell.tenant.quota.noLimit') : row.value.toLocaleString(locale),
      filter: { kind: 'number', value: (row) => row.value },
    },
    {
      key: 'source',
      kind: 'fixed',
      title: t('shell.cell.tenant.quota.source'),
      render: (_value, row) =>
        t(row.overridden ? 'shell.cell.tenant.quota.overridden' : 'shell.cell.tenant.quota.default'),
      filter: {
        kind: 'enum',
        value: (row) => (row.overridden ? 'overridden' : 'default'),
        options: [
          { value: 'overridden', label: t('shell.cell.tenant.quota.overridden') },
          { value: 'default', label: t('shell.cell.tenant.quota.default') },
        ],
      },
    },
  ]

  return (
    <PageLayout
      title={id}
      tags={
        tenant ? (
          <StatusTag tone={cellTenantStatusTones[tenant.status]}>
            {t(`shell.cell.tenants.status.${tenant.status}`)}
          </StatusTag>
        ) : deleted ? (
          <StatusTag tone="muted">{t('shell.cell.tenant.deleted')}</StatusTag>
        ) : null
      }
      line={t('shell.cell.tenant.kind')}
    >
      {tenantFailed && (
        <AxisAlert data-testid="cell-tenant-error" kind="error" message={t('shell.cell.tenant.loadFailed')} />
      )}
      {tenant && (
        <PageActions>
          <Flex gap="small">
            {canSuspend && (
              <AxisButton
                role="secondary"
                data-testid="cell-tenant-suspend"
                disabled={following !== null}
                onClick={() => openConfirmation('suspend')}
              >
                {t(actionTexts.suspend.button)}
              </AxisButton>
            )}
            {canResume && (
              <AxisButton
                role="secondary"
                data-testid="cell-tenant-resume"
                disabled={following !== null}
                onClick={() => openConfirmation('resume')}
              >
                {t(actionTexts.resume.button)}
              </AxisButton>
            )}
            {canExport && (
              <AxisButton
                role="secondary"
                data-testid="cell-tenant-export"
                disabled={following !== null}
                onClick={() => openConfirmation('export')}
              >
                {t(actionTexts.export.button)}
              </AxisButton>
            )}
            {canDelete && (
              <AxisButton
                role="secondary"
                data-testid="cell-tenant-delete"
                disabled={following !== null}
                onClick={() => openConfirmation('delete')}
              >
                {t(actionTexts.delete.button)}
              </AxisButton>
            )}
            {canCancelDeletion && (
              <AxisButton
                role="secondary"
                data-testid="cell-tenant-cancel-deletion"
                disabled={following !== null}
                onClick={() => openConfirmation('cancel-deletion')}
              >
                {t(actionTexts['cancel-deletion'].button)}
              </AxisButton>
            )}
            {ready && (
              <AxisButton
                role="secondary"
                data-testid="cell-tenant-download"
                disabled={following !== null}
                onClick={() => {
                  setDownloadError(null)
                  setDownloading(true)
                }}
              >
                {t('shell.cell.tenant.download')}
              </AxisButton>
            )}
            <AxisButton role="secondary" data-testid="cell-tenant-set-quotas" onClick={() => setOpenDialog('quotas')}>
              {t('shell.cell.tenant.setQuotas')}
            </AxisButton>
            <AxisButton role="secondary" data-testid="cell-tenant-add-host" onClick={() => setOpenDialog('host')}>
              {t('shell.cell.tenant.addHost')}
            </AxisButton>
          </Flex>
        </PageActions>
      )}
      <SectionParts>
        {tenant && (
          <SectionPart testId="cell-tenant">
            <TermList
              terms={[
                ...(tenant.status === 'deleting' && tenant.deleteAfter
                  ? [
                      {
                        key: 'deleteAfter',
                        term: t('shell.cell.tenant.deleteAfter'),
                        value: when(tenant.deleteAfter),
                      },
                    ]
                  : []),
                {
                  key: 'application',
                  term: t('shell.cell.tenant.application'),
                  value: tenant.application ?? <EmptyDash />,
                },
                {
                  key: 'hosts',
                  term: t('shell.cell.tenant.hosts'),
                  value:
                    tenant.hosts.length > 0 ? (
                      new Intl.ListFormat(locale, { type: 'conjunction' }).format(tenant.hosts)
                    ) : (
                      <EmptyDash />
                    ),
                },
              ]}
            />
          </SectionPart>
        )}
        {deleted && (
          <SectionPart testId="cell-tenant-deleted">
            <TermList
              terms={[
                { key: 'purgedAt', term: t('shell.cell.tenant.purgedAt'), value: when(deleted.deletedAt) },
                { key: 'completeAt', term: t('shell.cell.tenant.completeAt'), value: when(deleted.completeAt) },
              ]}
            />
          </SectionPart>
        )}
        {tenant && (
          <SectionPart testName="cell-tenant-host-requests" heading={{ title: t('shell.cell.tenant.hostRequests') }}>
            <HostRequestTable
              requests={tenant.hostRequests}
              texts="shell.cell.tenant"
              locale={locale}
              testId="cell-tenant-host-requests"
              view={{ query: hostRequestView.view, onChange: hostRequestView.setView }}
            />
          </SectionPart>
        )}
        {tenant && (
          <SectionPart testName="cell-tenant-quotas" heading={{ title: t('shell.cell.tenant.quotas') }}>
            <AxisTable<QuotaRow>
              testId="cell-tenant-quotas"
              rowKey={(row) => row.key}
              dataSource={quotas}
              columns={quotaColumns}
              view={{ query: quotaView.view, onChange: quotaView.setView }}
              emptyText={t('shell.cell.tenant.noQuotas')}
            />
          </SectionPart>
        )}
        {tenant && (
          <SectionPart testName="cell-tenant-usage" heading={{ title: t('shell.cell.tenant.usage') }}>
            <CellTenantUsage tenant={id} locale={locale} />
          </SectionPart>
        )}
        <SectionPart testName="cell-tenant-jobs" heading={{ title: t('shell.cell.tenant.jobs') }}>
          {jobsFailed && (
            <AxisAlert
              data-testid="cell-tenant-jobs-error"
              kind="error"
              message={t('shell.cell.tenant.jobsLoadFailed')}
            />
          )}
          {jobs && <CellJobTable jobs={jobs} locale={locale} />}
        </SectionPart>
      </SectionParts>
      {tenant && openDialog === 'quotas' && (
        <CellTenantQuotasDialog
          tenant={id}
          quotas={tenant.quotas}
          onSaved={(quotas) => setTenant((current) => current && { ...current, quotas })}
          onRefused={onRefused}
          onClose={() => setOpenDialog(null)}
        />
      )}
      {openDialog === 'host' && (
        <HostRequestDialog
          texts="shell.cell.tenant"
          title={t('shell.cell.tenant.addHostTitle').replace('{tenant}', id)}
          locale={locale}
          testId="cell-tenant-host"
          request={(host) => requestCellTenantHost(id, host)}
          refusal={({ title }) => t('shell.cell.tenant.actionRefused').replace('{title}', title)}
          onRefused={onRefused}
          onAdded={() => setReload((count) => count + 1)}
          onClose={() => setOpenDialog(null)}
        />
      )}
      {confirming && (
        <Modal
          open
          width={size.dialog.small}
          data-testid="cell-tenant-confirm"
          title={t(actionTexts[confirming].confirm).replace('{tenant}', id)}
          footer={
            <DialogFooter
              destructive={confirming === 'suspend' || confirming === 'delete'}
              finishText={t(actionTexts[confirming].ok)}
              cancelText={t(
                confirming === 'cancel-deletion' ? 'shell.cell.tenant.cancelDeletionClose' : 'shell.users.roles.cancel',
              )}
              finishing={busy}
              finishDisabled={confirming === 'delete' && typed !== id}
              onFinish={() => void runAction(confirming)}
              onCancel={() => setConfirming(null)}
            />
          }
          onCancel={() => setConfirming(null)}
        >
          <Flex vertical gap="small">
            {actionError && <AxisAlert data-testid="cell-tenant-action-error" kind="error" message={actionError} />}
            <Typography.Text>{t(actionTexts[confirming].body)}</Typography.Text>
            {confirming === 'delete' && (
              <AxisField
                label={t('shell.cell.tenant.deleteTypeId').replace('{tenant}', id)}
                help={t('shell.cell.tenant.deleteTypeId.help')}
              >
                <Input
                  data-testid="cell-tenant-delete-id"
                  value={typed}
                  autoComplete="off"
                  onChange={(event) => setTyped(event.target.value)}
                />
              </AxisField>
            )}
          </Flex>
        </Modal>
      )}
      {downloading && ready && (
        <Modal
          open
          width={size.dialog.small}
          data-testid="cell-tenant-download-warning"
          title={t('shell.cell.tenant.downloadConfirm').replace('{tenant}', id)}
          footer={
            <DialogFooter
              finishText={t('shell.cell.tenant.downloadOk')}
              cancelText={t('shell.users.roles.cancel')}
              finishing={busy}
              onFinish={() => void runDownload(ready.id)}
              onCancel={() => setDownloading(false)}
            />
          }
          onCancel={() => setDownloading(false)}
        >
          <Flex vertical gap="small">
            {downloadError && (
              <AxisAlert data-testid="cell-tenant-download-error" kind="error" message={downloadError} />
            )}
            <Typography.Text>{t('shell.cell.tenant.downloadBody')}</Typography.Text>
          </Flex>
        </Modal>
      )}
    </PageLayout>
  )
}
