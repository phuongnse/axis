// The icon names are Axis's, the closed list of a site's navigation `icon`. Only this map knows the icon library (D58).
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
  TagOutlined,
  TeamOutlined,
  ToolOutlined,
  UnorderedListOutlined,
  UserOutlined,
} from '@ant-design/icons'

/** The outlined icon of each Axis icon name. */
export const navigationIcons = Object.freeze({
  home: HomeOutlined,
  dashboard: DashboardOutlined,
  list: UnorderedListOutlined,
  inbox: InboxOutlined,
  tasks: CheckSquareOutlined,
  form: FormOutlined,
  document: FileTextOutlined,
  folder: FolderOutlined,
  chart: BarChartOutlined,
  process: ApartmentOutlined,
  people: TeamOutlined,
  person: UserOutlined,
  calendar: CalendarOutlined,
  clock: ClockCircleOutlined,
  money: DollarOutlined,
  cart: ShoppingCartOutlined,
  tag: TagOutlined,
  device: LaptopOutlined,
  tool: ToolOutlined,
  support: CustomerServiceOutlined,
  mail: MailOutlined,
  settings: SettingOutlined,
})

/** An Axis icon name. */
export type NavigationIconName = keyof typeof navigationIcons

/** The neutral icon of an entry that has no icon, or whose name is not in the list. */
export const pageIcon = FileOutlined

/** The icon of an Axis icon name, or the page icon when the name is missing or unknown. */
export function navigationIcon(name: string | null | undefined) {
  return name != null && Object.hasOwn(navigationIcons, name) ? navigationIcons[name as NavigationIconName] : pageIcon
}
