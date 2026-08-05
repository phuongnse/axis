import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
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
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ApiError } from '@/lib/api';
import type {
  RuleBindingDto,
  RuleBindingFailureBehavior,
  RuleInputDefinitionDto,
  RuleInputMappingDto,
  RuleInputMappingKind,
  UpdateRuleBindingRequest,
} from '@/lib/api-generated';
import {
  deleteRuleBinding,
  getRuleBinding,
  ruleBindingUsageQueryOptions,
  ruleDefinitionQueryKeys,
  updateRuleBinding,
} from '../api';

type BindingActionError = 'conflict' | 'request' | null;

interface MappingFormValue {
  kind: RuleInputMappingKind | 'Unmapped';
  value: string;
}

interface BindingFormValue {
  definitionKey: string;
  definitionVersion: string;
  targetType: string;
  targetId: string;
  useCaseOrTrigger: string;
  priority: string;
  enabled: boolean;
  failureBehavior: RuleBindingFailureBehavior;
  inputMappings: Record<string, MappingFormValue>;
}

const emptyRuleInputs: RuleInputDefinitionDto[] = [];

export function RuleBindingUsagePanel({
  definitionKey,
  version,
  active,
  inputs,
}: {
  definitionKey: string;
  version: number;
  active: boolean;
  inputs?: RuleInputDefinitionDto[];
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const inputDefinitions = inputs ?? emptyRuleInputs;
  const [bindingToDelete, setBindingToDelete] = useState<{
    id: string;
    revision: number;
  } | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState<BindingFormValue | null>(null);
  const [actionError, setActionError] = useState<BindingActionError>(null);
  const [editError, setEditError] = useState<BindingActionError>(null);

  const usageQuery = useQuery({
    ...ruleBindingUsageQueryOptions(definitionKey, version),
    enabled: active && Boolean(definitionKey && version),
  });
  const bindingQuery = useQuery({
    queryKey: ['rule-binding', editingId],
    queryFn: () => getRuleBinding(editingId as string),
    enabled: Boolean(editingId),
  });

  useEffect(() => {
    if (!bindingQuery.data) return;
    setForm(toBindingForm(bindingQuery.data, inputDefinitions));
  }, [bindingQuery.data, inputDefinitions]);

  async function refreshUsage() {
    setActionError(null);
    await usageQuery.refetch();
  }

  async function invalidateUsage(updated?: RuleBindingDto) {
    await queryClient.invalidateQueries({
      queryKey: ruleDefinitionQueryKeys.usage(definitionKey, version),
    });
    if (
      updated?.definitionKey &&
      updated.definitionVersion != null &&
      (updated.definitionKey !== definitionKey || updated.definitionVersion !== version)
    ) {
      await queryClient.invalidateQueries({
        queryKey: ruleDefinitionQueryKeys.usage(updated.definitionKey, updated.definitionVersion),
      });
    }
  }

  const deleteMutation = useMutation({
    mutationFn: ({ id, revision }: { id: string; revision: number }) =>
      deleteRuleBinding(id, revision),
    onSuccess: async () => {
      setBindingToDelete(null);
      setActionError(null);
      await invalidateUsage();
    },
    onError: (error) => setActionError(classifyBindingError(error)),
  });

  const toggleMutation = useMutation({
    mutationFn: async ({
      id,
      revision,
      enabled,
    }: {
      id: string;
      revision: number;
      enabled: boolean;
    }) => {
      const current = await getRuleBinding(id);
      return updateRuleBinding(id, toUpdateRequest(current, revision, { enabled }));
    },
    onSuccess: async (updated) => {
      setActionError(null);
      await invalidateUsage(updated);
    },
    onError: (error) => setActionError(classifyBindingError(error)),
  });

  const editMutation = useMutation({
    mutationFn: ({ binding, next }: { binding: RuleBindingDto; next: BindingFormValue }) => {
      if (!binding.id || binding.revision == null) throw new Error('Missing binding identity.');
      return updateRuleBinding(binding.id, toUpdateRequestFromForm(binding.revision, next));
    },
    onSuccess: async (updated) => {
      if (updated.id) queryClient.setQueryData(['rule-binding', updated.id], updated);
      setEditError(null);
      setEditingId(null);
      setForm(null);
      await invalidateUsage(updated);
    },
    onError: (error) => setEditError(classifyBindingError(error)),
  });

  const usages = usageQuery.data ?? [];
  const mutationPending =
    deleteMutation.isPending || toggleMutation.isPending || editMutation.isPending;

  return (
    <div data-slot="rule-binding-usage" className="space-y-4">
      <p className="text-sm leading-relaxed text-muted-foreground">
        {t('rules.bindingUsageDescription')}
      </p>
      {usageQuery.isLoading ? <p role="status">{t('rules.bindingUsageLoading')}</p> : null}
      {usageQuery.isError ? (
        <p role="alert" className="text-sm text-destructive">
          {t('rules.bindingUsageError')}
        </p>
      ) : null}
      {actionError ? (
        <StatusNotice tone={actionError === 'conflict' ? 'warning' : 'destructive'}>
          {actionError === 'conflict'
            ? t('rules.bindingConflict')
            : t('rules.bindingMutationError')}{' '}
          <Button type="button" variant="link" onClick={() => void refreshUsage()}>
            {t('rules.bindingRefresh')}
          </Button>
        </StatusNotice>
      ) : null}
      {!usageQuery.isLoading && !usageQuery.isError && usages.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('rules.bindingUsageEmpty')}</p>
      ) : null}
      {usages.length > 0 ? (
        <ul className="divide-y divide-border">
          {usages.map((usage) => (
            <li key={usage.bindingId} className="space-y-3 py-4 first:pt-0 last:pb-0">
              <div className="flex flex-wrap items-center gap-2">
                <MetadataTag>{usage.targetType ?? '—'}</MetadataTag>
                <StatusBadge tone={usage.enabled ? 'success' : 'muted'}>
                  {usage.enabled ? t('rules.bindingEnabled') : t('rules.bindingDisabled')}
                </StatusBadge>
              </div>
              <dl className="grid gap-3 text-sm sm:grid-cols-2">
                <UsageFact label={t('rules.bindingTarget')} value={usage.targetId ?? '—'} />
                <UsageFact
                  label={t('rules.bindingTrigger')}
                  value={usage.useCaseOrTrigger ?? '—'}
                />
                <UsageFact label={t('rules.bindingPriority')} value={String(usage.priority ?? 0)} />
                <UsageFact label={t('rules.bindingId')} value={usage.bindingId ?? '—'} />
              </dl>
              <div className="flex flex-wrap gap-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={!usage.bindingId || usage.revision == null || mutationPending}
                  onClick={() => {
                    if (!usage.bindingId || usage.revision == null) return;
                    setActionError(null);
                    toggleMutation.mutate({
                      id: usage.bindingId,
                      revision: usage.revision,
                      enabled: !usage.enabled,
                    });
                  }}
                >
                  {usage.enabled ? t('rules.bindingDisable') : t('rules.bindingEnable')}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={!usage.bindingId || mutationPending}
                  onClick={() => {
                    if (!usage.bindingId) return;
                    setEditError(null);
                    setForm(null);
                    setEditingId(usage.bindingId);
                  }}
                >
                  <Pencil aria-hidden />
                  {t('rules.bindingEdit')}
                </Button>
                <Button
                  type="button"
                  variant="destructive"
                  size="sm"
                  disabled={!usage.bindingId || usage.revision == null || mutationPending}
                  onClick={() => {
                    if (usage.bindingId && usage.revision != null) {
                      setActionError(null);
                      setBindingToDelete({ id: usage.bindingId, revision: usage.revision });
                    }
                  }}
                >
                  <Trash2 aria-hidden />
                  {t('rules.bindingRemove')}
                </Button>
              </div>
            </li>
          ))}
        </ul>
      ) : null}

      <AlertDialog
        open={bindingToDelete !== null}
        onOpenChange={(nextOpen) => {
          if (!nextOpen && !deleteMutation.isPending) setBindingToDelete(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('rules.bindingRemoveTitle')}</AlertDialogTitle>
            <AlertDialogDescription>{t('rules.bindingRemoveDescription')}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleteMutation.isPending}>
              {t('app.cancel')}
            </AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              disabled={deleteMutation.isPending}
              onClick={() => {
                if (bindingToDelete) deleteMutation.mutate(bindingToDelete);
              }}
            >
              {t('rules.bindingRemove')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <Dialog
        open={editingId !== null}
        onOpenChange={(nextOpen) => {
          if (!nextOpen && !editMutation.isPending) {
            setEditingId(null);
            setForm(null);
            setEditError(null);
          }
        }}
      >
        <DialogContent className="max-h-screen overflow-y-auto sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>{t('rules.bindingEdit')}</DialogTitle>
            <DialogDescription>{t('rules.bindingEditDescription')}</DialogDescription>
          </DialogHeader>
          {bindingQuery.isLoading ? <p role="status">{t('rules.bindingUsageLoading')}</p> : null}
          {bindingQuery.isError ? (
            <StatusNotice tone="destructive">{t('rules.bindingUsageError')}</StatusNotice>
          ) : null}
          {editError ? (
            <StatusNotice tone={editError === 'conflict' ? 'warning' : 'destructive'}>
              {editError === 'conflict'
                ? t('rules.bindingConflict')
                : t('rules.bindingMutationError')}{' '}
              {editError === 'conflict' ? (
                <Button
                  type="button"
                  variant="link"
                  onClick={() => {
                    setEditError(null);
                    void bindingQuery.refetch();
                    void usageQuery.refetch();
                  }}
                >
                  {t('rules.bindingRefresh')}
                </Button>
              ) : null}
            </StatusNotice>
          ) : null}
          {form && bindingQuery.data ? (
            <BindingEditForm form={form} onChange={setForm} inputs={inputDefinitions} />
          ) : null}
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              disabled={editMutation.isPending}
              onClick={() => {
                setEditingId(null);
                setForm(null);
                setEditError(null);
              }}
            >
              {t('app.cancel')}
            </Button>
            <Button
              type="button"
              disabled={!form || !bindingQuery.data || editMutation.isPending}
              onClick={() => {
                if (form && bindingQuery.data) {
                  setEditError(null);
                  editMutation.mutate({ binding: bindingQuery.data, next: form });
                }
              }}
            >
              {t('rules.bindingSave')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function BindingEditForm({
  form,
  onChange,
  inputs,
}: {
  form: BindingFormValue;
  onChange: (next: BindingFormValue) => void;
  inputs: RuleInputDefinitionDto[];
}) {
  const { t } = useTranslation();
  const mappingKeys = Array.from(
    new Set([
      ...inputs.flatMap((input) => (input.key ? [input.key] : [])),
      ...Object.keys(form.inputMappings),
    ]),
  );
  const update = <K extends keyof BindingFormValue>(key: K, value: BindingFormValue[K]) =>
    onChange({ ...form, [key]: value });

  return (
    <div className="space-y-5">
      <div className="grid gap-4 sm:grid-cols-2">
        <BindingTextField
          id="binding-definition-key"
          label={t('rules.bindingDefinitionKey')}
          value={form.definitionKey}
          onChange={(value) => update('definitionKey', value)}
        />
        <BindingTextField
          id="binding-definition-version"
          label={t('rules.bindingVersion')}
          value={form.definitionVersion}
          type="number"
          onChange={(value) => update('definitionVersion', value)}
        />
        <BindingTextField
          id="binding-target-type"
          label={t('rules.bindingTargetType')}
          value={form.targetType}
          onChange={(value) => update('targetType', value)}
        />
        <BindingTextField
          id="binding-target-id"
          label={t('rules.bindingTargetId')}
          value={form.targetId}
          onChange={(value) => update('targetId', value)}
        />
        <BindingTextField
          id="binding-trigger"
          label={t('rules.bindingTrigger')}
          value={form.useCaseOrTrigger}
          onChange={(value) => update('useCaseOrTrigger', value)}
        />
        <BindingTextField
          id="binding-priority"
          label={t('rules.bindingPriority')}
          value={form.priority}
          type="number"
          onChange={(value) => update('priority', value)}
        />
        <Field>
          <FieldLabel htmlFor="binding-failure-behavior">
            {t('rules.bindingFailureBehavior')}
          </FieldLabel>
          <Select
            value={form.failureBehavior}
            onValueChange={(value) =>
              update('failureBehavior', value as RuleBindingFailureBehavior)
            }
          >
            <SelectTrigger id="binding-failure-behavior">
              <SelectValue>
                {form.failureBehavior === 'FailClosed'
                  ? t('rules.bindingFailClosed')
                  : t('rules.bindingFailOpen')}
              </SelectValue>
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="FailClosed">{t('rules.bindingFailClosed')}</SelectItem>
              <SelectItem value="FailOpen">{t('rules.bindingFailOpen')}</SelectItem>
            </SelectContent>
          </Select>
        </Field>
        <Field orientation="horizontal" className="self-end pb-2">
          <Checkbox
            id="binding-enabled"
            checked={form.enabled}
            onCheckedChange={(checked) => update('enabled', checked === true)}
          />
          <FieldLabel htmlFor="binding-enabled">{t('rules.bindingEnabled')}</FieldLabel>
        </Field>
      </div>

      <fieldset className="space-y-3 rounded-lg border p-4">
        <legend className="px-1 text-sm font-semibold">{t('rules.bindingInputMappings')}</legend>
        {mappingKeys.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('rules.bindingMappingsEmpty')}</p>
        ) : null}
        {mappingKeys.map((key) => {
          const mapping = form.inputMappings[key] ?? { kind: 'Unmapped', value: '' };
          return (
            <div key={key} className="grid gap-3 rounded-md border p-3 sm:grid-cols-2">
              <Field>
                <FieldLabel htmlFor={`binding-mapping-${key}`}>{key}</FieldLabel>
                <Select
                  value={mapping.kind}
                  onValueChange={(value) =>
                    onChange({
                      ...form,
                      inputMappings: {
                        ...form.inputMappings,
                        [key]: { kind: value as MappingFormValue['kind'], value: '' },
                      },
                    })
                  }
                >
                  <SelectTrigger id={`binding-mapping-${key}`}>
                    <SelectValue>
                      {mapping.kind === 'Context'
                        ? t('rules.bindingMappingContext')
                        : mapping.kind === 'Literal'
                          ? t('rules.bindingMappingLiteral')
                          : t('rules.bindingMappingUnmapped')}
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Unmapped">{t('rules.bindingMappingUnmapped')}</SelectItem>
                    <SelectItem value="Context">{t('rules.bindingMappingContext')}</SelectItem>
                    <SelectItem value="Literal">{t('rules.bindingMappingLiteral')}</SelectItem>
                  </SelectContent>
                </Select>
              </Field>
              {mapping.kind !== 'Unmapped' ? (
                <BindingTextField
                  id={`binding-mapping-value-${key}`}
                  label={
                    mapping.kind === 'Context'
                      ? t('rules.bindingContextKey')
                      : t('rules.bindingLiteralValues')
                  }
                  value={mapping.value}
                  onChange={(value) =>
                    onChange({
                      ...form,
                      inputMappings: {
                        ...form.inputMappings,
                        [key]: { ...mapping, value },
                      },
                    })
                  }
                />
              ) : null}
            </div>
          );
        })}
      </fieldset>
    </div>
  );
}

function BindingTextField({
  id,
  label,
  value,
  type = 'text',
  onChange,
}: {
  id: string;
  label: string;
  value: string;
  type?: 'text' | 'number';
  onChange: (value: string) => void;
}) {
  return (
    <Field>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      <Input id={id} type={type} value={value} onChange={(event) => onChange(event.target.value)} />
    </Field>
  );
}

function toBindingForm(
  binding: RuleBindingDto,
  inputs: RuleInputDefinitionDto[],
): BindingFormValue {
  const inputMappings: Record<string, MappingFormValue> = {};
  for (const key of new Set([
    ...inputs.flatMap((input) => (input.key ? [input.key] : [])),
    ...Object.keys(binding.inputMappings ?? {}),
  ])) {
    const mapping = binding.inputMappings?.[key];
    inputMappings[key] = mapping ? toMappingForm(mapping) : { kind: 'Unmapped', value: '' };
  }
  return {
    definitionKey: binding.definitionKey ?? '',
    definitionVersion: String(binding.definitionVersion ?? ''),
    targetType: binding.targetType ?? '',
    targetId: binding.targetId ?? '',
    useCaseOrTrigger: binding.useCaseOrTrigger ?? '',
    priority: String(binding.priority ?? 0),
    enabled: binding.enabled ?? true,
    failureBehavior: binding.failureBehavior ?? 'FailClosed',
    inputMappings,
  };
}

function toMappingForm(mapping: RuleInputMappingDto): MappingFormValue {
  if (mapping.kind === 'Context') {
    return { kind: 'Context', value: mapping.contextKey ?? '' };
  }
  return { kind: 'Literal', value: (mapping.literalValues ?? []).join(', ') };
}

function toUpdateRequestFromForm(
  expectedRevision: number,
  form: BindingFormValue,
): UpdateRuleBindingRequest {
  const inputMappings: Record<string, RuleInputMappingDto> = {};
  for (const [key, mapping] of Object.entries(form.inputMappings)) {
    if (mapping.kind === 'Unmapped') continue;
    inputMappings[key] =
      mapping.kind === 'Context'
        ? { kind: 'Context', contextKey: mapping.value.trim(), literalValues: [] }
        : {
            kind: 'Literal',
            contextKey: null,
            literalValues: mapping.value
              .split(',')
              .map((value) => value.trim())
              .filter(Boolean),
          };
  }
  return {
    expectedRevision,
    definitionKey: form.definitionKey.trim(),
    definitionVersion: Number(form.definitionVersion),
    targetType: form.targetType.trim(),
    targetId: form.targetId.trim(),
    useCaseOrTrigger: form.useCaseOrTrigger.trim(),
    inputMappings,
    priority: Number(form.priority),
    enabled: form.enabled,
    failureBehavior: form.failureBehavior,
  };
}

function toUpdateRequest(
  binding: RuleBindingDto,
  expectedRevision: number,
  overrides: Partial<UpdateRuleBindingRequest> = {},
): UpdateRuleBindingRequest {
  return {
    expectedRevision,
    definitionKey: binding.definitionKey,
    definitionVersion: binding.definitionVersion,
    targetType: binding.targetType,
    targetId: binding.targetId,
    useCaseOrTrigger: binding.useCaseOrTrigger,
    inputMappings: binding.inputMappings,
    priority: binding.priority,
    enabled: binding.enabled,
    failureBehavior: binding.failureBehavior,
    ...overrides,
  };
}

function classifyBindingError(error: unknown): Exclude<BindingActionError, null> {
  return error instanceof ApiError && (error.status === 409 || error.status === 412)
    ? 'conflict'
    : 'request';
}

function UsageFact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="break-words font-medium text-foreground">{value}</dd>
    </div>
  );
}
