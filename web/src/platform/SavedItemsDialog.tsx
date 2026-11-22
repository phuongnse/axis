import { ConfigProvider, Flex, Form, Input, Modal } from 'antd'
import { useContext, useState } from 'react'
import { AxisAlert } from './AxisAlert'
import { AxisButton, AxisSplitButton } from './AxisButton'
import { AxisField } from './AxisField'
import { EmptyState } from './EmptyState'
import { nameCompare } from './listOrder'
import { requiredMark } from './requiredMark'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { DialogFooter } from './DialogFooter'

/** The longest name a saved item has, as the Name field allows. */
const nameLength = 80

/** What a caller gives `SavedItemsDialog`: the items and what the dialog does with them. */
export interface SavedItems<T> {
  items: readonly T[]
  keyOf: (item: T) => string
  nameOf: (item: T) => string
  /** Opens the item. The dialog closes. */
  open: (item: T) => void
  /** Saves what is shown now as a new item called `name`. Resolves to whether the server kept it. */
  save: (name: string) => Promise<boolean>
  /** Replaces the item's content with what is shown now, and keeps its name. */
  overwrite: (item: T) => Promise<boolean>
  rename: (item: T, name: string) => Promise<boolean>
  remove: (item: T) => Promise<boolean>
}

/** The text keys of the dialog. `overwriteConfirm` and `deleteConfirm` hold a `{name}` placeholder. */
export interface SavedItemsTexts {
  title: string
  save: string
  empty: string
  name: string
  nameHelp: string
  nameRequired: string
  nameTaken: string
  saveFailed: string
  overwrite: string
  overwriteConfirm: string
  overwriteFailed: string
  rename: string
  renameFailed: string
  delete: string
  deleteConfirm: string
  deleteFailed: string
}

/** What one line of the dialog is doing: showing its item, or asking for a new name or whether to overwrite or delete it. */
type Mode = 'show' | 'rename' | 'overwrite' | 'delete'

/** One item in the dialog: its name, which opens it, and the split button whose actions work in this line. */
function ItemLine<T>({
  item,
  saved,
  texts,
  onOpen,
}: {
  item: T
  saved: SavedItems<T>
  texts: SavedItemsTexts
  onOpen: () => void
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  const itemName = saved.nameOf(item)
  const [mode, setMode] = useState<Mode>('show')
  const [name, setName] = useState(itemName)
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
    if (nameTaken(saved, name, item)) {
      setProblem(t(texts.nameTaken))
      return
    }
    setProblem(undefined)
    setWorking(true)
    const renamed = await saved.rename(item, name.trim())
    setWorking(false)
    if (renamed) {
      show()
    } else {
      setFailure(t(texts.renameFailed))
    }
  }
  const overwrite = async () => {
    setWorking(true)
    const overwritten = await saved.overwrite(item)
    setWorking(false)
    if (overwritten) {
      show()
    } else {
      setFailure(t(texts.overwriteFailed))
    }
  }
  const remove = async () => {
    setWorking(true)
    const removed = await saved.remove(item)
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
            {itemName}
          </AxisButton>
          <AxisSplitButton
            nameSuffix={itemName}
            action={{ key: 'overwrite', label: t(texts.overwrite), onClick: () => setMode('overwrite') }}
            menu={[
              {
                key: 'rename',
                label: t(texts.rename),
                onClick: () => {
                  setName(itemName)
                  setMode('rename')
                },
              },
              { key: 'delete', label: t(texts.delete), destructive: true, onClick: () => setMode('delete') },
            ]}
          />
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
          <span>{t(texts.overwriteConfirm).replace('{name}', itemName)}</span>
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
          <span>{t(texts.deleteConfirm).replace('{name}', itemName)}</span>
          <Flex gap="small">
            <AxisButton role="secondary" destructive disabled={working} onClick={() => void remove()}>
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

/** Whether a name is taken by an item other than `own`, ignoring letter case and spaces at the ends. */
function nameTaken<T>(saved: SavedItems<T>, name: string, own?: T): boolean {
  return saved.items.some(
    (item) => item !== own && saved.nameOf(item).trim().toLowerCase() === name.trim().toLowerCase(),
  )
}

/**
 * The one dialog that keeps a person's named saved items, see docs/reference/actions-and-controls.md#fields. It lists
 * the items by name in the page language's collation. A name opens its item, and each line is one split button:
 * Overwrite, with Rename and Delete in the menu of its caret. All three work in the item's own line, Overwrite and
 * Delete after a question, so the dialog opens no dialog of its own. The Name field with Save saves what is shown now
 * as a new item. A save the server refuses keeps the dialog open and says so. `saved` and `texts` come from the caller,
 * whose `texts` are text keys.
 */
export function SavedItemsDialog<T>({
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
  const items = saved.items.toSorted((left, right) => compare(saved.nameOf(left), saved.nameOf(right)))

  const save = async (name: string) => {
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
      footer={
        <DialogFooter
          finishText={t(texts.save)}
          cancelText={t('shell.form.cancel')}
          finishing={saving}
          onFinish={() => form.submit()}
          onCancel={onClose}
        />
      }
      onCancel={onClose}
      afterClose={() => {
        form.resetFields()
        setFailed(false)
      }}
      destroyOnHidden
    >
      <Flex vertical gap={spacing.lg} data-saved-items>
        {items.length === 0 ? (
          <EmptyState text={t(texts.empty)} />
        ) : (
          <Flex vertical gap="small">
            {items.map((item) => (
              <ItemLine
                key={saved.keyOf(item)}
                item={item}
                saved={saved}
                onOpen={() => {
                  saved.open(item)
                  onClose()
                }}
                texts={texts}
              />
            ))}
          </Flex>
        )}
        {failed && <AxisAlert kind="error" message={t(texts.saveFailed)} />}
        <Form
          form={form}
          layout="vertical"
          requiredMark={requiredMark}
          scrollToFirstError={{ focus: true }}
          initialValues={{ name: '' }}
          onFinish={({ name }) => void save(name)}
        >
          <AxisField
            name="name"
            label={t(texts.name)}
            help={t(texts.nameHelp)}
            requiredMessage={t(texts.nameRequired)}
            rules={[
              {
                validator: (_rule, value: unknown) =>
                  typeof value === 'string' && value.trim() !== '' && nameTaken(saved, value)
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
