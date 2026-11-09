import { useEffect, useState } from 'react'
import { apiFetch } from '../src/platform/api'
import type { SignUpLocale } from './texts'

/** Proposed: a document the person accepts or is told of, at its current version, with its address in each language. */
export interface SignUpDocument {
  version: string
  urls: Record<SignUpLocale, string>
}

/** Proposed: the answer of `GET /api/sign-up/documents`, the region's current legal documents. */
export interface SignUpDocuments {
  terms: SignUpDocument
  dpa: SignUpDocument
  privacy: SignUpDocument
  licencesUrl: string
}

/**
 * The region's legal documents, `null` until they load or when they fail, and a function that loads them again, such
 * as after a sign-up was refused for a version that changed. Without them a page names the documents as plain text.
 */
export function useSignUpDocuments(): [SignUpDocuments | null, () => void] {
  const [documents, setDocuments] = useState<SignUpDocuments | null>(null)
  const [load, setLoad] = useState(0)
  useEffect(() => {
    const controller = new AbortController()
    apiFetch('/api/sign-up/documents', { signal: controller.signal }, { promptSignInOnUnauthorized: false })
      .then(async (response) => {
        if (response.ok) {
          setDocuments((await response.json()) as SignUpDocuments)
        }
      })
      .catch(() => {
        // The page names the documents as plain text, and the legal line leaves out their links.
      })
    return () => controller.abort()
  }, [load])
  return [documents, () => setLoad((count) => count + 1)]
}
