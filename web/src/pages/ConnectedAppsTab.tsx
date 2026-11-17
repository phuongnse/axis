import { Flex, Modal, Select, Typography } from 'antd'
import dayjs from 'dayjs'
import { useEffect, useState } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { AxisChoiceField } from '../platform/AxisChoiceField'
import { AxisField } from '../platform/AxisField'
import { AxisTable } from '../platform/AxisTable'
import { FilledText } from '../platform/FilledText'
import { recordPageSize } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import {
  fetchConnectedApps,
  fetchTenantOutsideClients,
  revokeConnectedApp,
  setTenantOutsideClients,
  type TenantConnectedApp,
  type TenantOutsideClients,
} from '../platform/tenantOutsideClients'
import { useDesignTokens } from '../platform/themeMode'
import { useTableView } from '../platform/useTableView'
import { usePlatform } from './context'

type Notice = 'saved' | 'saveFailed' | 'notApplied'

type RevokeNotice = 'revokeNotFinished' | 'revokeFailed'

const modes: TenantOutsideClients['outsideClients'][] = ['all', 'allowList', 'none']

const noParameters: string[] = []

const appFields = [
  { name: 'name', type: 'text' },
  { name: 'clientId', type: 'text' },
  { name: 'people', type: 'text' },
  { name: 'lastUsedAt', type: 'date' },
] as const

/** The names and email addresses of the people who consented, joined, or null when no person has either. */
function peopleText(app: TenantConnectedApp): string | null {
  return (
    app.people
      .flatMap((person) => [person.displayName, person.email])
      .filter(Boolean)
      .join(', ') || null
  )
}

/**
 * The "Connected apps" tab of the users page, where an administrator chooses which outside MCP clients may connect:
 * All, Allow list or None, with the client ids of the list. The list shows only under Allow list, but Save sends it
 * under every mode, because the server stores it under every mode. Save sends both values with
 * `PUT /api/tenant/outside-clients`, then reads them again, so the tab shows what the server stored, with the ids trimmed
 * and without repeats. A failed save keeps the edited values on screen, and a `502` says that the change is stored and
 * that saving again applies it. Each option has its own help. Below the choice, a table lists the apps that people consented to, read with
 * `GET /api/tenant/connected-apps`: the name, the client ID, the people who consented and the last use. The table
 * filters its own rows, kept in the address under `connectedApps.`, see docs/reference/actions-and-controls.md#tables.
 * Each row has a Revoke button that asks for a confirmation naming the client, then sends
 * `POST /api/tenant/connected-apps/{clientId}/revoke` and reads the list again. A failed revoke keeps the row, and a
 * `502` says that revoking again finishes it. A list that does not load shows a message and leaves the choice working.
 * The users page shows the tab only when the outside clients route answers `200`.
 * See docs/reference/platform-pages.md#connected-apps.
 */
