import { Button, Flex, Modal, Typography } from 'antd'
import { useState } from 'react'
import type { FieldAnswer, FieldQuestion } from '../platform/entityMigrations'
import { useText } from '../platform/texts'

/**
 * The questions that a save of an entity asks about its renamed and removed fields, one at a time. A rename offers Keep
 * data, Drop data and Cancel. A removal offers Drop data and Keep field. `onDone` gets one answer per question once
 * the last one is answered, and `onCancel` runs on Cancel and when the dialog is closed.
 */
export function EntityMigrationQuestions({
  questions,
  onDone,
  onCancel,
}: {
  questions: FieldQuestion[]
  onDone: (answers: FieldAnswer[]) => void
  onCancel: () => void
}) {
  const t = useText()
  const [answers, setAnswers] = useState<FieldAnswer[]>([])
  const question = questions[answers.length]

  function answer(next: FieldAnswer) {
    const all = [...answers, next]
    if (all.length === questions.length) {
      onDone(all)
    } else {
      setAnswers(all)
    }
  }

  if (!question) {
    return null
  }
  const rename = question.type === 'rename'

  return (
    <Modal
      open
      centered
      title={t(rename ? 'shell.studio.entity.migration.renameTitle' : 'shell.studio.entity.migration.removeTitle')}
      onCancel={onCancel}
      footer={
        <Flex gap="small" justify="end">
          {rename && <Button onClick={onCancel}>{t('shell.studio.cancel')}</Button>}
          <Button danger onClick={() => answer('drop')}>
            {t('shell.studio.entity.migration.dropData')}
          </Button>
          <Button type="primary" onClick={() => answer('keep')}>
            {t(rename ? 'shell.studio.entity.migration.keepData' : 'shell.studio.entity.migration.keepField')}
          </Button>
        </Flex>
      }
    >
      <div data-testid="studio-migration-question">
        <Flex vertical gap="small">
          <Typography.Text strong data-testid="studio-migration-field">
            {rename ? `${question.from} → ${question.to}` : question.field}
          </Typography.Text>
          <Typography.Text type="secondary">
            {t(rename ? 'shell.studio.entity.migration.renameHint' : 'shell.studio.entity.migration.removeHint')}
          </Typography.Text>
        </Flex>
      </div>
    </Modal>
  )
}
