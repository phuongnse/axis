import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, Save, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ManagedDialog, ManagedDialogBody } from '@/components/shared/ManagedDialog';
import { ManagedDialogTabs } from '@/components/shared/ManagedDialogTabs';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type * as ApiTypes from '@/lib/api-generated';
import {
  createRuleDefinition,
  getRuleDefinition,
  type RuleDefinitionDetail,
  ruleDefinitionQueryKeys,
  ruleExpressionLanguageQueryOptions,
  saveRuleDefinitionDraft,
} from '../api';
import { replaceConditionInputReferences, toDraftInputs } from '../condition-references';
import { valueTypeLabel } from '../reference';
import { RuleBehaviorSummary, RuleLogicPreview, RuleOutputSummary } from './RuleBehaviorSummary';
import { RuleBindingUsagePanel } from './RuleBindingUsagePanel';
import { RuleConditionComposer } from './RuleConditionComposer';
import { RuleOriginBadge } from './RuleOriginBadge';

type InputDefinition = ApiTypes.RuleDraftInputDefinitionDto;
type EditableInput = InputDefinition & { clientId: string };
type ValueType = ApiTypes.RuleValueType;

const valueTypes: ValueType[] = ['Text', 'Integer', 'Decimal', 'Date', 'DateTime', 'Boolean'];

