import { useCallback, useEffect, useState } from 'react'
import { apiFetch } from '../src/platform/api'
import type { SignUpLocale } from './texts'

/** A legal document with its version and its address in each language. */
export interface SignUpDocument {
  version: string
  urls: Record<SignUpLocale, string>
}

/** The answer of `GET /api/sign-up/documents`, see docs/reference/sign-up.md#the-documents. */
export interface SignUpDocuments {
  terms: SignUpDocument
  dpa: SignUpDocument
  privacy: SignUpDocument
  licencesUrl: string
}

/**
 * The region's documents once they load, undefined while they first load or when they fail. `reload` fetches them again and
 * keeps the last ones shown until the new ones arrive.
 */
export function useSignUpDocuments(): { documents: SignUpDocuments | undefined; reload: () => void } {
  const [documents, setDocuments] = useState<SignUpDocuments>()
  const [loads, setLoads] = useState(0)
  useEffect(() => {
    const controller = new AbortController()
    apiFetch('/api/sign-up/documents', { signal: controller.signal }, { promptSignInOnUnauthorized: false })
      .then(async (response) => {
        if (response.ok) {
          setDocuments((await response.json()) as SignUpDocuments)
        }
      })
      .catch(() => {
        // The pages name the documents as plain text, and the legal line shows no links.
      })
    return () => controller.abort()
  }, [loads])
  const reload = useCallback(() => setLoads((count) => count + 1), [])
  return { documents, reload }
}
