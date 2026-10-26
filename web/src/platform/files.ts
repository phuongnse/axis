import { apiFetch } from './api'
import { parseRecordJson, type FileValue } from './records'
import type { FieldMetadata } from './site'

/** The outcome of an upload: the stored file, or the status the server refused it with. */
export type UploadResult = { ok: true; file: FileValue } | { ok: false; status: number }

/**
 * Uploads a picked file to a file field's upload route as `multipart/form-data` with one part, `file`.
 * The server answers `201` with the file, which is `pending` until a save attaches it. Any other status
 * comes back as `{ ok: false, status }`, so the caller can show the message that fits it.
 */
export async function uploadFile(uploadPath: string, file: File, signal?: AbortSignal): Promise<UploadResult> {
  const body = new FormData()
  body.append('file', file, file.name)
  // No Content-Type: the browser writes it with the multipart boundary.
  const response = await apiFetch(uploadPath, { method: 'POST', body, signal })
  if (response.status !== 201) {
    return { ok: false, status: response.status }
  }
  return { ok: true, file: parseRecordJson<FileValue>(await response.text()) }
}

/** The address that downloads a file, or `null` when the field has no download path. */
export function fileUrl(field: Pick<FieldMetadata, 'downloadPath'>, value: FileValue): string | null {
  return field.downloadPath ? `${field.downloadPath}/${value.id}` : null
}
