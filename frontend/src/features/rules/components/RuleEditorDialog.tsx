import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AlertCircle, Archive, Braces, Play, Plus, Save, Send, Trash2 } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ManagedDialog, ManagedDialogBody } from '@/components/shared/ManagedDialog';
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
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from '@/components/ui/empty';
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
  createRuleDefinition,
  getRuleDefinition,
  publishRuleDefinition,
  type RuleConditionNode,
  type RuleContextSchema,
  type RuleDecision,
  type RuleDefinitionDetail,
  type RuleExpressionCardinality,
  type RuleExpressionFunction,
  type RuleExpressionLanguage,
  type RuleLogicalOperator,
  type RuleOperand,
  type RulePredicateOperator,
  type RuleScope,
  type RuleSeverity,
  type RuleValueType,
  ruleContextSchemasQueryOptions,
  ruleDefinitionQueryKeys,
  ruleExpressionLanguageQueryOptions,
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
  left: EditableOperand;
  operator: RulePredicateOperator;
  right: EditableOperand | null;
}

interface EditableOperand {
  id: string;
  kind: RuleOperandKind;
  reference: string;
  literalType: RuleValueType;
  literalValue: string;
  function: RuleExpressionFunction | null;
  arguments: EditableOperand[];
}

interface OperandShape {
  type: RuleValueType;
  cardinality: Exclude<RuleExpressionCardinality, 'Any'>;
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

const unsetSelectValue = '__unset__';

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
  const expressionLanguageQuery = useQuery({
    ...ruleExpressionLanguageQueryOptions(),
    enabled: open && Boolean(definitionKey),
  });
  const detail = detailQuery.data;
  const hasEligibleCreateSchema = useMemo(
    () =>
      (schemasQuery.data ?? []).some(
        (candidate) =>
          Boolean(candidate.scope) &&
          Boolean(candidate.contextKey) &&
          candidate.version !== undefined,
      ),
    [schemasQuery.data],
  );
  const createSchemaLoadFailed = creating && schemasQuery.isError;
  const createSchemaUnavailable = creating && schemasQuery.isSuccess && !hasEligibleCreateSchema;
  const createEditorPending =
    creating && schemasQuery.isSuccess && hasEligibleCreateSchema && editor === null;
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
    if (!creating || !open || editor || !schemasQuery.isSuccess) return;
    setEditor(toCreateEditorState(schemasQuery.data ?? []));
  }, [creating, editor, open, schemasQuery.data, schemasQuery.isSuccess]);

  useEffect(() => {
    if (!open) return;
    setFeedback(null);
    setSimulation(null);
    setSampleContext({});
    setSampleParameters({});
  }, [open]);

  const createMutation = useMutation({
    mutationFn: async (state: EditorState) => {
      const selectedSchema = (schemasQuery.data ?? []).find(
        (candidate) =>
          candidate.scope === state.scope &&
          candidate.contextKey === state.contextKey &&
          candidate.version === state.contextSchemaVersion,
      );
      if (
        !state.name.trim() ||
        !state.description.trim() ||
        !selectedSchema?.scope ||
        !selectedSchema.contextKey ||
        selectedSchema.version === undefined
      ) {
        throw new Error(t('rules.createError'));
      }
      return createRuleDefinition({
        name: state.name.trim(),
        description: state.description.trim(),
        scope: selectedSchema.scope,
        contextKey: selectedSchema.contextKey,
        contextSchemaVersion: selectedSchema.version,
        outcomeKind: state.outcomeKind,
      });
    },
    onSuccess: async (created) => {
      setDetailCache(queryClient, created);
      await queryClient.invalidateQueries({ queryKey: ruleDefinitionQueryKeys.all });
      onCreated?.(created);
    },
    onError: (error) =>
      setFeedback({ variant: 'destructive', text: readError(error, t('rules.createError')) }),
  });

  const saveMutation = useMutation({
    mutationFn: async (state: EditorState) => {
      if (!detail?.definitionKey || detail.revision == null || !schema) {
        throw new Error(t('rules.editorUnavailable'));
      }
      if (!expressionLanguageQuery.data) throw new Error(t('rules.editorUnavailable'));
      const validationError = validateEditor(state, schema, expressionLanguageQuery.data);
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
        condition: toConditionDto(state.condition),
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
      if (!detail?.definitionKey || detail.revision == null) {
        throw new Error(t('rules.editorUnavailable'));
      }
      if (action === 'publish') {
        const saved = editor ? await saveMutation.mutateAsync(editor) : detail;
        if (!saved.definitionKey || saved.revision == null) {
          throw new Error(t('rules.editorUnavailable'));
        }
        return publishRuleDefinition(saved.definitionKey, saved.revision);
      }
      return action === 'draft'
        ? startRuleDefinitionDraft(detail.definitionKey, detail.revision)
        : archiveRuleDefinition(detail.definitionKey, detail.revision);
    },
    onSuccess: async (saved) => {
      setArchiveOpen(false);
      setPublishOpen(false);
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

  const busy =
    createMutation.isPending ||
    saveMutation.isPending ||
    lifecycleMutation.isPending ||
    simulateMutation.isPending;
  const baselineEditor = useMemo(
    () => (detail ? toEditorState(detail, schemasQuery.data ?? []) : null),
    [detail, schemasQuery.data],
  );
  const isDirty = creating
    ? Boolean(
        editor &&
          (editor.name ||
            editor.description ||
            editor.contextKey ||
            editor.outcomeKind !== 'Validation'),
      )
    : detail?.status === 'Draft' &&
      editor !== null &&
      baselineEditor !== null &&
      JSON.stringify(editor) !== JSON.stringify(baselineEditor);
  const autoSizeReady =
    !schemasQuery.isLoading &&
    (creating
      ? createSchemaLoadFailed || createSchemaUnavailable || editor !== null
      : detailQuery.isError ||
        expressionLanguageQuery.isError ||
        Boolean(detail && editor && expressionLanguageQuery.data));

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
    <ManagedDialog
      open={open}
      onOpenChange={requestOpenChange}
      title={creating ? t('rules.createTitle') : (detail?.name ?? t('rules.loadingRule'))}
      description={
        creating ? t('rules.createDescription') : (detail?.description ?? t('rules.loadingRule'))
      }
      titleAccessory={detail ? <LifecycleBadge detail={detail} /> : null}
      closeDisabled={busy}
      dirty={isDirty}
      autoSizeKey={creating ? 'create' : `editor:${definitionKey ?? 'unknown'}`}
      autoSizeReady={autoSizeReady}
      footerClassName={
        detail && editor && detail.status !== 'Archived' && detail.latestPublishedVersion
          ? 'sm:justify-between'
          : undefined
      }
      footer={
        <>
          {detail && editor && detail.status !== 'Archived' && detail.latestPublishedVersion ? (
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
          <div className="flex flex-col-reverse gap-2 sm:flex-row">
            <Button
              type="button"
              variant="outline"
              disabled={busy}
              onClick={() => requestOpenChange(false)}
            >
              {creating || (detail?.status === 'Draft' && editor)
                ? t('app.cancel')
                : t('app.close')}
            </Button>
            {creating && editor ? (
              <Button
                type="button"
                disabled={
                  busy ||
                  !editor.name.trim() ||
                  !editor.description.trim() ||
                  !schema?.contextKey ||
                  schema.version === undefined
                }
                onClick={() => createMutation.mutate(editor)}
              >
                {t('rules.createAction')}
              </Button>
            ) : null}
            {detail && editor && detail.status === 'Draft' ? (
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
            ) : detail && editor && detail.status === 'Published' ? (
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
        </>
      }
    >
      <ManagedDialogBody>
        {detailQuery.isError || expressionLanguageQuery.isError || createSchemaLoadFailed ? (
          <Alert variant="destructive">
            <AlertCircle className="size-4" aria-hidden />
            <AlertTitle>{t('rules.loadErrorTitle')}</AlertTitle>
            <AlertDescription>{t('rules.loadErrorBody')}</AlertDescription>
          </Alert>
        ) : createSchemaUnavailable ? (
          <Empty className="border">
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <Braces aria-hidden />
              </EmptyMedia>
              <EmptyTitle>{t('rules.contextUnavailable')}</EmptyTitle>
              <EmptyDescription>{t('rules.noContextForScope')}</EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : editor && (creating || detail) ? (
          <div className="space-y-6">
            {feedback ? <StatusNotice tone={feedback.variant}>{feedback.text}</StatusNotice> : null}
            <RuleIdentitySection
              editor={editor}
              definitionKey={detail?.definitionKey}
              schemas={schemasQuery.data ?? []}
              disabled={(!creating && detail?.status !== 'Draft') || busy}
              onChange={setEditor}
            />
            {detail && schema && expressionLanguageQuery.data ? (
              <>
                <ParameterSection
                  parameters={editor.parameters}
                  disabled={detail.status !== 'Draft' || busy}
                  onChange={(parameters) => setEditor({ ...editor, parameters })}
                />
                <ConditionSection
                  condition={editor.condition}
                  schema={schema}
                  parameters={editor.parameters}
                  expressionLanguage={expressionLanguageQuery.data}
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
              </>
            ) : null}
          </div>
        ) : detailQuery.isLoading ||
          schemasQuery.isLoading ||
          expressionLanguageQuery.isLoading ||
          createEditorPending ? (
          <p className="text-sm text-muted-foreground">{t('rules.loadingRule')}</p>
        ) : null}
      </ManagedDialogBody>

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
            <AlertDialogAction disabled={busy} onClick={() => lifecycleMutation.mutate('publish')}>
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
    </ManagedDialog>
  );
}

function RuleIdentitySection({
  editor,
  definitionKey,
  schemas,
  disabled,
  onChange,
}: {
  editor: EditorState;
  definitionKey?: string | null;
  schemas: RuleContextSchema[];
  disabled: boolean;
  onChange: (editor: EditorState) => void;
}) {
  const { t } = useTranslation();
  const availableScopes = distinctDefined(schemas.map((schema) => schema.scope));
  const availableSchemas = schemas.filter((schema) => schema.scope === editor.scope);
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
          <FieldDescription>
            {t('rules.derivedKey', { key: definitionKey ?? deriveRuleKey(editor.name) })}
          </FieldDescription>
        </Field>
        <Field>
          <FieldLabel htmlFor="rule-editor-scope">{t('rules.scope')}</FieldLabel>
          <Select
            value={editor.scope}
            disabled={disabled || availableScopes.length === 0}
            onValueChange={(value) => {
              const scope = value as RuleScope;
              const nextSchema = schemas.find((candidate) => candidate.scope === scope);
              if (!nextSchema) return;
              onChange({
                ...editor,
                scope,
                contextKey: nextSchema.contextKey ?? '',
                contextSchemaVersion: nextSchema.version ?? 1,
                condition: defaultCondition(nextSchema),
              });
            }}
          >
            <SelectTrigger id="rule-editor-scope">
              <SelectValue>{(value) => t(`rules.scope${value}`)}</SelectValue>
            </SelectTrigger>
            <SelectContent>
              {availableScopes.map((scope) => (
                <SelectItem key={scope} value={scope}>
                  {t(`rules.scope${scope}`)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <FieldDescription>{t(`rules.scope${editor.scope}Description`)}</FieldDescription>
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
        <Field>
          <FieldLabel htmlFor="rule-editor-context">{t('rules.context')}</FieldLabel>
          <Select
            value={editor.contextKey || null}
            disabled={disabled || availableSchemas.length === 0}
            onValueChange={(value) => {
              const schema = availableSchemas.find((candidate) => candidate.contextKey === value);
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
                  availableSchemas.find((schema) => schema.contextKey === value)?.displayName ??
                  t('rules.selectContext')
                }
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              {availableSchemas.map((schema) => (
                <SelectItem
                  key={`${schema.contextKey}:${schema.version}`}
                  value={schema.contextKey}
                >
                  {schema.displayName}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {availableSchemas.length === 0 ? (
            <FieldDescription>{t('rules.noContextForScope')}</FieldDescription>
          ) : null}
        </Field>
        <Field>
          <FieldLabel htmlFor="rule-editor-outcome-kind">{t('rules.outcome')}</FieldLabel>
          <Select
            value={editor.outcomeKind}
            disabled={disabled}
            onValueChange={(value) =>
              value && onChange({ ...editor, outcomeKind: value as RuleOutcomeKind })
            }
          >
            <SelectTrigger id="rule-editor-outcome-kind">
              <SelectValue>
                {(value) =>
                  value === 'Decision' ? t('rules.outcomeDecision') : t('rules.outcomeValidation')
                }
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="Validation">{t('rules.outcomeValidation')}</SelectItem>
              <SelectItem value="Decision">{t('rules.outcomeDecision')}</SelectItem>
            </SelectContent>
          </Select>
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
  expressionLanguage,
  disabled,
  onChange,
}: {
  condition: EditableGroup;
  schema: RuleContextSchema;
  parameters: EditableParameter[];
  expressionLanguage: RuleExpressionLanguage;
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
        expressionLanguage={expressionLanguage}
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
  expressionLanguage,
  disabled,
  onChange,
  onRemove,
}: {
  node: EditableNode;
  root?: boolean;
  schema: RuleContextSchema;
  parameters: EditableParameter[];
  expressionLanguage: RuleExpressionLanguage;
  disabled: boolean;
  onChange: (node: EditableNode) => void;
  onRemove: () => void;
}) {
  const { t } = useTranslation();
  if (node.kind === 'predicate') {
    const leftShape = resolveOperandShape(node.left, schema, parameters, expressionLanguage);
    const operatorDefinitions = compatibleOperators(expressionLanguage, leftShape);
    const operatorDefinition = operatorDefinitions.find(
      (definition) => definition.operator === node.operator,
    );
    const unary = (operatorDefinition?.rightShapes ?? []).length === 0;
    const matchingRightShapes = (operatorDefinition?.rightShapes ?? []).filter(
      (shape) => !operatorDefinition?.requiresMatchingTypes || shape.type === leftShape?.type,
    );
    return (
      <div className="space-y-3 border-l-2 border-border pl-3">
        <div className="grid gap-3 lg:grid-cols-3 lg:items-start">
          <div className="lg:col-span-2">
            <OperandEditor
              label={t('rules.leftOperand')}
              operand={node.left}
              schema={schema}
              parameters={parameters}
              expressionLanguage={expressionLanguage}
              disabled={disabled}
              onChange={(left) => {
                const nextShape = resolveOperandShape(left, schema, parameters, expressionLanguage);
                const nextOperators = compatibleOperators(expressionLanguage, nextShape);
                const nextOperator = nextOperators.some(
                  (definition) => definition.operator === node.operator,
                )
                  ? node.operator
                  : (nextOperators[0]?.operator ?? node.operator);
                const nextDefinition = nextOperators.find(
                  (definition) => definition.operator === nextOperator,
                );
                onChange({
                  ...node,
                  left,
                  operator: nextOperator,
                  right:
                    (nextDefinition?.rightShapes ?? []).length === 0
                      ? null
                      : ensureCompatibleOperand(
                          node.right,
                          rightShapesFor(nextDefinition, nextShape),
                          schema,
                          parameters,
                          expressionLanguage,
                        ),
                });
              }}
            />
          </div>
          <Field>
            <FieldLabel htmlFor={`condition-${node.id}-operator`}>{t('rules.operator')}</FieldLabel>
            <Select
              value={node.operator}
              disabled={disabled}
              onValueChange={(value) => {
                if (!value) return;
                const operator = value as RulePredicateOperator;
                const definition = operatorDefinitions.find(
                  (candidate) => candidate.operator === operator,
                );
                onChange({
                  ...node,
                  operator,
                  right:
                    (definition?.rightShapes ?? []).length === 0
                      ? null
                      : ensureCompatibleOperand(
                          node.right,
                          rightShapesFor(definition, leftShape),
                          schema,
                          parameters,
                          expressionLanguage,
                        ),
                });
              }}
            >
              <SelectTrigger id={`condition-${node.id}-operator`}>
                <SelectValue>{(value) => t(`rules.operator${value}`)}</SelectValue>
              </SelectTrigger>
              <SelectContent>
                {operatorDefinitions.map((definition) => (
                  <SelectItem key={definition.operator} value={definition.operator}>
                    {t(`rules.operator${definition.operator}`)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
        </div>
        {!unary && node.right ? (
          <OperandEditor
            label={t('rules.rightOperand')}
            operand={node.right}
            schema={schema}
            parameters={parameters}
            expressionLanguage={expressionLanguage}
            acceptedShapes={matchingRightShapes}
            disabled={disabled}
            onChange={(right) => onChange({ ...node, right })}
          />
        ) : null}
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
            expressionLanguage={expressionLanguage}
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

function OperandEditor({
  label,
  operand,
  schema,
  parameters,
  expressionLanguage,
  acceptedShapes,
  disabled,
  onChange,
}: {
  label: string;
  operand: EditableOperand;
  schema: RuleContextSchema;
  parameters: EditableParameter[];
  expressionLanguage: RuleExpressionLanguage;
  acceptedShapes?: NonNullable<
    NonNullable<RuleExpressionLanguage['operators']>[number]['leftShapes']
  >;
  disabled: boolean;
  onChange: (operand: EditableOperand) => void;
}) {
  const { t } = useTranslation();
  const eligibleFields = (schema.fields ?? []).filter(
    (field) => field.type && acceptsShape(acceptedShapes, field.type, field.allowMultiple),
  );
  const eligibleParameters = parameters.filter((parameter) =>
    acceptsShape(acceptedShapes, parameter.type, parameter.allowMultiple),
  );
  const literalTypes = distinctDefined(
    acceptedShapes
      ?.filter((shape) => shape.cardinality !== 'Multiple')
      .map((shape) => shape.type) ?? valueTypes,
  );
  const eligibleFunctions = (expressionLanguage.functions ?? []).filter(
    (definition) =>
      definition.function &&
      definition.returnType &&
      acceptsShape(
        acceptedShapes,
        definition.returnType,
        definition.returnCardinality === 'Multiple',
      ),
  );
  const functionDefinition = (expressionLanguage.functions ?? []).find(
    (definition) => definition.function === operand.function,
  );

  return (
    <div className="space-y-3 rounded-md border bg-muted/20 p-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <Field>
          <FieldLabel htmlFor={`operand-${operand.id}-kind`}>{label}</FieldLabel>
          <Select
            value={operand.kind}
            disabled={disabled}
            onValueChange={(value) => {
              if (!value) return;
              onChange(
                createOperand(
                  value as RuleOperandKind,
                  schema,
                  parameters,
                  expressionLanguage,
                  acceptedShapes,
                ),
              );
            }}
          >
            <SelectTrigger id={`operand-${operand.id}-kind`}>
              <SelectValue>{(value) => t(`rules.operand${value}`)}</SelectValue>
            </SelectTrigger>
            <SelectContent>
              {eligibleFields.length > 0 ? (
                <SelectItem value="Context">{t('rules.operandContext')}</SelectItem>
              ) : null}
              {eligibleParameters.length > 0 ? (
                <SelectItem value="Parameter">{t('rules.operandParameter')}</SelectItem>
              ) : null}
              {literalTypes.length > 0 ? (
                <SelectItem value="Literal">{t('rules.operandLiteral')}</SelectItem>
              ) : null}
              {eligibleFunctions.length > 0 ? (
                <SelectItem value="Function">{t('rules.operandFunction')}</SelectItem>
              ) : null}
            </SelectContent>
          </Select>
        </Field>
        {operand.kind === 'Context' ? (
          <Field>
            <FieldLabel htmlFor={`operand-${operand.id}-context`}>
              {t('rules.contextField')}
            </FieldLabel>
            <Select
              value={operand.reference || null}
              disabled={disabled}
              onValueChange={(value) => onChange({ ...operand, reference: value ?? '' })}
            >
              <SelectTrigger id={`operand-${operand.id}-context`}>
                <SelectValue>
                  {(value) =>
                    eligibleFields.find((field) => field.path === value)?.displayName ??
                    t('rules.selectContextField')
                  }
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                {eligibleFields.map((field) => (
                  <SelectItem key={field.path} value={field.path}>
                    {field.displayName}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
        ) : null}
        {operand.kind === 'Parameter' ? (
          <Field>
            <FieldLabel htmlFor={`operand-${operand.id}-parameter`}>
              {t('rules.parameter')}
            </FieldLabel>
            <Select
              value={operand.reference || null}
              disabled={disabled}
              onValueChange={(value) => onChange({ ...operand, reference: value ?? '' })}
            >
              <SelectTrigger id={`operand-${operand.id}-parameter`}>
                <SelectValue>
                  {(value) =>
                    eligibleParameters.find((parameter) => parameter.key === value)?.key ??
                    t('rules.selectParameter')
                  }
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                {eligibleParameters.map((parameter) => (
                  <SelectItem key={parameter.id} value={parameter.key}>
                    {parameter.key || t('rules.unnamedParameter')}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
        ) : null}
        {operand.kind === 'Function' ? (
          <Field>
            <FieldLabel htmlFor={`operand-${operand.id}-function`}>
              {t('rules.function')}
            </FieldLabel>
            <Select
              value={operand.function}
              disabled={disabled}
              onValueChange={(value) => {
                if (!value) return;
                const definition = eligibleFunctions.find(
                  (candidate) => candidate.function === value,
                );
                onChange({
                  ...operand,
                  function: value as RuleExpressionFunction,
                  arguments: createFunctionArguments(
                    definition,
                    schema,
                    parameters,
                    expressionLanguage,
                  ),
                });
              }}
            >
              <SelectTrigger id={`operand-${operand.id}-function`}>
                <SelectValue>
                  {(value) => t(`rules.function${value}`, { defaultValue: value })}
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                {eligibleFunctions.map((definition) => (
                  <SelectItem key={definition.function} value={definition.function}>
                    {t(`rules.function${definition.function}`, {
                      defaultValue: definition.function,
                    })}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
        ) : null}
      </div>
      {operand.kind === 'Literal' ? (
        <div className="grid gap-3 sm:grid-cols-2">
          {literalTypes.length > 1 ? (
            <Field>
              <FieldLabel htmlFor={`operand-${operand.id}-literal-type`}>
                {t('rules.type')}
              </FieldLabel>
              <Select
                value={operand.literalType}
                disabled={disabled}
                onValueChange={(value) =>
                  value && onChange({ ...operand, literalType: value as RuleValueType })
                }
              >
                <SelectTrigger id={`operand-${operand.id}-literal-type`}>
                  <SelectValue>
                    {(value) => t(fieldTypeTranslationKey(value as RuleValueType))}
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  {literalTypes.map((type) => (
                    <SelectItem key={type} value={type}>
                      {t(fieldTypeTranslationKey(type))}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
          ) : null}
          <TypedValueField
            id={`operand-${operand.id}-literal`}
            label={t('rules.value')}
            type={operand.literalType}
            value={operand.literalValue}
            disabled={disabled}
            onChange={(literalValue) => onChange({ ...operand, literalValue })}
          />
        </div>
      ) : null}
      {operand.kind === 'Function' && functionDefinition ? (
        <div className="space-y-3 border-l-2 border-border pl-3">
          {(functionDefinition.parameters ?? []).map((parameter, index) => {
            const argument = operand.arguments[index];
            if (!argument) return null;
            const shapes = (parameter.acceptedTypes ?? []).map((type) => ({
              type,
              cardinality: parameter.cardinality,
            }));
            return (
              <OperandEditor
                key={argument.id}
                label={t('rules.functionArgument', { index: index + 1 })}
                operand={argument}
                schema={schema}
                parameters={parameters}
                expressionLanguage={expressionLanguage}
                acceptedShapes={shapes}
                disabled={disabled}
                onChange={(nextArgument) =>
                  onChange({
                    ...operand,
                    arguments: operand.arguments.map((item, itemIndex) =>
                      itemIndex === index ? nextArgument : item,
                    ),
                  })
                }
              />
            );
          })}
        </div>
      ) : null}
    </div>
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
  const field = schema.fields?.[0];
  return {
    id: crypto.randomUUID(),
    kind: 'predicate',
    left: editableOperand({ kind: 'Context', reference: field?.path ?? '' }),
    operator: 'Equal',
    right: editableOperand({ kind: 'Literal', literalType: field?.type ?? 'Text' }),
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

function toCreateEditorState(schemas: RuleContextSchema[]): EditorState | null {
  const firstSchema = schemas.find(
    (schema) => schema.scope && schema.contextKey && schema.version !== undefined,
  );
  if (!firstSchema?.scope) return null;
  return {
    name: '',
    description: '',
    scope: firstSchema.scope,
    contextKey: '',
    contextSchemaVersion: firstSchema.version ?? 1,
    outcomeKind: 'Validation',
    parameters: [],
    condition: defaultCondition(firstSchema),
    violationCode: '',
    severity: 'Error',
    message: '',
    decision: 'Deny',
  };
}

function toEditorState(detail: RuleDefinitionDetail, schemas: RuleContextSchema[]): EditorState {
  const schema = schemas.find(
    (candidate) =>
      candidate.contextKey === detail.contextKey &&
      candidate.version === detail.contextSchemaVersion,
  ) ?? {
    contextKey: detail.contextKey ?? undefined,
    version: detail.contextSchemaVersion ?? undefined,
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

function distinctDefined<T>(values: (T | null | undefined)[]): T[] {
  return [...new Set(values.filter((value): value is T => value != null))];
}

function deriveRuleKey(name: string): string {
  const normalized = name
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/đ/g, 'd')
    .toLocaleLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');
  const key = /^[a-z]/.test(normalized) ? normalized : normalized ? `rule_${normalized}` : 'rule';
  return key.slice(0, 63).replace(/_+$/g, '') || 'rule';
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
    left: node.left ? fromOperandDto(node.left) : editableOperand({ kind: 'Context' }),
    operator: node.predicateOperator ?? 'Equal',
    right: node.right ? fromOperandDto(node.right) : null,
  };
}

function fromOperandDto(operand: RuleOperand): EditableOperand {
  return editableOperand({
    kind: operand.kind ?? 'Literal',
    reference: operand.reference ?? '',
    literalType: operand.literal?.type ?? 'Text',
    literalValue: (operand.literal?.values ?? []).join(', '),
    function: operand.function ?? null,
    arguments: (operand.arguments ?? []).map(fromOperandDto),
  });
}

function ensureRootGroup(node: EditableNode): EditableGroup {
  return node.kind === 'group'
    ? node
    : { id: crypto.randomUUID(), kind: 'group', operator: 'All', children: [node] };
}

function toConditionDto(node: EditableNode): RuleConditionNode {
  if (node.kind === 'group') {
    return {
      nodeId: nodeId(node.id),
      logicalOperator: node.operator,
      predicateOperator: undefined,
      left: undefined,
      right: undefined,
      children: node.children.map(toConditionDto),
    };
  }
  return {
    nodeId: nodeId(node.id),
    logicalOperator: undefined,
    predicateOperator: node.operator,
    left: toOperandDto(node.left),
    right: node.right ? toOperandDto(node.right) : undefined,
    children: [],
  };
}

function toOperandDto(operand: EditableOperand): RuleOperand {
  if (operand.kind === 'Function') {
    return {
      kind: 'Function',
      function: operand.function ?? undefined,
      arguments: operand.arguments.map(toOperandDto),
    };
  }
  if (operand.kind === 'Literal') {
    return {
      kind: 'Literal',
      reference: null,
      literal: typedRuleValue(operand.literalType, operand.literalValue, false),
      arguments: [],
    };
  }
  return {
    kind: operand.kind,
    reference: operand.reference,
    arguments: [],
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

function editableOperand(
  value: Partial<Omit<EditableOperand, 'id'>> & Pick<EditableOperand, 'kind'>,
): EditableOperand {
  return {
    id: crypto.randomUUID(),
    kind: value.kind,
    reference: value.reference ?? '',
    literalType: value.literalType ?? 'Text',
    literalValue: value.literalValue ?? '',
    function: value.function ?? null,
    arguments: value.arguments ?? [],
  };
}

function createOperand(
  kind: RuleOperandKind,
  schema: RuleContextSchema,
  parameters: EditableParameter[],
  expressionLanguage: RuleExpressionLanguage,
  acceptedShapes?: NonNullable<
    NonNullable<RuleExpressionLanguage['operators']>[number]['leftShapes']
  >,
): EditableOperand {
  if (kind === 'Context') {
    const field = (schema.fields ?? []).find(
      (candidate) =>
        candidate.type && acceptsShape(acceptedShapes, candidate.type, candidate.allowMultiple),
    );
    return editableOperand({
      kind,
      reference: field?.path ?? '',
      literalType: field?.type ?? firstAcceptedType(acceptedShapes),
    });
  }
  if (kind === 'Parameter') {
    const parameter = parameters.find((candidate) =>
      acceptsShape(acceptedShapes, candidate.type, candidate.allowMultiple),
    );
    return editableOperand({
      kind,
      reference: parameter?.key ?? '',
      literalType: parameter?.type ?? firstAcceptedType(acceptedShapes),
    });
  }
  if (kind === 'Function') {
    const definition = (expressionLanguage.functions ?? []).find(
      (candidate) =>
        candidate.function &&
        candidate.returnType &&
        acceptsShape(
          acceptedShapes,
          candidate.returnType,
          candidate.returnCardinality === 'Multiple',
        ),
    );
    return editableOperand({
      kind,
      function: definition?.function ?? null,
      literalType: definition?.returnType ?? firstAcceptedType(acceptedShapes),
      arguments: createFunctionArguments(definition, schema, parameters, expressionLanguage),
    });
  }
  return editableOperand({ kind: 'Literal', literalType: firstAcceptedType(acceptedShapes) });
}

function createFunctionArguments(
  definition: NonNullable<RuleExpressionLanguage['functions']>[number] | undefined,
  schema: RuleContextSchema,
  parameters: EditableParameter[],
  expressionLanguage: RuleExpressionLanguage,
): EditableOperand[] {
  return (definition?.parameters ?? []).map((parameter) => {
    const acceptedShapes = (parameter.acceptedTypes ?? []).map((type) => ({
      type,
      cardinality: parameter.cardinality,
    }));
    const fieldAvailable = (schema.fields ?? []).some(
      (field) => field.type && acceptsShape(acceptedShapes, field.type, field.allowMultiple),
    );
    const parameterAvailable = parameters.some((candidate) =>
      acceptsShape(acceptedShapes, candidate.type, candidate.allowMultiple),
    );
    const acceptsLiteral = acceptedShapes.some((shape) => shape.cardinality !== 'Multiple');
    const kind: RuleOperandKind = fieldAvailable
      ? 'Context'
      : parameterAvailable
        ? 'Parameter'
        : acceptsLiteral
          ? 'Literal'
          : 'Context';
    return createOperand(kind, schema, parameters, expressionLanguage, acceptedShapes);
  });
}

function ensureCompatibleOperand(
  operand: EditableOperand | null,
  acceptedShapes: NonNullable<
    NonNullable<RuleExpressionLanguage['operators']>[number]['rightShapes']
  >,
  schema: RuleContextSchema,
  parameters: EditableParameter[],
  expressionLanguage: RuleExpressionLanguage,
): EditableOperand {
  const shape = operand
    ? resolveOperandShape(operand, schema, parameters, expressionLanguage)
    : null;
  if (
    operand &&
    shape &&
    acceptsShape(acceptedShapes, shape.type, shape.cardinality === 'Multiple')
  ) {
    return operand;
  }
  return createOperand('Literal', schema, parameters, expressionLanguage, acceptedShapes);
}

function resolveOperandShape(
  operand: EditableOperand,
  schema: RuleContextSchema,
  parameters: EditableParameter[],
  expressionLanguage: RuleExpressionLanguage,
): OperandShape | null {
  if (operand.kind === 'Literal') {
    return { type: operand.literalType, cardinality: 'Scalar' };
  }
  if (operand.kind === 'Context') {
    const field = (schema.fields ?? []).find((candidate) => candidate.path === operand.reference);
    return field?.type
      ? { type: field.type, cardinality: field.allowMultiple ? 'Multiple' : 'Scalar' }
      : null;
  }
  if (operand.kind === 'Parameter') {
    const parameter = parameters.find((candidate) => candidate.key === operand.reference);
    return parameter
      ? {
          type: parameter.type,
          cardinality: parameter.allowMultiple ? 'Multiple' : 'Scalar',
        }
      : null;
  }
  const definition = (expressionLanguage.functions ?? []).find(
    (candidate) => candidate.function === operand.function,
  );
  return definition?.returnType
    ? {
        type: definition.returnType,
        cardinality: definition.returnCardinality === 'Multiple' ? 'Multiple' : 'Scalar',
      }
    : null;
}

function compatibleOperators(
  expressionLanguage: RuleExpressionLanguage,
  shape: OperandShape | null,
) {
  if (!shape) return [];
  return (expressionLanguage.operators ?? []).filter(
    (definition) =>
      definition.operator &&
      acceptsShape(definition.leftShapes, shape.type, shape.cardinality === 'Multiple'),
  );
}

function rightShapesFor(
  definition: NonNullable<RuleExpressionLanguage['operators']>[number] | undefined,
  leftShape: OperandShape | null,
) {
  return (definition?.rightShapes ?? []).filter(
    (shape) => !definition?.requiresMatchingTypes || shape.type === leftShape?.type,
  );
}

function acceptsShape(
  acceptedShapes:
    | ReadonlyArray<{
        type?: RuleValueType;
        cardinality?: RuleExpressionCardinality;
      }>
    | null
    | undefined,
  type: RuleValueType,
  isMultiple: boolean | undefined,
): boolean {
  if (!acceptedShapes || acceptedShapes.length === 0) return true;
  return acceptedShapes.some(
    (shape) =>
      shape.type === type &&
      (shape.cardinality === 'Any' ||
        shape.cardinality === undefined ||
        (shape.cardinality === 'Multiple') === Boolean(isMultiple)),
  );
}

function firstAcceptedType(
  acceptedShapes?: ReadonlyArray<{ type?: RuleValueType }>,
): RuleValueType {
  return acceptedShapes?.find((shape) => shape.type)?.type ?? 'Text';
}

function validateOperand(
  operand: EditableOperand,
  schema: RuleContextSchema,
  parameters: EditableParameter[],
  expressionLanguage: RuleExpressionLanguage,
): string | null {
  if (operand.kind === 'Context') {
    return (schema.fields ?? []).some((field) => field.path === operand.reference)
      ? null
      : 'Select a valid context field.';
  }
  if (operand.kind === 'Parameter') {
    return parameters.some((parameter) => parameter.key === operand.reference)
      ? null
      : 'Select a valid parameter.';
  }
  if (operand.kind === 'Literal') {
    return operand.literalValue.trim() ? null : 'Condition values are required.';
  }
  const definition = (expressionLanguage.functions ?? []).find(
    (candidate) => candidate.function === operand.function,
  );
  if (!definition || (definition.parameters ?? []).length !== operand.arguments.length)
    return 'Select a valid function.';
  for (let index = 0; index < operand.arguments.length; index += 1) {
    const argument = operand.arguments[index];
    const parameter = definition.parameters?.[index];
    if (!argument || !parameter) return 'Complete every function argument.';
    const error = validateOperand(argument, schema, parameters, expressionLanguage);
    if (error) return error;
    const shape = resolveOperandShape(argument, schema, parameters, expressionLanguage);
    const acceptedShapes = (parameter.acceptedTypes ?? []).map((type) => ({
      type,
      cardinality: parameter.cardinality,
    }));
    if (!shape || !acceptsShape(acceptedShapes, shape.type, shape.cardinality === 'Multiple'))
      return 'Select a compatible function argument.';
  }
  return null;
}

function validateEditor(
  editor: EditorState,
  schema: RuleContextSchema,
  expressionLanguage: RuleExpressionLanguage,
): string | null {
  if (!editor.name.trim() || !editor.description.trim())
    return 'Name and description are required.';
  const keys = editor.parameters.map((parameter) => parameter.key.trim());
  if (keys.some((key) => !/^[a-z][a-z0-9_]*$/.test(key)) || new Set(keys).size !== keys.length) {
    return 'Parameter keys must be unique lowercase identifiers.';
  }
  if (!editor.condition.children.length) return 'Add at least one condition.';
  const conditionError = validateNode(
    editor.condition,
    schema,
    editor.parameters,
    expressionLanguage,
  );
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
  expressionLanguage: RuleExpressionLanguage,
): string | null {
  if (node.kind === 'group') {
    if (!node.children.length || (node.operator === 'Not' && node.children.length !== 1)) {
      return 'Condition groups must contain valid children.';
    }
    for (const child of node.children) {
      const error = validateNode(child, schema, parameters, expressionLanguage);
      if (error) return error;
    }
    return null;
  }
  const leftError = validateOperand(node.left, schema, parameters, expressionLanguage);
  if (leftError) return leftError;
  const leftShape = resolveOperandShape(node.left, schema, parameters, expressionLanguage);
  const definition = (expressionLanguage.operators ?? []).find(
    (candidate) => candidate.operator === node.operator,
  );
  if (
    !definition ||
    !leftShape ||
    !acceptsShape(definition.leftShapes, leftShape.type, leftShape.cardinality === 'Multiple')
  )
    return 'Select a compatible operator.';
  if ((definition.rightShapes ?? []).length === 0)
    return node.right ? 'Unary conditions cannot have a right operand.' : null;
  if (!node.right) return 'Select a right operand.';
  const rightError = validateOperand(node.right, schema, parameters, expressionLanguage);
  if (rightError) return rightError;
  const rightShape = resolveOperandShape(node.right, schema, parameters, expressionLanguage);
  if (
    !rightShape ||
    !acceptsShape(definition.rightShapes, rightShape.type, rightShape.cardinality === 'Multiple') ||
    (definition.requiresMatchingTypes && rightShape.type !== leftShape.type)
  )
    return 'Select a compatible right operand.';
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
