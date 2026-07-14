import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AlertCircle, Archive, Braces, Play, Plus, Save, Send, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { StatusNotice } from '@/components/shared/StatusNotice';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import { ApiError } from '@/lib/api';
import type { components } from '@/lib/api-types';
import {
  archiveRuleDefinition,
  getRuleDefinition,
  publishRuleDefinition,
  type RuleConditionNode,
  type RuleContextSchema,
  type RuleDecision,
  type RuleDefinitionDetail,
  type RuleLogicalOperator,
  type RulePredicateOperator,
  type RuleScope,
  type RuleSeverity,
  type RuleValueType,
  ruleContextSchemasQueryOptions,
  ruleDefinitionQueryKeys,
  saveRuleDefinitionDraft,
  simulateRuleDefinition,
  startRuleDefinitionDraft,
} from '../api';
import { fieldTypeTranslationKey } from '../metadata';

type RuleOperandKind = components['schemas']['RuleOperandKind'];
type RuleOutcomeKind = components['schemas']['RuleOutcomeKind'];
type RuleValue = components['schemas']['RuleValueDto'];

interface EditableParameter {
  id: string;
  key: string;
  type: RuleValueType;
  isRequired: boolean;
  allowMultiple: boolean;
  allowedValues: string;
}

interface EditableGroup {
  id: string;
  kind: 'group';
  operator: RuleLogicalOperator;
  children: EditableNode[];
}

interface EditablePredicate {
  id: string;
  kind: 'predicate';
  contextPath: string;
  operator: RulePredicateOperator;
  rightKind: Exclude<RuleOperandKind, 'Context'> | 'Context';
  rightReference: string;
  rightValue: string;
}

type EditableNode = EditableGroup | EditablePredicate;

interface EditorState {
  name: string;
  description: string;
  scope: RuleScope;
  contextKey: string;
  contextSchemaVersion: number;
  outcomeKind: RuleOutcomeKind;
  parameters: EditableParameter[];
  condition: EditableGroup;
  violationCode: string;
  severity: RuleSeverity;
  message: string;
  decision: RuleDecision;
}

const orderedOperators: RulePredicateOperator[] = [
  'Equal',
  'NotEqual',
  'GreaterThan',
  'GreaterThanOrEqual',
  'LessThan',
  'LessThanOrEqual',
  'IsNull',
  'IsNotNull',
];
const unsetSelectValue = '__unset__';

