import { DeleteOutlined, EditOutlined, SaveOutlined } from '@ant-design/icons'
import { ConfigProvider, Flex, Form, Input, Modal } from 'antd'
import { useContext, useState } from 'react'
import { AxisAlert } from './AxisAlert'
import { AxisButton } from './AxisButton'
import { AxisField } from './AxisField'
import { EmptyState } from './EmptyState'
import { nameCompare } from './listOrder'
import { requiredMark } from './requiredMark'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/** A saved item: its id and the name a person gave it. */
export interface SavedItem {
  id: string
  name: string
}

/** What the dialog does with the items: open one, save what is shown now under a name, and rename, overwrite or delete one. */
export interface SavedItems<T extends SavedItem> {
  items: readonly T[]
  open: (item: T) => void
  save: (name: string) => Promise<boolean>
  rename: (item: T, name: string) => Promise<boolean>
  /** Replaces the item with what is shown now, and keeps its name. */
  overwrite: (item: T) => Promise<boolean>
  remove: (item: T) => Promise<boolean>
}

/** The text key of each text the dialog says, so each use says it in its own words. */
export interface SavedItemsTexts {
  title: string
  empty: string
  name: string
  nameHelp: string
  nameRequired: string
  nameTaken: string
  save: string
  saveFailed: string
  rename: string
  renameFailed: string
  overwrite: string
  overwriteConfirm: string
  overwriteFailed: string
  delete: string
  deleteConfirm: string
  deleteFailed: string
}

/** The longest name an item has, as the Name field allows. */
const nameLength = 80

/** Whether a name is taken by an item other than `own`, ignoring letter case and spaces at the ends. */
const nameTaken = <T extends SavedItem>(items: readonly T[], name: string, own?: T): boolean =>
  items.some((item) => item !== own && item.name.trim().toLowerCase() === name.trim().toLowerCase())

/** What one line of the dialog is doing: showing its item, or asking for a new name or whether to overwrite or delete it. */
type Mode = 'show' | 'rename' | 'overwrite' | 'delete'

/** One saved item in the dialog: its name, which opens it, and the Rename, Overwrite and Delete actions that work in this line. */
function ItemLine<T extends SavedItem>({
  view,
  views,
  saved,
  texts,
  onOpen,
}: {
  view: T
  views: readonly T[]
  saved: SavedItems<T>
  texts: SavedItemsTexts
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
      setProblem(t(texts.nameRequired))
      return
    }
    if (nameTaken(views, name, view)) {
      setProblem(t(texts.nameTaken))
      return
    }
    setProblem(undefined)
    setWorking(true)
    const renamed = await saved.rename(view, name.trim())
    setWorking(false)
    if (renamed) {
      show()
    } else {
      setFailure(t(texts.renameFailed))
    }
  }
  const overwrite = async () => {
    setWorking(true)
    const overwritten = await saved.overwrite(view)
    setWorking(false)
    if (overwritten) {
      show()
    } else {
      setFailure(t(texts.overwriteFailed))
    }
  }
  const remove = async () => {
    setWorking(true)
    const removed = await saved.remove(view)
    setWorking(false)
    if (!removed) {
      setFailure(t(texts.deleteFailed))
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
              nameKey={texts.rename}
              nameSuffix={view.name}
              icon={<EditOutlined />}
              onClick={() => {
                setName(view.name)
                setMode('rename')
              }}
            />
            <AxisButton
              role="icon"
              nameKey={texts.overwrite}
              nameSuffix={view.name}
              icon={<SaveOutlined />}
              onClick={() => setMode('overwrite')}
            />
            <AxisButton
              role="icon"
              nameKey={texts.delete}
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
              aria-label={t(texts.name)}
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
      {mode === 'overwrite' && (
        <Flex align="center" justify="space-between" gap="small" wrap>
          <span>{t(texts.overwriteConfirm).replace('{name}', view.name)}</span>
          <Flex gap="small">
            <AxisButton role="main" disabled={working} onClick={() => void overwrite()}>
              {t(texts.overwrite)}
            </AxisButton>
            <AxisButton role="secondary" onClick={show}>
              {t('shell.form.cancel')}
            </AxisButton>
          </Flex>
        </Flex>
      )}
      {mode === 'delete' && (
        <Flex align="center" justify="space-between" gap="small" wrap>
          <span>{t(texts.deleteConfirm).replace('{name}', view.name)}</span>
          <Flex gap="small">
            <AxisButton role="destructive" disabled={working} onClick={() => void remove()}>
              {t(texts.delete)}
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
 * Proposed, see #3978: the one dialog that keeps a person's named, saved items, such as a table's views or a package's
 * run inputs, see docs/reference/actions-and-controls.md#tables. It lists the items by name in the page language's
 * collation. A name opens its item, and Rename, Overwrite and Delete work in the item's own line, Overwrite and Delete
 * after a question, so the dialog opens no dialog of its own. The Name field and Save save what is shown now as a new
 * item. A save the server refuses keeps the dialog open and says so. `texts` names each text by its key, so each use
 * says it in its own words.
 */
export function SavedItemsDialog<T extends SavedItem>({
  open,
  onClose,
  saved,
  texts,
}: {
  open: boolean
  onClose: () => void
  saved: SavedItems<T>
  texts: SavedItemsTexts
}) {
  const t = useText()
  const { size, spacing } = useDesignTokens()
  // The shell sets the locale of Ant Design's ConfigProvider. Outside it, there is none.
  const locale = useContext(ConfigProvider.ConfigContext).locale?.locale ?? 'en'
  const [saving, setSaving] = useState(false)
  const [failed, setFailed] = useState(false)
  const [form] = Form.useForm<{ name: string }>()
  const compare = nameCompare(locale)
  const views = saved.items.toSorted((left, right) => compare(left.name, right.name))

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
      onClose()
    } else {
      setFailed(true)
    }
  }

  return (
    <Modal
      open={open}
      width={size.dialog.medium}
      title={t(texts.title)}
      okText={t(texts.save)}
      cancelText={t('shell.form.cancel')}
      confirmLoading={saving}
      onOk={() => void save()}
      onCancel={onClose}
      afterClose={() => {
        form.resetFields()
        setFailed(false)
      }}
      destroyOnHidden
    >
      <Flex vertical gap={spacing.lg}>
        {views.length === 0 ? (
          <EmptyState text={t(texts.empty)} />
        ) : (
          <Flex vertical gap="small">
            {views.map((view) => (
              <ItemLine
                key={view.id}
                view={view}
                views={saved.items}
                saved={saved}
                texts={texts}
                onOpen={() => {
                  saved.open(view)
                  onClose()
                }}
              />
            ))}
          </Flex>
        )}
        {failed && <AxisAlert kind="error" message={t(texts.saveFailed)} />}
        <Form form={form} layout="vertical" requiredMark={requiredMark} initialValues={{ name: '' }} onFinish={save}>
          <AxisField
            name="name"
            label={t(texts.name)}
            help={t(texts.nameHelp)}
            requiredMessage={t(texts.nameRequired)}
            rules={[
              {
                validator: (_rule, value: unknown) =>
                  typeof value === 'string' && value.trim() !== '' && nameTaken(saved.items, value)
                    ? Promise.reject(new Error(t(texts.nameTaken)))
                    : Promise.resolve(),
              },
            ]}
          >
            <Input maxLength={nameLength} />
          </AxisField>
        </Form>
      </Flex>
    </Modal>
  )
}
