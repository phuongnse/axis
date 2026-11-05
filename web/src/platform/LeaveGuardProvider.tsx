import { Modal, Typography } from 'antd'
import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { dialogWidth } from './dialogButtons'
import { LeaveGuardContext } from './leaveGuard'
import { useText } from './texts'

/**
 * Proposed: the one guard on leaving unsaved changes, for every page of the shell. An editor with unsaved changes counts
 * itself in through `useUnsavedChanges`, which `SaveBar` calls, so every editor with a save bar is guarded. Then a click
 * on any link in the shell, the sidebar and the header among them, or a navigation an editor starts through `useGuard`,
 * asks "Leave without saving?" first, with Stay and Leave. A reload or closing the tab asks the browser's own question.
 * A link that opens a new tab or window is not asked about, as the page stays. The browser's Back is not guarded: the
 * router of the SPA is not a data router, so it cannot block it.
 */
export function LeaveGuardProvider({ children }: { children: ReactNode }) {
  const t = useText()
  const navigate = useNavigate()
  const held = useRef(0)
  const [pending, setPending] = useState<(() => void) | null>(null)

  const hold = useCallback(() => {
    held.current += 1
    return () => {
      held.current -= 1
    }
  }, [])
  const guard = useCallback((go: () => void) => {
    if (held.current > 0) {
      setPending(() => go)
    } else {
      go()
    }
  }, [])

  const value = useMemo(() => ({ hold, guard }), [hold, guard])

  return (
    <LeaveGuardContext.Provider value={value}>
      <div
        style={{ display: 'contents' }}
        onClickCapture={(event) => {
          if (
            held.current === 0 ||
            event.button !== 0 ||
            event.metaKey ||
            event.ctrlKey ||
            event.shiftKey ||
            event.altKey
          ) {
            return
          }
          const link = (event.target as Element).closest?.('a[href]')
          if (!(link instanceof HTMLAnchorElement) || (link.target !== '' && link.target !== '_self')) {
            return
          }
          const to = new URL(link.href, window.location.href)
          if (to.origin !== window.location.origin || to.href === window.location.href) {
            return
          }
          // The link's own navigation never runs, so the router moves only once the person chooses Leave.
          event.preventDefault()
          event.stopPropagation()
          setPending(() => () => navigate(`${to.pathname}${to.search}${to.hash}`))
        }}
      >
        {children}
      </div>
      <Modal
        open={pending !== null}
        width={dialogWidth('small')}
        title={t('shell.leaveGuard.title')}
        okText={t('shell.leaveGuard.leave')}
        okButtonProps={{ danger: true }}
        cancelText={t('shell.leaveGuard.stay')}
        onOk={() => {
          const go = pending
          setPending(null)
          go?.()
        }}
        onCancel={() => setPending(null)}
      >
        <Typography.Text>{t('shell.leaveGuard.body')}</Typography.Text>
      </Modal>
    </LeaveGuardContext.Provider>
  )
}
