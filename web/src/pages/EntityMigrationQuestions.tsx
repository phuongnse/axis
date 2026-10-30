import { Button, Flex, Input, Modal, Typography } from 'antd'
import { useState } from 'react'
import { asked, type FieldAnswer, type FieldQuestion } from '../platform/entityMigrations'
import { useText } from '../platform/texts'

/** The answers so far, with a `skip` for each question that the earlier answers make unnecessary. */
function advance(questions: FieldQuestion[], answers: FieldAnswer[]): FieldAnswer[] {
  const all = [...answers]
  while (all.length < questions.length && !asked(questions, all, all.length)) {
    all.push('skip')
  }
  return all
}

/**
 * The questions that a save of an entity asks about its changed fields, one at a time. A rename offers Keep data, Drop
 * data and Cancel. A removal offers Drop data and Keep field. A retyped field asks for a `convert` expression and a
 * field that became required asks for a `fill` expression. Each offers a button that adds it, a button that skips it
 * and Cancel. A question that an earlier answer makes unnecessary is skipped. `onDone` gets one answer per question
 * once the last one is answered, and `onCancel` runs on Cancel and when the dialog is closed.
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
  const [answers, setAnswers] = useState<FieldAnswer[]>(() => advance(questions, []))
  const question = questions[answers.length]

  function answer(next: FieldAnswer) {
    const all = advance(questions, [...answers, next])
    if (all.length === questions.length) {
      onDone(all)
    } else {
      setAnswers(all)
    }
  }

  if (!question) {
    return null
  }
  if (question.type === 'convert' || question.type === 'fill') {
    return <ExpressionQuestion key={answers.length} question={question} onAnswer={answer} onCancel={onCancel} />
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
          {/* Proposed: beside the solid button, the other actions are text buttons. */}
          {rename && (
            <Button type="text" onClick={onCancel}>
              {t('shell.studio.cancel')}
            </Button>
          )}
          <Button type="text" danger onClick={() => answer('drop')}>
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

/** The question for a `convert` or `fill` expression. The expression is plain text, and an empty one cannot be added. */
function ExpressionQuestion({
  question,
  onAnswer,
  onCancel,
}: {
  question: Extract<FieldQuestion, { type: 'convert' | 'fill' }>
  onAnswer: (answer: FieldAnswer) => void
  onCancel: () => void
}) {
  const t = useText()
  const [value, setValue] = useState('')
  const convert = question.type === 'convert'
  const hint = !convert
    ? t('shell.studio.entity.migration.fillHint')
    : question.reason === 'type'
      ? t('shell.studio.entity.migration.convertHintType')
      : question.reason === 'maxLength'
        ? t('shell.studio.entity.migration.convertHintMaxLength')
        : t('shell.studio.entity.migration.convertHintValues')

  return (
    <Modal
      open
      centered
      title={t(convert ? 'shell.studio.entity.migration.convertTitle' : 'shell.studio.entity.migration.fillTitle')}
      onCancel={onCancel}
      footer={
        <Flex gap="small" justify="end">
          <Button type="text" onClick={onCancel}>
            {t('shell.studio.cancel')}
          </Button>
          <Button type="text" onClick={() => onAnswer('skip')}>
            {t(convert ? 'shell.studio.entity.migration.skip' : 'shell.studio.entity.migration.leaveNulls')}
          </Button>
          <Button type="primary" disabled={value.trim() === ''} onClick={() => onAnswer({ value: value.trim() })}>
            {t(convert ? 'shell.studio.entity.migration.addConvert' : 'shell.studio.entity.migration.fillNulls')}
          </Button>
        </Flex>
      }
    >
      <div data-testid="studio-migration-question">
        <Flex vertical gap="small">
          <Typography.Text strong data-testid="studio-migration-field">
            {question.field}
          </Typography.Text>
          <Typography.Text type="secondary">{hint}</Typography.Text>
          <Input
            autoFocus
            aria-label={t(
              convert
                ? 'shell.studio.entity.migration.convertExpression'
                : 'shell.studio.entity.migration.fillExpression',
            )}
            value={value}
            onChange={(event) => setValue(event.target.value)}
          />
        </Flex>
      </div>
    </Modal>
  )
}
