import { solveChallenge } from 'altcha-lib/v1'
import { useCallback, useEffect, useMemo, useRef } from 'react'
import { apiFetch } from '../src/platform/api'

/** The challenge that the control plane serves, see docs/reference/sign-up.md#bots-and-abuse. */
interface Challenge {
  algorithm: string
  challenge: string
  maxnumber?: number
  salt: string
  signature: string
}

interface Solved {
  challenge: Challenge
  number: number
  took: number
}

/** One try at getting a challenge and solving it. `expires` is the Unix time in seconds that the salt names, once it is known. */
interface Attempt {
  solved: Promise<Solved | null>
  expires: number | null
  abort: () => void
}

function expiresOf(salt: string): number | null {
  const expires = Number(new URLSearchParams(salt.split('?')[1] ?? '').get('expires'))
  return Number.isFinite(expires) && expires > 0 ? expires : null
}

function startAttempt(): Attempt {
  const controller = new AbortController()
  const attempt: Attempt = { solved: Promise.resolve(null), expires: null, abort: () => controller.abort() }
  attempt.solved = (async () => {
    try {
      const response = await apiFetch(
        '/api/sign-up/challenge',
        { signal: controller.signal },
        { promptSignInOnUnauthorized: false },
      )
      if (!response.ok) {
        return null
      }
      const challenge = (await response.json()) as Challenge
      attempt.expires = expiresOf(challenge.salt)
      // The solver runs on the page's own thread: its worker variant would need a script that the page's policy does not allow.
      const solver = solveChallenge(challenge.challenge, challenge.salt, challenge.algorithm, challenge.maxnumber)
      controller.signal.addEventListener('abort', () => solver.controller.abort())
      if (controller.signal.aborted) {
        solver.controller.abort()
      }
      const solution = await solver.promise
      return solution ? { challenge, number: solution.number, took: solution.took } : null
    } catch {
      // No challenge or no solution, so the form goes without one and the route then refuses it under /altcha.
      return null
    }
  })()
  return attempt
}

/**
 * Fetches and solves a proof-of-work challenge out of sight while the person types, see
 * docs/reference/sign-up.md#bots-and-abuse. `solution` waits for the solve and answers the text to send as `altcha`, or null
 * when there is no challenge or no solution. It first takes a new challenge when the old one has expired. `renew` drops the
 * current challenge for a new one, which the page calls after a refusal.
 */
export function useProofOfWork() {
  const current = useRef<Attempt | null>(null)

  useEffect(() => {
    current.current = startAttempt()
    return () => current.current?.abort()
  }, [])

  const renew = useCallback(() => {
    current.current?.abort()
    current.current = startAttempt()
  }, [])

  const solution = useCallback(async (): Promise<string | null> => {
    let attempt = current.current ?? startAttempt()
    if (attempt.expires !== null && attempt.expires * 1000 <= Date.now()) {
      attempt.abort()
      attempt = startAttempt()
    }
    current.current = attempt
    const solved = await attempt.solved
    if (!solved) {
      return null
    }
    const { algorithm, challenge, salt, signature } = solved.challenge
    return btoa(JSON.stringify({ algorithm, challenge, number: solved.number, salt, signature, took: solved.took }))
  }, [])

  return useMemo(() => ({ solution, renew }), [solution, renew])
}
