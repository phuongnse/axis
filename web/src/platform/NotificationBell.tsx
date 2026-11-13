import {
  BellOutlined,
  CheckOutlined,
  ClockCircleOutlined,
  CloseCircleOutlined,
  InfoCircleOutlined,
} from '@ant-design/icons'
import { Badge, Flex, Pagination, Popover, Tooltip, Typography } from 'antd'
import { AxisAlert } from './AxisAlert'
import { AxisButton } from './AxisButton'
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { Announcement } from './Announcement'
import { formatValue } from './formatValue'
import {
  fetchNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  notificationAddress,
  type NotificationItem,
  type NotificationKind,
  type NotificationPage,
} from './notifications'
import { markCurrentPage } from './pagination'
import { useText } from './texts'
import { hoverItemTint } from './antTheme'
import { formatTimeAgo } from './relativeTime'
import { useDesignTokens, useThemeMode } from './themeMode'

const pageSize = 10

/** The tallest the list grows before it scrolls, about five notifications. */
const listHeight = 480

interface NotificationBellProps {
  /** The address of the application's notification routes. */
  path: string
  /** The path of the site, which a notification's page opens in, or `null` on the platform site. */
  sitePath: string | null
  locale: string
}

/**
 * The notification bell in the header of an application site, and of the platform site for every signed-in person.
 * It shows the signed-in user's unread count and opens a panel with the notifications, newest first, 10 to
 * a page. A row opens its page, if it has one, and marks the notification read. The count reloads when the bell mounts, on every navigation, when the
 * panel opens and after any mark read. The bell renders nothing until the first load succeeds, and after a
 * failed load while the panel is closed. It never shows the sign-in prompt.
 */
export function NotificationBell({ path, sitePath, locale }: NotificationBellProps) {
  const t = useText()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const { size, spacing } = useDesignTokens()
  const [data, setData] = useState<NotificationPage | null>(null)
  const [failed, setFailed] = useState(false)
  const [open, setOpen] = useState(false)
  const [markFailed, setMarkFailed] = useState(false)
  const request = useRef<AbortController | null>(null)
  const panel = useRef<HTMLDivElement>(null)
  const bell = useRef<HTMLButtonElement>(null)

  // The list takes the focus when it opens, so the keyboard goes on into its rows.
  useEffect(() => {
    if (open) {
      requestAnimationFrame(() => panel.current?.focus())
    }
  }, [open])

  // A newer load drops the answer of an older one.
  const load = useCallback(
    (page: number) => {
      request.current?.abort()
      const controller = new AbortController()
      request.current = controller
      return fetchNotifications(path, page, pageSize, controller.signal).then(
        (loaded) => {
          setData(loaded)
          setFailed(false)
        },
        (error: unknown) => {
          if (!controller.signal.aborted) {
            console.warn('Loading the notifications failed', error)
            setFailed(true)
          }
        },
      )
    },
    [path],
  )

  useEffect(() => {
    void load(1)
    return () => request.current?.abort()
  }, [load, pathname])

  if (data === null || (failed && !open)) {
    return null
  }

  const unread = data.unreadCount
  const label = t('shell.notifications.label')
  const count = unread > 0 ? `(${unread} ${t('shell.notifications.unread')})` : undefined
  const named = count === undefined ? label : `${label} ${count}`

  function close() {
    setOpen(false)
    bell.current?.focus()
  }

  function openPanel() {
    setMarkFailed(false)
    setOpen(true)
    void load(1)
  }

  async function openRow(item: NotificationItem) {
    setMarkFailed(false)
    let marked = true
    try {
      await markNotificationRead(path, item.id)
    } catch (error) {
      console.warn('Marking the notification read failed', error)
      marked = false
    }
    // The user asked to open the page, so a failed read does not stop them.
    if (item.open) {
      setOpen(false)
      navigate(notificationAddress(sitePath, item.open))
    } else if (!marked) {
      setMarkFailed(true)
    }
    await load(data?.page ?? 1)
  }

  async function markAllRead() {
    setMarkFailed(false)
    try {
      await markAllNotificationsRead(path)
    } catch (error) {
      console.warn('Marking the notifications read failed', error)
      setMarkFailed(true)
    }
    await load(data?.page ?? 1)
  }

  const list = (
    <div
      ref={panel}
      role="dialog"
      aria-label={label}
      tabIndex={-1}
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          event.stopPropagation()
          close()
        }
      }}
      style={{ width: size.dialog.small, maxWidth: 'calc(100vw - 16px)', outline: 'none' }}
    >
      <Flex
        justify="space-between"
        align="center"
        style={{ padding: `${spacing.xs}px ${spacing.xs}px ${spacing.xs}px ${spacing.md}px` }}
      >
        <Typography.Title level={5} style={{ margin: 0 }}>
          {label}
        </Typography.Title>
        <AxisButton
          role="icon"
          nameKey="shell.notifications.markAllRead"
          icon={<CheckOutlined />}
          disabled={unread === 0}
          onClick={() => void markAllRead()}
        />
      </Flex>
      {(failed || markFailed) && (
        <div style={{ paddingInline: spacing.md, paddingBottom: spacing.xs }}>
          {failed && <AxisAlert kind="error" message={t('shell.notifications.loadFailed')} />}
          {markFailed && <AxisAlert kind="error" message={t('shell.notifications.markFailed')} />}
        </div>
      )}
      {data.items.length === 0 ? (
        !failed && (
          <Typography.Paragraph type="secondary" style={{ margin: 0, padding: `${spacing.sm}px ${spacing.md}px` }}>
            {t('shell.notifications.empty')}
          </Typography.Paragraph>
        )
      ) : (
        <ul
          style={{
            listStyle: 'none',
            margin: 0,
            padding: spacing.xs,
            maxHeight: listHeight,
            overflowY: 'auto',
            overscrollBehavior: 'contain',
          }}
        >
          {data.items.map((item) => (
            <NotificationRow key={item.id} item={item} locale={locale} onOpen={() => void openRow(item)} />
          ))}
        </ul>
      )}
      {data.totalCount > pageSize && (
        <Flex justify="center" style={{ padding: spacing.xs }}>
          <Pagination
            size="small"
            current={data.page}
            pageSize={pageSize}
            total={data.totalCount}
            showSizeChanger={false}
            hideOnSinglePage
            itemRender={markCurrentPage(data.page)}
            onChange={(next) => void load(next)}
          />
        </Flex>
      )}
    </div>
  )

  return (
    <>
      {/* Proposed: the bell is an icon button as the app switcher's is, with its name as its tooltip and a small count
          on its corner, 9+ past nine. It opens its list under it as the header's menus do, with no close button: a
          click outside or Escape closes it. See the review on #3263. */}
      <Popover
        open={open}
        onOpenChange={(next) => (next ? openPanel() : close())}
        trigger="click"
        placement="bottomRight"
        arrow={false}
        content={list}
        styles={{ container: { padding: 0 } }}
      >
        <Badge
          count={unread}
          overflowCount={9}
          size="small"
          offset={[-7, 7]}
          styles={{ indicator: { fontSize: 10, height: 14, minWidth: 14, lineHeight: '14px', paddingInline: 3 } }}
        >
          <AxisButton
            ref={bell}
            role="icon"
            nameKey="shell.notifications.label"
            nameSuffix={count}
            icon={<BellOutlined />}
            aria-haspopup="dialog"
            aria-expanded={open}
          />
        </Badge>
      </Popover>
      <Announcement>{named}</Announcement>
    </>
  )
}