export function RuleEditorDialog({
  definitionKey,
  open,
  onOpenChange,
  onCreated,
}: {
  definitionKey: string | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onCreated?: (definition: RuleDefinitionDetail) => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const creating = definitionKey === null;
  const detailQuery = useQuery({
    queryKey: ruleDefinitionQueryKeys.detail(definitionKey ?? ''),
    queryFn: () => getRuleDefinition(definitionKey as string),
    enabled: open && !creating,
  });
  const languageQuery = useQuery({ ...ruleExpressionLanguageQueryOptions(), enabled: open });
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [inputs, setInputs] = useState<EditableInput[]>([]);
  const [condition, setCondition] = useState<ApiTypes.RuleConditionNodeDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [activeSection, setActiveSection] = useState('general');
  const inputContract = inputs.map(({ clientId: _clientId, ...input }) => input);

  useEffect(() => {
    if (!open) return;
    const detail = detailQuery.data;
    setName(detail?.name ?? '');
    setDescription(detail?.description ?? '');
    const sourceInputs = detail?.inputs ?? [];
    setInputs(
      toDraftInputs(sourceInputs).map((input) => ({ ...input, clientId: crypto.randomUUID() })),
    );
    setCondition(
      replaceConditionInputReferences(
        detail?.condition,
        new Map(
          sourceInputs.flatMap((input) =>
            input.key && input.label ? [[input.key, input.label] as const] : [],
          ),
        ),
      ),
    );
    setError(null);
  }, [open, detailQuery.data]);

  const saveMutation = useMutation({
    mutationFn: async () => {
      setError(null);
      let detail = detailQuery.data;
      if (!detail) {
        detail = await createRuleDefinition({ name, description });
      }
      if (!condition) {
        setActiveSection('behavior');
        throw new Error(t('rules.conditionRequired'));
      }
      const saved = await saveRuleDefinitionDraft(detail.definitionKey ?? '', {
        expectedRevision: detail.revision ?? 1,
        name,
        description,
        inputs: inputContract,
        condition,
      });
      return saved;
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ruleDefinitionQueryKeys.all });
      queryClient.setQueryData(ruleDefinitionQueryKeys.detail(saved.definitionKey ?? ''), saved);
      onCreated?.(saved);
      if (!onCreated) onOpenChange(false);
    },
    onError: (cause) => setError(cause instanceof Error ? cause.message : t('rules.saveError')),
  });

  const detail = detailQuery.data;
  const readOnly = Boolean(detail && detail.origin === 'System');
  return (
    <ManagedDialog
      open={open}
      onOpenChange={onOpenChange}
      title={detail?.name ?? (creating ? t('rules.createTitle') : t('rules.editorTitle'))}
      titleAccessory={
        detail?.origin ? (
          <>
            <RuleOriginBadge origin={detail.origin} />
            <StatusBadge tone={detail.status === 'Published' ? 'success' : 'neutral'}>
              {detail.status}
            </StatusBadge>
          </>
        ) : null
      }
      footer={
        readOnly ? (
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            {t('app.close')}
          </Button>
        ) : (
          <Button
            type="button"
            onClick={() => saveMutation.mutate()}
            disabled={saveMutation.isPending || !name.trim()}
          >
            <Save data-icon="inline-start" />
            {t('rules.save')}
          </Button>
        )
      }
    >
      <ManagedDialogBody>
        {detailQuery.isLoading ? <p role="status">{t('rules.loadingRule')}</p> : null}
        {error ? (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        ) : null}
        {!detailQuery.isLoading || creating ? (
          <ManagedDialogTabs
            label={t('rules.definitionSections')}
            generalLabel={t('dialog.general')}
            activeSection={activeSection}
            onActiveSectionChange={setActiveSection}
            general={
              readOnly && detail ? (
                <dl className="grid gap-5 sm:grid-cols-2">
                  <RuleDetail label={t('rules.name')} value={detail.name ?? name} />
                  <RuleDetail
                    label={t('rules.publishedVersion')}
                    value={String(detail.latestPublishedVersion ?? 1)}
                  />
                  <div className="sm:col-span-2">
                    <RuleDetail
                      label={t('rules.description')}
                      value={detail.description ?? description}
                    />
                  </div>
                </dl>
              ) : (
                <div className="space-y-5">
                  <div className="grid gap-4 sm:grid-cols-2">
                    <Field>
                      <FieldLabel htmlFor="rule-name">{t('rules.name')}</FieldLabel>
                      <Input
                        id="rule-name"
                        value={name}
                        onChange={(event) => setName(event.target.value)}
                      />
                    </Field>
                    <Field>
                      <FieldLabel htmlFor="rule-description">{t('rules.description')}</FieldLabel>
                      <Input
                        id="rule-description"
                        value={description}
                        onChange={(event) => setDescription(event.target.value)}
                      />
                    </Field>
                  </div>
                  {detail?.latestPublishedVersion ? (
                    <dl className="grid gap-5 sm:grid-cols-2">
                      <RuleDetail
                        label={t('rules.publishedVersion')}
                        value={String(detail.latestPublishedVersion)}
                      />
                    </dl>
                  ) : null}
                </div>
              )
            }
            sections={[
              {
                id: 'behavior',
                label: t('rules.ruleBehavior'),
                content:
                  readOnly && detail ? (
                    <RuleBehaviorSummary
                      condition={detail.condition}
                      output={detail.output}
                      expressionLanguageVersion={detail.expressionLanguageVersion}
                      inputs={detail.inputs}
                    />
                  ) : (
                    <div className="space-y-4">
                      <section className="space-y-2">
                        <div className="flex items-center justify-between">
                          <h3 className="text-sm font-semibold">{t('rules.inputs')}</h3>
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            onClick={() =>
                              setInputs([
                                ...inputs,
                                {
                                  clientId: crypto.randomUUID(),
                                  label: '',
                                  types: ['Text'],
                                  isRequired: false,
                                  allowMultiple: false,
                                  allowedValues: [],
                                },
                              ])
                            }
                          >
                            <Plus data-icon="inline-start" />
                            {t('rules.addInput')}
                          </Button>
                        </div>
                        <div className="space-y-2">
                          {inputs.map((input, index) => (
                            <InputRow
                              key={input.clientId}
                              input={input}
                              inputId={input.clientId}
                              language={languageQuery.data}
                              onChange={(next) =>
                                updateInput(inputs, index, input, next, setInputs, setCondition)
                              }
                              onRemove={() =>
                                setInputs(
                                  inputs.filter((_, candidateIndex) => candidateIndex !== index),
                                )
                              }
                            />
                          ))}
                        </div>
                      </section>
                      <RuleConditionComposer
                        condition={condition}
                        inputs={inputContract}
                        language={languageQuery.data}
                        onChange={setCondition}
                      />
                      {condition ? (
                        <section className="space-y-1.5 border-t pt-4">
                          <h3 className="text-sm font-semibold">{t('rules.expressionPreview')}</h3>
                          <RuleLogicPreview
                            condition={condition}
                            expressionLanguageVersion={languageQuery.data?.version}
                            inputs={inputContract}
                          />
                        </section>
                      ) : null}
                      <section className="space-y-1.5 border-t pt-4">
                        <h3 className="text-sm font-semibold">{t('rules.outputs')}</h3>
                        <RuleOutputSummary output={detail?.output} />
                      </section>
                    </div>
                  ),
              },
              ...(detail?.definitionKey && detail.latestPublishedVersion
                ? [
                    {
                      id: 'usage',
                      label: t('rules.usage'),
                      content: (
                        <RuleBindingUsagePanel
                          definitionKey={detail.definitionKey}
                          version={detail.latestPublishedVersion}
                          active={activeSection === 'usage'}
                        />
                      ),
                    },
                  ]
                : []),
            ]}
            systemInfo={
              detail
                ? {
                    label: t('dialog.systemInfo'),
                    content: (
                      <dl className="grid gap-5 sm:grid-cols-2">
                        <RuleDetail
                          label={t('rules.definitionKey')}
                          value={detail.definitionKey ?? '—'}
                        />
                        <RuleDetail
                          label={t('rules.expressionLanguage')}
                          value={String(detail.expressionLanguageVersion ?? 1)}
                        />
                      </dl>
                    ),
                  }
                : undefined
            }
          />
        ) : null}
      </ManagedDialogBody>
    </ManagedDialog>
  );
}

