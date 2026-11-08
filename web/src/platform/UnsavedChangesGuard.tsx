import { Modal } from 'antd'
import { useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import { useBlocker } from 'react-router'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import {
  BlockerContext,
  leavesWithoutAsking,
  onlyTableViewChanged,
  RegisterContext,
  type Register,
} from './unsavedChanges'

/**
 * The one guard that asks before unsaved changes are left, see docs/reference/studio.md#editor. It needs the data router
 * of docs/reference/frontend.md#routing. It blocks every move to another address while an editor is counted in, except one that only changes a table's view, and
 * cancels the browser's reload and close with its own question.
 */
export function UnsavedChangesGuard({ children }: { children: ReactNode }): ReactNode {
  const counted = useRef(0)
  const [unsaved, setUnsaved] = useState(false)
  const blocker = useBlocker(
    ({ currentLocation, nextLocation, historyAction }) =>
      counted.current > 0 &&
      !(historyAction !== 'POP' && leavesWithoutAsking(nextLocation.state)) &&
      currentLocation.pathname + currentLocation.search !== nextLocation.pathname + nextLocation.search &&
      !onlyTableViewChanged(currentLocation, nextLocation),
  )

  useEffect(() => {
    if (!unsaved) {
      return
    }
    const ask = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener('beforeunload', ask)
    return () => window.removeEventListener('beforeunload', ask)
  }, [unsaved])

  const register = useCallback<Register>(() => {
    counted.current += 1
    setUnsaved(true)
    return () => {
      counted.current -= 1
      setUnsaved(counted.current > 0)
    }
  }, [])
  return (
    <RegisterContext.Provider value={register}>
      <BlockerContext.Provider value={blocker}>{children}</BlockerContext.Provider>
    </RegisterContext.Provider>
  )
}

/** The question of the guard, "Leave without saving?". It sits inside the text provider, beside the guard's children. */
export function LeaveWithoutSaving(): ReactNode {
  const t = useText()
  const { size } = useDesignTokens()
  const blocker = useContext(BlockerContext)
  return (
    <Modal
      open={blocker?.state === 'blocked'}
      width={size.dialog.small}
      title={t('shell.studio.leave.title')}
      okText={t('shell.studio.leave.leave')}
      okButtonProps={{ danger: true }}
      cancelText={t('shell.studio.leave.stay')}
      onOk={() => blocker?.state === 'blocked' && blocker.proceed()}
      onCancel={() => blocker?.state === 'blocked' && blocker.reset()}
    />
  )
}
