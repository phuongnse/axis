import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Archive, Lightbulb, Plus, Save, Trash2 } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { toast } from 'sonner';
import { ManagedDialog, ManagedDialogBody } from '@/components/shared/ManagedDialog';
import { ManagedDialogTabs } from '@/components/shared/ManagedDialogTabs';
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
import { Field, FieldLabel } from '@/components/ui/field';
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
import type * as ApiTypes from '@/lib/api-generated';
import {
  activateRuleDefinitionVersion,
  archiveRuleDefinition,
  completeRuleAuthoring,
  createRuleDefinition,
  createRuleDefinitionVersion,
  deactivateRuleDefinition,
  getRuleDefinition,
  projectRuleAuthoring,
  type RuleDefinitionDetail,
  ruleDefinitionQueryKeys,
  ruleExpressionLanguageQueryOptions,
  saveRuleDefinitionDraft,
  simulateRuleDefinitionDraft,
  simulateRuleDefinitionVersion,
} from '../api';
import { toDraftInputs } from '../condition-references';
import { valueTypeLabel } from '../reference';
import { RuleBehaviorSummary, RuleLogicPreview, RuleOutputSummary } from './RuleBehaviorSummary';
import { RuleBindingUsagePanel } from './RuleBindingUsagePanel';
import { RuleConditionComposer } from './RuleConditionComposer';
import { RuleOriginBadge } from './RuleOriginBadge';

type InputDefinition = ApiTypes.RuleDraftInputDefinitionDto;
type EditableInput = InputDefinition & { clientId: string; keyLocked: boolean };
type ValueType = ApiTypes.RuleValueType;
type ProjectionRun = {
  source: ApiTypes.RuleAuthoringSourceDto;
  generation: number;
  requestId: number;
  hydrationSnapshot?: {
    name: string;
    description: string;
    inputs: InputDefinition[];
    condition: ApiTypes.RuleConditionNodeDto;
  };
};

const valueTypes: ValueType[] = ['Text', 'Integer', 'Decimal', 'Date', 'DateTime', 'Boolean'];

function draftSnapshot(next: {
  name: string;
  description: string;
  inputs: InputDefinition[];
  condition: ApiTypes.RuleConditionNodeDto | null;
  dsl: string;
}) {
  return JSON.stringify(next);
}

