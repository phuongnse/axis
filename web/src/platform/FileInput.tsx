import { Button, Flex, Typography } from 'antd'
import { useRef, useState, type ChangeEvent } from 'react'
import { fileUrl, uploadFile } from './files'
import type { FileValue } from './records'
import type { FieldMetadata } from './site'
import { useText } from './texts'
import { visuallyHidden } from './visuallyHidden'

interface FileLinkProps {
  field: Pick<FieldMetadata, 'downloadPath'>
  value: FileValue
}

/** A stored file's name as a link that downloads it, or as plain text when the field has no download path. */
export function FileLink({ field, value }: FileLinkProps) {
  const url = fileUrl(field, value)
  return url === null ? (
    <span>{value.name}</span>
  ) : (
    <Typography.Link href={url} download={value.name}>
      {value.name}
    </Typography.Link>
  )
}

interface FileInputProps {
  /** The file field to choose a file for. */
  field: FieldMetadata
  /** The file the field holds, or `null` when it is empty. */
  value: FileValue | null
  onChange: (value: FileValue | null) => void
  /** The id of the upload button, which the field's label points at. */
  id?: string
  /** The field's label, in the accessible names of the buttons. */
  label: string
  /** Shows the file as a link only, with no button. */
  readOnly?: boolean
}

// The text key of the message that fits the status the server refused an upload with.
function errorKey(status: number): string {
  switch (status) {
    case 413:
      return 'shell.form.fileTooLarge'
    case 415:
      return 'shell.form.fileTypeRefused'
    default:
      return 'shell.form.uploadFailed'
  }
}

/**
 * The input of a file field: the current file's name as a download link, an Upload button and, when
 * the field is not required, a Remove button. Picking a file uploads it at once and passes the stored
 * file to `onChange`, and the save then sends its id. A file above the field's `maxSize` is refused
 * in the browser with the message of a `413`. A refused upload shows its message under the buttons.
 * A polite live region announces each upload and removal. A field without an upload path, such as a
 * module entity's, shows no Upload button.
 */
export function FileInput({ field, value, onChange, id, label, readOnly = false }: FileInputProps) {
  const t = useText()
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [announcement, setAnnouncement] = useState('')
  const picker = useRef<HTMLInputElement>(null)
  const upload = useRef<HTMLButtonElement>(null)

  const pick = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    // The same file can be picked again after a remove or a refusal.
    event.target.value = ''
    if (!file || !field.uploadPath) {
      return
    }
    setError(null)
    if (field.maxSize != null && file.size > field.maxSize) {
      setError(t('shell.form.fileTooLarge'))
      return
    }
    setUploading(true)
    try {
      const result = await uploadFile(field.uploadPath, file)
      if (result.ok) {
        onChange(result.file)
        setAnnouncement(`${t('shell.form.uploaded')} ${result.file.name}`)
      } else {
        setError(t(errorKey(result.status)))
      }
    } catch {
      setError(t('shell.form.uploadFailed'))
    } finally {
      setUploading(false)
    }
  }

  const remove = () => {
    if (value === null) {
      return
    }
    setError(null)
    onChange(null)
    setAnnouncement(`${t('shell.form.removed')} ${value.name}`)
    upload.current?.focus()
  }

  const editable = !readOnly
  const canUpload = editable && !!field.uploadPath
  return (
    <div data-testid={`file-${field.name}`}>
      <Flex align="center" gap="small" wrap>
        {value !== null && <FileLink field={field} value={value} />}
        {canUpload && (
          <>
            <input
              ref={picker}
              type="file"
              hidden
              tabIndex={-1}
              accept={field.accept?.join(',')}
              data-testid={`file-picker-${field.name}`}
              onChange={(event) => void pick(event)}
            />
            <Button
              id={id}
              ref={upload}
              loading={uploading}
              aria-label={`${t('shell.form.upload')} ${label}`}
              onClick={() => picker.current?.click()}
            >
              {t('shell.form.upload')}
            </Button>
          </>
        )}
        {editable && !field.required && value !== null && (
          <Button aria-label={`${t('shell.form.removeFile')} ${label}`} onClick={remove}>
            {t('shell.form.removeFile')}
          </Button>
        )}
      </Flex>
      {error !== null && (
        <span role="alert" data-testid={`file-error-${field.name}`}>
          <Typography.Text type="danger">{error}</Typography.Text>
        </span>
      )}
      <div role="status" aria-live="polite" style={visuallyHidden} data-testid={`file-${field.name}-announcement`}>
        {announcement}
      </div>
    </div>
  )
}
