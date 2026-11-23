import { Form, Input, Modal, Select } from 'antd'
import { AxisForm } from '../platform/AxisForm'
import { useState } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { packageLanguages, packageStarter } from '../platform/packageStarters'
import { QuotaRefusedError } from '../platform/quota'
import { createStudioPackageFile, ServerUnreachableError, type StudioDiagnostic } from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { DialogFooter } from '../platform/DialogFooter'

/** The pattern of a package's id in the manifest schema, see docs/reference/studio-packages.md#the-packages-entry. */
const idPattern = /^[a-z][a-z0-9-]*$/

/** The file that names a package. It is written first, so an existing package is found before any other file is written. */
const manifestPath = 'axis-package.json'

/** What the server says is wrong with the author's input, as the text key to show under the id field. */
type Problem = 'exists' | 'invalid' | 'failed'

type Values = { id: string; language: string }

/**
 * The New package… button of the Packages view, a main button on the right of the heading, and its dialog. The dialog
 * asks for an id and a language, C#, TypeScript or Python, and creates the package from that language's starter,
 * see docs/reference/studio-packages.md#the-packages-entry. It writes the starter one file at a time, `axis-package.json`
 * first and then the rest in the starter's order, each with `If-None-Match: *`, so no file is ever replaced. The fields
 * are marked required and have help. An id that the manifest schema's pattern refuses shows under the id field on Create,
 * and nothing is sent. Create is on until a create runs.
 * A `409` or a `400` on the manifest shows under the id field and the dialog stays open, with no other file sent. A failure on a later file
 * leaves a part of the package, so it shows the failed text under the id field, keeps the dialog open and still calls `onCreated`,
 * which loads the list again. A quota refusal shows its title under the id field. `onCreated` gets the folder's diagnostics after the last write.
 */
export function StudioNewPackage({
  application,
  onCreated,
}: {
  application: string
  onCreated: (diagnostics: StudioDiagnostic[]) => void
}) {
  const t = useText()
  const { size } = useDesignTokens()
  const { lost } = useStudioConnection()
  const [open, setOpen] = useState(false)
  const [form] = Form.useForm<Values>()
  const [creating, setCreating] = useState(false)

  function problemOf(problem: Problem | string) {
    const text =
      problem === 'exists' || problem === 'invalid' || problem === 'failed'
        ? t(`shell.studio.newPackage.${problem}`)
        : problem
    form.setFields([{ name: 'id', errors: [text] }])
  }

  async function create({ id, language }: Values) {
    const starter = packageStarter(language, id)
    const paths = [manifestPath, ...Object.keys(starter).filter((path) => path !== manifestPath)]
    let diagnostics: StudioDiagnostic[] | null = null
    setCreating(true)
    try {
      for (const path of paths) {
        const result = await createStudioPackageFile(application, id, path, starter[path])
        if (result.status !== 'created') {
          // Nothing of the package is written when the manifest is refused, so there is nothing to show in the list.
          problemOf(diagnostics === null ? (result.status === 'conflict' ? 'exists' : 'invalid') : 'failed')
          break
        }
        diagnostics = result.diagnostics
        if (path === paths.at(-1)) {
          setOpen(false)
        }
      }
    } catch (error) {
      if (error instanceof ServerUnreachableError) {
        lost()
      } else if (error instanceof QuotaRefusedError) {
        problemOf(error.message)
      } else {
        console.warn(`Creating the package '${id}' in '${application}' failed`, error)
        problemOf('failed')
      }
    } finally {
      setCreating(false)
      // A package that was written in part still has a row, so the list and the Problems panel show it.
      if (diagnostics !== null) {
        onCreated(diagnostics)
      }
    }
  }

  return (
    <>
      <AxisButton role="main" onClick={() => setOpen(true)}>
        {t('shell.studio.newPackage.button')}
      </AxisButton>
      <Modal
        open={open}
        width={size.dialog.medium}
        title={t('shell.studio.newPackage.title')}
        footer={
          <DialogFooter
            finishText={t('shell.studio.newResource.create')}
            cancelText={t('shell.studio.cancel')}
            finishing={creating}
            onFinish={() => form.submit()}
            onCancel={() => setOpen(false)}
          />
        }
        onCancel={() => setOpen(false)}
        // Each opening starts with empty fields.
        afterClose={() => form.resetFields()}
      >
        <AxisForm form={form} initialValues={{ id: '' }} onFinish={create}>
          <AxisField
            name="id"
            label={t('shell.studio.newPackage.id')}
            help={t('shell.studio.newPackage.id.help')}
            requiredMessage={t('shell.studio.newPackage.idRequired')}
            rules={[{ pattern: idPattern, message: t('shell.studio.newPackage.idInvalid') }]}
          >
            <Input
              aria-label={t('shell.studio.newPackage.id')}
              // The browser also submits a form with one text box on Enter, so this stops it from creating twice.
              onPressEnter={(event) => {
                event.preventDefault()
                form.submit()
              }}
            />
          </AxisField>
          <AxisField
            name="language"
            label={t('shell.studio.newPackage.language')}
            help={t('shell.studio.newPackage.language.help')}
            requiredMessage={t('shell.studio.newPackage.languageRequired')}
          >
            <Select
              aria-label={t('shell.studio.newPackage.language')}
              // All languages render, so each one can be reached without scrolling the list.
              virtual={false}
              options={packageLanguages.map((language) => ({
                value: language,
                label: t(`shell.studio.packages.language.${language}`),
              }))}
            />
          </AxisField>
        </AxisForm>
      </Modal>
    </>
  )
}
