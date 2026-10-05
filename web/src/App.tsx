import { PageContainer, ProCard, ProLayout } from '@ant-design/pro-components'
import { Badge, Switch } from 'antd'
import { useServerStatus, type ServerStatus } from './platform/serverStatus'
import { useThemeMode } from './platform/themeMode'

const statusBadge: Record<ServerStatus, { status: 'processing' | 'success' | 'error'; text: string }> = {
  checking: { status: 'processing', text: 'Checking' },
  ready: { status: 'success', text: 'Ready' },
  unavailable: { status: 'error', text: 'Unavailable' },
}

export function App() {
  const { mode, toggleMode } = useThemeMode()
  const serverStatus = useServerStatus()
  const badge = statusBadge[serverStatus]

  return (
    <ProLayout
      title="Axis"
      logo={false}
      layout="top"
      navTheme={mode === 'dark' ? 'realDark' : 'light'}
      menuDataRender={() => []}
      actionsRender={() => [
        <Switch
          key="theme"
          aria-label="Dark mode"
          checked={mode === 'dark'}
          checkedChildren="Dark"
          unCheckedChildren="Light"
          onChange={toggleMode}
        />,
      ]}
    >
      <PageContainer title="Welcome to Axis">
        <ProCard title="Server status">
          <Badge data-testid="server-status" status={badge.status} text={badge.text} />
        </ProCard>
      </PageContainer>
    </ProLayout>
  )
}
