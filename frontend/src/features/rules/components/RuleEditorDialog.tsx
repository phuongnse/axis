import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Archive, Braces, Play, Plus, Save, Send, Trash2 } from 'lucide-react';
import { type ReactNode, useEffect, useId, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { selectedItemHighlight } from '@/components/shared/interactionStates';
import { ManagedDialog, ManagedDialogBody } from '@/components/shared/ManagedDialog';
import { MetadataTag } from '@/components/shared/MetadataTag';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { StatusNotice } from '@/components/shared/StatusNotice';
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
  assistRuleExpression,
  createRuleDefinition,
  getRuleDefinition,
  publishRuleDefinition,
  type RuleContextSchema,
  type RuleDecision,
  type RuleDefinitionDetail,
  type RuleExpressionAuthoring,
  type RuleExpressionCompletion,
  type RuleExpressionLanguage,
  type RuleParameterDefinition,
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
import { valueTypeLabel } from '../reference';
import { RuleBehaviorSummary } from './RuleBehaviorSummary';
import { RuleExpressionGuide } from './RuleExpressionGuide';
import { RuleExpressionView } from './RuleExpressionView';
import { RuleOriginBadge } from './RuleOriginBadge';

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

interface EditorState {
  name: string;
  description: string;
  scope: RuleScope;
  contextKey: string;
  contextSchemaVersion: number;
  outcomeKind: RuleOutcomeKind;
  parameters: EditableParameter[];
  expressionSyntax: string;
  violationCode: string;
  severity: RuleSeverity;
  message: string;
  decision: RuleDecision;
}

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
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const creating = definitionKey === null;
  const [editor, setEditor] = useState<EditorState | null>(null);
  const [cursor, setCursor] = useState(0);
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
  const initializedRef = useRef<string | null>(null);

  const detailQuery = useQuery({
    queryKey: ruleDefinitionQueryKeys.detail(definitionKey ?? ''),
    queryFn: () => {
      if (!definitionKey) throw new Error(t('rules.editorUnavailable'));
      return getRuleDefinition(definitionKey);
    },
    enabled: open && Boolean(definitionKey),
  });
  const detail = detailQuery.data;
  const schemasQuery = useQuery({ ...ruleContextSchemasQueryOptions(), enabled: open });
  const languageQuery = useQuery({
    ...ruleExpressionLanguageQueryOptions(),
    enabled: open && Boolean(detail && detail.status === 'Draft'),
  });
  const schema = useMemo(
    () =>
      (schemasQuery.data ?? []).find(
        (candidate) =>
          candidate.contextKey === editor?.contextKey &&
          candidate.version === editor?.contextSchemaVersion,
      ),
    [editor?.contextKey, editor?.contextSchemaVersion, schemasQuery.data],
  );
  const detailSchema = useMemo(
    () =>
      (schemasQuery.data ?? []).find(
        (candidate) =>
          candidate.contextKey === detail?.contextKey &&
          candidate.version === detail?.contextSchemaVersion,
      ),
    [detail?.contextKey, detail?.contextSchemaVersion, schemasQuery.data],
  );
  const initialRequest = useMemo(
    () => ({
      expressionLanguageVersion: detail?.expressionLanguageVersion,
      contextKey: detailSchema?.contextKey ?? null,
      contextSchemaVersion: detailSchema?.version ?? null,
      parameters: detail?.parameters ?? [],
      syntax: null,
      condition: detail?.condition,
      cursorOffset: 0,
      language: i18n.language,
    }),
    [
      detail?.condition,
      detail?.expressionLanguageVersion,
      detail?.parameters,
      detailSchema?.contextKey,
      detailSchema?.version,
      i18n.language,
    ],
  );
  const initialExpressionQuery = useQuery({
    queryKey: ruleDefinitionQueryKeys.expressionAssist(initialRequest),
    queryFn: () => assistRuleExpression(initialRequest),
    enabled: open && Boolean(detail?.condition),
    staleTime: Number.POSITIVE_INFINITY,
  });
  const authoringRequest = useMemo(
    () => ({
      expressionLanguageVersion: detail?.expressionLanguageVersion,
      contextKey: schema?.contextKey ?? null,
      contextSchemaVersion: schema?.version ?? null,
      parameters: toParameterDtos(editor?.parameters ?? []),
      syntax: editor?.expressionSyntax ?? '',
      condition: undefined,
      cursorOffset: editor?.expressionSyntax.length ?? 0,
      language: i18n.language,
    }),
    [
      detail?.expressionLanguageVersion,
      editor?.expressionSyntax,
      editor?.parameters,
      i18n.language,
      schema?.contextKey,
      schema?.version,
    ],
  );
  const authoringQuery = useQuery({
    queryKey: ruleDefinitionQueryKeys.expressionAssist(authoringRequest),
    queryFn: () => assistRuleExpression(authoringRequest),
    enabled: open && detail?.status === 'Draft' && Boolean(editor && schema),
    staleTime: Number.POSITIVE_INFINITY,
  });

  useEffect(() => {
    if (!open) {
      initializedRef.current = null;
      setEditor(null);
      return;
    }
    setFeedback(null);
    setSimulation(null);
    setSampleContext({});
    setSampleParameters({});
  }, [open]);

  useEffect(() => {
    if (!detail) return;
    if (detail.condition && !initialExpressionQuery.data) return;
    const identity = `${detail.definitionKey ?? ''}:${detail.revision ?? ''}`;
    if (initializedRef.current === identity) return;
    const state = detailToEditor(detail, initialExpressionQuery.data?.syntax ?? '');
    setEditor(state);
    setCursor(state.expressionSyntax.length);
    initializedRef.current = identity;
  }, [detail, initialExpressionQuery.data]);

  useEffect(() => {
    if (!creating || !open || editor || !schemasQuery.isSuccess) return;
    const first = firstSchema(schemasQuery.data ?? []);
    if (first) setEditor(createEditor(first));
  }, [creating, editor, open, schemasQuery.data, schemasQuery.isSuccess]);

  const createMutation = useMutation({
    mutationFn: async (state: EditorState) => {
      const selected = findSchema(schemasQuery.data ?? [], state);
      if (!state.name.trim() || !state.description.trim() || !selected?.scope)
        throw new Error(t('rules.createError'));
      return createRuleDefinition({
        name: state.name.trim(),
        description: state.description.trim(),
        scope: selected.scope,
        contextKey: selected.contextKey,
        contextSchemaVersion: selected.version,
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
      if (!detail?.definitionKey || detail.revision == null || !schema)
        throw new Error(t('rules.editorUnavailable'));
      const error = validateEditor(state, authoringQuery.data);
      if (error) throw new Error(error);
      return saveRuleDefinitionDraft(detail.definitionKey, {
        expectedRevision: detail.revision,
        name: state.name.trim(),
        description: state.description.trim(),
        scope: state.scope,
        contextKey: state.contextKey,
        contextSchemaVersion: state.contextSchemaVersion,
        outcomeKind: state.outcomeKind,
        parameters: toParameterDtos(state.parameters),
        expressionSyntax: state.expressionSyntax,
        outcome:
          state.outcomeKind === 'Validation'
            ? {
                kind: 'Validation',
                violationCode: state.violationCode.trim(),
                severity: state.severity,
                message: state.message.trim(),
              }
            : { kind: 'Decision', decision: state.decision },
      });
    },
    onSuccess: async (saved) => {
      setDetailCache(queryClient, saved);
      await queryClient.invalidateQueries({ queryKey: ruleDefinitionQueryKeys.all });
      setFeedback({ variant: 'success', text: t('rules.saved') });
    },
    onError: (error) =>
      setFeedback({ variant: 'destructive', text: readError(error, t('rules.saveError')) }),
  });

  const lifecycleMutation = useMutation({
    mutationFn: async (action: 'publish' | 'draft' | 'archive') => {
      if (!detail?.definitionKey || detail.revision == null)
        throw new Error(t('rules.editorUnavailable'));
      if (action === 'publish') {
        if (!editor) throw new Error(t('rules.editorUnavailable'));
        const saved = await saveMutation.mutateAsync(editor);
        if (!saved.definitionKey || saved.revision == null)
          throw new Error(t('rules.editorUnavailable'));
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
      return simulateRuleDefinition(saved.definitionKey ?? detail.definitionKey, {
        definitionVersion: null,
        parameters: Object.fromEntries(
          editor.parameters.flatMap((parameter) => {
            const key = parameter.key.trim();
            const value = sampleParameters[key]?.trim();
            return key && value
              ? [[key, typedRuleValue(parameter.type, value, parameter.allowMultiple)]]
              : [];
          }),
        ),
        context: Object.fromEntries(
          (schema.fields ?? []).flatMap((field) => {
            const path = field.path ?? '';
            const value = sampleContext[path]?.trim();
            return path && value
              ? [[path, typedRuleValue(field.type ?? 'Text', value, field.allowMultiple)]]
              : [];
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
  const expressionReady =
    Boolean(authoringQuery.data?.condition) &&
    (authoringQuery.data?.diagnostics ?? []).length === 0 &&
    !authoringQuery.isFetching;
  const baseline = detail
    ? detailToEditor(detail, initialExpressionQuery.data?.syntax ?? '')
    : null;
  const dirty =
    editor !== null &&
    (creating
      ? Boolean(editor.name || editor.description)
      : detail?.status === 'Draft' &&
        baseline !== null &&
        JSON.stringify(editorComparable(editor)) !== JSON.stringify(editorComparable(baseline)));

  const requestClose = (nextOpen: boolean) => {
    if (nextOpen) return onOpenChange(true);
    if (busy) return;
    if (dirty) return setDiscardOpen(true);
    onOpenChange(false);
  };

  const eligibleSchema = firstSchema(schemasQuery.data ?? []);
  const loadFailed =
    detailQuery.isError ||
    schemasQuery.isError ||
    languageQuery.isError ||
    initialExpressionQuery.isError;

  return (
    <ManagedDialog
      open={open}
      onOpenChange={requestClose}
      title={creating ? t('rules.createTitle') : (detail?.name ?? t('rules.loadingRule'))}
      description={
        creating ? t('rules.createDescription') : (detail?.description ?? t('rules.loadingRule'))
      }
      titleAccessory={
        detail ? (
          <>
            <RuleOriginBadge origin="Workspace" />
            <LifecycleBadge detail={detail} />
          </>
        ) : null
      }
      closeDisabled={busy}
      dirty={dirty}
      footer={
        <>
          {detail && detail.status !== 'Archived' && detail.latestPublishedVersion ? (
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
              onClick={() => requestClose(false)}
            >
              {creating || detail?.status === 'Draft' ? t('app.cancel') : t('app.close')}
            </Button>
            {creating && editor ? (
              <Button
                type="button"
                disabled={
                  busy ||
                  !editor.name.trim() ||
                  !editor.description.trim() ||
                  !findSchema(schemasQuery.data ?? [], editor)
                }
                onClick={() => createMutation.mutate(editor)}
              >
                {t('rules.createAction')}
              </Button>
            ) : null}
            {detail?.status === 'Draft' && editor ? (
              <>
                <Button
                  type="button"
                  variant="outline"
                  disabled={busy || !expressionReady}
                  onClick={() => saveMutation.mutate(editor)}
                >
                  <Save className="size-4" aria-hidden />
                  {t('rules.saveDraft')}
                </Button>
                <Button
                  type="button"
                  disabled={busy || !expressionReady}
                  onClick={() => setPublishOpen(true)}
                >
                  <Send className="size-4" aria-hidden />
                  {t('rules.publish')}
                </Button>
              </>
            ) : detail?.status === 'Published' ? (
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
        {feedback ? <StatusNotice tone={feedback.variant}>{feedback.text}</StatusNotice> : null}
        {loadFailed ? (
          <StatusNotice tone="destructive" title={t('rules.loadErrorTitle')}>
            {t('rules.loadErrorBody')}
          </StatusNotice>
        ) : creating && schemasQuery.isSuccess && !eligibleSchema ? (
          <Empty className="border">
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <Braces aria-hidden />
              </EmptyMedia>
              <EmptyTitle>{t('rules.contextUnavailable')}</EmptyTitle>
              <EmptyDescription>{t('rules.noContextForScope')}</EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : detail && detail.status !== 'Draft' ? (
          <ReadOnlyRule detail={detail} contextSchema={detailSchema} />
        ) : editor && (creating || detail) ? (
          <div className="space-y-6">
            <IdentitySection
              editor={editor}
              definitionKey={detail?.definitionKey}
              schemas={schemasQuery.data ?? []}
              disabled={busy}
              onChange={(next) => {
                setEditor(next);
                if (next.contextKey !== editor.contextKey) setCursor(0);
              }}
            />
            {detail && schema && languageQuery.data ? (
              <>
                <ParametersSection
                  parameters={editor.parameters}
                  language={languageQuery.data}
                  disabled={busy}
                  onChange={(parameters) => setEditor({ ...editor, parameters })}
                />
                <ExpressionSection
                  syntax={editor.expressionSyntax}
                  cursor={cursor}
                  schema={schema}
                  parameters={toParameterDtos(editor.parameters)}
                  language={languageQuery.data}
                  authoring={authoringQuery.data}
                  loading={authoringQuery.isFetching}
                  disabled={busy}
                  onCursorChange={setCursor}
                  onChange={(expressionSyntax) => setEditor({ ...editor, expressionSyntax })}
                />
                <OutcomeSection editor={editor} disabled={busy} onChange={setEditor} />
                <SimulationSection
                  schema={schema}
                  parameters={editor.parameters}
                  contextValues={sampleContext}
                  parameterValues={sampleParameters}
                  result={simulation}
                  disabled={busy || !expressionReady}
                  onContextChange={setSampleContext}
                  onParameterChange={setSampleParameters}
                  onSimulate={() => simulateMutation.mutate()}
                />
              </>
            ) : null}
          </div>
        ) : (
          <p role="status" className="text-sm text-muted-foreground">
            {t('rules.loadingRule')}
          </p>
        )}
      </ManagedDialogBody>

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
          {detail && editor && authoringQuery.data ? (
            <PublishReview
              detail={detail}
              editor={editor}
              schema={schema}
              authoring={authoringQuery.data}
            />
          ) : null}
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>{t('app.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              disabled={busy || !expressionReady}
              onClick={() => lifecycleMutation.mutate('publish')}
            >
              {t('rules.publish')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

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

function IdentitySection({
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
  onChange: (state: EditorState) => void;
}) {
  const { t } = useTranslation();
  const scopes = distinct(schemas.map((schema) => schema.scope));
  const candidates = schemas.filter((schema) => schema.scope === editor.scope);
  return (
    <EditorSection title={t('rules.definitionSection')} description={t('rules.definitionHelp')}>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field>
          <FieldLabel htmlFor="rule-name">{t('rules.name')}</FieldLabel>
          <Input
            id="rule-name"
            value={editor.name}
            disabled={disabled}
            onChange={(event) => onChange({ ...editor, name: event.target.value })}
          />
          <FieldDescription>
            {t('rules.derivedKey', { key: definitionKey ?? deriveKey(editor.name) })}
          </FieldDescription>
        </Field>
        <Field>
          <FieldLabel htmlFor="rule-scope">{t('rules.scope')}</FieldLabel>
          <Select
            value={editor.scope}
            disabled={disabled || scopes.length === 0}
            onValueChange={(value) => {
              const scope = value as RuleScope;
              const next = schemas.find((candidate) => candidate.scope === scope);
              if (!next?.contextKey || next.version === undefined) return;
              onChange({
                ...editor,
                scope,
                contextKey: next.contextKey,
                contextSchemaVersion: next.version,
                expressionSyntax: '',
              });
            }}
          >
            <SelectTrigger id="rule-scope">
              <SelectValue>{(value) => t(`rules.scope${value}`)}</SelectValue>
            </SelectTrigger>
            <SelectContent>
              {scopes.map((scope) => (
                <SelectItem key={scope} value={scope}>
                  {t(`rules.scope${scope}`)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <FieldDescription>{t(`rules.scope${editor.scope}Description`)}</FieldDescription>
        </Field>
        <Field className="sm:col-span-2">
          <FieldLabel htmlFor="rule-description">{t('rules.description')}</FieldLabel>
          <Textarea
            id="rule-description"
            value={editor.description}
            disabled={disabled}
            onChange={(event) => onChange({ ...editor, description: event.target.value })}
          />
        </Field>
        <Field>
          <FieldLabel htmlFor="rule-context">{t('rules.context')}</FieldLabel>
          <Select
            value={editor.contextKey}
            disabled={disabled || candidates.length === 0}
            onValueChange={(value) => {
              const next = candidates.find((candidate) => candidate.contextKey === value);
              if (!next?.contextKey || next.version === undefined) return;
              onChange({
                ...editor,
                contextKey: next.contextKey,
                contextSchemaVersion: next.version,
                expressionSyntax: '',
              });
            }}
          >
            <SelectTrigger id="rule-context">
              <SelectValue>
                {(value) =>
                  candidates.find((candidate) => candidate.contextKey === value)?.displayName ??
                  t('rules.selectContext')
                }
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              {candidates.flatMap((candidate) =>
                candidate.contextKey ? (
                  <SelectItem
                    key={`${candidate.contextKey}:${candidate.version}`}
                    value={candidate.contextKey}
                  >
                    {candidate.displayName}
                  </SelectItem>
                ) : (
                  []
                ),
              )}
            </SelectContent>
          </Select>
        </Field>
        <Field>
          <FieldLabel htmlFor="rule-outcome-kind">{t('rules.outcome')}</FieldLabel>
          <Select
            value={editor.outcomeKind}
            disabled={disabled}
            onValueChange={(value) =>
              onChange({ ...editor, outcomeKind: value as RuleOutcomeKind })
            }
          >
            <SelectTrigger id="rule-outcome-kind">
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

function ParametersSection({
  parameters,
  language,
  disabled,
  onChange,
}: {
  parameters: EditableParameter[];
  language: RuleExpressionLanguage;
  disabled: boolean;
  onChange: (parameters: EditableParameter[]) => void;
}) {
  const { t, i18n } = useTranslation();
  const types = (language.valueTypes ?? []).flatMap((definition) =>
    definition.type ? [definition.type] : [],
  );
  return (
    <EditorSection title={t('rules.parameters')} description={t('rules.parametersHelp')}>
      <div className="space-y-3">
        {parameters.map((parameter) => (
          <div
            key={parameter.id}
            className="grid gap-3 border-b border-border pb-3 last:border-0 sm:grid-cols-2"
          >
            <Field>
              <FieldLabel htmlFor={`parameter-${parameter.id}-key`}>{t('rules.key')}</FieldLabel>
              <Input
                id={`parameter-${parameter.id}-key`}
                value={parameter.key}
                disabled={disabled}
                onChange={(event) =>
                  onChange(
                    parameters.map((candidate) =>
                      candidate.id === parameter.id
                        ? { ...candidate, key: event.target.value }
                        : candidate,
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
                  onChange(
                    parameters.map((candidate) =>
                      candidate.id === parameter.id
                        ? { ...candidate, type: value as RuleValueType }
                        : candidate,
                    ),
                  )
                }
              >
                <SelectTrigger id={`parameter-${parameter.id}-type`}>
                  <SelectValue>
                    {(value) => valueTypeLabel(language, value as RuleValueType, i18n.language)}
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  {types.map((type) => (
                    <SelectItem key={type} value={type}>
                      {valueTypeLabel(language, type, i18n.language)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel htmlFor={`parameter-${parameter.id}-allowed-values`}>
                {t('rules.allowedValues')}
              </FieldLabel>
              <Input
                id={`parameter-${parameter.id}-allowed-values`}
                value={parameter.allowedValues}
                placeholder={t('rules.allowedValuesPlaceholder')}
                disabled={disabled}
                onChange={(event) =>
                  onChange(
                    parameters.map((candidate) =>
                      candidate.id === parameter.id
                        ? { ...candidate, allowedValues: event.target.value }
                        : candidate,
                    ),
                  )
                }
              />
            </Field>
            <div className="flex items-end justify-between gap-3 pb-1">
              <div className="flex flex-wrap gap-4">
                <FieldLabel
                  htmlFor={`parameter-${parameter.id}-required`}
                  className="flex h-9 items-center gap-2 text-sm"
                >
                  <Checkbox
                    id={`parameter-${parameter.id}-required`}
                    checked={parameter.isRequired}
                    disabled={disabled}
                    onCheckedChange={(checked) =>
                      onChange(
                        parameters.map((candidate) =>
                          candidate.id === parameter.id
                            ? { ...candidate, isRequired: checked === true }
                            : candidate,
                        ),
                      )
                    }
                  />
                  {t('rules.parameterRequired')}
                </FieldLabel>
                <FieldLabel
                  htmlFor={`parameter-${parameter.id}-multiple`}
                  className="flex h-9 items-center gap-2 text-sm"
                >
                  <Checkbox
                    id={`parameter-${parameter.id}-multiple`}
                    checked={parameter.allowMultiple}
                    disabled={disabled}
                    onCheckedChange={(checked) =>
                      onChange(
                        parameters.map((candidate) =>
                          candidate.id === parameter.id
                            ? { ...candidate, allowMultiple: checked === true }
                            : candidate,
                        ),
                      )
                    }
                  />
                  {t('rules.parameterMultiple')}
                </FieldLabel>
              </div>
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                aria-label={t('rules.removeParameter')}
                disabled={disabled}
                onClick={() =>
                  onChange(parameters.filter((candidate) => candidate.id !== parameter.id))
                }
              >
                <Trash2 aria-hidden />
              </Button>
            </div>
          </div>
        ))}
      </div>
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
              type: types[0] ?? 'Text',
              isRequired: true,
              allowMultiple: false,
              allowedValues: '',
            },
          ])
        }
      >
        <Plus aria-hidden />
        {t('rules.addParameter')}
      </Button>
    </EditorSection>
  );
}

function ExpressionSection({
  syntax,
  cursor,
  schema,
  parameters,
  language,
  authoring,
  loading,
  disabled,
  onCursorChange,
  onChange,
}: {
  syntax: string;
  cursor: number;
  schema: RuleContextSchema;
  parameters: RuleParameterDefinition[];
  language: RuleExpressionLanguage;
  authoring?: RuleExpressionAuthoring;
  loading: boolean;
  disabled: boolean;
  onCursorChange: (cursor: number) => void;
  onChange: (syntax: string) => void;
}) {
  const { t } = useTranslation();
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const [focused, setFocused] = useState(false);
  const [active, setActive] = useState(0);
  const prefix = currentPrefix(syntax, cursor);
  const suggestions = (authoring?.completions ?? [])
    .filter(
      (completion) =>
        !prefix ||
        (completion.label ?? '').toLocaleLowerCase().startsWith(prefix.toLocaleLowerCase()),
    )
    .slice(0, 8);

  const insert = (completion: RuleExpressionCompletion) => {
    const start = cursor - prefix.length;
    const length = prefix.length;
    const text = completion.insertText ?? '';
    const next = `${syntax.slice(0, start)}${text}${syntax.slice(start + length)}`;
    const nextCursor = start + (completion.cursorOffset ?? text.length);
    onChange(next);
    onCursorChange(nextCursor);
    requestAnimationFrame(() => {
      textareaRef.current?.focus();
      textareaRef.current?.setSelectionRange(nextCursor, nextCursor);
    });
  };

  const diagnostics = authoring?.diagnostics ?? [];
  return (
    <EditorSection
      title={t('rules.expressionEditorTitle')}
      description={t('rules.expressionEditorDescription')}
      action={
        <RuleExpressionGuide
          expressionLanguageVersion={language.version}
          contextSchema={schema}
          parameters={parameters}
          completions={authoring?.completions}
          onInsert={insert}
        />
      }
    >
      <Field>
        <FieldLabel htmlFor="rule-expression-syntax">{t('rules.expressionSyntax')}</FieldLabel>
        <div className="relative">
          <Textarea
            ref={textareaRef}
            id="rule-expression-syntax"
            className="min-h-32 font-mono text-sm"
            value={syntax}
            disabled={disabled}
            spellCheck={false}
            aria-invalid={diagnostics.length > 0}
            onFocus={() => setFocused(true)}
            onBlur={() => setTimeout(() => setFocused(false), 100)}
            onSelect={(event) => onCursorChange(event.currentTarget.selectionStart)}
            onChange={(event) => {
              onChange(event.target.value);
              onCursorChange(event.target.selectionStart);
              setActive(0);
            }}
            onKeyDown={(event) => {
              if (!focused || suggestions.length === 0) return;
              if (event.key === 'ArrowDown') {
                event.preventDefault();
                setActive((value) => (value + 1) % suggestions.length);
              } else if (event.key === 'ArrowUp') {
                event.preventDefault();
                setActive((value) => (value - 1 + suggestions.length) % suggestions.length);
              } else if (event.key === 'Enter' && suggestions[active] && prefix) {
                event.preventDefault();
                insert(suggestions[active]);
              } else if (event.key === 'Escape') {
                setFocused(false);
              }
            }}
          />
          {focused && prefix && suggestions.length > 0 ? (
            <div
              role="listbox"
              aria-label={t('rules.expressionSuggestions')}
              className="absolute right-0 left-0 z-20 mt-1 max-h-64 overflow-y-auto rounded-md border bg-popover p-1 shadow-md"
            >
              {suggestions.map((suggestion, index) => (
                <Button
                  key={`${suggestion.referenceKind}:${suggestion.referenceKey}`}
                  type="button"
                  variant="ghost"
                  role="option"
                  aria-selected={index === active}
                  className={`h-auto w-full items-start justify-between gap-3 rounded-sm px-2 py-2 text-left ${selectedItemHighlight}`}
                  onMouseDown={(event) => event.preventDefault()}
                  onClick={() => insert(suggestion)}
                >
                  <span>
                    <span className="block font-mono text-sm">{suggestion.label}</span>
                    <span className="mt-0.5 block text-xs text-muted-foreground">
                      {suggestion.summary}
                    </span>
                  </span>
                  <MetadataTag>{suggestion.referenceKind}</MetadataTag>
                </Button>
              ))}
            </div>
          ) : null}
        </div>
        {loading ? (
          <FieldDescription role="status">{t('rules.expressionChecking')}</FieldDescription>
        ) : null}
        {diagnostics.map((diagnostic) => (
          <Button
            key={`${diagnostic.code}:${diagnostic.start}`}
            type="button"
            variant="link"
            size="sm"
            className="h-auto justify-start p-0 text-left text-sm text-destructive"
            onClick={() => {
              const start = diagnostic.start ?? 0;
              textareaRef.current?.focus();
              textareaRef.current?.setSelectionRange(start, start + (diagnostic.length ?? 1));
            }}
          >
            {diagnostic.message}
          </Button>
        ))}
      </Field>

      <div className="border-t border-border pt-4">
        <h4 className="text-sm font-semibold">{t('rules.expressionPreview')}</h4>
        <p className="mt-1 text-xs text-muted-foreground">
          {t('rules.expressionPreviewDescription')}
        </p>
        <div className="mt-3">
          {authoring?.condition && authoring.display ? (
            <RuleExpressionView display={authoring.display} />
          ) : (
            <p className="text-sm text-muted-foreground">{t('rules.expressionPreviewEmpty')}</p>
          )}
        </div>
      </div>
    </EditorSection>
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
              onValueChange={(value) => onChange({ ...editor, severity: value as RuleSeverity })}
            >
              <SelectTrigger id="rule-severity">
                <SelectValue>{(value) => t(`rules.severity${value}`)}</SelectValue>
              </SelectTrigger>
              <SelectContent>
                {(['Info', 'Warning', 'Error'] as const).map((severity) => (
                  <SelectItem key={severity} value={severity}>
                    {t(`rules.severity${severity}`)}
                  </SelectItem>
                ))}
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
            onValueChange={(value) => onChange({ ...editor, decision: value as RuleDecision })}
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
        {(schema.fields ?? []).flatMap((field) => {
          const path = field.path;
          return path ? (
            <Field key={path}>
              <FieldLabel htmlFor={`context-${path}`}>{field.displayName ?? path}</FieldLabel>
              <Input
                id={`context-${path}`}
                value={contextValues[path] ?? ''}
                disabled={disabled}
                onChange={(event) =>
                  onContextChange({ ...contextValues, [path]: event.target.value })
                }
              />
            </Field>
          ) : (
            []
          );
        })}
        {parameters.flatMap((parameter) =>
          parameter.key.trim() ? (
            <Field key={parameter.id}>
              <FieldLabel htmlFor={`sample-${parameter.id}`}>
                {t('rules.parameter')}: {parameter.key.trim()}
              </FieldLabel>
              <Input
                id={`sample-${parameter.id}`}
                value={parameterValues[parameter.key.trim()] ?? ''}
                disabled={disabled}
                onChange={(event) =>
                  onParameterChange({
                    ...parameterValues,
                    [parameter.key.trim()]: event.target.value,
                  })
                }
              />
            </Field>
          ) : (
            []
          ),
        )}
      </div>
      <div className="flex items-center gap-3">
        <Button type="button" variant="outline" disabled={disabled} onClick={onSimulate}>
          <Play aria-hidden />
          {t('rules.runSimulation')}
        </Button>
        {result ? (
          <span className="text-sm font-medium">
            {t(result.isMatch ? 'rules.simulationMatched' : 'rules.simulationNotMatched')}
          </span>
        ) : null}
      </div>
    </EditorSection>
  );
}

function ReadOnlyRule({
  detail,
  contextSchema,
}: {
  detail: RuleDefinitionDetail;
  contextSchema?: RuleContextSchema;
}) {
  const { t, i18n } = useTranslation();
  const id = useId();
  const dateFormatter = useMemo(
    () => new Intl.DateTimeFormat(i18n.language, { dateStyle: 'medium', timeStyle: 'short' }),
    [i18n.language],
  );
  return (
    <div data-slot="workspace-rule-details" className="space-y-6">
      <p className="text-sm leading-relaxed text-muted-foreground">{detail.description}</p>
      <section aria-labelledby={`${id}-behavior`} className="space-y-3">
        <SectionHeading
          id={`${id}-behavior`}
          title={t('rules.whatThisRuleDoes')}
          description={t('rules.ruleBehaviorDescription')}
        />
        <RuleBehaviorSummary
          condition={detail.condition}
          outcome={detail.outcome}
          expressionLanguageVersion={detail.expressionLanguageVersion}
          contextSchema={contextSchema}
          parameters={detail.parameters}
          references
        />
      </section>
      <section aria-labelledby={`${id}-applies`} className="space-y-3 border-t pt-6">
        <SectionHeading
          id={`${id}-applies`}
          title={t('rules.whereThisRuleApplies')}
          description={t('rules.applicabilityDescription')}
        />
        <dl className="grid gap-5 sm:grid-cols-2">
          <Detail label={t('rules.scope')} value={t(`rules.scope${detail.scope}`)} />
          <Detail label={t('rules.context')} value={contextSchema?.displayName ?? '—'} />
        </dl>
      </section>
      {(detail.parameters ?? []).length > 0 ? (
        <section aria-labelledby={`${id}-parameters`} className="space-y-3 border-t pt-6">
          <SectionHeading
            id={`${id}-parameters`}
            title={t('rules.parameters')}
            description={t('rules.parametersHelp')}
          />
          <dl className="grid gap-5 sm:grid-cols-2">
            {(detail.parameters ?? []).map((parameter) => (
              <Detail
                key={parameter.key}
                label={parameter.key || '—'}
                value={
                  <span className="flex flex-wrap gap-2">
                    <MetadataTag>{parameter.type ?? '—'}</MetadataTag>
                    <MetadataTag>
                      {t(
                        parameter.isRequired
                          ? 'rules.parameterRequired'
                          : 'rules.parameterOptional',
                      )}
                    </MetadataTag>
                  </span>
                }
              />
            ))}
          </dl>
        </section>
      ) : null}
      <section aria-labelledby={`${id}-references`} className="space-y-3 border-t pt-6">
        <SectionHeading
          id={`${id}-references`}
          title={t('rules.versionAndReferences')}
          description={t('rules.versionAndReferencesDescription')}
        />
        <dl className="grid gap-5 sm:grid-cols-2">
          <Detail label={t('rules.definitionKey')} value={detail.definitionKey ?? '—'} />
          <Detail
            label={t('rules.publishedVersion')}
            value={
              detail.latestPublishedVersion
                ? t('rules.version', { version: detail.latestPublishedVersion })
                : t('rules.noPublishedVersions')
            }
          />
          <Detail
            label={t('rules.expressionLanguage')}
            value={t('rules.expressionLanguageVersion', {
              version: detail.expressionLanguageVersion ?? 1,
            })}
          />
          <Detail
            label={t('rules.updated')}
            value={
              detail.updatedAt
                ? dateFormatter.format(new Date(detail.updatedAt))
                : t('rules.dateUnavailable')
            }
          />
        </dl>
      </section>
      {(detail.versions ?? []).length > 0 ? (
        <section aria-labelledby={`${id}-versions`} className="space-y-3 border-t pt-6">
          <SectionHeading
            id={`${id}-versions`}
            title={t('rules.versionHistory')}
            description={t('rules.versionHistoryHelp')}
          />
          <div className="divide-y divide-border">
            {[...(detail.versions ?? [])]
              .sort((left, right) => (right.version ?? 0) - (left.version ?? 0))
              .map((version) => (
                <div
                  key={version.version}
                  className="flex items-start justify-between gap-4 py-3 first:pt-0"
                >
                  <div>
                    <p className="text-sm font-medium">
                      {t('rules.version', { version: version.version })}
                    </p>
                    <p className="mt-1 text-xs text-muted-foreground">
                      {version.publishedAt
                        ? dateFormatter.format(new Date(version.publishedAt))
                        : t('rules.dateUnavailable')}
                    </p>
                  </div>
                  <span className="text-xs font-medium text-muted-foreground">
                    {t('rules.immutable')}
                  </span>
                </div>
              ))}
          </div>
        </section>
      ) : null}
    </div>
  );
}

function PublishReview({
  detail,
  editor,
  schema,
  authoring,
}: {
  detail: RuleDefinitionDetail;
  editor: EditorState;
  schema?: RuleContextSchema;
  authoring: RuleExpressionAuthoring;
}) {
  const { t } = useTranslation();
  return (
    <div className="space-y-4">
      <dl className="grid grid-cols-2 gap-4">
        <Detail label={t('rules.scope')} value={t(`rules.scope${editor.scope}`)} />
        <Detail label={t('rules.context')} value={schema?.displayName ?? '—'} />
        <Detail
          label={t('rules.parameters')}
          value={t('rules.parameterCount', { count: editor.parameters.length })}
        />
        <Detail
          label={t('rules.publishedVersion')}
          value={t('rules.version', { version: (detail.latestPublishedVersion ?? 0) + 1 })}
        />
      </dl>
      <RuleBehaviorSummary
        condition={authoring.condition}
        authoring={authoring}
        expressionLanguageVersion={detail.expressionLanguageVersion}
        outcome={
          editor.outcomeKind === 'Validation'
            ? {
                kind: 'Validation',
                violationCode: editor.violationCode,
                severity: editor.severity,
                message: editor.message,
              }
            : { kind: 'Decision', decision: editor.decision }
        }
      />
    </div>
  );
}

function EditorSection({
  title,
  description,
  action,
  children,
}: {
  title: string;
  description: string;
  action?: ReactNode;
  children: ReactNode;
}) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="space-y-4 border-b border-border pb-6 last:border-0">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h3 id={id} className="text-sm font-semibold">
            {title}
          </h3>
          <p className="mt-1 text-xs leading-relaxed text-muted-foreground">{description}</p>
        </div>
        {action}
      </div>
      {children}
    </section>
  );
}

function SectionHeading({
  id,
  title,
  description,
}: {
  id: string;
  title: string;
  description: string;
}) {
  return (
    <div>
      <h3 id={id} className="text-sm font-semibold">
        {title}
      </h3>
      <p className="mt-1 text-xs leading-relaxed text-muted-foreground">{description}</p>
    </div>
  );
}

function Detail({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted-foreground">{label}</dt>
      <dd className="mt-1 text-sm font-medium">{value}</dd>
    </div>
  );
}

function LifecycleBadge({ detail }: { detail: RuleDefinitionDetail }) {
  const { t } = useTranslation();
  const status = detail.status ?? 'Draft';
  return (
    <StatusBadge
      tone={status === 'Published' ? 'success' : status === 'Archived' ? 'neutral' : 'info'}
    >
      {t(`rules.status${status}`)}
    </StatusBadge>
  );
}

function firstSchema(schemas: RuleContextSchema[]) {
  return schemas.find(
    (schema) => schema.scope && schema.contextKey && schema.version !== undefined,
  );
}

function findSchema(schemas: RuleContextSchema[], editor: EditorState) {
  return schemas.find(
    (schema) =>
      schema.scope === editor.scope &&
      schema.contextKey === editor.contextKey &&
      schema.version === editor.contextSchemaVersion,
  );
}

function createEditor(schema: RuleContextSchema): EditorState {
  return {
    name: '',
    description: '',
    scope: schema.scope ?? 'Field',
    contextKey: schema.contextKey ?? '',
    contextSchemaVersion: schema.version ?? 1,
    outcomeKind: 'Validation',
    parameters: [],
    expressionSyntax: '',
    violationCode: '',
    severity: 'Error',
    message: '',
    decision: 'Allow',
  };
}

function detailToEditor(detail: RuleDefinitionDetail, syntax: string): EditorState {
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
    expressionSyntax: syntax,
    violationCode: detail.outcome?.violationCode ?? '',
    severity: detail.outcome?.severity ?? 'Error',
    message: detail.outcome?.message ?? '',
    decision: detail.outcome?.decision ?? 'Allow',
  };
}

function toParameterDtos(parameters: EditableParameter[]): RuleParameterDefinition[] {
  return parameters.map((parameter) => ({
    key: parameter.key.trim(),
    type: parameter.type,
    isRequired: parameter.isRequired,
    allowMultiple: parameter.allowMultiple,
    allowedValues: splitValues(parameter.allowedValues),
  }));
}

function validateEditor(state: EditorState, authoring?: RuleExpressionAuthoring) {
  if (!state.name.trim() || !state.description.trim()) return 'Name and description are required.';
  if (state.parameters.some((parameter) => !parameter.key.trim()))
    return 'Parameter keys are required.';
  if (!authoring?.condition || (authoring.diagnostics ?? []).length > 0)
    return authoring?.diagnostics?.[0]?.message ?? 'Expression syntax is invalid.';
  if (state.outcomeKind === 'Validation' && (!state.violationCode.trim() || !state.message.trim()))
    return 'Validation code and message are required.';
  return null;
}

function currentPrefix(source: string, cursor: number) {
  let start = Math.min(cursor, source.length);
  while (start > 0 && /[@A-Za-z0-9_.]/.test(source[start - 1])) start -= 1;
  return source.slice(start, cursor);
}

function editorComparable(state: EditorState) {
  return {
    ...state,
    parameters: state.parameters.map(({ id: _id, ...parameter }) => parameter),
  };
}

function distinct<T>(values: (T | null | undefined)[]): T[] {
  return [...new Set(values.filter((value): value is T => value != null))];
}

function deriveKey(name: string) {
  return (
    name
      .trim()
      .toLowerCase()
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .replace(/[^a-z0-9]+/g, '_')
      .replace(/^_+|_+$/g, '')
      .slice(0, 63) || 'new_rule'
  );
}

function splitValues(source: string) {
  return source
    .split(',')
    .map((value) => value.trim())
    .filter(Boolean);
}

function typedRuleValue(type: RuleValueType, source: string, multiple = false): RuleValue {
  return {
    type,
    values: multiple ? splitValues(source) : [normalizeValue(type, source)],
  };
}

function normalizeValue(type: RuleValueType, source: string) {
  if (type === 'Boolean') return String(source).toLowerCase();
  return source.trim();
}

function setDetailCache(
  queryClient: ReturnType<typeof useQueryClient>,
  detail: RuleDefinitionDetail,
) {
  if (detail.definitionKey)
    queryClient.setQueryData(ruleDefinitionQueryKeys.detail(detail.definitionKey), detail);
}

function readError(error: unknown, fallback: string) {
  if (error instanceof ApiError) return error.message || fallback;
  if (error instanceof Error) return error.message || fallback;
  return fallback;
}
