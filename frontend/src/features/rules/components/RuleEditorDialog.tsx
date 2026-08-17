import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Archive, Lightbulb, Pause, Play, Plus, RefreshCw, Save, Trash2 } from 'lucide-react';
import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { toast } from 'sonner';
import { AsyncButton } from '@/components/shared/AsyncButton';
import { AsyncContent } from '@/components/shared/AsyncContent';
import {
  ManagedDialog,
  ManagedDialogAction,
  ManagedDialogAsyncAction,
  ManagedDialogBody,
} from '@/components/shared/ManagedDialog';
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
import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
  FieldLegend,
} from '@/components/ui/field';
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
import { referenceContent } from '@/lib/reference-metadata';
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
  inputs: InputDefinition[];
  generation: number;
  requestId: number;
  hydrationSnapshot?: {
    name: string;
    description: string;
    inputs: InputDefinition[];
    condition: ApiTypes.RuleConditionNodeDto;
  };
};
type CompletionRun = {
  generation: number;
  fence: number;
  text: string;
  cursor: number;
  inputs: InputDefinition[];
  expressionLanguageVersion?: number;
};
type SimulationRun = {
  fingerprint: string;
};
type SampleEntry = {
  id: string;
  value: string;
};
type SampleValue = {
  type?: ValueType;
  values: SampleEntry[];
};
type InputIssue = {
  key?: string;
  label?: string;
  allowedValues?: string;
};

let sampleEntrySequence = 0;

