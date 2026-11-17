import { DeleteOutlined, EditOutlined, FolderOpenOutlined } from '@ant-design/icons'
import { ConfigProvider, Flex, Form, Input, Modal } from 'antd'
import { useContext, useState } from 'react'
import { AxisAlert } from './AxisAlert'
import { AxisButton } from './AxisButton'
import { AxisField } from './AxisField'
import { EmptyState } from './EmptyState'
import { nameCompare } from './listOrder'
import { requiredMark } from './requiredMark'
import type { SavedTableView } from './tableChoices'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import type { TableSavedViews } from './useTableView'

/** The longest name a view has, as the Name field allows. */
const nameLength = 80

/** Whether a name is taken by a view other than `own`, ignoring letter case and spaces at the ends. */
const nameTaken = (views: readonly SavedTableView[], name: string, own?: SavedTableView): boolean =>
  views.some((view) => view !== own && view.name.trim().toLowerCase() === name.trim().toLowerCase())

/** What one line of the dialog is doing: showing its view, or asking for a new name or whether to delete it. */
type Mode = 'show' | 'rename' | 'delete'

/** One view in the dialog: its name, which opens it, and the Rename and Delete actions that work in this line. */
function ViewLine({
  view,
  views,
  saved,
  onOpen,
}: {
  view: SavedTableView
  views: readonly SavedTableView[]
  saved: TableSavedViews
  onOpen: () => void
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  const [mode, setMode] = useState<Mode>('show')
  const [name, setName] = useState(view.name)
  const [problem, setProblem] = useState<string>()
  const [failure, setFailure] = useState<string>()
  const [working, setWorking] = useState(false)

  const show = () => {
    setMode('show')
    setProblem(undefined)
    setFailure(undefined)
  }
  const rename = async () => {
    if (name.trim() === '') {
      setProblem(t('shell.table.viewsNameRequired'))
      return
    }
    if (nameTaken(views, name, view)) {
      setProblem(t('shell.table.viewsNameTaken'))
      return
    }
    setProblem(undefined)
    setWorking(true)
    const renamed = await saved.rename(view, name.trim())
    setWorking(false)
    if (renamed) {
      show()
    } else {
      setFailure(t('shell.table.viewsRenameFailed'))
    }
  }
  const remove = async () => {
    setWorking(true)
    const removed = await saved.remove(view)
    setWorking(false)
    if (!removed) {
      setFailure(t('shell.table.viewsDeleteFailed'))
    }
  }

  return (
    <Flex vertical gap="small">
      {mode === 'show' && (
        <Flex align="center" justify="space-between" gap="small">
          <AxisButton role="link" onClick={onOpen}>
            {view.name}
          </AxisButton>
          <Flex gap="small">
            <AxisButton
              role="icon"
              nameKey="shell.table.viewsRename"
              nameSuffix={view.name}
              icon={<EditOutlined />}
              onClick={() => {
                setName(view.name)
                setMode('rename')
              }}
            />
            <AxisButton
              role="icon"
              nameKey="shell.table.viewsDelete"
              nameSuffix={view.name}
              destructive
              icon={<DeleteOutlined />}
              onClick={() => setMode('delete')}
            />
          </Flex>
        </Flex>
      )}
      {mode === 'rename' && (
        <Flex vertical gap="small">
          <Flex align="center" gap="small">
            <Input
              aria-label={t('shell.table.viewsName')}
              aria-invalid={problem !== undefined}
              maxLength={nameLength}
              value={name}
              onChange={(event) => setName(event.target.value)}
              onPressEnter={() => void rename()}
            />
            <AxisButton role="main" disabled={working} onClick={() => void rename()}>
              {t('shell.form.save')}
            </AxisButton>
            <AxisButton role="secondary" onClick={show}>
              {t('shell.form.cancel')}
            </AxisButton>
          </Flex>
          {problem && <span style={{ color: colour.dangerText }}>{problem}</span>}
        </Flex>
      )}
      {mode === 'delete' && (
        <Flex align="center" justify="space-between" gap="small" wrap>
          <span>{t('shell.table.viewsDeleteConfirm').replace('{name}', view.name)}</span>
          <Flex gap="small">
            <AxisButton role="destructive" disabled={working} onClick={() => void remove()}>
              {t('shell.table.viewsDelete')}
            </AxisButton>
            <AxisButton role="secondary" onClick={show}>
              {t('shell.form.cancel')}
            </AxisButton>
          </Flex>
        </Flex>
      )}
      {failure && <AxisAlert kind="error" message={failure} />}
    </Flex>
  )
}

/**
 * The Views button of a table with a key and the dialog it opens, see docs/reference/actions-and-controls.md#tables.
 * The dialog lists the person's views by name in the page language's collation. A name opens its view, and Rename and
 * Delete work in the view's own line, Delete after a question, so the dialog opens no dialog of its own. The Name field
 * and Save view save the table's current search, filter and sort. A save the server refuses keeps the dialog open
 * and says so.
 */
export function TableViewsButton({ saved }: { saved: TableSavedViews }) {
  const t = useText()
  const { size, spacing } = useDesignTokens()
  // The shell sets the locale of Ant Design's ConfigProvider. Outside it, there is none.
  const locale = useContext(ConfigProvider.ConfigContext).locale?.locale ?? 'en'
  const [open, setOpen] = useState(false)
  const [saving, setSaving] = useState(false)
  const [failed, setFailed] = useState(false)
  const [form] = Form.useForm<{ name: string }>()
  const compare = nameCompare(locale)
  const views = saved.views.toSorted((left, right) => compare(left.name, right.name))

  const save = async () => {
    let name: string
    try {
      name = (await form.validateFields()).name
    } catch {
      return
    }
    setFailed(false)
    setSaving(true)
    const done = await saved.save(name.trim())
    setSaving(false)
    if (done) {
      setOpen(false)
    } else {
      setFailed(true)
    }
  }

  return (
    <>
      <AxisButton
        role="icon"
        nameKey="shell.table.views"
        icon={<FolderOpenOutlined />}
        onClick={() => {
          setFailed(false)
          setOpen(true)
        }}
      />
      <Modal
        open={open}
        width={size.dialog.medium}
        title={t('shell.table.views')}
        okText={t('shell.table.viewsSave')}
        cancelText={t('shell.form.cancel')}
        confirmLoading={saving}
        onOk={() => void save()}
        onCancel={() => setOpen(false)}
        afterClose={() => form.resetFields()}
        destroyOnHidden
      >
        <Flex vertical gap={spacing.lg}>
          {views.length === 0 ? (
            <EmptyState text={t('shell.table.viewsEmpty')} />
          ) : (
            <Flex vertical gap="small">
              {views.map((view) => (
                <ViewLine
                  key={view.id}
                  view={view}
                  views={saved.views}
                  saved={saved}
                  onOpen={() => {
                    saved.open(view)
                    setOpen(false)
                  }}
                />
              ))}
            </Flex>
          )}
          {failed && <AxisAlert kind="error" message={t('shell.table.viewsSaveFailed')} />}
          <Form form={form} layout="vertical" requiredMark={requiredMark} initialValues={{ name: '' }} onFinish={save}>
            <AxisField
              name="name"
              label={t('shell.table.viewsName')}
              help={t('shell.table.viewsName.help')}
              requiredMessage={t('shell.table.viewsNameRequired')}
              rules={[
                {
                  validator: (_rule, value: unknown) =>
                    typeof value === 'string' && value.trim() !== '' && nameTaken(saved.views, value)
                      ? Promise.reject(new Error(t('shell.table.viewsNameTaken')))
                      : Promise.resolve(),
                },
              ]}
            >
              <Input maxLength={nameLength} />
            </AxisField>
          </Form>
        </Flex>
      </Modal>
    </>
  )
}
