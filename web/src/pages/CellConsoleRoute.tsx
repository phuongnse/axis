import { Alert, Result, Typography } from 'antd'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Outlet, useLocation } from 'react-router'
import { cellSignInUrl, isSignInCardAddress, startSignIn } from '../platform/api'
import { fetchCellSession, type CellSession } from '../platform/cell'
import { assignLocation } from '../platform/navigation'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { SignInCard } from './HomePage'
import { PlatformShellRoute } from './PlatformShellRoute'
import type { CellConsoleContext } from './cellConsoleContext'

/** What the session read, or a refusal that a page reported later. `null` is a failed read. */
type Access = { kind: 'ok'; session: CellSession } | { kind: 'unauthorized' } | { kind: 'forbidden' } | null

function OperatorName({ session }: { session: CellSession }) {
  const { colour } = useDesignTokens()
  return (
    <Typography.Text data-testid="cell-account" style={{ color: colour.textMuted }}>
      {session.displayName}
    </Typography.Text>
  )
}

function Refused({ kind }: { kind: 'unauthorized' | 'forbidden' | 'failed' }) {
  const t = useText()
  if (kind === 'forbidden') {
    return (
      <div data-testid="cell-forbidden">
        <Result status="403" title={t('shell.cell.forbidden')} />
      </div>
    )
  }
  if (kind === 'failed') {
    return <Alert data-testid="cell-error" type="error" title={t('shell.cell.loadFailed')} />
  }
  return <SignInCard state="refused" onSignIn={() => startSignIn(cellSignInUrl)} />
}

/**
 * The layout route of the cell console's pages. It reads the operator's session once. Without a session the browser goes
 * to the console's sign-in, except on the sign-in card's address, where the card shows instead so that a refused sign-in
 * does not loop. A person without the operator role sees the forbidden page. A signed-in operator sees the pages in the
 * shell, with their display name in the header. A page reports a refusal of its own call through the outlet context.
 */
export function CellConsoleRoute() {
  const { pathname, search } = useLocation()
  const [access, setAccess] = useState<Access | undefined>()
  const onCard = isSignInCardAddress(pathname, search)
  const redirecting = access?.kind === 'unauthorized' && !onCard

  useEffect(() => {
    const controller = new AbortController()
    fetchCellSession(controller.signal).then(
      (result) => setAccess(result.kind === 'ok' ? { kind: 'ok', session: result.value } : result),
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the cell session failed', error)
          setAccess(null)
        }
      },
    )
    return () => controller.abort()
  }, [])

  useEffect(() => {
    if (redirecting) {
      assignLocation(cellSignInUrl(pathname + search))
    }
  }, [redirecting, pathname, search])

  const onRefused = useCallback((kind: 'unauthorized' | 'forbidden') => setAccess({ kind }), [])
  const context = useMemo<CellConsoleContext>(() => ({ onRefused }), [onRefused])

  if (access === undefined || redirecting) {
    return null
  }
  if (access?.kind === 'ok') {
    return (
      <PlatformShellRoute account={<OperatorName session={access.session} />}>
        <Outlet context={context} />
      </PlatformShellRoute>
    )
  }
  return (
    <PlatformShellRoute>
      <Refused kind={access === null ? 'failed' : access.kind} />
    </PlatformShellRoute>
  )
}