function createSampleEntry(value = ''): SampleEntry {
  sampleEntrySequence += 1;
  return { id: `rule-sample-entry-${sampleEntrySequence}`, value };
}

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
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const creating = definitionKey === null;
  const detailQuery = useQuery({
    queryKey: ruleDefinitionQueryKeys.detail(definitionKey ?? ''),
    queryFn: () => getRuleDefinition(definitionKey as string),
    enabled: open && !creating,
  });
  const detailErrorStatus =
    detailQuery.error instanceof ApiError ? detailQuery.error.status : undefined;
  const detailTemporarilyUnavailable = detailErrorStatus === 503;
  const detailActionUnavailable = detailErrorStatus === 403 || detailErrorStatus === 404;
  const languageQuery = useQuery({ ...ruleExpressionLanguageQueryOptions(), enabled: open });
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [inputs, setInputs] = useState<EditableInput[]>([]);
  const [condition, setCondition] = useState<ApiTypes.RuleConditionNodeDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [activeSection, setActiveSection] = useState('general');
  const [discardOpen, setDiscardOpen] = useState(false);
  const [reloadOpen, setReloadOpen] = useState(false);
  const [archiveOpen, setArchiveOpen] = useState(false);
  const [lifecycleAction, setLifecycleAction] = useState<
    'version' | 'activate' | 'deactivate' | null
  >(null);
  const [dsl, setDsl] = useState('');
  const dslValueRef = useRef('');
  const [diagnostics, setDiagnostics] = useState<ApiTypes.RuleAuthoringDiagnosticDto[]>([]);
  const [projectionError, setProjectionError] = useState<string | null>(null);
  const [simulation, setSimulation] = useState<ApiTypes.RuleSimulationResultDto | null>(null);
  const [simulationFailure, setSimulationFailure] = useState<'invalid' | 'error' | null>(null);
  const [sampleValues, setSampleValues] = useState<Record<string, SampleValue>>({});
  const [sampleValidationVisible, setSampleValidationVisible] = useState(false);
  const [completions, setCompletions] = useState<ApiTypes.RuleAuthoringCompletionDto[]>([]);
  const [cursor, setCursor] = useState(0);
  const [stale, setStale] = useState(false);
  const [createdDraft, setCreatedDraft] = useState<RuleDefinitionDetail | null>(null);
  const [selectedVersion, setSelectedVersion] = useState<number | null>(null);
  const snapshotRef = useRef('');
  const hydratedKeyRef = useRef<string | null>(null);
  const projectionGenerationRef = useRef(0);
  const projectionRequestRef = useRef(0);
  const latestProjectionRequestRef = useRef(0);
  const [hydrationCondition, setHydrationCondition] =
    useState<ApiTypes.RuleConditionNodeDto | null>(null);
  const dslRef = useRef<HTMLTextAreaElement>(null);
  const simulationFingerprintRef = useRef('');
  const inputContract = inputs.map(
    ({ clientId: _clientId, keyLocked: _keyLocked, ...input }) => input,
  );

  const setDslValue = useCallback((value: string) => {
    dslValueRef.current = value;
    setDsl(value);
  }, []);

  const advanceProjectionFence = () => {
    const requestId = ++projectionRequestRef.current;
    latestProjectionRequestRef.current = requestId;
    return requestId;
  };

  const projectMutation = useMutation({
    mutationFn: ({ source, inputs }: ProjectionRun) =>
      projectRuleAuthoring({
        source,
        inputs,
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
        if (projection.formattedDsl != null) {
          const displayChanged = projection.formattedDsl !== dslValueRef.current;
          setDslValue(projection.formattedDsl);
          if (displayChanged) {
            advanceProjectionFence();
            setCompletions([]);
          }
        }
      }
      setProjectionError(null);
      setDiagnostics(projection.diagnostics ?? []);
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
      setProjectionError(t('rules.expressionServiceUnavailable'));
    },
  });

  const projectAuthoring = (
    source: ApiTypes.RuleAuthoringSourceDto,
    projectionInputs: InputDefinition[] = inputContract,
  ) => {
    const requestId = advanceProjectionFence();
    projectMutation.mutate({
      source,
      inputs: projectionInputs,
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
    setDslValue('');
    setDiagnostics([]);
    setProjectionError(null);
    setCompletions([]);
    setSampleValues({});
    setSampleValidationVisible(false);
    setSimulation(null);
    setSimulationFailure(null);
    setStale(false);
    setError(null);
    setSelectedVersion(detail?.activeVersion ?? detail?.latestVersion ?? null);
    snapshotRef.current = draftSnapshot({
      name: detail?.name ?? '',
      description: detail?.description ?? '',
      inputs: nextInputs,
      condition: detail?.condition ?? null,
      dsl: '',
    });
    setHydrationCondition(detail?.condition ?? null);
  }, [open, detailQuery.data, creating, setDslValue]);

  useEffect(() => {
    if (!hydrationCondition) return;
    setHydrationCondition(null);
    const requestId = ++projectionRequestRef.current;
    latestProjectionRequestRef.current = requestId;
    projectMutation.mutate({
      source: { ast: hydrationCondition },
      inputs: inputContract,
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
      setDslValue('');
      setDiagnostics([]);
      setProjectionError(null);
      setCompletions([]);
      setSampleValues({});
      setSampleValidationVisible(false);
      setSimulation(null);
      setSimulationFailure(null);
      setError(null);
      setSelectedVersion(refreshed.activeVersion ?? refreshed.latestVersion ?? null);
      snapshotRef.current = draftSnapshot({
        name: refreshed.name ?? '',
        description: refreshed.description ?? '',
        inputs: nextInputs,
        condition: refreshed.condition ?? null,
        dsl: '',
      });
      setHydrationCondition(refreshed.condition ?? null);
      setStale(false);
      setReloadOpen(false);
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
      if (cause instanceof ApiError && cause.status === 409) {
        setStale(true);
        setError(null);
      } else {
        setError(
          readRuleMutationError(
            cause,
            t('rules.saveError'),
            t('rules.authorizationUnavailableDescription'),
            t('rules.authorizationTemporarilyUnavailableDescription'),
          ),
        );
      }
    },
  });

  const completionMutation = useMutation({
    mutationFn: (run: CompletionRun) =>
      completeRuleAuthoring({
        text: run.text,
        cursor: run.cursor,
        inputs: run.inputs,
        expressionLanguageVersion: run.expressionLanguageVersion,
      }),
    onSuccess: (result, run) => {
      if (
        run.generation !== projectionGenerationRef.current ||
        run.fence !== latestProjectionRequestRef.current
      )
        return;
      setCompletions(result);
    },
    onError: (_cause, run) => {
      if (
        run.generation !== projectionGenerationRef.current ||
        run.fence !== latestProjectionRequestRef.current
      )
        return;
      setError(t('rules.completionError'));
    },
  });
  const simulationMutation = useMutation({
    mutationFn: (_run: SimulationRun) => {
      const current = detailQuery.data ?? createdDraft;
      if (!current?.definitionKey) throw new Error(t('rules.simulationError'));
      if (authoringBlocked) throw new Error(t('rules.conditionRequired'));
      const immutableVersion = selectedVersion ?? current.activeVersion ?? current.latestVersion;
      const simulationInputs = canEditDraft
        ? inputContract
        : (current.versions?.find((version) => version.version === immutableVersion)?.inputs ?? []);
      const inputs = Object.fromEntries(
        simulationInputs.filter(hasInputKey).flatMap((input) => {
          const type = sampleValues[input.key]?.type ?? input.types?.[0];
          const values = (sampleValues[input.key]?.values ?? [])
            .map((entry) => normalizeRuleValue(type, entry.value))
            .filter((value): value is string => value != null && value !== '');
          return type && values.length ? [[input.key, { type, values }]] : [];
        }),
      );
      return canEditDraft
        ? simulateRuleDefinitionDraft(current.definitionKey, { inputs })
        : immutableVersion != null
          ? simulateRuleDefinitionVersion(current.definitionKey, immutableVersion, { inputs })
          : Promise.reject(new Error(t('rules.simulationError')));
    },
    onSuccess: (result, run) => {
      if (run.fingerprint !== simulationFingerprintRef.current) return;
      setSampleValidationVisible(false);
      setSimulationFailure(null);
      setSimulation(result);
    },
    onError: (cause, run) => {
      if (run.fingerprint !== simulationFingerprintRef.current) return;
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
      setSelectedVersion(saved.activeVersion ?? saved.latestVersion ?? null);
      toast.success(t('rules.lifecycleUpdated'));
      setArchiveOpen(false);
    },
    onError: (cause) => {
      if (cause instanceof ApiError && cause.status === 409) {
        setStale(true);
        setError(null);
      } else {
        setError(
          readRuleMutationError(
            cause,
            t('rules.lifecycleError'),
            t('rules.authorizationUnavailableDescription'),
            t('rules.authorizationTemporarilyUnavailableDescription'),
          ),
        );
      }
    },
  });

  const detail = detailQuery.data ?? createdDraft;
  const canEditDraft =
    !detailQuery.isError && (detail ? detail.actions?.canEditDraft === true : creating);
  const readOnly =
    detailQuery.isError || Boolean(detail && !canEditDraft) || (!creating && !detail);
  const dirty =
    canEditDraft &&
    snapshotRef.current !==
      draftSnapshot({ name, description, inputs: inputContract, condition, dsl });
  const inputValidation = validateInputContract(
    inputContract,
    languageQuery.data?.limits?.maxInputs,
    t,
  );
  const authoringBlockReason = !canEditDraft
    ? null
    : stale
      ? t('rules.resolveConflictFirst')
      : refreshMutation.isPending
        ? t('rules.refreshing')
        : hydrationCondition != null || projectMutation.isPending
          ? t('rules.expressionChecking')
          : projectionError
            ? projectionError
            : inputValidation.messages.length > 0
              ? t('rules.fixInputsFirst')
              : diagnostics.length > 0 || condition == null
                ? t('rules.conditionRequired')
                : dirty
                  ? t('rules.saveBeforeDependentActions')
                  : null;
  const authoringBlocked = authoringBlockReason !== null;
  const simulationVersion = canEditDraft
    ? null
    : (selectedVersion ?? detail?.activeVersion ?? detail?.latestVersion ?? null);
  const simulationSnapshot = detail?.versions?.find(
    (version) => version.version === simulationVersion,
  );
  const simulationInputs = canEditDraft ? inputContract : (simulationSnapshot?.inputs ?? []);
  const simulationCondition = canEditDraft ? condition : (simulationSnapshot?.condition ?? null);
  const simulationLanguageVersion = canEditDraft
    ? languageQuery.data?.version
    : simulationSnapshot?.expressionLanguageVersion;
  const selectedUsageVersion = detail?.versions?.find(
    (version) =>
      version.version === (selectedVersion ?? detail.activeVersion ?? detail.latestVersion),
  );
  const maxInputs = languageQuery.data?.limits?.maxInputs;
  const canAddInput = Boolean(
    languageQuery.data && (maxInputs == null || inputs.length < maxInputs),
  );
  const sampleErrors = validateSampleValues(simulationInputs, sampleValues);
  const simulationFingerprint = JSON.stringify({
    inputs: simulationInputs,
    condition: simulationCondition,
    sampleValues,
    version: simulationVersion,
  });
  simulationFingerprintRef.current = simulationFingerprint;
  const selectVersion = (version: number) => {
    setSelectedVersion(version);
    setSimulation(null);
    setSimulationFailure(null);
    setSampleValidationVisible(false);
  };
  const changeSampleValue = (key: string, value: SampleValue) => {
    setSampleValues((current) => ({ ...current, [key]: value }));
    setSimulation(null);
    setSimulationFailure(null);
  };
  const runSimulation = () => {
    setSampleValidationVisible(true);
    if (Object.keys(sampleErrors).length > 0) return;
    simulationMutation.mutate({ fingerprint: simulationFingerprint });
  };
  const requestClose = () => {
    if (dirty) setDiscardOpen(true);
    else onOpenChange(false);
  };
  const writeBusy = saveMutation.isPending || lifecycleMutation.isPending;
  const closeBusy = writeBusy;
  const localizedReference =
    detail?.origin === 'BuiltIn'
      ? referenceContent(detail.documentation, i18n.language)
      : undefined;
  const displayName = localizedReference?.displayName ?? detail?.name;
  const displaySummary = localizedReference?.summary ?? detail?.description;
  return (
    <ManagedDialog
      surfaceId="rule-editor"
      open={open}
      onOpenChange={(nextOpen) => {
        if (!nextOpen) requestClose();
      }}
      dirty={dirty}
      closeDisabled={closeBusy}
      title={
        !detailQuery.isError && displayName
          ? displayName
          : creating
            ? t('rules.createTitle')
            : t('rules.editorTitle')
      }
      description={
        detailQuery.isError
          ? undefined
          : readOnly && displaySummary
            ? displaySummary
            : creating
              ? t('rules.createDescription')
              : t('rules.editDescription')
      }
      titleAccessory={
        detail?.origin && !detailQuery.isError ? (
          <>
            <RuleOriginBadge origin={detail.origin} />
            <StatusBadge
              state={
                detail.status === 'Active'
                  ? 'positive'
                  : detail.status === 'Archived'
                    ? 'inactive'
                    : detail.status === 'Inactive'
                      ? 'inactive'
                      : 'neutral'
              }
            >
              {detail.status ? t(`rules.status${detail.status}`) : t('rules.statusUnknown')}
            </StatusBadge>
          </>
        ) : null
      }
      footer={
        readOnly ? (
          <ManagedDialogAction
            type="button"
            variant="outline"
            disabled={closeBusy}
            onClick={requestClose}
          >
            {t('app.close')}
          </ManagedDialogAction>
        ) : (
          <>
            <ManagedDialogAction
              type="button"
              variant="outline"
              disabled={writeBusy}
              onClick={requestClose}
            >
              {t('app.cancel')}
            </ManagedDialogAction>
            <ManagedDialogAsyncAction
              type="button"
              onClick={() => saveMutation.mutate()}
              disabled={
                saveMutation.isPending ||
                lifecycleMutation.isPending ||
                stale ||
                refreshMutation.isPending ||
                hydrationCondition != null ||
                projectMutation.isPending ||
                Boolean(projectionError) ||
                diagnostics.length > 0 ||
                inputValidation.messages.length > 0 ||
                simulationMutation.isPending ||
                condition == null ||
                !name.trim()
              }
              icon={<Save aria-hidden />}
              pending={saveMutation.isPending}
              pendingLabel={t('rules.saving')}
            >
              {t('rules.save')}
            </ManagedDialogAsyncAction>
          </>
        )
      }
    >
      <ManagedDialogBody>
        <AsyncContent
          pending={detailQuery.isPending}
          error={detailQuery.isError}
          pendingLabel={t('rules.loadingRule')}
        >
          <span />
        </AsyncContent>
        {detailQuery.isError ? (
          <StatusNotice
            tone={
              detailActionUnavailable || detailTemporarilyUnavailable ? 'warning' : 'destructive'
            }
            title={
              detailTemporarilyUnavailable
                ? t('rules.authorizationTemporarilyUnavailableTitle')
                : detailActionUnavailable
                  ? t('rules.authorizationUnavailableTitle')
                  : t('rules.loadErrorTitle')
            }
          >
            <span>
              {detailTemporarilyUnavailable
                ? t('rules.authorizationTemporarilyUnavailableDescription')
                : detailActionUnavailable
                  ? t('rules.authorizationUnavailableDescription')
                  : t('rules.loadErrorBody')}
            </span>
            {detailTemporarilyUnavailable ? (
              <>
                {' '}
                <Button
                  type="button"
                  variant="link"
                  disabled={detailQuery.isFetching}
                  onClick={() => void detailQuery.refetch()}
                >
                  {t('app.retry')}
                </Button>
              </>
            ) : null}
          </StatusNotice>
        ) : null}
        {error ? <StatusNotice tone="destructive">{error}</StatusNotice> : null}
        {stale ? (
          <StatusNotice tone="warning" title={t('rules.conflictTitle')}>
            <span>{t('rules.staleChanges')}</span>{' '}
            <Button
              type="button"
              variant="link"
              disabled={refreshMutation.isPending}
              onClick={() => setReloadOpen(true)}
            >
              <RefreshCw aria-hidden />
              {t('rules.reloadServerCopy')}
            </Button>
          </StatusNotice>
        ) : null}
        {creating || (!detailQuery.isPending && !detailQuery.isError && detail) ? (
          <ManagedDialogTabs
            label={t('rules.definitionSections')}
            generalLabel={t('dialog.general')}
            activeSection={activeSection}
            onActiveSectionChange={setActiveSection}
            general={
              readOnly && detail ? (
                <div className="space-y-5">
                  <dl className="grid gap-5 sm:grid-cols-2">
                    <RuleDetail
                      label={t('rules.name')}
                      value={displayName ?? t('rules.unknownRule')}
                    />
                    <RuleDetail
                      label={t('rules.activeVersion')}
                      value={
                        detail.activeVersion == null
                          ? t('rules.noActiveVersion')
                          : t('rules.version', { version: detail.activeVersion })
                      }
                    />
                    <div className="sm:col-span-2">
                      <RuleDetail
                        label={t('rules.description')}
                        value={displaySummary ?? t('rules.unknownRuleDescription')}
                      />
                    </div>
                  </dl>
                  {localizedReference?.usage ? (
                    <section className="space-y-1.5">
                      <h3 className="text-sm font-semibold">{t('rules.referenceUsage')}</h3>
                      <p className="text-sm leading-relaxed text-muted-foreground">
                        {localizedReference.usage}
                      </p>
                    </section>
                  ) : null}
                  {localizedReference?.examples?.length ? (
                    <section className="space-y-1.5">
                      <h3 className="text-sm font-semibold">{t('rules.referenceExamples')}</h3>
                      <ul className="list-disc space-y-1 pl-5 text-sm text-muted-foreground">
                        {localizedReference.examples.map((example) => (
                          <li key={example}>{example}</li>
                        ))}
                      </ul>
                    </section>
                  ) : null}
                </div>
              ) : (
                <div className="space-y-5">
                  <div className="grid gap-4 sm:grid-cols-2">
                    <Field data-invalid={!name.trim()}>
                      <FieldLabel htmlFor="rule-name">{t('rules.name')}</FieldLabel>
                      <Input
                        id="rule-name"
                        value={name}
                        required
                        aria-invalid={!name.trim()}
                        aria-describedby="rule-name-help"
                        onChange={(event) => setName(event.target.value)}
                      />
                      {!name.trim() ? <FieldError>{t('rules.nameRequired')}</FieldError> : null}
                      <FieldDescription id="rule-name-help">{t('rules.nameHelp')}</FieldDescription>
                    </Field>
                    {detail?.latestVersion ? (
                      <dl>
                        <RuleDetail
                          label={t('rules.latestVersion')}
                          value={t('rules.version', { version: detail.latestVersion })}
                        />
                      </dl>
                    ) : null}
                    <Field className="sm:col-span-2">
                      <FieldLabel htmlFor="rule-description">{t('rules.description')}</FieldLabel>
                      <Textarea
                        id="rule-description"
                        value={description}
                        aria-describedby="rule-description-help"
                        onChange={(event) => setDescription(event.target.value)}
                      />
                      <FieldDescription id="rule-description-help">
                        {t('rules.descriptionHelp')}
                      </FieldDescription>
                    </Field>
                  </div>
                </div>
              )
            }
            sections={[
              {
                id: 'behavior',
                label: t('rules.ruleBehavior'),
                content:
                  readOnly && detail ? (
                    <div className="space-y-5">
                      <RuleVersionSelector
                        id="rule-behavior-version"
                        label={t('rules.behaviorVersion')}
                        versions={detail.versions ?? []}
                        value={simulationVersion}
                        onChange={selectVersion}
                      />
                      {simulationSnapshot ? (
                        <RuleBehaviorSummary
                          condition={simulationSnapshot.condition}
                          output={simulationSnapshot.output}
                          expressionLanguageVersion={simulationSnapshot.expressionLanguageVersion}
                          inputs={simulationSnapshot.inputs}
                        />
                      ) : (
                        <StatusNotice tone="info" title={t('rules.testUnavailableTitle')}>
                          {t('rules.noVersionToInspect')}
                        </StatusNotice>
                      )}
                    </div>
                  ) : (
                    <div className="space-y-5">
                      <section className="space-y-3" aria-labelledby="rule-inputs-title">
                        <div className="flex flex-wrap items-start justify-between gap-3">
                          <div>
                            <h3 id="rule-inputs-title" className="text-sm font-semibold">
                              {t('rules.inputs')}
                            </h3>
                            <p className="text-xs text-muted-foreground">{t('rules.inputsHelp')}</p>
                          </div>
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            disabled={!canAddInput}
                            onClick={() => {
                              advanceProjectionFence();
                              setCompletions([]);
                              setSimulation(null);
                              setSimulationFailure(null);
                              const defaultType = languageQuery.data?.valueTypes?.find(
                                (candidate) => candidate.type,
                              )?.type;
                              if (!defaultType) return;
                              setInputs([
                                ...inputs,
                                {
                                  clientId: crypto.randomUUID(),
                                  keyLocked: false,
                                  key: '',
                                  label: '',
                                  types: [defaultType],
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
                        {languageQuery.isError ? (
                          <StatusNotice tone="warning" title={t('rules.languageUnavailableTitle')}>
                            <span>{t('rules.languageUnavailableDescription')}</span>{' '}
                            <Button
                              type="button"
                              variant="link"
                              disabled={languageQuery.isFetching}
                              onClick={() => void languageQuery.refetch()}
                            >
                              {t('app.retry')}
                            </Button>
                          </StatusNotice>
                        ) : null}
                        {maxInputs != null && inputs.length >= maxInputs ? (
                          <StatusNotice tone="warning">
                            {t('rules.maxInputsReached', { count: maxInputs })}
                          </StatusNotice>
                        ) : null}
                        {inputs.length ? (
                          <div className="space-y-3">
                            {inputs.map((input, index) => (
                              <InputRow
                                key={input.clientId}
                                input={input}
                                inputId={input.clientId}
                                inputNumber={index + 1}
                                issues={inputValidation.byIndex[index]}
                                keyLocked={input.keyLocked}
                                language={languageQuery.data}
                                onChange={(next) => {
                                  advanceProjectionFence();
                                  setCompletions([]);
                                  setSimulation(null);
                                  setSimulationFailure(null);
                                  const nextInputs = replaceInput(inputs, index, input, next);
                                  setInputs(nextInputs);
                                  if (condition) {
                                    projectAuthoring(
                                      { ast: condition },
                                      stripEditableInputState(nextInputs),
                                    );
                                  }
                                }}
                                onRemove={() => {
                                  advanceProjectionFence();
                                  setCompletions([]);
                                  setSimulation(null);
                                  setSimulationFailure(null);
                                  const nextInputs = inputs.filter(
                                    (_, candidateIndex) => candidateIndex !== index,
                                  );
                                  setInputs(nextInputs);
                                  if (condition) {
                                    projectAuthoring(
                                      { ast: condition },
                                      stripEditableInputState(nextInputs),
                                    );
                                  }
                                }}
                              />
                            ))}
                          </div>
                        ) : (
                          <p role="status" className="text-sm text-muted-foreground">
                            {t('rules.inputsEmpty')}
                          </p>
                        )}
                      </section>
                      {languageQuery.isError ? null : (
                        <RuleConditionComposer
                          condition={condition}
                          definitionKey={detail?.definitionKey}
                          inputs={inputContract}
                          language={languageQuery.data}
                          onChange={(next) => {
                            setCompletions([]);
                            setCondition(next);
                            setSimulation(null);
                            setSimulationFailure(null);
                            projectAuthoring({ ast: next ?? undefined });
                          }}
                        />
                      )}
                      <Field data-invalid={diagnostics.length > 0}>
                        <div className="flex flex-wrap items-center justify-between gap-3">
                          <FieldLabel htmlFor="rule-dsl">{t('rules.expressionSyntax')}</FieldLabel>
                          <AsyncButton
                            type="button"
                            variant="outline"
                            size="sm"
                            disabled={completionMutation.isPending || !dsl}
                            icon={<Lightbulb aria-hidden />}
                            pending={completionMutation.isPending}
                            pendingLabel={t('rules.loadingSuggestions')}
                            onClick={() =>
                              completionMutation.mutate({
                                generation: projectionGenerationRef.current,
                                fence: latestProjectionRequestRef.current,
                                text: dsl,
                                cursor,
                                inputs: inputContract,
                                expressionLanguageVersion: languageQuery.data?.version,
                              })
                            }
                          >
                            {t('rules.showSuggestions')}
                          </AsyncButton>
                        </div>
                        <Textarea
                          ref={dslRef}
                          id="rule-dsl"
                          value={dsl}
                          aria-invalid={diagnostics.length > 0}
                          onChange={(event) => {
                            advanceProjectionFence();
                            setDslValue(event.target.value);
                            setCursor(event.currentTarget.selectionStart);
                            setCompletions([]);
                            setSimulation(null);
                            setSimulationFailure(null);
                          }}
                          onSelect={(event) => setCursor(event.currentTarget.selectionStart)}
                          onBlur={() => projectAuthoring({ text: dsl })}
                        />
                        {diagnostics.map((diagnostic, index) => (
                          <FieldError
                            key={`${diagnostic.code ?? 'diagnostic'}:${diagnostic.start ?? index}`}
                          >
                            {diagnostic.message ?? t('rules.expressionInvalid')}
                          </FieldError>
                        ))}
                      </Field>
                      {projectionError ? (
                        <StatusNotice tone="warning">{projectionError}</StatusNotice>
                      ) : null}
                      {completions.length ? (
                        <section
                          aria-label={t('rules.expressionSuggestions')}
                          className="space-y-2"
                        >
                          <h4 className="text-sm font-medium">
                            {t('rules.expressionSuggestions')}
                          </h4>
                          <div className="flex flex-wrap gap-2">
                            {completions.map((completion) => (
                              <Button
                                key={`${completion.label}:${completion.start}`}
                                type="button"
                                variant="outline"
                                size="sm"
                                aria-label={
                                  completion.label ??
                                  completion.insertText ??
                                  t('rules.applySuggestion')
                                }
                                onClick={() => {
                                  const start = completion.start ?? cursor;
                                  const length = completion.length ?? 0;
                                  const nextDsl = `${dsl.slice(0, start)}${completion.insertText ?? ''}${dsl.slice(start + length)}`;
                                  setDslValue(nextDsl);
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
              {
                id: 'test',
                label: t('rules.testRule'),
                content:
                  detail?.definitionKey && (canEditDraft || simulationSnapshot) ? (
                    <div className="space-y-4">
                      {!canEditDraft && detail ? (
                        <RuleVersionSelector
                          id="rule-test-version"
                          label={t('rules.testVersion')}
                          versions={detail.versions ?? []}
                          value={simulationVersion}
                          onChange={selectVersion}
                        />
                      ) : null}
                      <RuleSimulationPanel
                        inputs={simulationInputs}
                        values={sampleValues}
                        onChange={changeSampleValue}
                        onSimulate={runSimulation}
                        pending={simulationMutation.isPending}
                        blockedReason={
                          authoringBlockReason ??
                          (writeBusy ? t('rules.operationInProgress') : null)
                        }
                        simulation={simulation}
                        failure={simulationFailure}
                        version={simulationVersion}
                        condition={simulationCondition}
                        expressionLanguageVersion={simulationLanguageVersion}
                        language={languageQuery.data}
                        validationVisible={sampleValidationVisible}
                        validationErrors={sampleErrors}
                      />
                    </div>
                  ) : (
                    <StatusNotice tone="info" title={t('rules.testUnavailableTitle')}>
                      {creating ? t('rules.saveBeforeTesting') : t('rules.noVersionToTest')}
                    </StatusNotice>
                  ),
              },
              {
                id: 'versions',
                label: t('rules.versions'),
                content: (
                  <div className="space-y-5">
                    <VersionHistory
                      versions={detail?.versions ?? []}
                      activeVersion={detail?.activeVersion}
                      latestVersion={detail?.latestVersion}
                    />
                    {!detail?.definitionKey ? (
                      <StatusNotice tone="info">{t('rules.saveBeforeVersioning')}</StatusNotice>
                    ) : null}
                    {detail && !readOnly ? (
                      <div className="space-y-3 border-t pt-4">
                        {authoringBlockReason &&
                        (detail.actions?.canCreateVersion || detail.actions?.canActivateVersion) ? (
                          <p role="status" className="text-sm text-muted-foreground">
                            {authoringBlockReason}
                          </p>
                        ) : null}
                        <div className="flex flex-wrap gap-2">
                          {detail.actions?.canCreateVersion ? (
                            <AsyncButton
                              type="button"
                              variant="secondary"
                              disabled={
                                authoringBlocked || writeBusy || simulationMutation.isPending
                              }
                              onClick={() => setLifecycleAction('version')}
                              icon={<Plus aria-hidden />}
                              pending={
                                lifecycleMutation.isPending &&
                                lifecycleMutation.variables === 'version'
                              }
                              pendingLabel={t('rules.updatingLifecycle')}
                            >
                              {t('rules.createVersion')}
                            </AsyncButton>
                          ) : null}
                          {detail.actions?.canActivateVersion ? (
                            <AsyncButton
                              type="button"
                              disabled={
                                authoringBlocked || writeBusy || simulationMutation.isPending
                              }
                              onClick={() => setLifecycleAction('activate')}
                              icon={<Play aria-hidden />}
                              pending={
                                lifecycleMutation.isPending &&
                                lifecycleMutation.variables === 'activate'
                              }
                              pendingLabel={t('rules.updatingLifecycle')}
                            >
                              {t('rules.activate')}
                            </AsyncButton>
                          ) : null}
                          {detail.actions?.canDeactivate ? (
                            <AsyncButton
                              type="button"
                              variant="outline"
                              disabled={
                                writeBusy ||
                                simulationMutation.isPending ||
                                stale ||
                                refreshMutation.isPending ||
                                hydrationCondition != null
                              }
                              onClick={() => setLifecycleAction('deactivate')}
                              icon={<Pause aria-hidden />}
                              pending={
                                lifecycleMutation.isPending &&
                                lifecycleMutation.variables === 'deactivate'
                              }
                              pendingLabel={t('rules.updatingLifecycle')}
                            >
                              {t('rules.deactivate')}
                            </AsyncButton>
                          ) : null}
                          {detail.actions?.canArchive ? (
                            <AsyncButton
                              type="button"
                              variant="destructive"
                              disabled={
                                writeBusy ||
                                simulationMutation.isPending ||
                                stale ||
                                refreshMutation.isPending ||
                                hydrationCondition != null
                              }
                              onClick={() => setArchiveOpen(true)}
                              icon={<Archive aria-hidden />}
                              pending={
                                lifecycleMutation.isPending &&
                                lifecycleMutation.variables === 'archive'
                              }
                              pendingLabel={t('rules.updatingLifecycle')}
                            >
                              {t('rules.archive')}
                            </AsyncButton>
                          ) : null}
                        </div>
                      </div>
                    ) : null}
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
                          <RuleVersionSelector
                            id="rule-usage-version"
                            label={t('rules.usageVersion')}
                            versions={detail.versions ?? []}
                            value={selectedUsageVersion.version}
                            onChange={selectVersion}
                          />
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
                          value={detail.definitionKey ?? t('table.emptyValue')}
                        />
                        <RuleDetail
                          label={t('rules.revision')}
                          value={
                            detail.revision == null
                              ? t('table.emptyValue')
                              : String(detail.revision)
                          }
                        />
                        <RuleDetail
                          label={t('rules.expressionLanguage')}
                          value={t('rules.expressionLanguageVersion', {
                            version: detail.expressionLanguageVersion ?? 1,
                          })}
                        />
                        <RuleDetail
                          label={t('rules.createdAt')}
                          value={formatRuleDate(
                            detail.createdAt,
                            i18n.language,
                            t('rules.dateUnavailable'),
                          )}
                        />
                        <RuleDetail
                          label={t('rules.updated')}
                          value={formatRuleDate(
                            detail.updatedAt,
                            i18n.language,
                            t('rules.dateUnavailable'),
                          )}
                        />
                        {detail.archivedAt ? (
                          <RuleDetail
                            label={t('rules.archivedAt')}
                            value={formatRuleDate(
                              detail.archivedAt,
                              i18n.language,
                              t('rules.dateUnavailable'),
                            )}
                          />
                        ) : null}
                      </dl>
                    ),
                  }
                : undefined
            }
          />
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
      <AlertDialog open={reloadOpen} onOpenChange={setReloadOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('rules.reloadTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('rules.reloadDescription')}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={refreshMutation.isPending}>
              {t('rules.keepLocalChanges')}
            </AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              disabled={refreshMutation.isPending}
              onClick={() => refreshMutation.mutate()}
            >
              {t('rules.reloadAndReplace')}
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
            <AlertDialogCancel disabled={lifecycleMutation.isPending}>
              {t('app.cancel')}
            </AlertDialogCancel>
            <AlertDialogAction
              disabled={lifecycleMutation.isPending}
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
            <AlertDialogCancel disabled={lifecycleMutation.isPending}>
              {t('app.cancel')}
            </AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              disabled={lifecycleMutation.isPending}
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

function readRuleMutationError(
  error: unknown,
  fallback: string,
  actionUnavailable: string,
  temporarilyUnavailable: string,
): string {
  if (error instanceof ApiError) {
    if (error.status === 403 || error.status === 404) return actionUnavailable;
    if (error.status === 503) return temporarilyUnavailable;
  }
  return error instanceof Error ? error.message : fallback;
}

function VersionHistory({
  versions,
  activeVersion,
  latestVersion,
}: {
  versions: ApiTypes.RuleDefinitionVersionDto[];
  activeVersion?: number | null;
  latestVersion?: number | null;
}) {
  const { t, i18n } = useTranslation();
  return (
    <section aria-label={t('rules.versionHistory')} className="space-y-3">
      <div>
        <h3 className="text-sm font-semibold">{t('rules.versionHistory')}</h3>
        <p className="text-xs text-muted-foreground">{t('rules.versionHistoryDescription')}</p>
      </div>
      {versions.length ? (
        <ol className="divide-y divide-border">
          {[...versions].reverse().map((version) => (
            <li key={version.version} className="space-y-2 py-3 first:pt-0 last:pb-0">
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-sm font-semibold">
                  {t('rules.version', { version: version.version })}
                </span>
                {version.version === activeVersion ? (
                  <StatusBadge state="positive">{t('rules.activeVersion')}</StatusBadge>
                ) : null}
                {version.version === latestVersion ? (
                  <StatusBadge state="neutral">{t('rules.latestVersion')}</StatusBadge>
                ) : null}
              </div>
              <dl className="grid gap-3 text-sm sm:grid-cols-2">
                <RuleDetail
                  label={t('rules.publishedAt')}
                  value={formatRuleDate(
                    version.createdAt,
                    i18n.language,
                    t('rules.dateUnavailable'),
                  )}
                />
                <RuleDetail
                  label={t('rules.expressionLanguage')}
                  value={t('rules.expressionLanguageVersion', {
                    version: version.expressionLanguageVersion ?? 1,
                  })}
                />
              </dl>
            </li>
          ))}
        </ol>
      ) : (
        <p role="status" className="text-sm text-muted-foreground">
          {t('rules.noVersions')}
        </p>
      )}
    </section>
  );
}

function RuleVersionSelector({
  id,
  label,
  versions,
  value,
  onChange,
}: {
  id: string;
  label: string;
  versions: ApiTypes.RuleDefinitionVersionDto[];
  value?: number | null;
  onChange: (version: number) => void;
}) {
  const { t } = useTranslation();
  const selected = versions.find((version) => version.version === value) ?? versions.at(-1);
  if (selected?.version == null) return null;
  return (
    <Field>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      <Select value={String(selected.version)} onValueChange={(next) => onChange(Number(next))}>
        <SelectTrigger id={id}>
          <SelectValue>{t('rules.version', { version: selected.version })}</SelectValue>
        </SelectTrigger>
        <SelectContent>
          {[...versions].reverse().map((version) =>
            version.version != null ? (
              <SelectItem key={version.version} value={String(version.version)}>
                {t('rules.version', { version: version.version })}
              </SelectItem>
            ) : null,
          )}
        </SelectContent>
      </Select>
    </Field>
  );
}

function RuleSimulationPanel({
  inputs,
  values,
  onChange,
  onSimulate,
  pending,
  blockedReason,
  simulation,
  failure,
  version,
  condition,
  expressionLanguageVersion,
  language,
  validationVisible,
  validationErrors,
}: {
  inputs: InputDefinition[];
  values: Record<string, SampleValue>;
  onChange: (key: string, value: SampleValue) => void;
  onSimulate: () => void;
  pending: boolean;
  blockedReason: string | null;
  simulation: ApiTypes.RuleSimulationResultDto | null;
  failure: 'invalid' | 'error' | null;
  version?: number | null;
  condition: ApiTypes.RuleConditionNodeDto | null;
  expressionLanguageVersion?: number;
  language?: ApiTypes.RuleExpressionLanguageDto;
  validationVisible: boolean;
  validationErrors: Record<string, string>;
}) {
  const { t } = useTranslation();
  return (
    <section aria-label={t('rules.simulation')} className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold">{t('rules.testRule')}</h3>
          <p className="text-sm text-muted-foreground">{t('rules.simulationHelp')}</p>
        </div>
        <AsyncButton
          type="button"
          variant="outline"
          onClick={onSimulate}
          disabled={pending || Boolean(blockedReason)}
          icon={<Play aria-hidden />}
          pending={pending}
          pendingLabel={t('rules.simulating')}
        >
          {t('rules.runSimulation')}
        </AsyncButton>
      </div>
      {blockedReason ? (
        <StatusNotice tone="info" title={t('rules.testUnavailableTitle')}>
          {blockedReason}
        </StatusNotice>
      ) : null}
      {inputs.length ? (
        <fieldset disabled={pending} className="grid gap-3 sm:grid-cols-2">
          <FieldLegend className="sr-only">{t('rules.sampleInputs')}</FieldLegend>
          {inputs.filter(hasInputKey).map((input) => (
            <SampleInput
              key={input.key}
              input={input}
              value={values[input.key]}
              onChange={(value) => onChange(input.key, value)}
              error={validationVisible ? validationErrors[input.key] : undefined}
              language={language}
            />
          ))}
        </fieldset>
      ) : (
        <p className="text-sm text-muted-foreground">{t('rules.noParameters')}</p>
      )}
      {failure ? (
        <StatusNotice tone="destructive">
          {failure === 'invalid' ? t('rules.simulationInvalid') : t('rules.simulationError')}
        </StatusNotice>
      ) : null}
      {simulation ? (
        <div className="space-y-4" aria-live="polite">
          <StatusNotice
            tone={simulation.isMatch ? 'success' : 'warning'}
            title={
              simulation.isMatch ? t('rules.simulationMatched') : t('rules.simulationNotMatched')
            }
          >
            {simulation.definitionVersion == null
              ? t('rules.simulationDraft')
              : t('rules.version', { version: simulation.definitionVersion ?? version })}
          </StatusNotice>
          {simulation.diagnostics?.length ? (
            <section className="space-y-3" aria-labelledby="rule-simulation-explanation">
              <h4 id="rule-simulation-explanation" className="text-sm font-semibold">
                {simulation.isMatch
                  ? t('rules.simulationWhyMatched')
                  : t('rules.simulationWhyNotMatched')}
              </h4>
              <ol className="space-y-3">
                {simulation.diagnostics.map((diagnostic) => {
                  const diagnosticCondition = findConditionNode(condition, diagnostic.nodeId);
                  return (
                    <li
                      key={diagnostic.nodeId ?? 'rule-root'}
                      className="space-y-1.5 border-l pl-3"
                    >
                      <p className="text-xs font-medium text-muted-foreground">
                        {diagnostic.isMatch
                          ? t('rules.simulationNodeMatched')
                          : t('rules.simulationNodeNotMatched')}
                      </p>
                      {diagnosticCondition ? (
                        <RuleLogicPreview
                          condition={diagnosticCondition}
                          expressionLanguageVersion={expressionLanguageVersion}
                          inputs={inputs}
                        />
                      ) : null}
                    </li>
                  );
                })}
              </ol>
            </section>
          ) : null}
          {simulation.correlationId ? (
            <p className="text-xs text-muted-foreground">
              {t('rules.supportReference', { reference: simulation.correlationId })}
            </p>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

function SampleInput({
  input,
  value,
  onChange,
  error,
  language,
}: {
  input: InputDefinition;
  value?: SampleValue;
  onChange: (value: SampleValue) => void;
  error?: string;
  language?: ApiTypes.RuleExpressionLanguageDto;
}) {
  const { t, i18n } = useTranslation();
  const type = value?.type ?? input.types?.[0] ?? 'Text';
  const currentEntries = value?.values?.length ? value.values : [createSampleEntry()];
  const currentValues = currentEntries.map((entry) => entry.value);
  const id = `rule-sample-${input.key}`;
  const errorMessage =
    error === 'required' ? t('rules.sampleRequired') : error ? t('rules.sampleInvalidValue') : null;
  const updateValue = (index: number, next: string) =>
    onChange({
      type,
      values: currentEntries.map((entry, candidateIndex) =>
        candidateIndex === index ? { ...entry, value: next } : entry,
      ),
    });

  if (input.allowMultiple) {
    const allowedValues = input.allowedValues ?? [];
    return (
      <fieldset className="space-y-3 rounded-lg border p-3" data-invalid={Boolean(error)}>
        <FieldLegend variant="label">{input.label ?? input.key}</FieldLegend>
        <p className="text-xs text-muted-foreground">
          {t('rules.sampleInputContract', {
            type: valueTypeLabel(language, type, i18n.language),
            requirement: input.isRequired
              ? t('rules.inputRequirementRequired')
              : t('rules.inputRequirementOptional'),
          })}
        </p>
        {allowedValues.length ? (
          <div className="space-y-2">
            {allowedValues.map((allowedValue) => {
              const checked = currentValues.includes(allowedValue);
              const optionId = `${id}-${encodeURIComponent(allowedValue)}`;
              return (
                <Field key={allowedValue} orientation="horizontal">
                  <Checkbox
                    id={optionId}
                    checked={checked}
                    onCheckedChange={(next) =>
                      onChange({
                        type,
                        values:
                          next === true
                            ? [
                                ...currentEntries.filter((entry) => entry.value !== ''),
                                createSampleEntry(allowedValue),
                              ]
                            : currentEntries.filter((entry) => entry.value !== allowedValue),
                      })
                    }
                  />
                  <FieldLabel htmlFor={optionId}>{allowedValue}</FieldLabel>
                </Field>
              );
            })}
          </div>
        ) : (
          <div className="space-y-2">
            {currentEntries.map((entry, index) => (
              <div key={entry.id} className="flex items-center gap-2">
                <Input
                  type={sampleHtmlType(type)}
                  value={entry.value}
                  aria-label={t('rules.sampleValueNumber', {
                    label: input.label ?? input.key,
                    number: index + 1,
                  })}
                  onChange={(event) => updateValue(index, event.target.value)}
                />
                {currentValues.length > 1 ? (
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label={t('rules.removeSampleValue', { number: index + 1 })}
                    onClick={() =>
                      onChange({
                        type,
                        values: currentEntries.filter(
                          (_, candidateIndex) => candidateIndex !== index,
                        ),
                      })
                    }
                  >
                    <Trash2 />
                  </Button>
                ) : null}
              </div>
            ))}
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => onChange({ type, values: [...currentEntries, createSampleEntry()] })}
            >
              <Plus data-icon="inline-start" />
              {t('rules.addSampleValue')}
            </Button>
          </div>
        )}
        {errorMessage ? <FieldError>{errorMessage}</FieldError> : null}
      </fieldset>
    );
  }

  const current = currentValues[0] ?? '';
  const typeControl =
    (input.types ?? []).length > 1 ? (
      <Field>
        <FieldLabel htmlFor={`${id}-type`}>{t('rules.valueType')}</FieldLabel>
        <Select
          value={type}
          onValueChange={(next) =>
            onChange({ type: next as ValueType, values: [createSampleEntry()] })
          }
        >
          <SelectTrigger id={`${id}-type`}>
            <SelectValue>{type}</SelectValue>
          </SelectTrigger>
          <SelectContent>
            {(input.types ?? []).map((candidate) => (
              <SelectItem key={candidate} value={candidate}>
                {valueTypeLabel(language, candidate, i18n.language)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </Field>
    ) : null;

  const allowedValues = input.allowedValues ?? [];
  if (allowedValues.length) {
    return (
      <div className="space-y-3">
        {typeControl}
        <Field data-invalid={Boolean(error)}>
          <FieldLabel htmlFor={id}>
            {input.label ?? input.key ?? t('rules.unknownInput')}
          </FieldLabel>
          <Select
            value={current}
            onValueChange={(next) => {
              if (next != null) onChange({ type, values: [createSampleEntry(next)] });
            }}
          >
            <SelectTrigger id={id}>
              <SelectValue placeholder={t('rules.selectValue')}>{current || undefined}</SelectValue>
            </SelectTrigger>
            <SelectContent>
              {allowedValues.map((allowedValue) => (
                <SelectItem key={allowedValue} value={allowedValue}>
                  {allowedValue}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <FieldDescription>
            {input.isRequired
              ? t('rules.inputRequirementRequired')
              : t('rules.inputRequirementOptional')}
          </FieldDescription>
          {errorMessage ? <FieldError>{errorMessage}</FieldError> : null}
        </Field>
      </div>
    );
  }

  if (type === 'Boolean') {
    return (
      <div className="space-y-3">
        {typeControl}
        <Field data-invalid={Boolean(error)}>
          <FieldLabel htmlFor={id}>{input.label ?? input.key}</FieldLabel>
          <Select
            value={current}
            onValueChange={(next) => {
              if (next != null) onChange({ type, values: [createSampleEntry(next)] });
            }}
          >
            <SelectTrigger id={id}>
              <SelectValue placeholder={t('rules.selectValue')}>
                {current === 'true'
                  ? t('rules.booleanTrue')
                  : current === 'false'
                    ? t('rules.booleanFalse')
                    : undefined}
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="true">{t('rules.booleanTrue')}</SelectItem>
              <SelectItem value="false">{t('rules.booleanFalse')}</SelectItem>
            </SelectContent>
          </Select>
          {errorMessage ? <FieldError>{errorMessage}</FieldError> : null}
        </Field>
      </div>
    );
  }
  return (
    <div className="space-y-3">
      {typeControl}
      <Field data-invalid={Boolean(error)}>
        <FieldLabel htmlFor={id}>{input.label ?? input.key}</FieldLabel>
        <Input
          id={id}
          type={sampleHtmlType(type)}
          value={current}
          onChange={(event) => onChange({ type, values: [createSampleEntry(event.target.value)] })}
        />
        <FieldDescription>
          {t('rules.sampleInputContract', {
            type: valueTypeLabel(language, type, i18n.language),
            requirement: input.isRequired
              ? t('rules.inputRequirementRequired')
              : t('rules.inputRequirementOptional'),
          })}
        </FieldDescription>
        {errorMessage ? <FieldError>{errorMessage}</FieldError> : null}
      </Field>
    </div>
  );
}

function InputRow({
  input,
  inputId,
  inputNumber,
  issues,
  keyLocked,
  language,
  onChange,
  onRemove,
}: {
  input: InputDefinition;
  inputId: string;
  inputNumber: number;
  issues?: InputIssue;
  keyLocked: boolean;
  language: ApiTypes.RuleExpressionLanguageDto | undefined;
  onChange: (input: InputDefinition) => void;
  onRemove: () => void;
}) {
  const { t, i18n } = useTranslation();
  const availableTypes = (language?.valueTypes ?? [])
    .map((definition) => definition.type)
    .filter((type): type is ValueType => Boolean(type));
  return (
    <fieldset className="grid gap-3 rounded-lg border p-3 sm:grid-cols-12">
      <FieldLegend variant="label" className="sm:col-span-12">
        {t('rules.inputNumber', { number: inputNumber })}
      </FieldLegend>
      <Field className="sm:col-span-3" data-invalid={Boolean(issues?.key)}>
        <FieldLabel htmlFor={`${inputId}-key`}>{t('rules.key')}</FieldLabel>
        <Input
          id={`${inputId}-key`}
          value={input.key ?? ''}
          disabled={keyLocked}
          aria-invalid={Boolean(issues?.key)}
          onChange={(event) => onChange({ ...input, key: event.target.value })}
        />
        {issues?.key ? <FieldError>{issues.key}</FieldError> : null}
        <FieldDescription>{t('rules.inputKeyHelp')}</FieldDescription>
      </Field>
      <Field className="sm:col-span-5" data-invalid={Boolean(issues?.label)}>
        <FieldLabel htmlFor={`${inputId}-label`}>{t('rules.inputLabel')}</FieldLabel>
        <Input
          id={`${inputId}-label`}
          aria-label={t('rules.inputLabel')}
          value={input.label ?? ''}
          aria-invalid={Boolean(issues?.label)}
          placeholder={t('rules.inputLabelPlaceholder')}
          onChange={(event) => onChange({ ...input, label: event.target.value })}
        />
        {issues?.label ? <FieldError>{issues.label}</FieldError> : null}
      </Field>
      <Field className="sm:col-span-3">
        <FieldLabel htmlFor={`${inputId}-type`}>{t('rules.type')}</FieldLabel>
        <Select
          value={input.types?.[0] ?? availableTypes[0] ?? ''}
          onValueChange={(value) => onChange({ ...input, types: [value as ValueType] })}
        >
          <SelectTrigger id={`${inputId}-type`} aria-label={t('rules.type')}>
            <SelectValue>{valueTypeLabel(language, input.types?.[0], i18n.language)}</SelectValue>
          </SelectTrigger>
          <SelectContent>
            {availableTypes.map((type) => (
              <SelectItem key={type} value={type}>
                {valueTypeLabel(language, type, i18n.language)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </Field>
      <Field className="sm:col-span-4" data-invalid={Boolean(issues?.allowedValues)}>
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
        {issues?.allowedValues ? <FieldError>{issues.allowedValues}</FieldError> : null}
        <FieldDescription>{t('rules.allowedValuesHelp')}</FieldDescription>
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
        aria-label={t('rules.removeInput', { number: inputNumber })}
        className="sm:col-span-6 sm:justify-self-end"
        onClick={onRemove}
      >
        <Trash2 />
      </Button>
    </fieldset>
  );
}

function replaceInput(
  inputs: EditableInput[],
  index: number,
  current: EditableInput,
  next: InputDefinition,
): EditableInput[] {
  return inputs.map((candidate, candidateIndex) =>
    candidateIndex === index
      ? { ...next, clientId: current.clientId, keyLocked: current.keyLocked }
      : candidate,
  );
}

function stripEditableInputState(inputs: EditableInput[]): InputDefinition[] {
  return inputs.map(({ clientId: _clientId, keyLocked: _keyLocked, ...input }) => input);
}

function validateInputContract(
  inputs: InputDefinition[],
  maxInputs: number | undefined,
  t: ReturnType<typeof useTranslation>['t'],
) {
  const byIndex: Record<number, InputIssue> = {};
  const messages: string[] = [];
  const keys = inputs.map((input) => input.key?.trim() ?? '');
  const labels = inputs.map((input) => input.label?.trim() ?? '');
  const addMessage = (message: string) => {
    if (!messages.includes(message)) messages.push(message);
  };

  if (maxInputs != null && inputs.length > maxInputs) {
    addMessage(t('rules.maxInputsReached', { count: maxInputs }));
  }

  inputs.forEach((input, index) => {
    const issue: InputIssue = {};
    const key = keys[index];
    const label = labels[index];
    if (!key || !/^[a-z][a-z0-9_]*$/.test(key)) issue.key = t('rules.inputKeyInvalid');
    else if (keys.filter((candidate) => candidate === key).length > 1)
      issue.key = t('rules.inputKeyDuplicate');
    if (!label || label.length > 120) issue.label = t('rules.inputLabelInvalid');
    else if (
      labels.filter((candidate) => candidate.toLocaleLowerCase() === label.toLocaleLowerCase())
        .length > 1
    )
      issue.label = t('rules.inputLabelDuplicate');
    if (issue.key || issue.label) {
      byIndex[index] = issue;
      if (issue.key) addMessage(issue.key);
      if (issue.label) addMessage(issue.label);
    }
    if (!(input.types ?? []).length) addMessage(t('rules.inputTypeRequired'));
    const allowedValues = input.allowedValues ?? [];
    const allowedType = input.types?.length === 1 ? input.types[0] : undefined;
    const normalizedAllowedValues = allowedType
      ? allowedValues.map((value) => normalizeRuleValue(allowedType, value))
      : [];
    if (allowedValues.length && normalizedAllowedValues.some((value) => value == null)) {
      issue.allowedValues = t('rules.allowedValuesInvalid');
      addMessage(issue.allowedValues);
    } else if (new Set(normalizedAllowedValues).size !== normalizedAllowedValues.length) {
      issue.allowedValues = t('rules.allowedValuesDuplicate');
      addMessage(issue.allowedValues);
    }
    if (issue.allowedValues) byIndex[index] = issue;
  });

  return { byIndex, messages };
}

function validateSampleValues(
  inputs: InputDefinition[],
  samples: Record<string, SampleValue>,
): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const input of inputs) {
    if (!input.key) continue;
    const sample = samples[input.key];
    const values = (sample?.values ?? [])
      .map((entry) => entry.value)
      .filter((value) => value !== '');
    if (input.isRequired && values.length === 0) {
      errors[input.key] = 'required';
      continue;
    }
    const type = sample?.type ?? input.types?.[0];
    if (type && values.some((value) => normalizeRuleValue(type, value) == null)) {
      errors[input.key] = 'invalid';
      continue;
    }
    if (
      input.allowedValues?.length &&
      values.some((value) => !input.allowedValues?.includes(value))
    ) {
      errors[input.key] = 'invalid';
    }
  }
  return errors;
}

function normalizeRuleValue(type: ValueType | undefined, value: string): string | null {
  if (!type) return null;
  const trimmed = value.trim();
  if (type === 'Text') return value;
  if (type === 'Integer') {
    if (!/^[+-]?\d+$/.test(trimmed)) return null;
    try {
      return BigInt(trimmed).toString();
    } catch {
      return null;
    }
  }
  if (type === 'Decimal') {
    const match = /^([+-]?)(?:(\d+)(?:\.(\d*))?|\.(\d+))$/.exec(trimmed);
    if (!match) return null;
    const integer = (match[2] ?? '0').replace(/^0+(?=\d)/, '');
    const fraction = (match[3] ?? match[4] ?? '').replace(/0+$/, '');
    const significantDigits = `${integer}${fraction}`.replace(/^0+/, '').length;
    if (significantDigits > 29) return null;
    const magnitude = fraction ? `${integer}.${fraction}` : integer;
    const sign = match[1] === '-' && magnitude !== '0' ? '-' : '';
    return `${sign}${magnitude}`;
  }
  if (type === 'Boolean') return trimmed === 'true' || trimmed === 'false' ? trimmed : null;
  if (type === 'Date') {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) return null;
    const parsed = new Date(`${trimmed}T00:00:00Z`);
    return !Number.isNaN(parsed.valueOf()) && parsed.toISOString().startsWith(trimmed)
      ? trimmed
      : null;
  }
  if (type === 'DateTime') {
    const parsed = new Date(trimmed);
    return trimmed && !Number.isNaN(parsed.valueOf()) ? parsed.toISOString() : null;
  }
  return null;
}

function sampleHtmlType(type: ValueType) {
  if (type === 'Integer' || type === 'Decimal') return 'number';
  if (type === 'Date') return 'date';
  if (type === 'DateTime') return 'datetime-local';
  return 'text';
}

function findConditionNode(
  condition: ApiTypes.RuleConditionNodeDto | null,
  nodeId: string | undefined,
): ApiTypes.RuleConditionNodeDto | null {
  if (!condition || !nodeId) return null;
  if (condition.nodeId === nodeId) return condition;
  for (const child of condition.children ?? []) {
    const match = findConditionNode(child, nodeId);
    if (match) return match;
  }
  return null;
}

function formatRuleDate(value: string | null | undefined, locale: string, fallback: string) {
  if (!value) return fallback;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return fallback;
  return new Intl.DateTimeFormat(locale, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date);
}
