import { Modal, Typography } from 'antd'
import { dialogWidth } from '../platform/dialogButtons'
import { ReasonDialog } from '../platform/ReasonDialog'
import { ReferenceLookup } from '../platform/ReferenceLookup'
import { useText } from '../platform/texts'
import { fields } from './fixtures'

const noop = () => {}

/** Proposed: a small dialog, a confirmation, as Activate… on a release asks it. */
export function SmallDialogPreview() {
  const t = useText()
  return (
    <Modal
      open
      width={dialogWidth('small')}
      title={t('shell.studio.activate.title').replace('{number}', '6')}
      okText={t('shell.studio.activate.confirm')}
      cancelText={t('shell.studio.cancel')}
      onOk={noop}
      onCancel={noop}
    >
      <Typography.Text>
        {t('shell.studio.activate.replaces').replace('{number}', '6').replace('{live}', '7')}
      </Typography.Text>
    </Modal>
  )
}

/** Proposed: a medium dialog, a short form, as an operator's Cancel instance asks for a reason. */
export function MediumDialogPreview() {
  const t = useText()
  return (
    <ReasonDialog
      title={t('shell.inspector.commands.cancel')}
      onSend={async () => ({ ok: true })}
      onClose={noop}
      onChanged={noop}
    />
  )
}

/** Proposed: a large dialog, a list to choose from, as Choose… on a reference field opens it. */
export function LargeDialogPreview() {
  const t = useText()
  return <ReferenceLookup field={fields.reference} title={t('request.category')} open onPick={noop} onClose={noop} />
}
