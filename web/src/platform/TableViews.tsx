import { FolderOpenOutlined } from '@ant-design/icons'
import { useState } from 'react'
import { AxisButton } from './AxisButton'
import { SavedItemsDialog, type SavedItemsTexts } from './SavedItemsDialog'
import type { SavedTableView } from './tableChoices'
import type { TableSavedViews } from './useTableView'

/** The texts of a table's views, which the saved items dialog reads. */
const viewsTexts: SavedItemsTexts = {
  title: 'shell.table.views',
  save: 'shell.table.viewsSave',
  empty: 'shell.table.viewsEmpty',
  name: 'shell.table.viewsName',
  nameHelp: 'shell.table.viewsName.help',
  nameRequired: 'shell.table.viewsNameRequired',
  nameTaken: 'shell.table.viewsNameTaken',
  saveFailed: 'shell.table.viewsSaveFailed',
  overwrite: 'shell.table.viewsOverwrite',
  overwriteConfirm: 'shell.table.viewsOverwriteConfirm',
  overwriteFailed: 'shell.table.viewsOverwriteFailed',
  rename: 'shell.table.viewsRename',
  renameFailed: 'shell.table.viewsRenameFailed',
  delete: 'shell.table.viewsDelete',
  deleteConfirm: 'shell.table.viewsDeleteConfirm',
  deleteFailed: 'shell.table.viewsDeleteFailed',
}

/**
 * The Views button of a table with a key, which opens the one saved items dialog with the person's views, see
 * docs/reference/actions-and-controls.md#tables. The Name field and Save view save the table's current search, filter
 * and sort, and Overwrite replaces a view's with them.
 */
export function TableViewsButton({ saved }: { saved: TableSavedViews }) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <AxisButton role="icon" nameKey="shell.table.views" icon={<FolderOpenOutlined />} onClick={() => setOpen(true)} />
      <SavedItemsDialog<SavedTableView>
        open={open}
        onClose={() => setOpen(false)}
        texts={viewsTexts}
        saved={{
          items: saved.views,
          keyOf: (view) => view.id,
          nameOf: (view) => view.name,
          open: saved.open,
          save: saved.save,
          overwrite: saved.overwrite,
          rename: saved.rename,
          remove: saved.remove,
        }}
      />
    </>
  )
}
