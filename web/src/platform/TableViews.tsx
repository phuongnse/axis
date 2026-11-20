import { FolderOpenOutlined } from '@ant-design/icons'
import { useState } from 'react'
import { AxisButton } from './AxisButton'
import { SavedItemsDialog, type SavedItemsTexts } from './SavedItemsDialog'
import type { TableSavedViews } from './useTableView'

/** What a table's Views dialog says. */
const viewTexts: SavedItemsTexts = {
  title: 'shell.table.views',
  empty: 'shell.table.viewsEmpty',
  name: 'shell.table.viewsName',
  nameHelp: 'shell.table.viewsName.help',
  nameRequired: 'shell.table.viewsNameRequired',
  nameTaken: 'shell.table.viewsNameTaken',
  save: 'shell.table.viewsSave',
  saveFailed: 'shell.table.viewsSaveFailed',
  rename: 'shell.table.viewsRename',
  renameFailed: 'shell.table.viewsRenameFailed',
  overwrite: 'shell.table.viewsOverwrite',
  overwriteConfirm: 'shell.table.viewsOverwriteConfirm',
  overwriteFailed: 'shell.table.viewsOverwriteFailed',
  delete: 'shell.table.viewsDelete',
  deleteConfirm: 'shell.table.viewsDeleteConfirm',
  deleteFailed: 'shell.table.viewsDeleteFailed',
}

/**
 * The Views button of a table with a key and the dialog it opens, see docs/reference/actions-and-controls.md#tables.
 * It draws the views through `SavedItemsDialog`. The dialog lists the person's views by name in the page language's collation. A name opens its view, and Rename,
 * Overwrite and Delete work in the view's own line, Overwrite and Delete after a question, so the dialog opens no dialog of its own. The Name field
 * and Save view save the table's current search, filter and sort. A save the server refuses keeps the dialog open
 * and says so.
 */
export function TableViewsButton({ saved }: { saved: TableSavedViews }) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <AxisButton role="icon" nameKey="shell.table.views" icon={<FolderOpenOutlined />} onClick={() => setOpen(true)} />
      <SavedItemsDialog
        open={open}
        onClose={() => setOpen(false)}
        saved={{ ...saved, items: saved.views }}
        texts={viewTexts}
      />
    </>
  )
}
