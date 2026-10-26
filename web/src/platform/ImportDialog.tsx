import { Alert, Button, Flex, Modal, Typography, Upload } from 'antd'
import { useEffect, useRef, useState } from 'react'
import {
  commitImport,
  fetchImport,
  importContentType,
  importErrorRows,
  uploadImport,
  type ImportError,
} from './imports'
import type { FieldMetadata } from './site'
import { useText } from './texts'

/** How long the dialog waits between two checks of an import. */
const pollDelay = 1000

type Phase = 'idle' | 'uploading' | 'checking' | 'checked' | 'committing' | 'failed'

interface ImportDialogProps {
  /** The imports route of the table's entity. */
  importsPath: string
  /** The table's fields, whose labels name the fields of the errors. */
  fields: readonly FieldMetadata[]
  onClose: () => void
  /** Called once the server has committed the import, so the caller reads its rows again. */
  onCommitted: () => void
}

/**
 * The dialog that imports records from a CSV or xlsx file. The user picks one file and presses Check,
 * which uploads it with the content type of its extension. The dialog reads the import at once and
 * then every second, with no time limit, until it is `checked` or `failed`. It lists each error as
 * its row and the label of its field, or as a row, column or file error. Commit shows only when the
 * import is `checked`. It sends the commit and checks every second until the import is `committed`,
 * which calls `onCommitted`, or `failed`, which lists its errors. Picking another file starts a new
 * import. Closing the dialog stops the checking. The status and the errors sit in an `aria-live`
 * region, and the picker and every button take the keyboard.
 */