function RuleDetail({ label, value }: { label: string; value: string }) {
  return (
    <div className="space-y-2">
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="text-sm leading-relaxed font-medium text-foreground">{value}</dd>
    </div>
  );
}

function InputRow({
  input,
  inputId,
  language,
  onChange,
  onRemove,
}: {
  input: InputDefinition;
  inputId: string;
  language: ApiTypes.RuleExpressionLanguageDto | undefined;
  onChange: (input: InputDefinition) => void;
  onRemove: () => void;
}) {
  const { t, i18n } = useTranslation();
  return (
    <fieldset className="grid gap-3 rounded-lg border p-3 sm:grid-cols-12">
      <Field className="sm:col-span-5">
        <FieldLabel htmlFor={`${inputId}-label`}>{t('rules.inputLabel')}</FieldLabel>
        <Input
          id={`${inputId}-label`}
          aria-label={t('rules.inputLabel')}
          value={input.label ?? ''}
          placeholder={t('rules.inputLabelPlaceholder')}
          onChange={(event) => onChange({ ...input, label: event.target.value })}
        />
      </Field>
      <Field className="sm:col-span-3">
        <FieldLabel htmlFor={`${inputId}-type`}>{t('rules.type')}</FieldLabel>
        <Select
          value={input.types?.[0] ?? 'Text'}
          onValueChange={(value) => onChange({ ...input, types: [value as ValueType] })}
        >
          <SelectTrigger id={`${inputId}-type`} aria-label="Input type">
            <SelectValue>{valueTypeLabel(language, input.types?.[0], i18n.language)}</SelectValue>
          </SelectTrigger>
          <SelectContent>
            {valueTypes.map((type) => (
              <SelectItem key={type} value={type}>
                {valueTypeLabel(language, type, i18n.language)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </Field>
      <Field className="sm:col-span-4">
        <FieldLabel htmlFor={`${inputId}-allowed-values`}>{t('rules.allowedValues')}</FieldLabel>
        <Input
          id={`${inputId}-allowed-values`}
          value={(input.allowedValues ?? []).join(', ')}
          placeholder={t('rules.allowedValuesPlaceholder')}
          onChange={(event) =>
            onChange({
              ...input,
              allowedValues: event.target.value
                .split(',')
                .map((value) => value.trim())
                .filter(Boolean),
            })
          }
        />
      </Field>
      <Field orientation="horizontal" className="sm:col-span-3">
        <Checkbox
          id={`${inputId}-required`}
          checked={input.isRequired ?? false}
          onCheckedChange={(checked) => onChange({ ...input, isRequired: checked === true })}
        />
        <FieldLabel htmlFor={`${inputId}-required`}>{t('rules.inputRequired')}</FieldLabel>
      </Field>
      <Field orientation="horizontal" className="sm:col-span-3">
        <Checkbox
          id={`${inputId}-multiple`}
          checked={input.allowMultiple ?? false}
          onCheckedChange={(checked) => onChange({ ...input, allowMultiple: checked === true })}
        />
        <FieldLabel htmlFor={`${inputId}-multiple`}>{t('rules.inputMultiple')}</FieldLabel>
      </Field>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        aria-label="Remove input"
        className="sm:col-span-6 sm:justify-self-end"
        onClick={onRemove}
      >
        <Trash2 />
      </Button>
    </fieldset>
  );
}

function updateInput(
  inputs: EditableInput[],
  index: number,
  current: EditableInput,
  next: InputDefinition,
  setInputs: (next: EditableInput[]) => void,
  setCondition: (
    update: (current: ApiTypes.RuleConditionNodeDto | null) => ApiTypes.RuleConditionNodeDto | null,
  ) => void,
) {
  setInputs(
    inputs.map((candidate, candidateIndex) =>
      candidateIndex === index ? { ...next, clientId: current.clientId } : candidate,
    ),
  );
  const previousLabel = current.label?.trim();
  const nextLabel = next.label?.trim();
  if (!previousLabel || !nextLabel || previousLabel === nextLabel) return;
  setCondition((currentCondition) =>
    currentCondition
      ? replaceConditionInputReferences(currentCondition, new Map([[previousLabel, nextLabel]]))
      : null,
  );
}