export function RuleEditorDialog({
  definitionKey,
  open,
  onOpenChange,
}: {
  definitionKey: string | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [editor, setEditor] = useState<EditorState | null>(null);
  const [feedback, setFeedback] = useState<{
    variant: 'success' | 'destructive';
    text: string;
  } | null>(null);
  const [sampleContext, setSampleContext] = useState<Record<string, string>>({});
  const [sampleParameters, setSampleParameters] = useState<Record<string, string>>({});
  const [simulation, setSimulation] = useState<Awaited<
    ReturnType<typeof simulateRuleDefinition>
  > | null>(null);
  const [archiveOpen, setArchiveOpen] = useState(false);
  const [publishOpen, setPublishOpen] = useState(false);
  const [discardOpen, setDiscardOpen] = useState(false);

  const detailQuery = useQuery({
    queryKey: ruleDefinitionQueryKeys.detail(definitionKey ?? ''),
    queryFn: () => {
      if (!definitionKey) throw new Error(t('rules.editorUnavailable'));
      return getRuleDefinition(definitionKey);
    },
    enabled: open && Boolean(definitionKey),
  });
  const schemasQuery = useQuery({ ...ruleContextSchemasQueryOptions(), enabled: open });
  const detail = detailQuery.data;
  const schema = useMemo(
    () =>
      (schemasQuery.data ?? []).find(
        (candidate) =>
          candidate.contextKey === editor?.contextKey &&
          candidate.version === editor?.contextSchemaVersion,
      ),
    [editor?.contextKey, editor?.contextSchemaVersion, schemasQuery.data],
  );

  useEffect(() => {
    if (!detail) return;
    setEditor(toEditorState(detail, schemasQuery.data ?? []));
  }, [detail, schemasQuery.data]);

  useEffect(() => {
    if (!open || !definitionKey) return;
    setFeedback(null);
    setSimulation(null);
    setSampleContext({});
    setSampleParameters({});
  }, [definitionKey, open]);

  const saveMutation = useMutation({
    mutationFn: async (state: EditorState) => {
      if (!detail?.definitionKey || detail.revision === undefined || !schema) {
        throw new Error(t('rules.editorUnavailable'));
      }
      const validationError = validateEditor(state, schema);
      if (validationError) throw new Error(validationError);
      return saveRuleDefinitionDraft(detail.definitionKey, {
        expectedRevision: detail.revision,
        name: state.name.trim(),
        description: state.description.trim(),
        scope: state.scope,
        contextKey: state.contextKey,
        contextSchemaVersion: state.contextSchemaVersion,
        outcomeKind: state.outcomeKind,
        parameters: state.parameters.map((parameter) => ({
          key: parameter.key.trim(),
          type: parameter.type,
          isRequired: parameter.isRequired,
          allowMultiple: parameter.allowMultiple,
          allowedValues: splitValues(parameter.allowedValues),
        })),
        condition: toConditionDto(state.condition, schema, state.parameters),
        outcome:
          state.outcomeKind === 'Validation'
            ? {
                kind: 'Validation',
                violationCode: state.violationCode.trim(),
                severity: state.severity,
                message: state.message.trim(),
                decision: undefined,
              }
            : {
                kind: 'Decision',
                violationCode: undefined,
                severity: undefined,
                message: undefined,
                decision: state.decision,
              },
      });
    },
    onSuccess: async (saved) => {
      setArchiveOpen(false);
      setPublishOpen(false);
      setSimulation(null);
      setDetailCache(queryClient, saved);
      await queryClient.invalidateQueries({ queryKey: ruleDefinitionQueryKeys.all });
      setFeedback({ variant: 'success', text: t('rules.saved') });
    },
    onError: (error) =>
      setFeedback({ variant: 'destructive', text: readError(error, t('rules.saveError')) }),
  });

  const lifecycleMutation = useMutation({
    mutationFn: async (action: 'publish' | 'draft' | 'archive') => {
      if (!detail?.definitionKey || detail.revision === undefined) {
        throw new Error(t('rules.editorUnavailable'));
      }
      if (action === 'publish') {
        const saved = editor ? await saveMutation.mutateAsync(editor) : detail;
        if (!saved.definitionKey || saved.revision === undefined) {
          throw new Error(t('rules.editorUnavailable'));
        }
        return publishRuleDefinition(saved.definitionKey, saved.revision);
      }
      return action === 'draft'
        ? startRuleDefinitionDraft(detail.definitionKey, detail.revision)
        : archiveRuleDefinition(detail.definitionKey, detail.revision);
    },
    onSuccess: async (saved) => {
      setDetailCache(queryClient, saved);
      await queryClient.invalidateQueries({ queryKey: ruleDefinitionQueryKeys.all });
      setFeedback({ variant: 'success', text: t('rules.lifecycleUpdated') });
    },
    onError: (error) =>
      setFeedback({ variant: 'destructive', text: readError(error, t('rules.lifecycleError')) }),
  });

  const simulateMutation = useMutation({
    mutationFn: async () => {
      if (!editor || !detail?.definitionKey || !schema)
        throw new Error(t('rules.editorUnavailable'));
      const saved = await saveMutation.mutateAsync(editor);
      if (!saved.definitionKey) throw new Error(t('rules.editorUnavailable'));
      return simulateRuleDefinition(saved.definitionKey, {
        definitionVersion: null,
        parameters: Object.fromEntries(
          editor.parameters
            .filter((parameter) => sampleParameters[parameter.key.trim()]?.trim())
            .map((parameter) => [
              parameter.key.trim(),
              typedRuleValue(
                parameter.type,
                sampleParameters[parameter.key.trim()] ?? '',
                parameter.allowMultiple,
              ),
            ]),
        ),
        context: Object.fromEntries(
          (schema.fields ?? []).flatMap((field) => {
            const path = field.path;
            if (!path || !sampleContext[path]?.trim()) return [];
            return [
              [
                path,
                typedRuleValue(
                  field.type ?? 'Text',
                  sampleContext[path] ?? '',
                  field.allowMultiple,
                ),
              ] as const,
            ];
          }),
        ),
        correlationId: crypto.randomUUID(),
      });
    },
    onSuccess: (result) => {
      setSimulation(result);
      setFeedback(null);
    },
    onError: (error) =>
      setFeedback({ variant: 'destructive', text: readError(error, t('rules.simulationError')) }),
  });

  const busy = saveMutation.isPending || lifecycleMutation.isPending || simulateMutation.isPending;
  const baselineEditor = useMemo(
    () => (detail ? toEditorState(detail, schemasQuery.data ?? []) : null),
    [detail, schemasQuery.data],
  );
  const isDirty =
    detail?.status === 'Draft' &&
    editor !== null &&
    baselineEditor !== null &&
    JSON.stringify(editor) !== JSON.stringify(baselineEditor);
  const availableSchemas = (schemasQuery.data ?? []).filter(
    (candidate) => candidate.scope === editor?.scope,
  );

  function requestOpenChange(nextOpen: boolean) {
    if (nextOpen) {
      onOpenChange(true);
      return;
    }
    if (busy) return;
    if (isDirty) {
      setDiscardOpen(true);
      return;
    }
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={requestOpenChange}>
      <DialogContent>
        <DialogHeader>
          <div className="flex min-w-0 flex-wrap items-center gap-2">
            <DialogTitle>{detail?.name ?? t('rules.loadingRule')}</DialogTitle>
            {detail ? <LifecycleBadge detail={detail} /> : null}
          </div>
          <DialogDescription>{detail?.description ?? t('rules.loadingRule')}</DialogDescription>
        </DialogHeader>

        <div data-slot="dialog-body" className="max-h-96 min-h-0 overflow-y-auto">
          {detailQuery.isError ? (
            <Alert variant="destructive">
              <AlertCircle className="size-4" aria-hidden />
              <AlertTitle>{t('rules.loadErrorTitle')}</AlertTitle>
              <AlertDescription>{t('rules.loadErrorBody')}</AlertDescription>
            </Alert>
          ) : null}

          {detail && editor && schema ? (
            <div className="space-y-6">
              {feedback ? (
                <StatusNotice tone={feedback.variant}>{feedback.text}</StatusNotice>
              ) : null}
              <RuleIdentitySection
                editor={editor}
                schemas={availableSchemas}
                disabled={detail.status !== 'Draft' || busy}
                onChange={setEditor}
              />
              <ParameterSection
                parameters={editor.parameters}
                disabled={detail.status !== 'Draft' || busy}
                onChange={(parameters) => setEditor({ ...editor, parameters })}
              />
              <ConditionSection
                condition={editor.condition}
                schema={schema}
                parameters={editor.parameters}
                disabled={detail.status !== 'Draft' || busy}
                onChange={(condition) => setEditor({ ...editor, condition })}
              />
              <OutcomeSection
                editor={editor}
                disabled={detail.status !== 'Draft' || busy}
                onChange={setEditor}
              />
              <SimulationSection
                schema={schema}
                parameters={editor.parameters}
                contextValues={sampleContext}
                parameterValues={sampleParameters}
                result={simulation}
                disabled={detail.status !== 'Draft' || busy}
                onContextChange={setSampleContext}
                onParameterChange={setSampleParameters}
                onSimulate={() => simulateMutation.mutate()}
              />
              <VersionHistory detail={detail} />
            </div>
          ) : detailQuery.isLoading ? (
            <p className="text-sm text-muted-foreground">{t('rules.loadingRule')}</p>
          ) : null}
        </div>

        {detail && editor ? (
          <DialogFooter>
            <div>
              {detail.status !== 'Archived' && detail.latestPublishedVersion ? (
                <Button
                  type="button"
                  variant="outline"
                  disabled={busy}
                  onClick={() => setArchiveOpen(true)}
                >
                  <Archive className="size-4" aria-hidden />
                  {t('rules.archive')}
                </Button>
              ) : null}
            </div>
            <div className="flex gap-2">
              {detail.status === 'Draft' ? (
                <>
                  <Button
                    type="button"
                    variant="outline"
                    disabled={busy}
                    onClick={() => saveMutation.mutate(editor)}
                  >
                    <Save className="size-4" aria-hidden />
                    {t('rules.saveDraft')}
                  </Button>
                  <Button type="button" disabled={busy} onClick={() => setPublishOpen(true)}>
                    <Send className="size-4" aria-hidden />
                    {t('rules.publish')}
                  </Button>
                </>
              ) : detail.status === 'Published' ? (
                <Button
                  type="button"
                  disabled={busy}
                  onClick={() => lifecycleMutation.mutate('draft')}
                >
                  <Braces className="size-4" aria-hidden />
                  {t('rules.startRevision')}
                </Button>
              ) : null}
            </div>
          </DialogFooter>
        ) : null}

        <AlertDialog open={archiveOpen} onOpenChange={setArchiveOpen}>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>{t('rules.archiveTitle')}</AlertDialogTitle>
              <AlertDialogDescription>{t('rules.archiveDescription')}</AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel disabled={busy}>{t('app.cancel')}</AlertDialogCancel>
              <AlertDialogAction
                variant="destructive"
                disabled={busy}
                onClick={() => lifecycleMutation.mutate('archive')}
              >
                {t('rules.archive')}
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
        <AlertDialog open={publishOpen} onOpenChange={setPublishOpen}>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>{t('rules.publishTitle')}</AlertDialogTitle>
              <AlertDialogDescription>
                {t('rules.publishDescription', {
                  version: (detail?.latestPublishedVersion ?? 0) + 1,
                })}
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel disabled={busy}>{t('app.cancel')}</AlertDialogCancel>
              <AlertDialogAction
                disabled={busy}
                onClick={() => lifecycleMutation.mutate('publish')}
              >
                {t('rules.publish')}
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
        <AlertDialog open={discardOpen} onOpenChange={setDiscardOpen}>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>{t('rules.discardTitle')}</AlertDialogTitle>
              <AlertDialogDescription>{t('rules.discardDescription')}</AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>{t('rules.keepEditing')}</AlertDialogCancel>
              <AlertDialogAction
                variant="destructive"
                onClick={() => {
                  setDiscardOpen(false);
                  setEditor(baselineEditor);
                  onOpenChange(false);
                }}
              >
                {t('rules.discard')}
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      </DialogContent>
    </Dialog>
  );
}

function RuleIdentitySection({
  editor,
  schemas,
  disabled,
  onChange,
}: {
  editor: EditorState;
  schemas: RuleContextSchema[];
  disabled: boolean;
  onChange: (editor: EditorState) => void;
}) {
  const { t } = useTranslation();
  return (
    <EditorSection title={t('rules.definitionSection')} description={t('rules.definitionHelp')}>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field>
          <FieldLabel htmlFor="rule-editor-name">{t('rules.name')}</FieldLabel>
          <Input
            id="rule-editor-name"
            value={editor.name}
            disabled={disabled}
            onChange={(event) => onChange({ ...editor, name: event.target.value })}
          />
        </Field>
        <Field>
          <FieldLabel htmlFor="rule-editor-context">{t('rules.context')}</FieldLabel>
          <Select
            value={editor.contextKey}
            disabled={disabled}
            onValueChange={(value) => {
              const schema = schemas.find((candidate) => candidate.contextKey === value);
              if (!schema) return;
              onChange({
                ...editor,
                contextKey: schema.contextKey ?? '',
                contextSchemaVersion: schema.version ?? 1,
                condition: defaultCondition(schema),
              });
            }}
          >
            <SelectTrigger id="rule-editor-context">
              <SelectValue>
                {(value) =>
                  schemas.find((schema) => schema.contextKey === value)?.displayName ?? value
                }
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              {schemas.map((schema) => (
                <SelectItem
                  key={`${schema.contextKey}:${schema.version}`}
                  value={schema.contextKey}
                >
                  {schema.displayName}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </Field>
        <Field className="sm:col-span-2">
          <FieldLabel htmlFor="rule-editor-description">{t('rules.description')}</FieldLabel>
          <Textarea
            id="rule-editor-description"
            value={editor.description}
            disabled={disabled}
            onChange={(event) => onChange({ ...editor, description: event.target.value })}
          />
        </Field>
      </div>
    </EditorSection>
  );
}

function ParameterSection({
  parameters,
  disabled,
  onChange,
}: {
  parameters: EditableParameter[];
  disabled: boolean;
  onChange: (parameters: EditableParameter[]) => void;
}) {
  const { t } = useTranslation();
  return (
    <EditorSection title={t('rules.parameters')} description={t('rules.parametersHelp')}>
      <div className="space-y-2">
        {parameters.map((parameter) => (
          <div
            key={parameter.id}
            className="grid gap-2 border-b border-border pb-3 last:border-0 last:pb-0 sm:grid-cols-2 lg:grid-cols-3 lg:items-end"
          >
            <Field>
              <FieldLabel htmlFor={`parameter-${parameter.id}-key`}>{t('rules.key')}</FieldLabel>
              <Input
                id={`parameter-${parameter.id}-key`}
                value={parameter.key}
                disabled={disabled}
                onChange={(event) =>
                  onChange(
                    parameters.map((item) =>
                      item.id === parameter.id ? { ...item, key: event.target.value } : item,
                    ),
                  )
                }
              />
            </Field>
            <Field>
              <FieldLabel htmlFor={`parameter-${parameter.id}-type`}>{t('rules.type')}</FieldLabel>
              <Select
                value={parameter.type}
                disabled={disabled}
                onValueChange={(value) =>
                  value &&
                  onChange(
                    parameters.map((item) =>
                      item.id === parameter.id ? { ...item, type: value as RuleValueType } : item,
                    ),
                  )
                }
              >
                <SelectTrigger id={`parameter-${parameter.id}-type`}>
                  <SelectValue>
                    {(value) => t(fieldTypeTranslationKey(value as RuleValueType))}
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  {valueTypes.map((type) => (
                    <SelectItem key={type} value={type}>
                      {t(fieldTypeTranslationKey(type))}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel htmlFor={`parameter-${parameter.id}-allowed`}>
                {t('rules.allowedValues')}
              </FieldLabel>
              <Input
                id={`parameter-${parameter.id}-allowed`}
                value={parameter.allowedValues}
                placeholder={t('rules.allowedValuesPlaceholder')}
                disabled={disabled}
                onChange={(event) =>
                  onChange(
                    parameters.map((item) =>
                      item.id === parameter.id
                        ? { ...item, allowedValues: event.target.value }
                        : item,
                    ),
                  )
                }
              />
            </Field>
            <LabeledCheckbox
              id={`parameter-${parameter.id}-required`}
              label={t('rules.parameterRequired')}
              checked={parameter.isRequired}
              disabled={disabled}
              onChange={(checked) =>
                onChange(
                  parameters.map((item) =>
                    item.id === parameter.id ? { ...item, isRequired: checked } : item,
                  ),
                )
              }
            />
            <LabeledCheckbox
              id={`parameter-${parameter.id}-multiple`}
              label={t('rules.parameterMultiple')}
              checked={parameter.allowMultiple}
              disabled={disabled}
              onChange={(checked) =>
                onChange(
                  parameters.map((item) =>
                    item.id === parameter.id ? { ...item, allowMultiple: checked } : item,
                  ),
                )
              }
            />
            <Button
              type="button"
              variant="ghost"
              size="icon-sm"
              disabled={disabled}
              aria-label={t('rules.removeParameter')}
              onClick={() => onChange(parameters.filter((item) => item.id !== parameter.id))}
            >
              <Trash2 className="size-4" aria-hidden />
            </Button>
          </div>
        ))}
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled}
          onClick={() =>
            onChange([
              ...parameters,
              {
                id: crypto.randomUUID(),
                key: '',
                type: 'Text',
                isRequired: true,
                allowMultiple: false,
                allowedValues: '',
              },
            ])
          }
        >
          <Plus className="size-4" aria-hidden />
          {t('rules.addParameter')}
        </Button>
      </div>
    </EditorSection>
  );
}

function ConditionSection({
  condition,
  schema,
  parameters,
  disabled,
  onChange,
}: {
  condition: EditableGroup;
  schema: RuleContextSchema;
  parameters: EditableParameter[];
  disabled: boolean;
  onChange: (condition: EditableGroup) => void;
}) {
  const { t } = useTranslation();
  return (
    <EditorSection title={t('rules.conditions')} description={t('rules.conditionsHelp')}>
      <ConditionNodeEditor
        node={condition}
        root
        schema={schema}
        parameters={parameters}
        disabled={disabled}
        onChange={(node) => onChange(node as EditableGroup)}
        onRemove={() => undefined}
      />
    </EditorSection>
  );
}

function ConditionNodeEditor({
  node,
  root = false,
  schema,
  parameters,
  disabled,
  onChange,
  onRemove,
}: {
  node: EditableNode;
  root?: boolean;
  schema: RuleContextSchema;
  parameters: EditableParameter[];
  disabled: boolean;
  onChange: (node: EditableNode) => void;
  onRemove: () => void;
}) {
  const { t } = useTranslation();
  if (node.kind === 'predicate') {
    const contextField = schema.fields?.find((field) => field.path === node.contextPath);
    const unary = node.operator === 'IsNull' || node.operator === 'IsNotNull';
    return (
      <div className="grid gap-2 border-l-2 border-border pl-3 sm:grid-cols-2 lg:grid-cols-3 lg:items-end">
        <Field>
          <FieldLabel htmlFor={`condition-${node.id}-field`}>{t('rules.contextField')}</FieldLabel>
          <Select
            value={node.contextPath}
            disabled={disabled}
            onValueChange={(value) =>
              value && onChange({ ...node, contextPath: value, operator: 'Equal' })
            }
          >
            <SelectTrigger id={`condition-${node.id}-field`}>
              <SelectValue>
                {(value) =>
                  (schema.fields ?? []).find((field) => field.path === value)?.displayName ?? value
                }
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              {(schema.fields ?? []).map((field) => (
                <SelectItem key={field.path} value={field.path}>
                  {field.displayName}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </Field>
        <Field>
          <FieldLabel htmlFor={`condition-${node.id}-operator`}>{t('rules.operator')}</FieldLabel>
          <Select
            value={node.operator}
            disabled={disabled}
            onValueChange={(value) =>
              value && onChange({ ...node, operator: value as RulePredicateOperator })
            }
          >
            <SelectTrigger id={`condition-${node.id}-operator`}>
              <SelectValue>{(value) => t(`rules.operator${value}`)}</SelectValue>
            </SelectTrigger>
            <SelectContent>
              {operatorsFor(contextField).map((operator) => (
                <SelectItem key={operator} value={operator}>
                  {t(`rules.operator${operator}`)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </Field>
        {!unary ? (
          <Field>
            <FieldLabel htmlFor={`condition-${node.id}-right-kind`}>
              {t('rules.compareWith')}
            </FieldLabel>
            <Select
              value={node.rightKind}
              disabled={disabled}
              onValueChange={(value) =>
                value &&
                onChange({
                  ...node,
                  rightKind: value as EditablePredicate['rightKind'],
                })
              }
            >
              <SelectTrigger id={`condition-${node.id}-right-kind`}>
                <SelectValue>
                  {(value) =>
                    value === 'Parameter'
                      ? t('rules.parameter')
                      : value === 'Context'
                        ? t('rules.contextField')
                        : t('rules.literal')
                  }
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Literal">{t('rules.literal')}</SelectItem>
                <SelectItem value="Parameter">{t('rules.parameter')}</SelectItem>
                <SelectItem value="Context">{t('rules.contextField')}</SelectItem>
              </SelectContent>
            </Select>
          </Field>
        ) : (
          <span />
        )}
        {!unary ? (
          <RightOperandInput
            node={node}
            contextField={contextField}
            schema={schema}
            parameters={parameters}
            disabled={disabled}
            onChange={onChange}
          />
        ) : (
          <span />
        )}
        <Button
          type="button"
          variant="ghost"
          size="icon-sm"
          disabled={disabled}
          aria-label={t('rules.removeCondition')}
          onClick={onRemove}
        >
          <Trash2 className="size-4" aria-hidden />
        </Button>
      </div>
    );
  }

  const canAdd = node.operator !== 'Not' || node.children.length === 0;
  return (
    <div className={root ? 'space-y-3' : 'space-y-3 border-l-2 border-border pl-3'}>
      <div className="flex flex-wrap items-end gap-2">
        <Field>
          <FieldLabel htmlFor={`condition-${node.id}-group`}>{t('rules.group')}</FieldLabel>
          <Select
            value={node.operator}
            disabled={disabled}
            onValueChange={(value) => {
              if (!value) return;
              const operator = value as RuleLogicalOperator;
              onChange({
                ...node,
                operator,
                children: operator === 'Not' ? node.children.slice(0, 1) : node.children,
              });
            }}
          >
            <SelectTrigger id={`condition-${node.id}-group`}>
              <SelectValue>
                {(value) =>
                  value === 'Any'
                    ? t('rules.groupAny')
                    : value === 'Not'
                      ? t('rules.groupNot')
                      : t('rules.groupAll')
                }
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="All">{t('rules.groupAll')}</SelectItem>
              <SelectItem value="Any">{t('rules.groupAny')}</SelectItem>
              <SelectItem value="Not">{t('rules.groupNot')}</SelectItem>
            </SelectContent>
          </Select>
        </Field>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled || !canAdd}
          onClick={() =>
            onChange({ ...node, children: [...node.children, defaultPredicate(schema)] })
          }
        >
          <Plus className="size-4" aria-hidden />
          {t('rules.addCondition')}
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled || !canAdd}
          onClick={() =>
            onChange({
              ...node,
              children: [...node.children, { ...defaultCondition(schema), children: [] }],
            })
          }
        >
          <Plus className="size-4" aria-hidden />
          {t('rules.addGroup')}
        </Button>
        {!root ? (
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            disabled={disabled}
            aria-label={t('rules.removeGroup')}
            onClick={onRemove}
          >
            <Trash2 className="size-4" aria-hidden />
          </Button>
        ) : null}
      </div>
      <div className="space-y-3">
        {node.children.map((child, index) => (
          <ConditionNodeEditor
            key={child.id}
            node={child}
            schema={schema}
            parameters={parameters}
            disabled={disabled}
            onChange={(nextChild) =>
              onChange({
                ...node,
                children: node.children.map((item, itemIndex) =>
                  itemIndex === index ? nextChild : item,
                ),
              })
            }
            onRemove={() =>
              onChange({
                ...node,
                children: node.children.filter((_, itemIndex) => itemIndex !== index),
              })
            }
          />
        ))}
      </div>
    </div>
  );
}

function RightOperandInput({
  node,
  contextField,
  schema,
  parameters,
  disabled,
  onChange,
}: {
  node: EditablePredicate;
  contextField: NonNullable<RuleContextSchema['fields']>[number] | undefined;
  schema: RuleContextSchema;
  parameters: EditableParameter[];
  disabled: boolean;
  onChange: (node: EditableNode) => void;
}) {
  const { t } = useTranslation();
  if (node.rightKind === 'Parameter') {
    return (
      <Field>
        <FieldLabel htmlFor={`condition-${node.id}-parameter`}>{t('rules.parameter')}</FieldLabel>
        <Select
          value={node.rightReference || null}
          disabled={disabled}
          onValueChange={(value) => onChange({ ...node, rightReference: value ?? '' })}
        >
          <SelectTrigger id={`condition-${node.id}-parameter`}>
            <SelectValue>
              {(value) =>
                parameters.find((parameter) => parameter.key === value)?.key ??
                t('rules.selectParameter')
              }
            </SelectValue>
          </SelectTrigger>
          <SelectContent>
            {parameters
              .filter((parameter) => parameter.type === contextField?.type)
              .map((parameter) => (
                <SelectItem key={parameter.id} value={parameter.key}>
                  {parameter.key || t('rules.unnamedParameter')}
                </SelectItem>
              ))}
          </SelectContent>
        </Select>
      </Field>
    );
  }

  if (node.rightKind === 'Context') {
    return (
      <Field>
        <FieldLabel htmlFor={`condition-${node.id}-context`}>{t('rules.contextField')}</FieldLabel>
        <Select
          value={node.rightReference || null}
          disabled={disabled}
          onValueChange={(value) => onChange({ ...node, rightReference: value ?? '' })}
        >
          <SelectTrigger id={`condition-${node.id}-context`}>
            <SelectValue>
              {(value) =>
                (schema.fields ?? []).find((field) => field.path === value)?.displayName ??
                t('rules.selectContextField')
              }
            </SelectValue>
          </SelectTrigger>
          <SelectContent>
            {(schema.fields ?? [])
              .filter((field) => field.type === contextField?.type)
              .map((field) => (
                <SelectItem key={field.path} value={field.path}>
                  {field.displayName}
                </SelectItem>
              ))}
          </SelectContent>
        </Select>
      </Field>
    );
  }

  return (
    <TypedValueField
      id={`condition-${node.id}-literal`}
      label={t('rules.value')}
      type={contextField?.type ?? 'Text'}
      value={node.rightValue}
      disabled={disabled}
      onChange={(rightValue) => onChange({ ...node, rightValue })}
    />
  );
}

function OutcomeSection({
  editor,
  disabled,
  onChange,
}: {
  editor: EditorState;
  disabled: boolean;
  onChange: (editor: EditorState) => void;
}) {
  const { t } = useTranslation();
  return (
    <EditorSection title={t('rules.outcome')} description={t('rules.outcomeHelp')}>
      {editor.outcomeKind === 'Validation' ? (
        <div className="grid gap-3 sm:grid-cols-2">
          <Field>
            <FieldLabel htmlFor="rule-violation-code">{t('rules.violationCode')}</FieldLabel>
            <Input
              id="rule-violation-code"
              value={editor.violationCode}
              disabled={disabled}
              onChange={(event) => onChange({ ...editor, violationCode: event.target.value })}
            />
          </Field>
          <Field>
            <FieldLabel htmlFor="rule-severity">{t('rules.severity')}</FieldLabel>
            <Select
              value={editor.severity}
              disabled={disabled}
              onValueChange={(value) =>
                value && onChange({ ...editor, severity: value as RuleSeverity })
              }
            >
              <SelectTrigger id="rule-severity">
                <SelectValue>
                  {(value) =>
                    value === 'Info'
                      ? t('rules.severityInfo')
                      : value === 'Warning'
                        ? t('rules.severityWarning')
                        : t('rules.severityError')
                  }
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Info">{t('rules.severityInfo')}</SelectItem>
                <SelectItem value="Warning">{t('rules.severityWarning')}</SelectItem>
                <SelectItem value="Error">{t('rules.severityError')}</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field className="sm:col-span-2">
            <FieldLabel htmlFor="rule-message">{t('rules.message')}</FieldLabel>
            <Input
              id="rule-message"
              value={editor.message}
              disabled={disabled}
              onChange={(event) => onChange({ ...editor, message: event.target.value })}
            />
          </Field>
        </div>
      ) : (
        <Field>
          <FieldLabel htmlFor="rule-decision">{t('rules.decision')}</FieldLabel>
          <Select
            value={editor.decision}
            disabled={disabled}
            onValueChange={(value) =>
              value && onChange({ ...editor, decision: value as RuleDecision })
            }
          >
            <SelectTrigger id="rule-decision">
              <SelectValue>
                {(value) => (value === 'Deny' ? t('rules.decisionDeny') : t('rules.decisionAllow'))}
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="Allow">{t('rules.decisionAllow')}</SelectItem>
              <SelectItem value="Deny">{t('rules.decisionDeny')}</SelectItem>
            </SelectContent>
          </Select>
        </Field>
      )}
    </EditorSection>
  );
}

function SimulationSection({
  schema,
  parameters,
  contextValues,
  parameterValues,
  result,
  disabled,
  onContextChange,
  onParameterChange,
  onSimulate,
}: {
  schema: RuleContextSchema;
  parameters: EditableParameter[];
  contextValues: Record<string, string>;
  parameterValues: Record<string, string>;
  result: Awaited<ReturnType<typeof simulateRuleDefinition>> | null;
  disabled: boolean;
  onContextChange: (values: Record<string, string>) => void;
  onParameterChange: (values: Record<string, string>) => void;
  onSimulate: () => void;
}) {
  const { t } = useTranslation();
  return (
    <EditorSection title={t('rules.simulation')} description={t('rules.simulationHelp')}>
      <div className="grid gap-3 sm:grid-cols-2">
        {(schema.fields ?? []).map((field) => (
          <TypedValueField
            key={field.path}
            id={`sample-context-${field.path}`}
            label={field.displayName ?? field.path ?? t('rules.value')}
            type={field.type ?? 'Text'}
            value={contextValues[field.path ?? ''] ?? ''}
            multiple={field.allowMultiple}
            disabled={disabled}
            onChange={(value) => onContextChange({ ...contextValues, [field.path ?? '']: value })}
          />
        ))}
        {parameters.map((parameter) => (
          <TypedValueField
            key={parameter.id}
            id={`sample-parameter-${parameter.id}`}
            label={`${t('rules.parameter')}: ${parameter.key || t('rules.unnamedParameter')}`}
            type={parameter.type}
            value={parameterValues[parameter.key.trim()] ?? ''}
            multiple={parameter.allowMultiple}
            disabled={disabled}
            onChange={(value) =>
              onParameterChange({ ...parameterValues, [parameter.key.trim()]: value })
            }
          />
        ))}
      </div>
      <div className="mt-3 flex items-center gap-3">
        <Button type="button" variant="outline" disabled={disabled} onClick={onSimulate}>
          <Play className="size-4" aria-hidden />
          {t('rules.runSimulation')}
        </Button>
        {result ? (
          <Badge variant="outline">
            {result.isMatch ? t('rules.simulationMatched') : t('rules.simulationNotMatched')}
          </Badge>
        ) : null}
      </div>
      {result?.outcome ? (
        <p className="mt-2 text-sm text-foreground">
          {result.outcome.message ?? result.outcome.decision}
        </p>
      ) : null}
    </EditorSection>
  );
}

function VersionHistory({ detail }: { detail: RuleDefinitionDetail }) {
  const { t } = useTranslation();
  return (
    <EditorSection title={t('rules.versionHistory')} description={t('rules.versionHistoryHelp')}>
      {(detail.versions ?? []).length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('rules.noPublishedVersions')}</p>
      ) : (
        <div className="divide-y divide-border">
          {[...(detail.versions ?? [])]
            .sort((left, right) => (right.version ?? 0) - (left.version ?? 0))
            .map((version) => (
              <div key={version.version} className="flex items-center justify-between gap-3 py-2">
                <div>
                  <p className="text-sm font-medium text-foreground">
                    {t('rules.version', { version: version.version })}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {version.publishedAt
                      ? new Intl.DateTimeFormat(undefined, {
                          dateStyle: 'medium',
                          timeStyle: 'short',
                        }).format(new Date(version.publishedAt))
                      : t('rules.dateUnavailable')}
                  </p>
                </div>
                <Badge variant="outline">{t('rules.immutable')}</Badge>
              </div>
            ))}
        </div>
      )}
    </EditorSection>
  );
}

function EditorSection({
  title,
  description,
  children,
}: {
  title: string;
  description: string;
  children: React.ReactNode;
}) {
  return (
    <section className="space-y-4">
      <div>
        <h3 className="text-sm font-semibold text-foreground">{title}</h3>
        <p className="mt-1 text-sm text-muted-foreground">{description}</p>
      </div>
      <div>{children}</div>
    </section>
  );
}

function TypedValueField({
  id,
  label,
  type,
  value,
  multiple = false,
  disabled,
  onChange,
}: {
  id: string;
  label: string;
  type: RuleValueType;
  value: string;
  multiple?: boolean;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  const { t } = useTranslation();
  if (type === 'Boolean') {
    return (
      <Field>
        <FieldLabel htmlFor={id}>{label}</FieldLabel>
        <Select
          value={value || unsetSelectValue}
          disabled={disabled}
          onValueChange={(nextValue) =>
            nextValue && onChange(nextValue === unsetSelectValue ? '' : nextValue)
          }
        >
          <SelectTrigger id={id}>
            <SelectValue>
              {(selectedValue) =>
                selectedValue === 'true'
                  ? t('rules.booleanTrue')
                  : selectedValue === 'false'
                    ? t('rules.booleanFalse')
                    : t('rules.notSet')
              }
            </SelectValue>
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={unsetSelectValue}>{t('rules.notSet')}</SelectItem>
            <SelectItem value="true">{t('rules.booleanTrue')}</SelectItem>
            <SelectItem value="false">{t('rules.booleanFalse')}</SelectItem>
          </SelectContent>
        </Select>
      </Field>
    );
  }
  const inputType =
    type === 'Date'
      ? 'date'
      : type === 'DateTime'
        ? 'datetime-local'
        : type === 'Integer' || type === 'Decimal'
          ? 'number'
          : 'text';
  return (
    <Field>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      <Input
        id={id}
        type={inputType}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
      />
      {multiple ? <FieldDescription>Separate values with commas.</FieldDescription> : null}
    </Field>
  );
}

function LabeledCheckbox({
  id,
  label,
  checked,
  disabled,
  onChange,
}: {
  id: string;
  label: string;
  checked: boolean;
  disabled: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <div className="flex h-8 items-center gap-2 text-sm text-foreground">
      <Checkbox
        id={id}
        checked={checked}
        disabled={disabled}
        onCheckedChange={(value) => onChange(value === true)}
      />
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
    </div>
  );
}

function LifecycleBadge({ detail }: { detail: RuleDefinitionDetail }) {
  const { t } = useTranslation();
  if (detail.status === 'Published') {
    return <StatusBadge tone="success">{t('rules.statusPublished')}</StatusBadge>;
  }
  if (detail.status === 'Archived') {
    return <StatusBadge tone="muted">{t('rules.statusArchived')}</StatusBadge>;
  }
  return <StatusBadge tone="neutral">{t('rules.statusDraft')}</StatusBadge>;
}

const valueTypes: RuleValueType[] = ['Text', 'Integer', 'Decimal', 'Date', 'DateTime', 'Boolean'];

function defaultPredicate(schema: RuleContextSchema): EditablePredicate {
  return {
    id: crypto.randomUUID(),
    kind: 'predicate',
    contextPath: schema.fields?.[0]?.path ?? '',
    operator: 'Equal',
    rightKind: 'Literal',
    rightReference: '',
    rightValue: '',
  };
}

function defaultCondition(schema: RuleContextSchema): EditableGroup {
  return {
    id: crypto.randomUUID(),
    kind: 'group',
    operator: 'All',
    children: schema.fields?.length ? [defaultPredicate(schema)] : [],
  };
}

function toEditorState(detail: RuleDefinitionDetail, schemas: RuleContextSchema[]): EditorState {
  const schema = schemas.find(
    (candidate) =>
      candidate.contextKey === detail.contextKey &&
      candidate.version === detail.contextSchemaVersion,
  ) ?? {
    contextKey: detail.contextKey,
    version: detail.contextSchemaVersion,
    scope: detail.scope,
    fields: [],
  };
  return {
    name: detail.name ?? '',
    description: detail.description ?? '',
    scope: detail.scope ?? 'Field',
    contextKey: detail.contextKey ?? '',
    contextSchemaVersion: detail.contextSchemaVersion ?? 1,
    outcomeKind: detail.outcomeKind ?? 'Validation',
    parameters: (detail.parameters ?? []).map((parameter) => ({
      id: crypto.randomUUID(),
      key: parameter.key ?? '',
      type: parameter.type ?? 'Text',
      isRequired: parameter.isRequired ?? false,
      allowMultiple: parameter.allowMultiple ?? false,
      allowedValues: (parameter.allowedValues ?? []).join(', '),
    })),
    condition: detail.condition
      ? ensureRootGroup(fromConditionDto(detail.condition))
      : defaultCondition(schema),
    violationCode: detail.outcome?.violationCode ?? '',
    severity: detail.outcome?.severity ?? 'Error',
    message: detail.outcome?.message ?? '',
    decision: detail.outcome?.decision ?? 'Deny',
  };
}

function fromConditionDto(node: RuleConditionNode): EditableNode {
  if (node.logicalOperator) {
    return {
      id: crypto.randomUUID(),
      kind: 'group',
      operator: node.logicalOperator,
      children: (node.children ?? []).map(fromConditionDto),
    };
  }
  return {
    id: crypto.randomUUID(),
    kind: 'predicate',
    contextPath: node.left?.reference ?? '',
    operator: node.predicateOperator ?? 'Equal',
    rightKind: node.right?.kind ?? 'Literal',
    rightReference: node.right?.reference ?? '',
    rightValue: (node.right?.literal?.values ?? []).join(', '),
  };
}

function ensureRootGroup(node: EditableNode): EditableGroup {
  return node.kind === 'group'
    ? node
    : { id: crypto.randomUUID(), kind: 'group', operator: 'All', children: [node] };
}

function toConditionDto(
  node: EditableNode,
  schema: RuleContextSchema,
  parameters: EditableParameter[],
): RuleConditionNode {
  if (node.kind === 'group') {
    return {
      nodeId: nodeId(node.id),
      logicalOperator: node.operator,
      predicateOperator: undefined,
      left: undefined,
      right: undefined,
      children: node.children.map((child) => toConditionDto(child, schema, parameters)),
    };
  }
  const field = schema.fields?.find((candidate) => candidate.path === node.contextPath);
  const unary = node.operator === 'IsNull' || node.operator === 'IsNotNull';
  return {
    nodeId: nodeId(node.id),
    logicalOperator: undefined,
    predicateOperator: node.operator,
    left: { kind: 'Context', reference: node.contextPath, literal: undefined },
    right: unary
      ? undefined
      : node.rightKind === 'Literal'
        ? {
            kind: 'Literal',
            reference: null,
            literal: typedRuleValue(field?.type ?? 'Text', node.rightValue, false),
          }
        : {
            kind: node.rightKind,
            reference: node.rightReference,
            literal: undefined,
          },
    children: [],
  };
}

function typedRuleValue(type: RuleValueType, source: string, multiple = false): RuleValue {
  const values = multiple ? splitValues(source) : [normalizeTypedValue(type, source)];
  return { type, values };
}

function normalizeTypedValue(type: RuleValueType, source: string): string {
  if (type !== 'DateTime' || !source) return source.trim();
  const parsed = new Date(source);
  return Number.isNaN(parsed.getTime()) ? source.trim() : parsed.toISOString();
}

function splitValues(source: string): string[] {
  return source
    .split(/[,\n]/)
    .map((value) => value.trim())
    .filter(Boolean);
}

function operatorsFor(
  field: NonNullable<RuleContextSchema['fields']>[number] | undefined,
): RulePredicateOperator[] {
  if (!field) return ['Equal', 'NotEqual', 'IsNull', 'IsNotNull'];
  if (field.allowMultiple) return ['Equal', 'NotEqual', 'Contains', 'IsNull', 'IsNotNull'];
  if (field.type === 'Text') {
    return [
      'Equal',
      'NotEqual',
      'Contains',
      'StartsWith',
      'EndsWith',
      'GreaterThan',
      'GreaterThanOrEqual',
      'LessThan',
      'LessThanOrEqual',
      'IsNull',
      'IsNotNull',
    ];
  }
  if (field.type === 'Boolean') return ['Equal', 'NotEqual', 'IsNull', 'IsNotNull'];
  return orderedOperators;
}

function validateEditor(editor: EditorState, schema: RuleContextSchema): string | null {
  if (!editor.name.trim() || !editor.description.trim())
    return 'Name and description are required.';
  const keys = editor.parameters.map((parameter) => parameter.key.trim());
  if (keys.some((key) => !/^[a-z][a-z0-9_]*$/.test(key)) || new Set(keys).size !== keys.length) {
    return 'Parameter keys must be unique lowercase identifiers.';
  }
  if (!editor.condition.children.length) return 'Add at least one condition.';
  const conditionError = validateNode(editor.condition, schema, editor.parameters);
  if (conditionError) return conditionError;
  if (
    editor.outcomeKind === 'Validation' &&
    (!/^[a-z][a-z0-9_.]*$/.test(editor.violationCode.trim()) || !editor.message.trim())
  ) {
    return 'A stable violation code and message are required.';
  }
  return null;
}

function validateNode(
  node: EditableNode,
  schema: RuleContextSchema,
  parameters: EditableParameter[],
): string | null {
  if (node.kind === 'group') {
    if (!node.children.length || (node.operator === 'Not' && node.children.length !== 1)) {
      return 'Condition groups must contain valid children.';
    }
    for (const child of node.children) {
      const error = validateNode(child, schema, parameters);
      if (error) return error;
    }
    return null;
  }
  const field = schema.fields?.find((candidate) => candidate.path === node.contextPath);
  if (!field) return 'Select a valid context field for every condition.';
  if (node.operator === 'IsNull' || node.operator === 'IsNotNull') return null;
  if (node.rightKind === 'Literal' && !node.rightValue.trim())
    return 'Condition values are required.';
  if (node.rightKind === 'Parameter') {
    const parameter = parameters.find((candidate) => candidate.key === node.rightReference);
    if (!parameter || parameter.type !== field.type) return 'Select a compatible parameter.';
  }
  if (node.rightKind === 'Context') {
    const right = schema.fields?.find((candidate) => candidate.path === node.rightReference);
    if (!right || right.type !== field.type) return 'Select a compatible context field.';
  }
  return null;
}

function nodeId(id: string): string {
  return `node_${id.replace(/-/g, '').slice(0, 32)}`;
}

function setDetailCache(
  queryClient: ReturnType<typeof useQueryClient>,
  detail: RuleDefinitionDetail,
) {
  if (detail.definitionKey) {
    queryClient.setQueryData(ruleDefinitionQueryKeys.detail(detail.definitionKey), detail);
  }
}

function readError(error: unknown, fallback: string): string {
  if (error instanceof Error && !(error instanceof ApiError)) return error.message;
  if (!(error instanceof ApiError) || typeof error.data !== 'object' || error.data === null) {
    return fallback;
  }
  const detail = (error.data as { detail?: unknown }).detail;
  return typeof detail === 'string' && detail ? detail : fallback;
}