function hasInputKey(input: InputDefinition): input is InputDefinition & { key: string } {
  return Boolean(input.key);
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
  const [discardOpen, setDiscardOpen] = useState(false);
  const [archiveOpen, setArchiveOpen] = useState(false);
  const [lifecycleAction, setLifecycleAction] = useState<
    'version' | 'activate' | 'deactivate' | null
  >(null);
  const [dsl, setDsl] = useState('');
  const [diagnostics, setDiagnostics] = useState<string[]>([]);
  const [simulation, setSimulation] = useState<ApiTypes.RuleSimulationResultDto | null>(null);
  const [simulationFailure, setSimulationFailure] = useState<'invalid' | 'error' | null>(null);
  const [sampleValues, setSampleValues] = useState<Record<string, string>>({});
  const [completions, setCompletions] = useState<ApiTypes.RuleAuthoringCompletionDto[]>([]);
  const [cursor, setCursor] = useState(0);
  const [stale, setStale] = useState(false);
  const [createdDraft, setCreatedDraft] = useState<RuleDefinitionDetail | null>(null);
  const [usageVersion, setUsageVersion] = useState<number | null>(null);
  const snapshotRef = useRef('');
  const hydratedKeyRef = useRef<string | null>(null);
  const projectionGenerationRef = useRef(0);
  const projectionRequestRef = useRef(0);
  const latestProjectionRequestRef = useRef(0);
  const [hydrationCondition, setHydrationCondition] =
    useState<ApiTypes.RuleConditionNodeDto | null>(null);
  const dslRef = useRef<HTMLTextAreaElement>(null);
  const inputContract = inputs.map(
    ({ clientId: _clientId, keyLocked: _keyLocked, ...input }) => input,
  );

  const projectMutation = useMutation({
    mutationFn: ({ source }: ProjectionRun) =>
      projectRuleAuthoring({
        source,
        inputs: inputContract,
        expressionLanguageVersion: languageQuery.data?.version,
      }),
    onSuccess: (projection, request) => {
      if (
        request.generation !== projectionGenerationRef.current ||
        request.requestId !== latestProjectionRequestRef.current
      )
        return;
      if (projection.isValid) {
        if (projection.condition) setCondition(projection.condition);
        if (projection.formattedDsl != null) setDsl(projection.formattedDsl);
      }
      setDiagnostics(
        (projection.diagnostics ?? []).map((item) => item.message ?? t('rules.expressionInvalid')),
      );
      if (request.hydrationSnapshot) {
        snapshotRef.current = draftSnapshot({
          name: request.hydrationSnapshot.name,
          description: request.hydrationSnapshot.description,
          inputs: request.hydrationSnapshot.inputs,
          condition:
            projection.isValid && projection.condition
              ? projection.condition
              : request.hydrationSnapshot.condition,
          dsl: projection.formattedDsl ?? '',
        });
      }
    },
    onError: (_cause, request) => {
      if (
        request.generation !== projectionGenerationRef.current ||
        request.requestId !== latestProjectionRequestRef.current
      )
        return;
      setDiagnostics([t('rules.expressionInvalid')]);
    },
  });

  const advanceProjectionFence = () => {
    const requestId = ++projectionRequestRef.current;
    latestProjectionRequestRef.current = requestId;
    return requestId;
  };

  const projectAuthoring = (source: ApiTypes.RuleAuthoringSourceDto) => {
    const requestId = advanceProjectionFence();
    projectMutation.mutate({
      source,
      generation: projectionGenerationRef.current,
      requestId,
    });
  };

  useEffect(() => {
    if (!open) {
      projectionGenerationRef.current += 1;
      latestProjectionRequestRef.current = 0;
      hydratedKeyRef.current = null;
      setCreatedDraft(null);
      setHydrationCondition(null);
      return;
    }
    const detail = detailQuery.data;
    const hydrateKey = creating ? '__new__' : detail?.definitionKey;
    if (!hydrateKey || hydratedKeyRef.current === hydrateKey) return;
    projectionGenerationRef.current += 1;
    latestProjectionRequestRef.current = 0;
    hydratedKeyRef.current = hydrateKey;
    setName(detail?.name ?? '');
    setDescription(detail?.description ?? '');
    const nextInputs = toDraftInputs(detail?.inputs ?? []);
    setInputs(
      nextInputs.map((input) => ({ ...input, clientId: crypto.randomUUID(), keyLocked: true })),
    );
    setCondition(detail?.condition ?? null);
    setDsl('');
    setDiagnostics([]);
    setCompletions([]);
    setSampleValues({});
    setSimulation(null);
    setSimulationFailure(null);
    setStale(false);
    setError(null);
    setUsageVersion(detail?.activeVersion ?? detail?.latestVersion ?? null);
    snapshotRef.current = draftSnapshot({
      name: detail?.name ?? '',
      description: detail?.description ?? '',
      inputs: nextInputs,
      condition: detail?.condition ?? null,
      dsl: '',
    });
    setHydrationCondition(detail?.condition ?? null);
  }, [open, detailQuery.data, creating]);

  useEffect(() => {
    if (!hydrationCondition) return;
    setHydrationCondition(null);
    const requestId = ++projectionRequestRef.current;
    latestProjectionRequestRef.current = requestId;
    projectMutation.mutate({
      source: { ast: hydrationCondition },
      generation: projectionGenerationRef.current,
      requestId,
      hydrationSnapshot: {
        name,
        description,
        inputs: inputContract,
        condition: hydrationCondition,
      },
    });
  }, [hydrationCondition, projectMutation.mutate, name, description, inputContract]);

  const refreshMutation = useMutation({
    mutationFn: async () => {
      projectionGenerationRef.current += 1;
      latestProjectionRequestRef.current = 0;
      const current = detailQuery.data ?? createdDraft;
      if (!current?.definitionKey) throw new Error(t('rules.loadErrorTitle'));
      return getRuleDefinition(current.definitionKey);
    },
    onSuccess: (refreshed) => {
      const definitionKey = refreshed.definitionKey ?? '';
      queryClient.setQueryData(ruleDefinitionQueryKeys.detail(definitionKey), refreshed);
      if (creating) setCreatedDraft(refreshed);
      hydratedKeyRef.current = definitionKey;
      setName(refreshed.name ?? '');
      setDescription(refreshed.description ?? '');
      const nextInputs = toDraftInputs(refreshed.inputs ?? []);
      setInputs(
        nextInputs.map((input) => ({ ...input, clientId: crypto.randomUUID(), keyLocked: true })),
      );
      setCondition(refreshed.condition ?? null);
      setDsl('');
      setDiagnostics([]);
      setCompletions([]);
      setSampleValues({});
      setSimulation(null);
      setSimulationFailure(null);
      setError(null);
      setUsageVersion(refreshed.activeVersion ?? refreshed.latestVersion ?? null);
      snapshotRef.current = draftSnapshot({
        name: refreshed.name ?? '',
        description: refreshed.description ?? '',
        inputs: nextInputs,
        condition: refreshed.condition ?? null,
        dsl: '',
      });
      setHydrationCondition(refreshed.condition ?? null);
      setStale(false);
    },
    onError: (cause) => {
      setError(cause instanceof Error ? cause.message : t('rules.loadErrorTitle'));
    },
  });

  const saveMutation = useMutation({
    mutationFn: async () => {
      setError(null);
      if (
        stale ||
        refreshMutation.isPending ||
        hydrationCondition != null ||
        !condition ||
        diagnostics.length > 0 ||
        projectMutation.isPending
      ) {
        setActiveSection('behavior');
        throw new Error(t('rules.conditionRequired'));
      }
      let current = detailQuery.data ?? createdDraft;
      if (!current) {
        current = await createRuleDefinition({ name, description });
        setCreatedDraft(current);
        if (current.definitionKey) {
          queryClient.setQueryData(ruleDefinitionQueryKeys.detail(current.definitionKey), current);
        }
        await queryClient.invalidateQueries({ queryKey: ruleDefinitionQueryKeys.lists() });
      }
      const saved = await saveRuleDefinitionDraft(current.definitionKey ?? '', {
        expectedRevision: current.revision ?? 1,
        name,
        description,
        inputs: inputContract,
        condition,
      });
      return saved;
    },
    onSuccess: (saved) => {
      setInputs((current) => current.map((input) => ({ ...input, keyLocked: true })));
      snapshotRef.current = draftSnapshot({
        name,
        description,
        inputs: inputContract,
        condition,
        dsl,
      });
      void queryClient.invalidateQueries({ queryKey: ruleDefinitionQueryKeys.lists() });
      queryClient.setQueryData(ruleDefinitionQueryKeys.detail(saved.definitionKey ?? ''), saved);
      toast.success(t('rules.saved'));
      onCreated?.(saved);
      if (!onCreated) onOpenChange(false);
    },
    onError: (cause) => {
      if (cause instanceof ApiError && cause.status === 409) setStale(true);
      setError(cause instanceof Error ? cause.message : t('rules.saveError'));
    },
  });

  const completionMutation = useMutation({
    mutationFn: () =>
      completeRuleAuthoring({
        text: dsl,
        cursor,
        inputs: inputContract,
        expressionLanguageVersion: languageQuery.data?.version,
      }),
    onSuccess: setCompletions,
    onError: () => setError(t('rules.completionError')),
  });
  const simulationMutation = useMutation({
    mutationFn: () => {
      const current = detailQuery.data ?? createdDraft;
      if (!current?.definitionKey) throw new Error(t('rules.simulationError'));
      if (authoringBlocked) throw new Error(t('rules.conditionRequired'));
      const immutableVersion = current.activeVersion ?? current.latestVersion;
      const simulationInputs = canEditDraft
        ? inputContract
        : (current.versions?.find((version) => version.version === immutableVersion)?.inputs ?? []);
      const inputs = Object.fromEntries(
        simulationInputs.filter(hasInputKey).map((input) => [
          input.key,
          {
            type: input.types?.[0],
            values: sampleValues[input.key] ? [sampleValues[input.key]] : [],
          },
        ]),
      );
      return canEditDraft
        ? simulateRuleDefinitionDraft(current.definitionKey, { inputs })
        : immutableVersion != null
          ? simulateRuleDefinitionVersion(current.definitionKey, immutableVersion, { inputs })
          : Promise.reject(new Error(t('rules.simulationError')));
    },
    onSuccess: (result) => {
      setSimulationFailure(null);
      setSimulation(result);
    },
    onError: (cause) => {
      setSimulation(null);
      setSimulationFailure(cause instanceof ApiError && cause.status === 400 ? 'invalid' : 'error');
    },
  });

  const lifecycleMutation = useMutation({
    mutationFn: (action: 'version' | 'activate' | 'deactivate' | 'archive') => {
      const current = detailQuery.data ?? createdDraft;
      if (!current?.definitionKey || current.revision == null)
        throw new Error(t('rules.lifecycleError'));
      if (stale || refreshMutation.isPending || hydrationCondition != null)
        throw new Error(t('rules.staleChanges'));
      if ((action === 'version' || action === 'activate') && authoringBlocked)
        throw new Error(t('rules.conditionRequired'));
      if (action === 'version')
        return createRuleDefinitionVersion(current.definitionKey, current.revision);
      if (action === 'activate') {
        if (current.latestVersion == null) throw new Error(t('rules.lifecycleError'));
        return activateRuleDefinitionVersion(
          current.definitionKey,
          current.latestVersion,
          current.revision,
        );
      }
      if (action === 'deactivate')
        return deactivateRuleDefinition(current.definitionKey, current.revision);
      return archiveRuleDefinition(current.definitionKey, current.revision);
    },
    onSuccess: (saved) => {
      queryClient.setQueryData(ruleDefinitionQueryKeys.detail(saved.definitionKey ?? ''), saved);
      void queryClient.invalidateQueries({ queryKey: ruleDefinitionQueryKeys.lists() });
      setUsageVersion(saved.activeVersion ?? saved.latestVersion ?? null);
      toast.success(t('rules.lifecycleUpdated'));
      setArchiveOpen(false);
    },
    onError: (cause) => {
      if (cause instanceof ApiError && cause.status === 409) setStale(true);
      setError(cause instanceof Error ? cause.message : t('rules.lifecycleError'));
    },
  });

  const detail = detailQuery.data ?? createdDraft;
  const canEditDraft = detail ? detail.actions?.canEditDraft === true : creating;
  const readOnly = Boolean(detail && !canEditDraft);
  const dirty =
    canEditDraft &&
    snapshotRef.current !==
      draftSnapshot({ name, description, inputs: inputContract, condition, dsl });
  const authoringBlocked =
    canEditDraft &&
    (stale ||
      refreshMutation.isPending ||
      hydrationCondition != null ||
      dirty ||
      projectMutation.isPending ||
      diagnostics.length > 0 ||
      condition == null);
  const simulationVersion = canEditDraft
    ? null
    : (detail?.activeVersion ?? detail?.latestVersion ?? null);
  const simulationSnapshot = detail?.versions?.find(
    (version) => version.version === simulationVersion,
  );
  const simulationInputs = canEditDraft ? inputContract : (simulationSnapshot?.inputs ?? []);
  const selectedUsageVersion = detail?.versions?.find(
    (version) => version.version === (usageVersion ?? detail.activeVersion ?? detail.latestVersion),
  );
  const requestClose = () => {
    if (dirty) setDiscardOpen(true);
    else onOpenChange(false);
  };
  return (
    <ManagedDialog
      open={open}
      onOpenChange={(nextOpen) => {
        if (!nextOpen) requestClose();
      }}
      dirty={dirty}
      closeDisabled={saveMutation.isPending || lifecycleMutation.isPending}
      title={detail?.name ?? (creating ? t('rules.createTitle') : t('rules.editorTitle'))}
      titleAccessory={
        detail?.origin ? (
          <>
            <RuleOriginBadge origin={detail.origin} />
            <StatusBadge
              tone={
                detail.status === 'Active'
                  ? 'success'
                  : detail.status === 'Archived'
                    ? 'muted'
                    : 'neutral'
              }
            >
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
            disabled={
              saveMutation.isPending ||
              stale ||
              refreshMutation.isPending ||
              hydrationCondition != null ||
              projectMutation.isPending ||
              diagnostics.length > 0 ||
              !name.trim()
            }
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
                    label={t('rules.latestVersion')}
                    value={detail.latestVersion == null ? '—' : String(detail.latestVersion)}
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
                  {detail?.latestVersion ? (
                    <dl className="grid gap-5 sm:grid-cols-2">
                      <RuleDetail
                        label={t('rules.latestVersion')}
                        value={String(detail.latestVersion)}
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
                            onClick={() => {
                              advanceProjectionFence();
                              setInputs([
                                ...inputs,
                                {
                                  clientId: crypto.randomUUID(),
                                  keyLocked: false,
                                  key: '',
                                  label: '',
                                  types: ['Text'],
                                  isRequired: false,
                                  allowMultiple: false,
                                  allowedValues: [],
                                },
                              ]);
                            }}
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
                              keyLocked={input.keyLocked}
                              language={languageQuery.data}
                              onChange={(next) => {
                                advanceProjectionFence();
                                updateInput(inputs, index, input, next, setInputs, setCondition);
                              }}
                              onRemove={() => {
                                advanceProjectionFence();
                                setInputs(
                                  inputs.filter((_, candidateIndex) => candidateIndex !== index),
                                );
                              }}
                            />
                          ))}
                        </div>
                      </section>
                      <RuleConditionComposer
                        condition={condition}
                        inputs={inputContract}
                        language={languageQuery.data}
                        onChange={(next) => {
                          setCondition(next);
                          projectAuthoring({ ast: next ?? undefined });
                        }}
                      />
                      <Field>
                        <div className="flex items-center justify-between gap-3">
                          <FieldLabel htmlFor="rule-dsl">{t('rules.expressionSyntax')}</FieldLabel>
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            disabled={completionMutation.isPending || !dsl}
                            onClick={() => completionMutation.mutate()}
                          >
                            <Lightbulb data-icon="inline-start" />
                            {t('rules.showSuggestions')}
                          </Button>
                        </div>
                        <Textarea
                          ref={dslRef}
                          id="rule-dsl"
                          value={dsl}
                          onChange={(event) => {
                            advanceProjectionFence();
                            setDsl(event.target.value);
                            setCursor(event.currentTarget.selectionStart);
                            setCompletions([]);
                          }}
                          onSelect={(event) => setCursor(event.currentTarget.selectionStart)}
                          onBlur={() => projectAuthoring({ text: dsl })}
                        />
                      </Field>
                      {completions.length ? (
                        <section
                          aria-label={t('rules.expressionSuggestions')}
                          className="space-y-2"
                        >
                          <h4 className="text-sm font-medium">
                            {t('rules.expressionSuggestions')}
                          </h4>
                          <div role="listbox" className="flex flex-wrap gap-2">
                            {completions.map((completion) => (
                              <Button
                                key={completion.label}
                                type="button"
                                variant="outline"
                                size="sm"
                                role="option"
                                aria-label={
                                  completion.label ??
                                  completion.insertText ??
                                  t('rules.applySuggestion')
                                }
                                onClick={() => {
                                  const start = completion.start ?? cursor;
                                  const length = completion.length ?? 0;
                                  const nextDsl = `${dsl.slice(0, start)}${completion.insertText ?? ''}${dsl.slice(start + length)}`;
                                  setDsl(nextDsl);
                                  setCursor(start + (completion.insertText?.length ?? 0));
                                  setCompletions([]);
                                  projectAuthoring({ text: nextDsl });
                                  requestAnimationFrame(() => {
                                    dslRef.current?.focus();
                                    dslRef.current?.setSelectionRange(
                                      start + (completion.insertText?.length ?? 0),
                                      start + (completion.insertText?.length ?? 0),
                                    );
                                  });
                                }}
                              >
                                {completion.label ?? completion.insertText}
                              </Button>
                            ))}
                          </div>
                        </section>
                      ) : null}
                      {diagnostics.map((diagnostic) => (
                        <StatusNotice key={diagnostic} tone="destructive">
                          {diagnostic}
                        </StatusNotice>
                      ))}
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
              ...(detail?.definitionKey && selectedUsageVersion?.version != null
                ? [
                    {
                      id: 'usage',
                      label: t('rules.usage'),
                      content: (
                        <div className="space-y-4">
                          <Field>
                            <FieldLabel htmlFor="rule-usage-version">
                              {t('rules.usageVersion')}
                            </FieldLabel>
                            <Select
                              value={String(selectedUsageVersion.version)}
                              onValueChange={(value) => setUsageVersion(Number(value))}
                            >
                              <SelectTrigger id="rule-usage-version">
                                <SelectValue>
                                  {t('rules.version', { version: selectedUsageVersion.version })}
                                </SelectValue>
                              </SelectTrigger>
                              <SelectContent>
                                {(detail.versions ?? []).map((version) =>
                                  version.version != null ? (
                                    <SelectItem
                                      key={version.version}
                                      value={String(version.version)}
                                    >
                                      {t('rules.version', { version: version.version })}
                                    </SelectItem>
                                  ) : null,
                                )}
                              </SelectContent>
                            </Select>
                          </Field>
                          <RuleBindingUsagePanel
                            definitionKey={detail.definitionKey}
                            version={selectedUsageVersion.version}
                            active={activeSection === 'usage'}
                            inputs={selectedUsageVersion.inputs}
                          />
                        </div>
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
        {detail?.definitionKey && (canEditDraft || simulationSnapshot) ? (
          <RuleSimulationPanel
            inputs={simulationInputs}
            values={sampleValues}
            onChange={(key, value) => setSampleValues((current) => ({ ...current, [key]: value }))}
            onSimulate={() => simulationMutation.mutate()}
            pending={simulationMutation.isPending}
            disabled={authoringBlocked}
            simulation={simulation}
            failure={simulationFailure}
            version={simulationVersion}
          />
        ) : null}
        {stale ? (
          <StatusNotice tone="warning">
            {t('rules.staleChanges')}{' '}
            <Button
              type="button"
              variant="link"
              disabled={refreshMutation.isPending}
              onClick={() => refreshMutation.mutate()}
            >
              {t('rules.refetch')}
            </Button>
          </StatusNotice>
        ) : null}
        {detail && !readOnly ? (
          <div className="space-y-3 border-t pt-4">
            <VersionHistory versions={detail.versions ?? []} activeVersion={detail.activeVersion} />
            <div className="flex flex-wrap gap-2">
              {detail.actions?.canCreateVersion ? (
                <Button
                  type="button"
                  variant="secondary"
                  disabled={authoringBlocked}
                  onClick={() => setLifecycleAction('version')}
                >
                  {t('rules.createVersion')}
                </Button>
              ) : null}
              {detail.actions?.canActivateVersion ? (
                <Button
                  type="button"
                  disabled={authoringBlocked}
                  onClick={() => setLifecycleAction('activate')}
                >
                  {t('rules.activate')}
                </Button>
              ) : null}
              {detail.actions?.canDeactivate ? (
                <Button
                  type="button"
                  variant="outline"
                  disabled={stale || refreshMutation.isPending || hydrationCondition != null}
                  onClick={() => setLifecycleAction('deactivate')}
                >
                  {t('rules.deactivate')}
                </Button>
              ) : null}
              {detail.actions?.canArchive ? (
                <Button
                  type="button"
                  variant="destructive"
                  disabled={stale || refreshMutation.isPending || hydrationCondition != null}
                  onClick={() => setArchiveOpen(true)}
                >
                  <Archive aria-hidden />
                  {t('rules.archive')}
                </Button>
              ) : null}
            </div>
          </div>
        ) : null}
      </ManagedDialogBody>
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
      <AlertDialog
        open={lifecycleAction !== null}
        onOpenChange={(nextOpen) => {
          if (!nextOpen) setLifecycleAction(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {lifecycleAction === 'version'
                ? t('rules.createVersionTitle')
                : lifecycleAction === 'activate'
                  ? t('rules.activateTitle')
                  : t('rules.deactivateTitle')}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {lifecycleAction === 'version'
                ? t('rules.createVersionDescription')
                : lifecycleAction === 'activate'
                  ? t('rules.activateDescription')
                  : t('rules.deactivateDescription')}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('app.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              variant={lifecycleAction === 'deactivate' ? 'destructive' : 'default'}
              onClick={() => {
                if (lifecycleAction) lifecycleMutation.mutate(lifecycleAction);
                setLifecycleAction(null);
              }}
            >
              {lifecycleAction === 'version'
                ? t('rules.createVersion')
                : lifecycleAction === 'activate'
                  ? t('rules.activate')
                  : t('rules.deactivate')}
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
            <AlertDialogCancel>{t('app.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              onClick={() => lifecycleMutation.mutate('archive')}
            >
              {t('rules.archive')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
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

function VersionHistory({
  versions,
  activeVersion,
}: {
  versions: ApiTypes.RuleDefinitionVersionDto[];
  activeVersion?: number | null;
}) {
  const { t } = useTranslation();
  if (!versions.length) return null;
  return (
    <section aria-label={t('rules.versionHistory')} className="space-y-2">
      <h3 className="text-sm font-semibold">{t('rules.versionHistory')}</h3>
      <ul className="space-y-1 text-sm">
        {versions.map((version) => (
          <li key={version.version} className="flex items-center justify-between gap-3">
            <span>{t('rules.version', { version: version.version })}</span>
            <span className="text-muted-foreground">
              {version.version === activeVersion ? t('rules.activeVersion') : t('rules.immutable')}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function RuleSimulationPanel({
  inputs,
  values,
  onChange,
  onSimulate,
  pending,
  disabled,
  simulation,
  failure,
  version,
}: {
  inputs: InputDefinition[];
  values: Record<string, string>;
  onChange: (key: string, value: string) => void;
  onSimulate: () => void;
  pending: boolean;
  disabled: boolean;
  simulation: ApiTypes.RuleSimulationResultDto | null;
  failure: 'invalid' | 'error' | null;
  version?: number | null;
}) {
  const { t } = useTranslation();
  return (
    <section aria-label={t('rules.simulation')} className="space-y-3 border-t pt-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold">{t('rules.simulation')}</h3>
          <p className="text-sm text-muted-foreground">{t('rules.simulationHelp')}</p>
        </div>
        <Button type="button" variant="outline" onClick={onSimulate} disabled={pending || disabled}>
          {t('rules.runSimulation')}
        </Button>
      </div>
      {inputs.length ? (
        <div className="grid gap-3 sm:grid-cols-2">
          {inputs.filter(hasInputKey).map((input) => (
            <SampleInput
              key={input.key}
              input={input}
              value={values[input.key] ?? ''}
              onChange={(value) => onChange(input.key, value)}
            />
          ))}
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">{t('rules.noParameters')}</p>
      )}
      {failure ? (
        <StatusNotice tone="destructive">
          {failure === 'invalid' ? t('rules.simulationInvalid') : t('rules.simulationError')}
        </StatusNotice>
      ) : null}
      {simulation ? (
        <StatusNotice tone={simulation.isMatch ? 'success' : 'warning'}>
          <strong>
            {simulation.isMatch ? t('rules.simulationMatched') : t('rules.simulationNotMatched')}
          </strong>
          {' · '}
          {simulation.definitionVersion == null
            ? t('rules.simulationDraft')
            : t('rules.version', { version: simulation.definitionVersion ?? version })}
          {simulation.diagnostics?.length ? (
            <span>
              {' · '}
              {t('rules.simulationDiagnostics', { count: simulation.diagnostics.length })}
            </span>
          ) : null}
        </StatusNotice>
      ) : null}
    </section>
  );
}

function SampleInput({
  input,
  value,
  onChange,
}: {
  input: InputDefinition;
  value: string;
  onChange: (value: string) => void;
}) {
  const { t } = useTranslation();
  const type = input.types?.[0] ?? 'Text';
  const id = `rule-sample-${input.key}`;
  if (type === 'Boolean') {
    return (
      <Field>
        <FieldLabel htmlFor={id}>{input.label ?? input.key}</FieldLabel>
        <Select
          value={value}
          onValueChange={(next) => {
            if (next != null) onChange(next);
          }}
        >
          <SelectTrigger id={id}>
            <SelectValue placeholder={t('rules.selectValue')}>
              {value === 'true'
                ? t('rules.booleanTrue')
                : value === 'false'
                  ? t('rules.booleanFalse')
                  : undefined}
            </SelectValue>
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="true">{t('rules.booleanTrue')}</SelectItem>
            <SelectItem value="false">{t('rules.booleanFalse')}</SelectItem>
          </SelectContent>
        </Select>
      </Field>
    );
  }
  const htmlType =
    type === 'Integer' || type === 'Decimal'
      ? 'number'
      : type === 'Date'
        ? 'date'
        : type === 'DateTime'
          ? 'datetime-local'
          : 'text';
  return (
    <Field>
      <FieldLabel htmlFor={id}>{input.label ?? input.key}</FieldLabel>
      <Input
        id={id}
        type={htmlType}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
    </Field>
  );
}

function InputRow({
  input,
  inputId,
  keyLocked,
  language,
  onChange,
  onRemove,
}: {
  input: InputDefinition;
  inputId: string;
  keyLocked: boolean;
  language: ApiTypes.RuleExpressionLanguageDto | undefined;
  onChange: (input: InputDefinition) => void;
  onRemove: () => void;
}) {
  const { t, i18n } = useTranslation();
  return (
    <fieldset className="grid gap-3 rounded-lg border p-3 sm:grid-cols-12">
      <Field className="sm:col-span-3">
        <FieldLabel htmlFor={`${inputId}-key`}>{t('rules.key')}</FieldLabel>
        <Input
          id={`${inputId}-key`}
          value={input.key ?? ''}
          disabled={keyLocked}
          onChange={(event) => onChange({ ...input, key: event.target.value })}
        />
      </Field>
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
  _setCondition: (
    update: (current: ApiTypes.RuleConditionNodeDto | null) => ApiTypes.RuleConditionNodeDto | null,
  ) => void,
) {
  setInputs(
    inputs.map((candidate, candidateIndex) =>
      candidateIndex === index
        ? { ...next, clientId: current.clientId, keyLocked: current.keyLocked }
        : candidate,
    ),
  );
}