const kindIcons: Record<NotificationKind, ReactNode> = {
  action: <ClockCircleOutlined />,
  info: <InfoCircleOutlined />,
  problem: <CloseCircleOutlined />,
}

/**
 * Proposed: one notification, drawn as a menu's item is, see docs/reference/actions-and-controls.md#menus: the whole
 * row opens its page, `spacing-xs` in from the list's edges with `radius-base` corners, on the hover tint under the
 * pointer. A dot and a bold title mark it unread. Its kind's icon leads it, in `primaryText` when it asks for an
 * action, `dangerText` for a problem and `textMuted` for information, with the kind in words for assistive technology.
 * Its source and how long ago it came head it, the exact time in a tooltip, and its body shows two lines at most. See
 * the review on #3263.
 */
function NotificationRow({ item, locale, onOpen }: { item: NotificationItem; locale: string; onOpen: () => void }) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const { mode } = useThemeMode()
  const [hovered, setHovered] = useState(false)
  const kind = item.kind ?? 'info'
  const kindColour = kind === 'action' ? colour.primaryText : kind === 'problem' ? colour.dangerText : colour.textMuted
  const exact = formatValue({ name: 'createdAt', type: 'date-time' }, item.createdAt, {}, { locale, text: t })
  const ago = formatTimeAgo(item.createdAt, locale)
  // Its parts in the order they show, each a phrase of its own for assistive technology.
  const name = [
    t(`shell.notifications.kind.${kind}`),
    item.source,
    ago,
    ...(item.read ? [] : [t('shell.notifications.unreadItem')]),
    item.subject,
    item.body,
  ].join(', ')
  return (
    <li>
      <button
        type="button"
        aria-label={name}
        onClick={onOpen}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        style={{
          display: 'grid',
          gridTemplateColumns: `${spacing.sm}px auto 1fr`,
          columnGap: spacing.xs,
          width: '100%',
          padding: `${spacing.sm}px ${spacing.sm}px ${spacing.sm}px ${spacing.xs}px`,
          border: 0,
          borderRadius: radius.base,
          background: hovered ? hoverItemTint(mode) : 'transparent',
          color: colour.text,
          font: 'inherit',
          textAlign: 'start',
          cursor: 'pointer',
        }}
      >
        <span aria-hidden style={{ paddingTop: 7 }}>
          {!item.read && (
            <span
              style={{ display: 'block', width: 6, height: 6, borderRadius: '50%', background: colour.primaryText }}
            />
          )}
        </span>
        <span aria-hidden style={{ color: kindColour, paddingTop: 1 }}>
          {kindIcons[kind]}
        </span>
        <span style={{ minWidth: 0 }}>
          <Flex justify="space-between" gap={spacing.sm} style={{ color: colour.textMuted }}>
            <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{item.source}</span>
            <Tooltip title={exact}>
              <time dateTime={item.createdAt} style={{ flex: 'none' }}>
                {ago}
              </time>
            </Tooltip>
          </Flex>
          <span style={{ display: 'block', fontWeight: item.read ? undefined : 600 }}>{item.subject}</span>
          <span
            style={{
              display: '-webkit-box',
              WebkitLineClamp: 2,
              WebkitBoxOrient: 'vertical',
              overflow: 'hidden',
              color: colour.textMuted,
            }}
          >
            {item.body}
          </span>
        </span>
      </button>
    </li>
  )
}
