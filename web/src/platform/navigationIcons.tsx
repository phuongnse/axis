import {
  ApartmentOutlined,
  BarChartOutlined,
  CalendarOutlined,
  CheckSquareOutlined,
  ClockCircleOutlined,
  CustomerServiceOutlined,
  DashboardOutlined,
  DollarOutlined,
  FileOutlined,
  FileTextOutlined,
  FolderOutlined,
  FormOutlined,
  HomeOutlined,
  InboxOutlined,
  LaptopOutlined,
  MailOutlined,
  SettingOutlined,
  ShoppingCartOutlined,
  TagsOutlined,
  TeamOutlined,
  ToolOutlined,
  UnorderedListOutlined,
  UserOutlined,
} from '@ant-design/icons'
import type { ReactNode } from 'react'

// Proposed: configuration names a navigation icon by an Axis name from this closed list, never by an Ant Design
// name, so the UI library stays a detail of the SPA (D58). Validation would refuse a name outside the list.
const icons: Record<string, ReactNode> = {
  home: <HomeOutlined />,
  dashboard: <DashboardOutlined />,
  list: <UnorderedListOutlined />,
  inbox: <InboxOutlined />,
  tasks: <CheckSquareOutlined />,
  form: <FormOutlined />,
  document: <FileTextOutlined />,
  folder: <FolderOutlined />,
  chart: <BarChartOutlined />,
  process: <ApartmentOutlined />,
  people: <TeamOutlined />,
  person: <UserOutlined />,
  calendar: <CalendarOutlined />,
  clock: <ClockCircleOutlined />,
  money: <DollarOutlined />,
  cart: <ShoppingCartOutlined />,
  tag: <TagsOutlined />,
  device: <LaptopOutlined />,
  tool: <ToolOutlined />,
  support: <CustomerServiceOutlined />,
  mail: <MailOutlined />,
  settings: <SettingOutlined />,
}

/** The icon of a navigation entry, or a neutral page icon when the entry names none. It is hidden from assistive technology. */
export function navigationIcon(name: string | null | undefined): ReactNode {
  return <span aria-hidden>{(name && icons[name]) ?? <FileOutlined />}</span>
}
