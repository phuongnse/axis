import { Flex, Radio, Select } from 'antd'
import { useState } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { useText } from '../platform/texts'
import {
  fetchTenantOutsideClients,
  setTenantOutsideClients,
  type TenantOutsideClients,
} from '../platform/tenantOutsideClients'

type Notice = 'saved' | 'saveFailed' | 'notApplied'

const modes: TenantOutsideClients['outsideClients'][] = ['all', 'allowList', 'none']

/**
 * The "Connected apps" tab of the users page, where an administrator chooses which outside MCP clients may connect:
 * All, Allow list or None, with the client ids of the list. The list shows only under Allow list, but Save sends it
 * under every mode, because the server stores it under every mode. Save sends both values with
 * `PUT /api/tenant/outside-clients`, then reads them again, so the tab shows what the server stored, with the ids trimmed
 * and without repeats. A failed save keeps the edited values on screen, and a `502` says that the change is stored and
 * that saving again applies it. The users page shows the tab only when the route answers `200`.
 * See docs/reference/platform-pages.md#connected-apps.
 */
export function ConnectedAppsTab({ initial }: { initial: TenantOutsideClients }) {
  const t = useText()
  const [mode, setMode] = useState(initial.outsideClients)
  const [clients, setClients] = useState(initial.allowedClients)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<Notice>()

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
      <AxisFieldGroup title={t('shell.users.connectedApps.mode')} help={t('shell.users.connectedApps.modeHelp')}>
        <Radio.Group value={mode} onChange={(event) => setMode(event.target.value)}>
          <Flex vertical>
            {modes.map((value) => (
              <Radio key={value} value={value}>
                {t(`shell.users.connectedApps.${value}`)}
              </Radio>
            ))}
          </Flex>
        </Radio.Group>
      </AxisFieldGroup>
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
    </Flex>
  )
}
