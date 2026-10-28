import { BellOutlined } from '@ant-design/icons'
import { Alert, Badge, Button, Drawer, Flex, Pagination, Typography } from 'antd'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { formatValue } from './formatValue'
import {
  fetchNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  notificationAddress,
  type NotificationItem,
  type NotificationPage,
} from './notifications'
import { markCurrentPage } from './pagination'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { visuallyHidden } from './visuallyHidden'

const pageSize = 10

interface NotificationBellProps {
  /** The address of the application's notification routes. */
  path: string
  /** The path of the site, which a notification's page opens in. */
  sitePath: string
  locale: string
}

/**
 * The notification bell in the header of an application site. It shows the signed-in user's unread count
 * and opens a panel with the notifications, newest first, 10 to a page. A row opens its page, if it has one,
 * and marks the notification read. The count reloads when the bell mounts, on every navigation, when the
 * panel opens and after any mark read. The bell renders nothing until the first load succeeds, and after a
 * failed load while the panel is closed. It never shows the sign-in prompt.
 */
export function NotificationBell({ path, sitePath, locale }: NotificationBellProps) {
  const t = useText()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const { colour, spacing, radius } = useDesignTokens()
  const [data, setData] = useState<NotificationPage | null>(null)
  const [failed, setFailed] = useState(false)
  const [open, setOpen] = useState(false)
  const [markFailed, setMarkFailed] = useState(false)
  const request = useRef<AbortController | null>(null)

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
  const named = unread > 0 ? `${label}, ${unread} ${t('shell.notifications.unread')}` : label

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

  return (
    <>
      <Button type="text" aria-label={named} aria-haspopup="dialog" onClick={openPanel}>
        <Badge count={unread} size="small" offset={[2, -2]}>
          <BellOutlined aria-hidden />
        </Badge>
      </Button>
      <span role="status" aria-live="polite" style={visuallyHidden}>
        {named}
      </span>
      <Drawer title={label} open={open} onClose={() => setOpen(false)} size="default">
        <Flex vertical gap={spacing.md}>
          {failed && <Alert type="error" showIcon title={t('shell.notifications.loadFailed')} />}
          {markFailed && <Alert type="error" showIcon title={t('shell.notifications.markFailed')} />}
          <Button disabled={unread === 0} onClick={markAllRead} style={{ alignSelf: 'flex-start' }}>
            {t('shell.notifications.markAllRead')}
          </Button>
          {data.items.length === 0 ? (
            !failed && <Typography.Text type="secondary">{t('shell.notifications.empty')}</Typography.Text>
          ) : (
            <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
              {data.items.map((item) => (
                <li key={item.id} style={{ borderBottom: `1px solid ${colour.borderSubtle}` }}>
                  <button
                    type="button"
                    onClick={() => void openRow(item)}
                    style={{
                      display: 'block',
                      width: '100%',
                      padding: spacing.sm,
                      border: 0,
                      borderRadius: radius.base,
                      background: 'transparent',
                      color: colour.text,
                      font: 'inherit',
                      textAlign: 'start',
                      cursor: 'pointer',
                    }}
                  >
                    <Flex gap={spacing.xs} align="center">
                      {!item.read && <Badge status="processing" />}
                      <Typography.Text strong>{item.subject}</Typography.Text>
                      {!item.read && <span style={visuallyHidden}>{t('shell.notifications.unreadItem')}</span>}
                    </Flex>
                    <div>{item.body}</div>
                    <Typography.Text type="secondary">
                      {formatValue({ name: 'createdAt', type: 'date-time' }, item.createdAt, {}, { locale, text: t })}
                    </Typography.Text>
                  </button>
                </li>
              ))}
            </ul>
          )}
          {data.totalCount > pageSize && (
            <Pagination
              current={data.page}
              pageSize={pageSize}
              total={data.totalCount}
              showSizeChanger={false}
              hideOnSinglePage
              itemRender={markCurrentPage(data.page)}
              onChange={(next) => void load(next)}
            />
          )}
        </Flex>
      </Drawer>
    </>
  )
}
