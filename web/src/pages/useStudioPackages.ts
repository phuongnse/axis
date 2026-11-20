import { useEffect, useState } from 'react'
import {
  fetchStudioPackageFile,
  fetchStudioPackageFiles,
  ServerUnreachableError,
  type StudioPackageFileItem,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'

/** The file that names a package, see docs/reference/studio-packages.md#the-packages-entry. */
const manifestPath = 'axis-package.json'

/**
 * One package folder under `packages/`. `label`, `language` and `version` come from its manifest. A package whose manifest is
 * missing or is not valid JSON has its id as the label, and no language or version. `files` are the paths in the package.
 */
export interface PackageRow {
  id: string
  label: string
  language: string | null
  version: string | null
  files: string[]
}

/** The packages of one application, or null when they did not load. `application` says which one they were loaded for. */
type PackagesState = { application: string; rows: PackageRow[] | null }

/** The text of a manifest's field, or null when the manifest has none. */
function manifestText(manifest: Record<string, unknown> | null, name: string): string | null {
  const value = manifest?.[name]
  return typeof value === 'string' && value !== '' ? value : null
}

/** The manifest at the package's root, or null when the package has none or it is not a JSON object. */
async function readManifest(
  application: string,
  pkg: string,
  files: readonly string[],
  signal: AbortSignal,
): Promise<Record<string, unknown> | null> {
  if (!files.includes(manifestPath)) {
    return null
  }
  const file = await fetchStudioPackageFile(application, pkg, manifestPath, signal)
  try {
    const manifest: unknown = file === null ? null : JSON.parse(file.content)
    return typeof manifest === 'object' && manifest !== null && !Array.isArray(manifest)
      ? (manifest as Record<string, unknown>)
      : null
  } catch {
    return null
  }
}

/** One row for each package that `items` holds files of, in ordinal order of the id, with its manifest read. */
async function loadRows(
  application: string,
  items: readonly StudioPackageFileItem[],
  signal: AbortSignal,
): Promise<PackageRow[]> {
  const filesOf = new Map<string, string[]>()
  for (const item of items) {
    filesOf.set(item.package, [...(filesOf.get(item.package) ?? []), item.path])
  }
  return Promise.all(
    [...filesOf.keys()].toSorted().map(async (id) => {
      const files = filesOf.get(id)!
      const manifest = await readManifest(application, id, files, signal)
      return {
        id,
        label: manifestText(manifest, 'label') ?? id,
        language: manifestText(manifest, 'language'),
        version: manifestText(manifest, 'version'),
        files: files.toSorted(),
      }
    }),
  )
}

/**
 * The packages of an application's working folder, loaded when the application changes, after a write (`folderRevision`)
 * and after a reconnect. The state is undefined until they load. One loader serves the list and a package's workspace.
 */
export function useStudioPackages(application: string, folderRevision: number): PackagesState | undefined {
  const { generation, lost } = useStudioConnection()
  const [state, setState] = useState<PackagesState>()

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioPackageFiles(application, controller.signal)
      .then((items) => loadRows(application, items, controller.signal))
      .then(
        (rows) => setState({ application, rows }),
        (error: unknown) => {
          if (controller.signal.aborted) {
            return
          }
          if (error instanceof ServerUnreachableError) {
            lost()
            return
          }
          console.warn(`Loading the packages of '${application}' failed`, error)
          setState({ application, rows: null })
        },
      )
    return () => controller.abort()
  }, [application, generation, folderRevision, lost])

  return state?.application === application ? state : undefined
}
