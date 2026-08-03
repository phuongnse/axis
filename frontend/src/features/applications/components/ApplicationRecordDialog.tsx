import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Save, Send } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ManagedDialog, ManagedDialogBody } from '@/components/shared/ManagedDialog';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { StatusNotice } from '@/components/shared/StatusNotice';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import { ApiError } from '@/lib/api';
import {
  applicationQueryKeys,
  applicationRecordDetailQueryOptions,
  type BusinessObjectRecordDetail,
  type BusinessObjectRecordField,
  type BusinessObjectRecordRuleEvaluation,
  saveBusinessObjectRecord,
  submitBusinessObjectRecord,
} from '../api';

export function ApplicationRecordDialog({
  recordId,
  title,
  onClose,
}: {
  recordId: string;
  title: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const recordQuery = useQuery(applicationRecordDetailQueryOptions(recordId));
  const [values, setValues] = useState<Record<string, string[]>>({});
  const [evaluations, setEvaluations] = useState<BusinessObjectRecordRuleEvaluation[]>([]);
  const [requestError, setRequestError] = useState<string | null>(null);
  const [workflowBusy, setWorkflowBusy] = useState(false);
  const record = recordQuery.data;
  const recordRef = useRef(record);
  recordRef.current = record;
  const recordIdentity = `${record?.id ?? ''}:${record?.revision ?? ''}:${record?.status ?? ''}`;

  useEffect(() => {
    if (!recordIdentity) return;
    const currentRecord = recordRef.current;
    if (!currentRecord) return;
    setValues(normalizeValues(currentRecord.values));
    setEvaluations(currentRecord.ruleEvaluations ?? []);
  }, [recordIdentity]);

  const dirty = useMemo(
    () => JSON.stringify(values) !== JSON.stringify(normalizeValues(record?.values)),
    [record?.values, values],
  );
  const readOnly = record?.status === 'Submitted';
  const busy = workflowBusy || recordQuery.isFetching;

  function setFieldValue(fieldKey: string, next: string[]) {
    setValues((current) => ({ ...current, [fieldKey]: next }));
    setRequestError(null);
  }

  async function saveDraft(currentRecord = record) {
    if (!currentRecord?.id || currentRecord.revision == null) return currentRecord;
    const saved = await saveBusinessObjectRecord(currentRecord.id, {
      expectedRevision: currentRecord.revision,
      values,
    });
    updateRecordCache(queryClient, saved);
    setValues(normalizeValues(saved.values));
    await queryClient.invalidateQueries({ queryKey: applicationQueryKeys.lists() });
    return saved;
  }

  async function handleSave() {
    if (!record || readOnly) return;
    setWorkflowBusy(true);
    setRequestError(null);
    try {
      await saveDraft(record);
    } catch (error) {
      setRequestError(readApiError(error, t('applications.saveError')));
    } finally {
      setWorkflowBusy(false);
    }
  }

  async function handleSubmit() {
    if (!record || readOnly) return;
    setWorkflowBusy(true);
    setRequestError(null);
    try {
      const currentRecord = dirty ? await saveDraft(record) : record;
      if (!currentRecord?.id || currentRecord.revision == null) return;
      const result = await submitBusinessObjectRecord(currentRecord.id, {
        expectedRevision: currentRecord.revision,
      });
      setEvaluations(result.ruleEvaluations ?? []);
      if (result.record) updateRecordCache(queryClient, result.record);
      await queryClient.invalidateQueries({ queryKey: applicationQueryKeys.lists() });
      if (!result.isSubmitted) {
        setRequestError(t('applications.ruleMismatchDescription'));
      }
    } catch (error) {
      setRequestError(readApiError(error, t('applications.submitError')));
    } finally {
      setWorkflowBusy(false);
    }
  }

  if (recordQuery.isLoading) {
    return (
      <ManagedDialog
        open
        title={title}
        description={t('applications.loading')}
        onOpenChange={(open) => {
          if (!open) onClose();
        }}
        footer={
          <Button type="button" variant="outline" onClick={onClose}>
            {t('app.close')}
          </Button>
        }
      >
        <ManagedDialogBody>
          <div className="h-24 animate-pulse rounded-lg bg-muted" />
        </ManagedDialogBody>
      </ManagedDialog>
    );
  }

  if (!record || recordQuery.isError) {
    return (
      <ManagedDialog
        open
        title={title}
        description={t('applications.loadErrorDescription')}
        onOpenChange={(open) => {
          if (!open) onClose();
        }}
        footer={
          <Button type="button" variant="outline" onClick={onClose}>
            {t('app.close')}
          </Button>
        }
      >
        <ManagedDialogBody>
          <StatusNotice tone="destructive" title={t('applications.loadError')}>
            {t('applications.loadErrorDescription')}
          </StatusNotice>
        </ManagedDialogBody>
      </ManagedDialog>
    );
  }

  return (
    <ManagedDialog
      open
      title={title}
      description={
        readOnly ? t('applications.submittedDescription') : t('applications.editorDescription')
      }
      titleAccessory={
        record.status === 'Submitted' ? (
          <StatusBadge tone="success">{t('applications.submitted')}</StatusBadge>
        ) : (
          <StatusBadge tone="neutral">{t('applications.draft')}</StatusBadge>
        )
      }
      onOpenChange={(open) => {
        if (!open && !busy) onClose();
      }}
      closeDisabled={busy}
      dirty={!readOnly && dirty}
      footer={
        <>
          <Button type="button" variant="outline" disabled={busy} onClick={onClose}>
            {t('app.close')}
          </Button>
          {!readOnly ? (
            <>
              <Button
                type="button"
                variant="outline"
                disabled={busy || !dirty}
                onClick={handleSave}
              >
                <Save aria-hidden />
                {busy ? t('applications.saving') : t('applications.saveDraft')}
              </Button>
              <Button type="button" disabled={busy} onClick={handleSubmit}>
                <Send aria-hidden />
                {busy ? t('applications.submitting') : t('applications.submit')}
              </Button>
            </>
          ) : null}
        </>
      }
    >
      <ManagedDialogBody className="space-y-5">
        {requestError ? (
          <StatusNotice
            tone={
              evaluations.some((evaluation) => evaluation.isMatch === false)
                ? 'warning'
                : 'destructive'
            }
            title={
              evaluations.some((evaluation) => evaluation.isMatch === false)
                ? t('applications.ruleMismatchTitle')
                : t('applications.requestErrorTitle')
            }
          >
            {requestError}
          </StatusNotice>
        ) : null}

        <div className="rounded-lg border bg-muted/20 p-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <p className="text-sm font-medium text-foreground">
                {t('applications.workflowStep')}
              </p>
              <p className="mt-1 text-sm text-muted-foreground">
                {readOnly ? t('applications.workflowComplete') : t('applications.workflowDraft')}
              </p>
            </div>
            <span className="text-xs text-muted-foreground">
              {t('applications.revision', { revision: record.revision ?? 0 })}
            </span>
          </div>
        </div>

        <div className="space-y-5">
          {(record.fields ?? []).map((field) => (
            <RecordField
              key={field.fieldKey}
              field={field}
              values={values[field.fieldKey ?? ''] ?? []}
              evaluations={evaluations.filter((candidate) => candidate.fieldKey === field.fieldKey)}
              disabled={readOnly || busy}
              onChange={(next) => setFieldValue(field.fieldKey ?? '', next)}
            />
          ))}
        </div>

        {readOnly && record.submittedAt ? (
          <StatusNotice tone="success" title={t('applications.submittedTitle')}>
            <p>
              {t('applications.submittedAt', {
                date: new Intl.DateTimeFormat(undefined, {
                  dateStyle: 'medium',
                  timeStyle: 'short',
                }).format(new Date(record.submittedAt)),
              })}
            </p>
            {record.submittedByUserId ? (
              <p className="mt-1 text-xs text-muted-foreground">
                {t('applications.submittedBy', { userId: record.submittedByUserId })}
              </p>
            ) : null}
          </StatusNotice>
        ) : null}
      </ManagedDialogBody>
    </ManagedDialog>
  );
}

function RecordField({
  field,
  values,
  evaluations,
  disabled,
  onChange,
}: {
  field: BusinessObjectRecordField;
  values: string[];
  evaluations: BusinessObjectRecordRuleEvaluation[];
  disabled: boolean;
  onChange: (values: string[]) => void;
}) {
  const { t } = useTranslation();
  const fieldKey = field.fieldKey ?? '';
  const label = field.label ?? fieldKey;
  const type = field.fieldType ?? 'Text';
  const hasMismatch = evaluations.some((evaluation) => evaluation.isMatch === false);
  const labelWithStatus = (
    <div className="flex flex-wrap items-center justify-between gap-2">
      <FieldLabel htmlFor={`application-${fieldKey}`}>
        {label}
        {field.rules && field.rules.length > 0 ? (
          <span className="ml-1 text-muted-foreground" title={t('applications.rulesAttached')}>
            · {field.rules.length}
          </span>
        ) : null}
      </FieldLabel>
      {evaluations.length > 0 ? (
        <StatusBadge tone={hasMismatch ? 'muted' : 'success'}>
          {hasMismatch ? t('applications.ruleNotMatched') : t('applications.ruleMatched')}
        </StatusBadge>
      ) : null}
    </div>
  );

  if (type === 'Boolean') {
    return (
      <div className="space-y-3">
        <Field orientation="horizontal" className="items-start rounded-lg border p-3">
          <Checkbox
            id={`application-${fieldKey}`}
            checked={values[0] === 'true'}
            disabled={disabled}
            onCheckedChange={(checked) => onChange(checked ? ['true'] : ['false'])}
          />
          <div className="space-y-1">
            <Label htmlFor={`application-${fieldKey}`}>{label}</Label>
            <p className="text-xs text-muted-foreground">{fieldKey}</p>
          </div>
        </Field>
        {evaluations.length > 0 ? <RuleEvidence evaluations={evaluations} /> : null}
      </div>
    );
  }

  return (
    <Field>
      {type === 'Choice' && field.choiceConfiguration?.selectionMode === 'Multiple'
        ? null
        : labelWithStatus}
      {type === 'Choice' && field.choiceConfiguration?.selectionMode === 'Multiple' ? null : (
        <FieldDescription>{fieldKey}</FieldDescription>
      )}
      {type === 'Choice' ? (
        field.choiceConfiguration?.selectionMode === 'Multiple' ? (
          <fieldset aria-labelledby={`application-${fieldKey}-label`} className="grid gap-2">
            <legend id={`application-${fieldKey}-label`} className="text-sm font-medium">
              {label}
              {field.rules && field.rules.length > 0 ? (
                <span
                  className="ml-1 text-muted-foreground"
                  title={t('applications.rulesAttached')}
                >
                  · {field.rules.length}
                </span>
              ) : null}
            </legend>
            <div className="flex flex-wrap items-center justify-between gap-2">
              {evaluations.length > 0 ? (
                <StatusBadge tone={hasMismatch ? 'muted' : 'success'}>
                  {hasMismatch ? t('applications.ruleNotMatched') : t('applications.ruleMatched')}
                </StatusBadge>
              ) : null}
            </div>
            <FieldDescription>{fieldKey}</FieldDescription>
            {choiceOptions(field).map((option) => {
              const optionId = `application-${fieldKey}-option-${option.value}`;
              return (
                <Field
                  key={option.value}
                  orientation="horizontal"
                  className="items-center rounded-lg border p-3"
                >
                  <Checkbox
                    id={optionId}
                    checked={values.includes(option.value)}
                    disabled={disabled}
                    onCheckedChange={(checked) =>
                      onChange(
                        checked
                          ? [...values, option.value]
                          : values.filter((value) => value !== option.value),
                      )
                    }
                  />
                  <Label htmlFor={optionId}>{option.label}</Label>
                </Field>
              );
            })}
          </fieldset>
        ) : (
          <div className="flex items-center gap-2">
            <Select
              value={values[0] || undefined}
              disabled={disabled}
              onValueChange={(value) => onChange(value ? [value] : [])}
            >
              <SelectTrigger id={`application-${fieldKey}`} className="min-w-0 flex-1">
                <SelectValue placeholder={t('applications.chooseValue')}>
                  {choiceOptions(field).find((option) => option.value === values[0])?.label ??
                    t('applications.chooseValue')}
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                {choiceOptions(field).map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={disabled || values.length === 0}
              onClick={() => onChange([])}
            >
              {t('applications.clearValue')}
            </Button>
          </div>
        )
      ) : type === 'Text' && fieldKey === 'purpose' ? (
        <Textarea
          id={`application-${fieldKey}`}
          value={values[0] ?? ''}
          disabled={disabled}
          onChange={(event) => onChange([event.currentTarget.value])}
          rows={3}
        />
      ) : (
        <Input
          id={`application-${fieldKey}`}
          type={inputType(type)}
          value={type === 'DateTime' ? dateTimeInputValue(values[0] ?? '') : (values[0] ?? '')}
          disabled={disabled}
          onChange={(event) =>
            onChange([
              type === 'DateTime'
                ? dateTimeValueFromInput(event.currentTarget.value)
                : event.currentTarget.value,
            ])
          }
        />
      )}
      {evaluations.length > 0 ? <RuleEvidence evaluations={evaluations} /> : null}
    </Field>
  );
}

function RuleEvidence({ evaluations }: { evaluations: BusinessObjectRecordRuleEvaluation[] }) {
  const { t } = useTranslation();

  return (
    <div className="space-y-2 rounded-lg border border-dashed bg-muted/10 p-3">
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {t('applications.ruleEvidence')}
      </p>
      {evaluations.map((evaluation) => (
        <div
          key={`${evaluation.bindingId}-${evaluation.bindingRevision}-${evaluation.definitionKey}`}
          className="space-y-2 rounded-md border bg-background p-3 text-sm"
        >
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="font-medium text-foreground">
              {evaluation.definitionKey ?? t('applications.unknownRule')} v
              {evaluation.definitionVersion ?? 0}
            </span>
            <StatusBadge tone={evaluation.isMatch === false ? 'muted' : 'success'}>
              {evaluation.isMatch === false
                ? t('applications.ruleNotMatched')
                : t('applications.ruleMatched')}
            </StatusBadge>
          </div>
          <p className="text-xs text-muted-foreground">
            {t('applications.ruleBindingEvidence', {
              bindingId: evaluation.bindingId,
              revision: evaluation.bindingRevision ?? 0,
            })}
          </p>
          {evaluation.diagnostics && evaluation.diagnostics.length > 0 ? (
            <ul className="space-y-1 text-xs text-muted-foreground">
              {evaluation.diagnostics.map((diagnostic) => (
                <li key={diagnostic.nodeId ?? 'unknown-node'}>
                  {diagnostic.nodeId ?? t('applications.unknownRuleNode')}:{' '}
                  {diagnostic.isMatch
                    ? t('applications.diagnosticMatched')
                    : t('applications.diagnosticNotMatched')}
                </li>
              ))}
            </ul>
          ) : (
            <p className="text-xs text-muted-foreground">{t('applications.noRuleDiagnostics')}</p>
          )}
        </div>
      ))}
    </div>
  );
}

function choiceOptions(field: BusinessObjectRecordField) {
  return (field.choiceConfiguration?.options ?? []).flatMap((option) =>
    option.optionKey ? [{ value: option.optionKey, label: option.label ?? option.optionKey }] : [],
  );
}

function inputType(fieldType: BusinessObjectRecordField['fieldType']) {
  switch (fieldType) {
    case 'Integer':
    case 'Decimal':
      return 'number';
    case 'Date':
      return 'date';
    case 'DateTime':
      return 'datetime-local';
    default:
      return 'text';
  }
}

function dateTimeInputValue(value: string) {
  if (!value) return '';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  const pad = (part: number) => String(part).padStart(2, '0');
  return `${parsed.getFullYear()}-${pad(parsed.getMonth() + 1)}-${pad(parsed.getDate())}T${pad(parsed.getHours())}:${pad(parsed.getMinutes())}`;
}

function dateTimeValueFromInput(value: string) {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toISOString();
}

function normalizeValues(values?: Record<string, string[]> | null) {
  return Object.fromEntries(
    Object.entries(values ?? {}).map(([key, fieldValues]) => [key, [...(fieldValues ?? [])]]),
  );
}

function updateRecordCache(
  queryClient: ReturnType<typeof useQueryClient>,
  record: BusinessObjectRecordDetail,
) {
  if (record.id) {
    queryClient.setQueryData(applicationQueryKeys.detail(record.id), record);
  }
}

function readApiError(error: unknown, fallback: string): string {
  if (!(error instanceof ApiError) || typeof error.data !== 'object' || error.data === null) {
    return fallback;
  }
  const detail = (error.data as { detail?: unknown }).detail;
  return typeof detail === 'string' && detail ? detail : fallback;
}