export function ConnectedAppsTab({ initial }: { initial: TenantOutsideClients }) {
  const t = useText()
  const { size } = useDesignTokens()
  const { locale } = usePlatform().current
  const [mode, setMode] = useState(initial.outsideClients)
  const [clients, setClients] = useState(initial.allowedClients)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<Notice>()
  const [apps, setApps] = useState<TenantConnectedApp[]>()
  const [appsFailed, setAppsFailed] = useState(false)
  // Counts the successful revokes, so each one loads the list again.
  const [reloads, setReloads] = useState(0)
  const [pending, setPending] = useState<TenantConnectedApp | null>(null)
  const [revoking, setRevoking] = useState(false)
  const [revokeNotice, setRevokeNotice] = useState<RevokeNotice>()
  const appsView = useTableView(appFields, recordPageSize, noParameters, false, true, false, 'connectedApps.')

  useEffect(() => {
    const controller = new AbortController()
    fetchConnectedApps(controller.signal).then(
      (answer) => {
        if (!controller.signal.aborted) {
          setApps(answer ?? undefined)
          setAppsFailed(answer === null)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the connected apps failed', error)
          setAppsFailed(true)
        }
      },
    )
    return () => controller.abort()
  }, [reloads])

  async function revoke() {
    if (!pending) {
      return
    }
    setRevoking(true)
    setRevokeNotice(undefined)
    try {
      const result = await revokeConnectedApp(pending.clientId)
      if (result === 'ok') {
        setReloads((count) => count + 1)
      } else {
        setRevokeNotice(result === 'notFinished' ? 'revokeNotFinished' : 'revokeFailed')
      }
    } catch (error: unknown) {
      console.warn('Revoking the consent failed', error)
      setRevokeNotice('revokeFailed')
    } finally {
      setRevoking(false)
      setPending(null)
    }
  }

  async function save() {
    setBusy(true)
    setNotice(undefined)
    try {
      const result = await setTenantOutsideClients({ outsideClients: mode, allowedClients: clients })
      if (result === 'ok') {
        const stored = await fetchTenantOutsideClients()
        if (stored) {
          setMode(stored.outsideClients)
          setClients(stored.allowedClients)
        }
        setNotice('saved')
      } else {
        setNotice(result === 'notApplied' ? 'notApplied' : 'saveFailed')
      }
    } catch (error: unknown) {
      console.warn('Saving the outside clients setting failed', error)
      setNotice('saveFailed')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Flex vertical gap="middle" data-testid="connected-apps">
      <AxisChoiceField
        label={t('shell.users.connectedApps.mode')}
        help={t('shell.users.connectedApps.modeHelp')}
        value={mode}
        onChange={(value) => {
          const next = modes.find((candidate) => candidate === value)
          if (next) {
            setMode(next)
          }
        }}
        options={modes.map((value) => ({
          value,
          label: t(`shell.users.connectedApps.${value}`),
          help: t(`shell.users.connectedApps.${value}Help`),
        }))}
      />
      {mode === 'allowList' && (
        <AxisField label={t('shell.users.connectedApps.clients')} help={t('shell.users.connectedApps.clientsHelp')}>
          <Select
            mode="tags"
            virtual={false}
            open={false}
            style={{ minWidth: 200 }}
            value={clients}
            onChange={(next: string[]) => setClients(next.filter((id) => id.trim() !== ''))}
          />
        </AxisField>
      )}
      {notice && (
        <AxisAlert
          data-testid="connected-apps-notice"
          kind={notice === 'saved' ? 'success' : 'error'}
          message={t(`shell.users.connectedApps.${notice}`)}
        />
      )}
      <div>
        <AxisButton role="finish" loading={busy} onClick={() => void save()}>
          {t('shell.users.connectedApps.save')}
        </AxisButton>
      </div>
      <Typography.Title level={3}>{t('shell.users.connectedApps.apps')}</Typography.Title>
      <AxisTable<TenantConnectedApp>
        testId="connected-apps-list"
        rowKey={(app) => app.clientId}
        view={{ query: appsView.view, onChange: appsView.setView, paging: false }}
        loading={apps === undefined && !appsFailed}
        dataSource={apps ?? []}
        emptyText={t('shell.users.connectedApps.empty')}
        failure={
          appsFailed ? (
            <AxisAlert
              data-testid="connected-apps-load-error"
              kind="error"
              message={t('shell.users.connectedApps.loadFailed')}
            />
          ) : undefined
        }
        below={
          revokeNotice && (
            <AxisAlert
              data-testid="connected-apps-revoke-notice"
              kind="error"
              message={t(`shell.users.connectedApps.${revokeNotice}`)}
            />
          )
        }
        rowActions={{
          configured: 1,
          actions: (app) => [
            {
              key: 'revoke',
              labelKey: 'shell.users.connectedApps.revoke',
              destructive: true,
              onClick: () => {
                setRevokeNotice(undefined)
                setPending(app)
              },
            },
          ],
        }}
        columns={[
          {
            key: 'name',
            kind: 'freeText',
            title: t('shell.users.connectedApps.name'),
            render: (_value, app) => app.name,
            filter: { kind: 'text', value: (app) => app.name },
          },
          {
            key: 'clientId',
            kind: 'freeText',
            title: t('shell.users.connectedApps.clientId'),
            render: (_value, app) => app.clientId,
            filter: { kind: 'text', value: (app) => app.clientId },
          },
          {
            key: 'people',
            kind: 'freeText',
            title: t('shell.users.connectedApps.people'),
            render: (_value, app) => (
              <Flex vertical>
                {app.people.map((person) => (
                  <Flex key={person.userId} vertical>
                    <span>{person.displayName}</span>
                    {person.email && <Typography.Text type="secondary">{person.email}</Typography.Text>}
                  </Flex>
                ))}
              </Flex>
            ),
            filter: { kind: 'text', value: peopleText },
          },
          {
            key: 'lastUsedAt',
            kind: 'fixed',
            title: t('shell.users.connectedApps.lastUsedAt'),
            render: (_value, app) => (app.lastUsedAt ? new Date(app.lastUsedAt).toLocaleString(locale) : null),
            filter: {
              kind: 'date',
              value: (app) => (app.lastUsedAt ? dayjs(app.lastUsedAt).format('YYYY-MM-DD') : null),
            },
          },
        ]}
      />
      <Modal
        open={pending !== null}
        width={size.dialog.small}
        title={t('shell.users.connectedApps.revokeConfirm')}
        okText={t('shell.users.connectedApps.revokeOk')}
        cancelText={t('shell.users.roles.cancel')}
        okButtonProps={{ danger: true }}
        confirmLoading={revoking}
        onOk={() => void revoke()}
        onCancel={() => setPending(null)}
      >
        {pending && (
          <FilledText
            text={t('shell.users.connectedApps.revokeEffect')}
            parts={{ client: <strong>{pending.name ?? pending.clientId}</strong> }}
          />
        )}
      </Modal>
    </Flex>
  )
}