export function ImportDialog({ importsPath, fields, onClose, onCommitted }: ImportDialogProps) {
  const t = useText()
  const [file, setFile] = useState<File | null>(null)
  const [phase, setPhase] = useState<Phase>('idle')
  const [importId, setImportId] = useState<string | null>(null)
  const [errors, setErrors] = useState<ImportError[]>([])
  const [problem, setProblem] = useState<string | null>(null)
  // The import to check, and whether it waits for its commit.
  const [watch, setWatch] = useState<{ id: string; commit: boolean } | null>(null)
  const committedHandler = useRef(onCommitted)
  useEffect(() => {
    committedHandler.current = onCommitted
  }, [onCommitted])

  useEffect(() => {
    if (watch === null) {
      return
    }
    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout>

    async function check() {
      try {
        const result = await fetchImport(importsPath, watch!.id, controller.signal)
        if (!result.ok) {
          setProblem(t('shell.table.importUploadFailed'))
          setPhase(watch!.commit ? 'checked' : 'idle')
          return
        }
        const { state, errors: found } = result.import
        // A queued commit leaves the import checked until the worker ends it.
        if (state === 'pending' || (watch!.commit && state === 'checked')) {
          timer = setTimeout(() => void check(), pollDelay)
        } else if (state === 'checked') {
          setPhase('checked')
        } else if (state === 'failed') {
          setErrors(importErrorRows(found))
          setPhase('failed')
        } else {
          committedHandler.current()
        }
      } catch (error) {
        if (controller.signal.aborted) {
          return
        }
        console.warn(`Checking the import '${watch!.id}' failed`, error)
        setProblem(t('shell.table.importUploadFailed'))
        setPhase(watch!.commit ? 'checked' : 'idle')
      }
    }

    void check()
    return () => {
      controller.abort()
      clearTimeout(timer)
    }
  }, [watch, importsPath, t])

  // Picking a file, or removing it, drops the import of the file before.
  const pick = (next: File | null) => {
    setFile(next)
    setPhase('idle')
    setImportId(null)
    setWatch(null)
    setErrors([])
    setProblem(null)
  }

  // An upload has its own message for a 403, 413 and 415. Any other refusal is a generic failure.
  const refuse = (status: number, uploading: boolean) =>
    setProblem(
      uploading && status === 403
        ? t('shell.table.importDenied')
        : uploading && status === 413
          ? t('shell.table.importTooLarge')
          : uploading && status === 415
            ? t('shell.table.importWrongType')
            : t('shell.table.importUploadFailed'),
    )

  const upload = async () => {
    const contentType = file === null ? null : importContentType(file)
    if (file === null || contentType === null) {
      return
    }
    setPhase('uploading')
    setProblem(null)
    setErrors([])
    try {
      const result = await uploadImport(importsPath, file, contentType)
      if (!result.ok) {
        refuse(result.status, true)
        setPhase('idle')
        return
      }
      setImportId(result.import.id)
      setPhase('checking')
      setWatch({ id: result.import.id, commit: false })
    } catch (error) {
      console.warn(`Uploading '${file.name}' failed`, error)
      setProblem(t('shell.table.importUploadFailed'))
      setPhase('idle')
    }
  }

  const commit = async () => {
    if (importId === null) {
      return
    }
    setPhase('committing')
    setProblem(null)
    try {
      const result = await commitImport(importsPath, importId)
      if (!result.ok) {
        refuse(result.status, false)
        setPhase('checked')
        return
      }
      setWatch({ id: importId, commit: true })
    } catch (error) {
      console.warn(`Committing the import '${importId}' failed`, error)
      setProblem(t('shell.table.importUploadFailed'))
      setPhase('checked')
    }
  }

  const label = (name: string) => {
    const field = fields.find((candidate) => candidate.name.toLowerCase() === name.toLowerCase())
    return field?.labelKey ? t(field.labelKey) : (field?.name ?? name)
  }
  const describe = (error: ImportError): string => {
    switch (error.kind) {
      case 'field':
        return `${t('shell.table.importRow')} ${error.row} · ${label(error.field)}: ${error.message}`
      case 'row':
        return `${t('shell.table.importRow')} ${error.row}: ${error.message}`
      case 'column':
        return `${t('shell.table.importColumn')} ${error.column}: ${error.message}`
      default:
        return `${t('shell.table.importFile')}: ${error.message}`
    }
  }

  const busy = phase === 'uploading' || phase === 'checking' || phase === 'committing'
  const status =
    phase === 'uploading' || phase === 'checking'
      ? t('shell.table.importChecking')
      : phase === 'checked'
        ? t('shell.table.importChecked')
        : phase === 'committing'
          ? t('shell.table.importCommitting')
          : phase === 'failed'
            ? t('shell.table.importFailed')
            : null

  return (
    <Modal
      open
      title={t('shell.table.importTitle')}
      onCancel={onClose}
      destroyOnHidden
      footer={[
        <Button key="close" onClick={onClose}>
          {t('shell.table.importClose')}
        </Button>,
        <Button
          key="check"
          type={phase === 'checked' || phase === 'committing' ? 'default' : 'primary'}
          disabled={file === null || busy || phase === 'checked'}
          loading={phase === 'uploading' || phase === 'checking'}
          onClick={() => void upload()}
        >
          {t('shell.table.importCheck')}
        </Button>,
        (phase === 'checked' || phase === 'committing') && (
          <Button key="commit" type="primary" loading={phase === 'committing'} onClick={() => void commit()}>
            {t('shell.table.importCommit')}
          </Button>
        ),
      ]}
    >
      <Flex vertical gap="middle">
        <Upload
          accept=".csv,.xlsx"
          maxCount={1}
          disabled={busy}
          beforeUpload={(picked) => {
            if (importContentType(picked) === null) {
              pick(null)
              setProblem(t('shell.table.importWrongType'))
              return Upload.LIST_IGNORE
            }
            pick(picked)
            return false
          }}
          onRemove={() => pick(null)}
        >
          <Button>{t('shell.table.importChoose')}</Button>
        </Upload>
        <div aria-live="polite" data-testid="import-status">
          {status !== null && <Typography.Text>{status}</Typography.Text>}
          {errors.length > 0 && (
            <ul>
              {errors.map((error, index) => (
                <li key={index}>{describe(error)}</li>
              ))}
            </ul>
          )}
          {problem !== null && <Alert data-testid="import-problem" type="error" title={problem} />}
        </div>
      </Flex>
    </Modal>
  )
}
