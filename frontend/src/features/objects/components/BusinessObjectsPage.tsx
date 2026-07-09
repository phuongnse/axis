import { zodResolver } from '@hookform/resolvers/zod';
import { type QueryClient, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  AlertCircle,
  ArrowDown,
  ArrowUp,
  CheckCircle2,
  FilePlus2,
  ListChecks,
  LockKeyhole,
  Maximize2,
  Minimize2,
  Plus,
  Trash2,
  UploadCloud,
} from 'lucide-react';
import { type ReactNode, useEffect, useMemo, useRef, useState } from 'react';
import { type UseFormSetError, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardFooter, CardHeader, CardTitle } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Item,
  ItemActions,
  ItemContent,
  ItemDescription,
  ItemHeader,
  ItemTitle,
} from '@/components/ui/item';
import { NativeSelect, NativeSelectOption } from '@/components/ui/native-select';
import { Textarea } from '@/components/ui/textarea';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import {
  compareFieldRuleDefinitions,
  type FieldRuleDefinition,
  fieldRuleDefinitionKeys,
  fieldRuleDefinitionsListQueryOptions,
  fieldRuleDescriptionTranslationKey,
  fieldRuleNameTranslationKey,
  fieldTypeTranslationKey,
} from '@/features/rules';
import { defaultDebounceMs, useDebouncedValue } from '@/hooks/use-debounced-value';
import {
  defaultMinimumPendingIndicatorMs,
  useMinimumVisiblePending,
} from '@/hooks/use-minimum-visible-pending';
import { ApiError } from '@/lib/api';
import { cn } from '@/lib/utils';
import {
  createObjectDefinition,
  type ObjectDefinitionDetail,
  type ObjectFieldDefinitionInput,
  type ObjectFieldRuleInput,
  type ObjectFieldType,
  objectDefinitionDetailQueryOptions,
  objectDefinitionQueryKeys,
  objectDefinitionsDefaultPageSize,
  objectDefinitionsListQueryOptions,
  publishObjectDefinition,
  saveUnpublishedObjectDefinition,
} from '../api';

const keyPattern = /^[a-z][a-z0-9_]{0,62}$/;
const autosaveDebounceMs = defaultDebounceMs;
const autosaveSavingMinimumMs = defaultMinimumPendingIndicatorMs;
const supportedFieldTypes = [
  'Text',
  'Integer',
  'Decimal',
  'Date',
  'Boolean',
  'SingleSelect',
] as const satisfies readonly ObjectFieldType[];

const identitySchema = z.object({
  name: z.string().trim().min(1, 'objects.validationName'),
});

type IdentityFormValues = z.infer<typeof identitySchema>;

interface EditableField {
  clientId: string;
  fieldKey: string;
  label: string;
  fieldType: ObjectFieldType;
  required: boolean;
  minNumber: string;
  maxNumber: string;
  minDate: string;
  maxDate: string;
  minLength: string;
  maxLength: string;
  pattern: string;
  optionsText: string;
}

type FeedbackScope = 'identity' | 'fields' | 'publish';

interface FeedbackState {
  scope: FeedbackScope;
  variant: 'success' | 'destructive';
  titleKey: string;
  detail?: string;
}

interface EditableFieldErrors {
  fieldKey?: string;
  label?: string;
  fieldType?: string;
  minNumber?: string;
  maxNumber?: string;
  minDate?: string;
  maxDate?: string;
  minLength?: string;
  maxLength?: string;
  pattern?: string;
  optionsText?: string;
}

type EditableFieldInput = keyof EditableFieldErrors;
type FieldInputTouches = Partial<Record<EditableFieldInput, true>>;
type TouchedFieldInputs = Record<string, FieldInputTouches>;
type ObjectFieldRuleContract = NonNullable<
  NonNullable<ObjectDefinitionDetail['fields']>[number]['rules']
>[number];

interface DefinitionSnapshotField {
  fieldKey: string;
  label: string;
  fieldType: ObjectFieldType;
  required: boolean;
  minNumber: string;
  maxNumber: string;
  minDate: string;
  maxDate: string;
  minLength: string;
  maxLength: string;
  pattern: string;
  optionsText: string;
}

interface DefinitionSnapshot {
  name: string;
  fields: DefinitionSnapshotField[];
}

interface FieldValidationResult {
  messages: string[];
  formMessages: string[];
  fieldErrors: Record<string, EditableFieldErrors>;
}

export function BusinessObjectsPage() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [selectedDefinition, setSelectedDefinition] = useState<ObjectDefinitionDetail | null>(null);
  const [fields, setFields] = useState<EditableField[]>([]);
  const [feedback, setFeedback] = useState<FeedbackState | null>(null);
  const [fieldValidationMode, setFieldValidationMode] = useState<'save' | 'publish' | null>(null);
  const [validatedFieldIds, setValidatedFieldIds] = useState<string[] | null>(null);
  const [touchedFieldInputs, setTouchedFieldInputs] = useState<TouchedFieldInputs>({});
  const [fieldsExpanded, setFieldsExpanded] = useState(false);
  const [savedSnapshot, setSavedSnapshot] = useState<DefinitionSnapshot | null>(null);
  const [hasAutosaveActivity, setHasAutosaveActivity] = useState(false);

  const identityForm = useForm<IdentityFormValues>({
    resolver: zodResolver(identitySchema),
    defaultValues: { name: '' },
  });
  const watchedName = identityForm.watch('name');
  const currentSnapshot = useMemo(
    () => toDefinitionSnapshot(watchedName, fields),
    [fields, watchedName],
  );
  const debouncedSnapshot = useDebouncedValue(currentSnapshot, autosaveDebounceMs);
  const currentSnapshotRef = useRef<DefinitionSnapshot>(currentSnapshot);

  useEffect(() => {
    currentSnapshotRef.current = currentSnapshot;
  }, [currentSnapshot]);

  const listQuery = useQuery(objectDefinitionsListQueryOptions(page));
  const fieldRuleDefinitionsQuery = useQuery(fieldRuleDefinitionsListQueryOptions());
  const detailQuery = useQuery({
    ...objectDefinitionDetailQueryOptions(selectedId ?? ''),
    enabled: Boolean(selectedId),
  });

  const createMutation = useMutation({
    mutationFn: createObjectDefinition,
    onSuccess: (definition) => {
      setSelectedId(definition.id ?? null);
      setSelectedDefinition(definition);
      const definitionFields = fromDefinitionFields(definition);
      setFields(definitionFields);
      identityForm.reset({
        name: definition.name ?? '',
      });
      setSavedSnapshot(toDefinitionSnapshot(definition.name ?? '', definitionFields));
      setHasAutosaveActivity(false);
      setTouchedFieldInputs({});
      setFeedback({ scope: 'identity', variant: 'success', titleKey: 'objects.created' });
      clearFieldValidation();
      cacheObjectDefinitionResult(queryClient, definition);
    },
    onError: (error) => {
      const hasIdentityError = applyIdentityApiErrors(error, identityForm.setError);
      setFeedback(apiErrorFeedback(error, 'identity', hasIdentityError ? ['name'] : []));
    },
  });

  const saveMutation = useMutation({
    mutationFn: ({
      id,
      request,
    }: {
      id: string;
      request: Parameters<typeof saveUnpublishedObjectDefinition>[1];
      snapshot: DefinitionSnapshot;
    }) => saveUnpublishedObjectDefinition(id, request),
    onSuccess: (definition, variables) => {
      setSelectedId(definition.id ?? null);
      setSelectedDefinition(definition);
      const definitionFields = fromDefinitionFields(definition);
      const serverSnapshot = toDefinitionSnapshot(definition.name ?? '', definitionFields);
      setSavedSnapshot(serverSnapshot);
      if (definitionSnapshotsEqual(currentSnapshotRef.current, variables.snapshot)) {
        setFields(definitionFields);
        identityForm.reset({
          name: definition.name ?? '',
        });
        setHasAutosaveActivity(true);
        setTouchedFieldInputs({});
        setFeedback(null);
        clearFieldValidation();
      } else {
        setHasAutosaveActivity(false);
      }
      cacheObjectDefinitionResult(queryClient, definition);
    },
    onError: (error) => {
      const hasIdentityError = applyIdentityApiErrors(error, identityForm.setError);
      setFeedback(
        apiErrorFeedback(
          error,
          hasIdentityError ? 'identity' : 'fields',
          hasIdentityError ? ['name'] : [],
        ),
      );
    },
  });

  const publishMutation = useMutation({
    mutationFn: ({ id, expectedRevision }: { id: string; expectedRevision: number }) =>
      publishObjectDefinition(id, { expectedRevision }),
    onSuccess: (definition) => {
      setSelectedId(definition.id ?? null);
      setSelectedDefinition(definition);
      const definitionFields = fromDefinitionFields(definition);
      setFields(definitionFields);
      setSavedSnapshot(toDefinitionSnapshot(definition.name ?? '', definitionFields));
      setHasAutosaveActivity(false);
      setTouchedFieldInputs({});
      setFeedback({ scope: 'publish', variant: 'success', titleKey: 'objects.published' });
      clearFieldValidation();
      cacheObjectDefinitionResult(queryClient, definition);
    },
    onError: (error) => setFeedback(apiErrorFeedback(error, 'publish')),
  });
  const showSavingStatus = useMinimumVisiblePending(saveMutation.isPending, {
    minimumMs: autosaveSavingMinimumMs,
  });

  useEffect(() => {
    if (!selectedDefinition) return;
    identityForm.reset({
      name: selectedDefinition.name ?? '',
    });
  }, [identityForm, selectedDefinition]);

  useEffect(() => {
    if (!detailQuery.data) return;
    if (detailQuery.data.id && detailQuery.data.id === selectedDefinition?.id) return;

    setSelectedDefinition(detailQuery.data);
    const definitionFields = fromDefinitionFields(detailQuery.data);
    setFields(definitionFields);
    identityForm.reset({
      name: detailQuery.data.name ?? '',
    });
    setSavedSnapshot(toDefinitionSnapshot(detailQuery.data.name ?? '', definitionFields));
    setHasAutosaveActivity(false);
    setTouchedFieldInputs({});
  }, [detailQuery.data, identityForm, selectedDefinition?.id]);

  const totalCount = listQuery.data?.totalCount ?? 0;
  const fieldRuleDefinitions = fieldRuleDefinitionsQuery.data ?? [];
  const totalPages = Math.max(1, Math.ceil(totalCount / objectDefinitionsDefaultPageSize));
  const selectedStatus = selectedDefinition?.status ?? 'Unpublished';
  const isPublished = selectedStatus === 'Published';
  const busy = createMutation.isPending || saveMutation.isPending || publishMutation.isPending;
  const addFieldUnavailable = !selectedDefinition || isPublished;
  const editorSubmitUnavailable = busy || isPublished;
  const publishValidation = selectedDefinition
    ? validateFields(fields, t, { requireFields: true })
    : emptyFieldValidationResult();
  const hasUnsavedChanges = Boolean(
    selectedDefinition &&
      savedSnapshot &&
      !definitionSnapshotsEqual(currentSnapshot, savedSnapshot),
  );
  const autosaveIdentityResult = identitySchema.safeParse({ name: currentSnapshot.name });
  const autosaveValidation = selectedDefinition
    ? validateFields(fields, t, { requireFields: false })
    : emptyFieldValidationResult();
  const canAutosaveCurrentSnapshot = Boolean(
    selectedDefinition &&
      savedSnapshot &&
      hasUnsavedChanges &&
      autosaveIdentityResult.success &&
      autosaveValidation.messages.length === 0,
  );
  const showAutosavePending = canAutosaveCurrentSnapshot || showSavingStatus;
  const publishUnavailable =
    !selectedDefinition ||
    busy ||
    isPublished ||
    hasUnsavedChanges ||
    showAutosavePending ||
    publishValidation.messages.length > 0;
  const activeFieldValidation = fieldValidationMode
    ? validateFields(fields, t, {
        requireFields: fieldValidationMode === 'publish',
        fieldIds: validatedFieldIds,
        fieldTouches: fieldValidationMode === 'save' ? touchedFieldInputs : undefined,
      })
    : emptyFieldValidationResult();
  const identityFeedback = feedback?.scope === 'identity' ? feedback : null;
  const fieldsFeedback =
    feedback?.scope === 'fields'
      ? currentValidationFeedback(feedback, fieldValidationMode === 'save', activeFieldValidation)
      : null;
  const publishFeedback =
    !isPublished && feedback?.scope === 'publish'
      ? currentValidationFeedback(
          feedback,
          fieldValidationMode === 'publish',
          activeFieldValidation,
        )
      : null;
  const objectKeyPreview = selectedDefinition?.objectKey ?? deriveObjectKey(watchedName);
  const autosaveStatusKind = showAutosavePending
    ? 'pending'
    : hasAutosaveActivity && !publishUnavailable
      ? 'saved'
      : 'idle';
  const autosaveStatusLabel =
    autosaveStatusKind === 'pending'
      ? t('objects.autosavePendingLabel')
      : autosaveStatusKind === 'saved'
        ? t('objects.autosaveSaved')
        : '';
  const autosaveStatusPlaceholder = t('objects.autosaveSaved');

  useEffect(() => {
    if (
      !selectedDefinition?.id ||
      selectedDefinition.revision == null ||
      isPublished ||
      !savedSnapshot
    ) {
      return;
    }

    if (!definitionSnapshotsEqual(currentSnapshot, debouncedSnapshot)) {
      return;
    }

    if (definitionSnapshotsEqual(debouncedSnapshot, savedSnapshot)) {
      return;
    }

    if (saveMutation.isPending) return;

    const definitionId = selectedDefinition.id;
    const expectedRevision = selectedDefinition.revision;
    const identityResult = identitySchema.safeParse({ name: debouncedSnapshot.name });
    const fullValidation = validateFields(fields, t, { requireFields: false });
    const visibleValidation = validateFields(fields, t, {
      requireFields: false,
      fieldTouches: touchedFieldInputs,
    });

    if (!identityResult.success || fullValidation.messages.length > 0) {
      if (!identityResult.success) {
        identityForm.setError('name', {
          type: 'client',
          message: 'objects.validationName',
        });
      } else {
        identityForm.clearErrors('name');
      }

      setHasAutosaveActivity(false);
      if (visibleValidation.messages.length > 0) {
        setFieldValidationMode('save');
      } else {
        setFieldValidationMode(null);
      }
      setValidatedFieldIds(null);
      setFeedback(
        visibleValidation.formMessages.length > 0
          ? {
              scope: 'fields',
              variant: 'destructive',
              titleKey: 'objects.validationTitle',
              detail: visibleValidation.formMessages.join(' '),
            }
          : null,
      );
      return;
    }

    identityForm.clearErrors('name');
    setFieldValidationMode(null);
    setValidatedFieldIds(null);
    setFeedback(null);
    setHasAutosaveActivity(true);
    saveMutation.mutate({
      id: definitionId,
      snapshot: debouncedSnapshot,
      request: {
        expectedRevision,
        name: identityResult.data.name,
        fields: toRequestFields(debouncedSnapshot),
      },
    });
  }, [
    currentSnapshot,
    debouncedSnapshot,
    fields,
    identityForm,
    isPublished,
    saveMutation.isPending,
    saveMutation.mutate,
    savedSnapshot,
    selectedDefinition?.id,
    selectedDefinition?.revision,
    t,
    touchedFieldInputs,
  ]);

  async function handleCreate(values: IdentityFormValues) {
    setFeedback(null);
    identityForm.clearErrors();
    createMutation.mutate({
      name: values.name,
    });
  }

  function handleNewDefinition() {
    setSelectedDefinition(null);
    setSelectedId(null);
    setFields([]);
    setFeedback(null);
    setSavedSnapshot(null);
    setHasAutosaveActivity(false);
    setTouchedFieldInputs({});
    clearFieldValidation();
    setFieldsExpanded(false);
    identityForm.reset({ name: '' });
  }

  function handleSelect(id: string | undefined) {
    if (!id) return;
    setSelectedId(id);
    setFeedback(null);
    setHasAutosaveActivity(false);
    setTouchedFieldInputs({});
    clearFieldValidation();
  }

  function prefetchDefinition(id: string | undefined) {
    if (!id) return;
    void queryClient.prefetchQuery(objectDefinitionDetailQueryOptions(id));
  }

  function prefetchPage(nextPage: number) {
    if (nextPage < 1 || nextPage > totalPages || nextPage === page) return;
    void queryClient.prefetchQuery(objectDefinitionsListQueryOptions(nextPage));
  }

  function addField() {
    setFields((current) => [
      ...current,
      {
        clientId: crypto.randomUUID(),
        fieldKey: '',
        label: '',
        ...defaultFieldConfiguration(),
      },
    ]);
  }

  function startFieldValidation(mode: 'save' | 'publish') {
    setFieldValidationMode(mode);
    setValidatedFieldIds(fields.map((field) => field.clientId));
  }

  function clearFieldValidation() {
    setFieldValidationMode(null);
    setValidatedFieldIds(null);
  }

  function updateField(clientId: string, patch: Partial<EditableField>) {
    const touchedInputs = Object.keys(patch).filter(isEditableFieldInput);
    if (touchedInputs.length > 0) {
      setTouchedFieldInputs((current) => ({
        ...current,
        [clientId]: {
          ...current[clientId],
          ...Object.fromEntries(touchedInputs.map((key) => [key, true])),
        },
      }));
    }
    setFields((current) =>
      current.map((field) => (field.clientId === clientId ? { ...field, ...patch } : field)),
    );
  }

  function removeField(clientId: string) {
    setTouchedFieldInputs((current) => omitRecordKey(current, clientId));
    setFields((current) => current.filter((field) => field.clientId !== clientId));
  }

  function moveField(clientId: string, direction: -1 | 1) {
    setFields((current) => {
      const index = current.findIndex((field) => field.clientId === clientId);
      const nextIndex = index + direction;
      if (index < 0 || nextIndex < 0 || nextIndex >= current.length) return current;

      const next = [...current];
      [next[index], next[nextIndex]] = [next[nextIndex], next[index]];
      return next;
    });
  }

  function handlePublish() {
    if (!selectedDefinition?.id || selectedDefinition.revision == null) return;

    const validation = validateFields(fields, t, { requireFields: true });
    startFieldValidation('publish');
    if (validation.messages.length > 0) {
      setFeedback(
        validation.formMessages.length > 0
          ? {
              scope: 'publish',
              variant: 'destructive',
              titleKey: 'objects.validationTitle',
              detail: validation.formMessages.join(' '),
            }
          : null,
      );
      return;
    }

    setFeedback(null);
    clearFieldValidation();
    publishMutation.mutate({
      id: selectedDefinition.id,
      expectedRevision: selectedDefinition.revision,
    });
  }

  return (
    <div
      className={cn(
        'flex h-full min-h-0 w-full min-w-0 flex-col gap-4 overflow-x-hidden p-4 sm:p-6 lg:p-8',
        fieldsExpanded ? 'overflow-hidden' : 'overflow-y-auto 2xl:overflow-hidden',
      )}
    >
      <div className="flex min-w-0 shrink-0 flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold text-foreground">{t('objects.title')}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t('objects.pageDescription')}</p>
        </div>
        <Button type="button" onClick={handleNewDefinition}>
          <FilePlus2 className="size-4" aria-hidden />
          {t('objects.new')}
        </Button>
      </div>

      <div
        className={cn(
          'grid min-w-0 gap-4',
          fieldsExpanded
            ? 'min-h-0 flex-1 grid-cols-1 overflow-hidden'
            : 'shrink-0 lg:grid-cols-[minmax(16rem,20rem)_minmax(0,1fr)] 2xl:min-h-0 2xl:flex-1 2xl:grid-cols-[minmax(16rem,20rem)_minmax(0,1fr)_minmax(17rem,20rem)] 2xl:overflow-hidden',
        )}
      >
        {fieldsExpanded ? null : (
          <Card
            size="sm"
            role="region"
            aria-labelledby="objects-list-title"
            className="min-h-0 min-w-0 gap-0 py-0"
          >
            <div className="flex items-center justify-between border-b border-border px-4 py-3">
              <div className="min-w-0">
                <h2 id="objects-list-title" className="text-sm font-semibold">
                  {t('objects.listTitle')}
                </h2>
                <p className="mt-1 text-xs text-muted-foreground">
                  {t('objects.definitionCount', { count: totalCount })}
                </p>
              </div>
            </div>

            <div className="min-h-64 flex-1 overflow-y-auto p-2 2xl:min-h-0">
              {listQuery.isLoading ? (
                <p className="px-2 py-3 text-sm text-muted-foreground">{t('app.saving')}</p>
              ) : null}
              {listQuery.isError ? (
                <Alert variant="destructive">
                  <AlertCircle className="size-4" aria-hidden />
                  <AlertTitle>{t('objects.loadError')}</AlertTitle>
                </Alert>
              ) : null}
              {!listQuery.isLoading && !listQuery.isError && totalCount === 0 ? (
                <div className="rounded-md border border-dashed border-border px-3 py-4">
                  <p className="text-sm font-medium text-foreground">{t('objects.emptyTitle')}</p>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t('objects.emptyDescription')}
                  </p>
                </div>
              ) : null}
              <div className="flex flex-col gap-1.5">
                {(listQuery.data?.items ?? []).map((definition) => {
                  const active =
                    definition.id === selectedId || definition.id === selectedDefinition?.id;
                  return (
                    <Item
                      key={definition.id}
                      variant="outline"
                      size="xs"
                      render={<Button type="button" variant="ghost" />}
                      aria-current={active ? 'true' : undefined}
                      onFocus={() => prefetchDefinition(definition.id)}
                      onMouseEnter={() => prefetchDefinition(definition.id)}
                      onClick={() => handleSelect(definition.id)}
                      className={cn(
                        'h-auto justify-start rounded-md bg-transparent text-left whitespace-normal hover:bg-accent hover:text-foreground dark:hover:bg-accent',
                        active && 'bg-accent',
                      )}
                    >
                      <ItemContent className="min-w-0">
                        <ItemHeader className="min-w-0">
                          <ItemTitle className="min-w-0 flex-1">
                            <span className="truncate">{definition.name}</span>
                          </ItemTitle>
                          {definition.latestPublishedVersionNumber ? (
                            <ItemActions className="shrink-0 text-xs text-muted-foreground">
                              {t('objects.latestVersion', {
                                version: definition.latestPublishedVersionNumber,
                              })}
                            </ItemActions>
                          ) : null}
                        </ItemHeader>
                        <ItemDescription className="flex min-w-0 items-center gap-2">
                          <span className="truncate">{definition.objectKey}</span>
                          <StatusBadge status={definition.status ?? 'Unpublished'} />
                        </ItemDescription>
                      </ItemContent>
                    </Item>
                  );
                })}
              </div>
            </div>

            <div className="flex items-center justify-between border-t border-border px-4 py-3 text-xs text-muted-foreground">
              <span>
                {page} / {totalPages}
              </span>
              <div className="flex gap-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  aria-label={t('objects.previousPage')}
                  title={t('objects.previousPage')}
                  disabled={page <= 1}
                  onFocus={() => prefetchPage(page - 1)}
                  onMouseEnter={() => prefetchPage(page - 1)}
                  onClick={() => setPage((current) => Math.max(1, current - 1))}
                >
                  <ArrowUp className="size-3.5" aria-hidden />
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  aria-label={t('objects.nextPage')}
                  title={t('objects.nextPage')}
                  disabled={page >= totalPages}
                  onFocus={() => prefetchPage(page + 1)}
                  onMouseEnter={() => prefetchPage(page + 1)}
                  onClick={() => setPage((current) => Math.min(totalPages, current + 1))}
                >
                  <ArrowDown className="size-3.5" aria-hidden />
                </Button>
              </div>
            </div>
          </Card>
        )}

        <Card
          size="sm"
          aria-labelledby="objects-editor-title"
          className={cn('min-h-0 min-w-0 gap-0 py-0', fieldsExpanded && 'h-full')}
        >
          <form
            id="objects-definition-form"
            aria-labelledby="objects-editor-title"
            className="flex h-full min-h-0 min-w-0 flex-col"
            onSubmit={
              selectedDefinition
                ? (event) => event.preventDefault()
                : identityForm.handleSubmit(handleCreate)
            }
            noValidate
          >
            <div className="flex shrink-0 flex-col gap-3 border-b border-border px-4 py-4 sm:flex-row sm:items-start sm:justify-between">
              <div className="min-w-0">
                <h2 id="objects-editor-title" className="text-sm font-semibold">
                  {selectedDefinition ? selectedDefinition.name : t('objects.defineTitle')}
                </h2>
                <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
                  {selectedDefinition
                    ? t('objects.editorDescription')
                    : t('objects.defineDescription')}
                </p>
              </div>
            </div>

            {!fieldsExpanded && identityFeedback ? (
              <div className="shrink-0 px-4 pt-4">
                <FeedbackAlert feedback={identityFeedback} />
              </div>
            ) : null}

            {fieldsExpanded ? null : (
              <div className="grid shrink-0 gap-4 p-4 sm:grid-cols-2">
                <Field data-invalid={Boolean(identityForm.formState.errors.name)}>
                  <FieldLabel htmlFor="object-name" required>
                    {t('objects.name')}
                  </FieldLabel>
                  <Input
                    id="object-name"
                    required
                    aria-describedby={
                      identityForm.formState.errors.name
                        ? 'object-name-help object-name-error'
                        : 'object-name-help'
                    }
                    aria-invalid={Boolean(identityForm.formState.errors.name)}
                    disabled={isPublished}
                    {...identityForm.register('name')}
                  />
                  <FieldError id="object-name-error">
                    {identityForm.formState.errors.name?.message
                      ? t(identityForm.formState.errors.name.message)
                      : null}
                  </FieldError>
                  <FieldDescription id="object-name-help">{t('objects.nameHelp')}</FieldDescription>
                </Field>
                <Field>
                  <FieldLabel htmlFor="object-key">{t('objects.objectKey')}</FieldLabel>
                  <div className="group/locked-control relative">
                    <Input
                      id="object-key"
                      value={objectKeyPreview}
                      readOnly
                      aria-readonly="true"
                      className="pr-8"
                    />
                    <LockedControlIndicator />
                  </div>
                  <FieldDescription>{t('objects.objectKeyHelp')}</FieldDescription>
                </Field>
              </div>
            )}

            <section
              aria-labelledby="objects-fields-title"
              className={cn(
                'flex flex-1 flex-col border-t border-border px-4 py-4',
                fieldsExpanded ? 'min-h-0' : 'min-h-56 2xl:min-h-0',
              )}
            >
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0 flex-1">
                  <h3 id="objects-fields-title" className="text-sm font-semibold">
                    {t('objects.fields')}
                  </h3>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {selectedDefinition ? t('objects.fieldsDescription') : t('objects.fieldsHelp')}
                  </p>
                </div>
                {selectedDefinition ? (
                  <FieldsExpansionButton
                    expanded={fieldsExpanded}
                    label={
                      fieldsExpanded
                        ? t('objects.restoreFieldsLayout')
                        : t('objects.expandFieldsEditor')
                    }
                    onToggle={() => setFieldsExpanded((current) => !current)}
                  />
                ) : null}
              </div>

              <div className="mt-3 flex justify-start">
                <DisabledActionHint
                  active={addFieldUnavailable}
                  label={t('objects.actionUnavailable')}
                >
                  <Button
                    type="button"
                    variant={addFieldUnavailable ? 'outline' : 'default'}
                    onClick={addField}
                    disabled={addFieldUnavailable}
                  >
                    <Plus className="size-4" aria-hidden />
                    {t('objects.addField')}
                  </Button>
                </DisabledActionHint>
              </div>

              {fieldsFeedback ? (
                <div className="mt-3">
                  <FeedbackAlert feedback={fieldsFeedback} />
                </div>
              ) : null}

              <div className="mt-4 flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto pr-1">
                {fields.map((field, index) => (
                  <FieldEditor
                    key={field.clientId}
                    field={field}
                    index={index}
                    isLast={index === fields.length - 1}
                    disabled={isPublished}
                    onChange={(patch) => updateField(field.clientId, patch)}
                    onMove={moveField}
                    onRemove={removeField}
                    errors={activeFieldValidation.fieldErrors[field.clientId]}
                    ruleDefinitions={fieldRuleDefinitions}
                    ruleCatalogLoading={fieldRuleDefinitionsQuery.isLoading}
                    ruleCatalogUnavailable={fieldRuleDefinitionsQuery.isError}
                  />
                ))}
                {fields.length === 0 ? (
                  <div className="flex w-full flex-col self-start rounded-lg border border-dashed border-border px-3 py-3 text-sm">
                    <p className="font-medium text-foreground">
                      {selectedDefinition
                        ? t('objects.noFieldsTitle')
                        : t('objects.fieldsLockedTitle')}
                    </p>
                    <p className="mt-1 max-w-xl text-muted-foreground">
                      {selectedDefinition
                        ? t('objects.noFieldsDescription')
                        : t('objects.fieldsLockedDescription')}
                    </p>
                  </div>
                ) : null}
              </div>
            </section>

            {!isPublished ? (
              <CardFooter className="flex shrink-0 flex-col items-stretch gap-3">
                {publishFeedback ? <FeedbackAlert feedback={publishFeedback} /> : null}
                <div className="flex shrink-0 flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                  {selectedDefinition ? (
                    <p
                      role={autosaveStatusLabel ? 'status' : undefined}
                      aria-live="polite"
                      aria-atomic="true"
                      className="min-h-5 min-w-20 text-xs font-medium text-muted-foreground"
                    >
                      {autosaveStatusKind === 'pending' ? (
                        <>
                          <span className="sr-only">{autosaveStatusLabel}</span>
                          <AutosavePendingDots />
                        </>
                      ) : (
                        <span
                          aria-hidden={autosaveStatusLabel ? undefined : true}
                          className={cn(!autosaveStatusLabel && 'invisible')}
                        >
                          {autosaveStatusLabel || autosaveStatusPlaceholder}
                        </span>
                      )}
                    </p>
                  ) : null}
                  <div className="flex flex-col gap-2 sm:ml-auto sm:flex-row sm:justify-end">
                    {!selectedDefinition ? (
                      <DisabledActionHint
                        active={editorSubmitUnavailable}
                        label={t('objects.actionUnavailable')}
                      >
                        <Button
                          type="submit"
                          form="objects-definition-form"
                          variant={editorSubmitUnavailable ? 'outline' : 'default'}
                          disabled={editorSubmitUnavailable}
                          className="w-full sm:w-auto"
                        >
                          <Plus className="size-4" aria-hidden />
                          {createMutation.isPending ? t('objects.creating') : t('objects.create')}
                        </Button>
                      </DisabledActionHint>
                    ) : null}
                    {selectedDefinition ? (
                      <DisabledActionHint
                        active={publishUnavailable}
                        label={t('objects.actionUnavailable')}
                      >
                        <Button
                          type="button"
                          variant={publishUnavailable ? 'outline' : 'default'}
                          disabled={publishUnavailable}
                          onClick={handlePublish}
                          className="w-full sm:w-auto"
                        >
                          <UploadCloud className="size-4" aria-hidden />
                          {publishMutation.isPending
                            ? t('objects.publishing')
                            : t('objects.publish')}
                        </Button>
                      </DisabledActionHint>
                    ) : null}
                  </div>
                </div>
              </CardFooter>
            ) : null}
          </form>
        </Card>

        {fieldsExpanded ? null : (
          <aside
            aria-label={t('objects.workflowLabel')}
            className="grid min-h-0 min-w-0 content-start gap-4 overflow-visible pr-1 md:grid-cols-2 lg:col-span-2 2xl:col-span-1 2xl:grid-cols-1 2xl:overflow-y-auto"
          >
            <Card
              size="sm"
              role="region"
              aria-labelledby="objects-workflow-title"
              className="min-w-0"
            >
              <CardHeader>
                <CardTitle id="objects-workflow-title">{t('objects.workflowLabel')}</CardTitle>
              </CardHeader>
              <CardContent>
                <ol className="text-sm" aria-label={t('objects.workflowLabel')}>
                  <WorkflowStep
                    index={1}
                    title={t('objects.workflowIdentityTitle')}
                    detail={t('objects.workflowIdentityDetail')}
                  />
                  <WorkflowStep
                    index={2}
                    title={t('objects.workflowFieldsTitle')}
                    detail={t('objects.workflowFieldsDetail')}
                  />
                  <WorkflowStep
                    index={3}
                    title={t('objects.workflowPublishTitle')}
                    detail={t('objects.workflowPublishDetail')}
                  />
                </ol>
              </CardContent>
            </Card>
          </aside>
        )}
      </div>
    </div>
  );
}

function FieldsExpansionButton({
  expanded,
  label,
  onToggle,
}: {
  expanded: boolean;
  label: string;
  onToggle: () => void;
}) {
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            aria-label={label}
            aria-pressed={expanded}
            onClick={onToggle}
          >
            {expanded ? (
              <Minimize2 className="size-4" aria-hidden />
            ) : (
              <Maximize2 className="size-4" aria-hidden />
            )}
          </Button>
        }
      />
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  );
}

function FeedbackAlert({ feedback }: { feedback: FeedbackState }) {
  const { t } = useTranslation();

  return (
    <Alert variant={feedback.variant === 'destructive' ? 'destructive' : 'default'}>
      {feedback.variant === 'success' ? (
        <CheckCircle2 className="size-4" aria-hidden />
      ) : (
        <AlertCircle className="size-4" aria-hidden />
      )}
      <AlertTitle>{t(feedback.titleKey)}</AlertTitle>
      {feedback.detail ? <AlertDescription>{feedback.detail}</AlertDescription> : null}
    </Alert>
  );
}

function currentValidationFeedback(
  feedback: FeedbackState,
  active: boolean,
  validation: FieldValidationResult,
): FeedbackState | null {
  if (!active) return feedback;
  if (validation.messages.length === 0) return null;
  if (validation.formMessages.length === 0) return null;

  return {
    ...feedback,
    detail: validation.formMessages.join(' '),
  };
}

function LockedControlIndicator() {
  return (
    <LockKeyhole
      className="pointer-events-none absolute right-2 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground transition-colors group-hover/locked-control:text-destructive group-focus-within/locked-control:text-destructive"
      aria-hidden
    />
  );
}

function DisabledActionHint({
  active,
  label,
  children,
}: {
  active: boolean;
  label: string;
  children: ReactNode;
}) {
  if (!active) return children;

  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <span className="block cursor-not-allowed rounded-lg" data-disabled-action-hint="true">
            {children}
          </span>
        }
      />
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  );
}

function WorkflowStep({ index, title, detail }: { index: number; title: string; detail: string }) {
  return (
    <li className="relative grid min-w-0 grid-cols-[1.75rem_minmax(0,1fr)] gap-3 pb-5 before:absolute before:left-3.5 before:top-7 before:bottom-0 before:w-px before:bg-border last:pb-0 last:before:hidden">
      <span className="relative z-10 flex size-7 shrink-0 items-center justify-center rounded-full border border-border bg-accent text-xs font-semibold text-accent-foreground shadow-sm dark:border-foreground/20 dark:bg-accent">
        {index}
      </span>
      <span className="min-w-0 pt-0.5">
        <span className="block font-medium text-foreground">{title}</span>
        <span className="mt-0.5 block text-muted-foreground">{detail}</span>
      </span>
    </li>
  );
}

function cacheObjectDefinitionResult(queryClient: QueryClient, definition: ObjectDefinitionDetail) {
  if (definition.id) {
    queryClient.setQueryData(objectDefinitionQueryKeys.detail(definition.id), definition);
  }

  void queryClient.invalidateQueries({ queryKey: objectDefinitionQueryKeys.lists() });
}

function deriveObjectKey(name: string) {
  if (!name.trim()) return '';

  const normalized = name
    .trim()
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/\u0111/g, 'd')
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '')
    .replace(/_{2,}/g, '_');

  const key = /^[a-z]/.test(normalized)
    ? normalized
    : normalized
      ? `object_${normalized}`
      : 'object';

  return key.slice(0, 63).replace(/_+$/g, '') || 'object';
}

function FieldEditor({
  field,
  index,
  isLast,
  disabled,
  onChange,
  onMove,
  onRemove,
  errors,
  ruleDefinitions,
  ruleCatalogLoading,
  ruleCatalogUnavailable,
}: {
  field: EditableField;
  index: number;
  isLast: boolean;
  disabled: boolean;
  onChange: (patch: Partial<EditableField>) => void;
  onMove: (clientId: string, direction: -1 | 1) => void;
  onRemove: (clientId: string) => void;
  errors?: EditableFieldErrors;
  ruleDefinitions: readonly FieldRuleDefinition[];
  ruleCatalogLoading: boolean;
  ruleCatalogUnavailable: boolean;
}) {
  const { t } = useTranslation();
  const prefix = `field-${field.clientId}`;
  const compatibleRules = compatibleRuleDefinitions(ruleDefinitions, field.fieldType);
  const displayedRules =
    compatibleRules.length > 0 ? compatibleRules : fallbackRuleDefinitions(field.fieldType);
  const configuredRuleCount = configuredFieldRuleCount(field);

  return (
    <div className="rounded-lg border border-border bg-background/45 p-3">
      <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_12rem_auto] lg:items-start">
        <Field data-invalid={Boolean(errors?.fieldKey)}>
          <FieldLabel htmlFor={`${prefix}-key`} required>
            {t('objects.fieldKey')}
          </FieldLabel>
          <Input
            id={`${prefix}-key`}
            value={field.fieldKey}
            required
            aria-invalid={Boolean(errors?.fieldKey)}
            aria-describedby={errors?.fieldKey ? `${prefix}-key-error` : undefined}
            disabled={disabled}
            onChange={(event) => onChange({ fieldKey: event.target.value })}
          />
          <FieldError id={`${prefix}-key-error`}>{errors?.fieldKey}</FieldError>
        </Field>
        <Field data-invalid={Boolean(errors?.label)}>
          <FieldLabel htmlFor={`${prefix}-label`} required>
            {t('objects.label')}
          </FieldLabel>
          <Input
            id={`${prefix}-label`}
            value={field.label}
            required
            aria-invalid={Boolean(errors?.label)}
            aria-describedby={errors?.label ? `${prefix}-label-error` : undefined}
            disabled={disabled}
            onChange={(event) => onChange({ label: event.target.value })}
          />
          <FieldError id={`${prefix}-label-error`}>{errors?.label}</FieldError>
        </Field>
        <Field data-invalid={Boolean(errors?.fieldType)}>
          <FieldLabel htmlFor={`${prefix}-type`} required>
            {t('objects.fieldType')}
          </FieldLabel>
          <NativeSelect
            id={`${prefix}-type`}
            value={field.fieldType}
            aria-invalid={Boolean(errors?.fieldType)}
            aria-describedby={
              errors?.fieldType ? `${prefix}-type-error ${prefix}-type-help` : `${prefix}-type-help`
            }
            disabled={disabled}
            onChange={(event) => onChange(fieldTypePatch(event.target.value as ObjectFieldType))}
            className="w-full"
          >
            {supportedFieldTypes.map((fieldType) => (
              <NativeSelectOption key={fieldType} value={fieldType}>
                {t(fieldTypeTranslationKey(fieldType))}
              </NativeSelectOption>
            ))}
          </NativeSelect>
          <FieldError id={`${prefix}-type-error`}>{errors?.fieldType}</FieldError>
          <FieldDescription id={`${prefix}-type-help`}>
            {t('objects.fieldTypeHelp')}
          </FieldDescription>
        </Field>
        <div className="flex items-start gap-1 lg:self-start lg:pt-7">
          <IconButton
            label={t('objects.moveUp')}
            disabled={disabled || index === 0}
            onClick={() => onMove(field.clientId, -1)}
          >
            <ArrowUp className="size-4" aria-hidden />
          </IconButton>
          <IconButton
            label={t('objects.moveDown')}
            disabled={disabled || isLast}
            onClick={() => onMove(field.clientId, 1)}
          >
            <ArrowDown className="size-4" aria-hidden />
          </IconButton>
          <IconButton
            label={t('objects.removeField')}
            disabled={disabled}
            onClick={() => onRemove(field.clientId)}
          >
            <Trash2 className="size-4" aria-hidden />
          </IconButton>
        </div>
      </div>
      <section
        aria-labelledby={`${prefix}-rules-title`}
        aria-describedby={`${prefix}-rules-help`}
        className="mt-3 rounded-lg border border-border bg-muted/20"
      >
        <div className="flex min-w-0 flex-col gap-3 border-b border-border px-3 py-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="min-w-0">
            <div className="flex min-w-0 items-center gap-2">
              <ListChecks className="size-4 shrink-0 text-muted-foreground" aria-hidden />
              <h4 id={`${prefix}-rules-title`} className="text-sm font-semibold text-foreground">
                {t('objects.fieldRulesTitle')}
              </h4>
            </div>
            <p id={`${prefix}-rules-help`} className="mt-1 text-sm text-muted-foreground">
              {t('objects.fieldRulesDescription', {
                fieldType: t(fieldTypeTranslationKey(field.fieldType)),
              })}
            </p>
          </div>
          <Badge variant={configuredRuleCount > 0 ? 'default' : 'outline'} className="rounded-md">
            {t('objects.configuredRulesCount', { count: configuredRuleCount })}
          </Badge>
        </div>

        {ruleCatalogUnavailable ? (
          <div className="px-3 pt-3">
            <Alert variant="destructive">
              <AlertCircle className="size-4" aria-hidden />
              <AlertTitle>{t('objects.rulesCatalogUnavailableTitle')}</AlertTitle>
              <AlertDescription>{t('objects.rulesCatalogUnavailableDescription')}</AlertDescription>
            </Alert>
          </div>
        ) : null}

        <div className="flex min-w-0 flex-col gap-2 border-b border-border px-3 py-3">
          <p className="text-xs font-medium text-muted-foreground">
            {t('objects.compatibleRules')}
          </p>
          <div className="flex flex-wrap gap-2">
            {ruleCatalogLoading && compatibleRules.length === 0 ? (
              <span className="text-sm text-muted-foreground">
                {t('objects.rulesCatalogLoading')}
              </span>
            ) : (
              displayedRules.map((definition) => (
                <Badge
                  key={definition.definitionKey}
                  variant="secondary"
                  className="rounded-md"
                  title={fieldRuleDescription(definition, t)}
                >
                  {fieldRuleDisplayName(definition, t)}
                </Badge>
              ))
            )}
          </div>
        </div>

        <div className="grid gap-3 p-3 sm:grid-cols-2 xl:grid-cols-3">
          <Field>
            <div className="flex items-start gap-2 pt-1">
              <Checkbox
                id={`${prefix}-required`}
                checked={field.required}
                onCheckedChange={(checked) => onChange({ required: checked === true })}
                disabled={disabled}
                aria-describedby={`${prefix}-required-help`}
              />
              <div className="min-w-0">
                <FieldLabel htmlFor={`${prefix}-required`} className="font-normal">
                  {t('objects.ruleRequired')}
                </FieldLabel>
                <FieldDescription id={`${prefix}-required-help`}>
                  {t('objects.ruleRequiredHelp')}
                </FieldDescription>
              </div>
            </div>
          </Field>
          {field.fieldType === 'Text' ? (
            <>
              <Field data-invalid={Boolean(errors?.minLength || errors?.maxLength)}>
                <FieldLabel htmlFor={`${prefix}-min-length`}>{t('objects.minLength')}</FieldLabel>
                <div className="grid grid-cols-2 gap-2">
                  <Input
                    id={`${prefix}-min-length`}
                    type="number"
                    inputMode="numeric"
                    min={0}
                    value={field.minLength}
                    aria-invalid={Boolean(errors?.minLength)}
                    aria-describedby={errors?.minLength ? `${prefix}-min-length-error` : undefined}
                    disabled={disabled}
                    onChange={(event) => onChange({ minLength: event.target.value })}
                  />
                  <Input
                    id={`${prefix}-max-length`}
                    type="number"
                    inputMode="numeric"
                    min={0}
                    value={field.maxLength}
                    aria-label={t('objects.maxLength')}
                    aria-invalid={Boolean(errors?.maxLength)}
                    aria-describedby={errors?.maxLength ? `${prefix}-max-length-error` : undefined}
                    disabled={disabled}
                    onChange={(event) => onChange({ maxLength: event.target.value })}
                  />
                </div>
                <FieldError id={`${prefix}-min-length-error`}>{errors?.minLength}</FieldError>
                <FieldError id={`${prefix}-max-length-error`}>{errors?.maxLength}</FieldError>
              </Field>
              <Field data-invalid={Boolean(errors?.pattern)}>
                <FieldLabel htmlFor={`${prefix}-pattern`}>{t('objects.pattern')}</FieldLabel>
                <Input
                  id={`${prefix}-pattern`}
                  value={field.pattern}
                  aria-invalid={Boolean(errors?.pattern)}
                  aria-describedby={errors?.pattern ? `${prefix}-pattern-error` : undefined}
                  disabled={disabled}
                  onChange={(event) => onChange({ pattern: event.target.value })}
                />
                <FieldError id={`${prefix}-pattern-error`}>{errors?.pattern}</FieldError>
              </Field>
            </>
          ) : null}
          {field.fieldType === 'Integer' || field.fieldType === 'Decimal' ? (
            <Field data-invalid={Boolean(errors?.minNumber || errors?.maxNumber)}>
              <FieldLabel htmlFor={`${prefix}-min-number`}>{t('objects.numericRange')}</FieldLabel>
              <div className="grid grid-cols-2 gap-2">
                <Input
                  id={`${prefix}-min-number`}
                  type="number"
                  inputMode={field.fieldType === 'Integer' ? 'numeric' : 'decimal'}
                  value={field.minNumber}
                  aria-label={t('objects.numericMin')}
                  aria-invalid={Boolean(errors?.minNumber)}
                  aria-describedby={errors?.minNumber ? `${prefix}-min-number-error` : undefined}
                  disabled={disabled}
                  onChange={(event) => onChange({ minNumber: event.target.value })}
                />
                <Input
                  id={`${prefix}-max-number`}
                  type="number"
                  inputMode={field.fieldType === 'Integer' ? 'numeric' : 'decimal'}
                  value={field.maxNumber}
                  aria-label={t('objects.numericMax')}
                  aria-invalid={Boolean(errors?.maxNumber)}
                  aria-describedby={errors?.maxNumber ? `${prefix}-max-number-error` : undefined}
                  disabled={disabled}
                  onChange={(event) => onChange({ maxNumber: event.target.value })}
                />
              </div>
              <FieldError id={`${prefix}-min-number-error`}>{errors?.minNumber}</FieldError>
              <FieldError id={`${prefix}-max-number-error`}>{errors?.maxNumber}</FieldError>
            </Field>
          ) : null}
          {field.fieldType === 'Date' ? (
            <Field data-invalid={Boolean(errors?.minDate || errors?.maxDate)}>
              <FieldLabel htmlFor={`${prefix}-min-date`}>{t('objects.dateRange')}</FieldLabel>
              <div className="grid grid-cols-2 gap-2">
                <Input
                  id={`${prefix}-min-date`}
                  type="date"
                  value={field.minDate}
                  aria-label={t('objects.dateMin')}
                  aria-invalid={Boolean(errors?.minDate)}
                  aria-describedby={errors?.minDate ? `${prefix}-min-date-error` : undefined}
                  disabled={disabled}
                  onChange={(event) => onChange({ minDate: event.target.value })}
                />
                <Input
                  id={`${prefix}-max-date`}
                  type="date"
                  value={field.maxDate}
                  aria-label={t('objects.dateMax')}
                  aria-invalid={Boolean(errors?.maxDate)}
                  aria-describedby={errors?.maxDate ? `${prefix}-max-date-error` : undefined}
                  disabled={disabled}
                  onChange={(event) => onChange({ maxDate: event.target.value })}
                />
              </div>
              <FieldError id={`${prefix}-min-date-error`}>{errors?.minDate}</FieldError>
              <FieldError id={`${prefix}-max-date-error`}>{errors?.maxDate}</FieldError>
            </Field>
          ) : null}
          {field.fieldType === 'SingleSelect' ? (
            <Field data-invalid={Boolean(errors?.optionsText)} className="sm:col-span-2">
              <FieldLabel htmlFor={`${prefix}-options`} required>
                {t('objects.options')}
              </FieldLabel>
              <Textarea
                id={`${prefix}-options`}
                value={field.optionsText}
                aria-invalid={Boolean(errors?.optionsText)}
                aria-describedby={
                  errors?.optionsText
                    ? `${prefix}-options-help ${prefix}-options-error`
                    : `${prefix}-options-help`
                }
                disabled={disabled}
                onChange={(event) => onChange({ optionsText: event.target.value })}
              />
              <FieldError id={`${prefix}-options-error`}>{errors?.optionsText}</FieldError>
              <FieldDescription id={`${prefix}-options-help`}>
                {t('objects.optionsHelp')}
              </FieldDescription>
            </Field>
          ) : null}
        </div>
      </section>
    </div>
  );
}

function IconButton({
  label,
  disabled,
  onClick,
  children,
}: {
  label: string;
  disabled: boolean;
  onClick: () => void;
  children: ReactNode;
}) {
  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      aria-label={label}
      title={label}
      disabled={disabled}
      onClick={onClick}
      className="size-8 px-0"
    >
      {children}
    </Button>
  );
}

function AutosavePendingDots() {
  return (
    <span aria-hidden="true" className="inline-flex min-h-5 items-center gap-1">
      <span className="size-[5px] animate-pulse rounded-full bg-muted-foreground/80 [animation-delay:0ms]" />
      <span className="size-[5px] animate-pulse rounded-full bg-muted-foreground/80 [animation-delay:150ms]" />
      <span className="size-[5px] animate-pulse rounded-full bg-muted-foreground/80 [animation-delay:300ms]" />
    </span>
  );
}

function StatusBadge({ status }: { status: string }) {
  const { t } = useTranslation();
  const published = status === 'Published';

  return (
    <Badge
      variant="outline"
      className={cn(
        published
          ? 'border-emerald-600/30 bg-emerald-500/15 text-emerald-800 dark:border-emerald-400/30 dark:bg-emerald-400/15 dark:text-emerald-200'
          : 'border-amber-500/35 bg-amber-400/20 text-amber-900 dark:border-amber-300/30 dark:bg-amber-300/15 dark:text-amber-100',
      )}
    >
      {published ? t('objects.published') : t('objects.unpublished')}
    </Badge>
  );
}

function fromDefinitionFields(definition: ObjectDefinitionDetail): EditableField[] {
  return (definition.fields ?? []).map((field) => ({
    clientId: field.id ?? crypto.randomUUID(),
    fieldKey: field.fieldKey ?? '',
    label: field.label ?? '',
    ...configurationFromRules(field.fieldType ?? 'Text', field.rules ?? []),
  }));
}

function toDefinitionSnapshot(name: string, fields: readonly EditableField[]): DefinitionSnapshot {
  return {
    name,
    fields: fields.map((field) => ({
      fieldKey: field.fieldKey,
      label: field.label,
      fieldType: field.fieldType,
      required: field.required,
      minNumber: field.minNumber,
      maxNumber: field.maxNumber,
      minDate: field.minDate,
      maxDate: field.maxDate,
      minLength: field.minLength,
      maxLength: field.maxLength,
      pattern: field.pattern,
      optionsText: field.optionsText,
    })),
  };
}

function toRequestFields(snapshot: DefinitionSnapshot): ObjectFieldDefinitionInput[] {
  return snapshot.fields.map((field) => ({
    fieldKey: field.fieldKey.trim(),
    label: field.label.trim(),
    fieldType: field.fieldType,
    rules: toRequestRules(field),
  }));
}

function definitionSnapshotsEqual(left: DefinitionSnapshot, right: DefinitionSnapshot): boolean {
  if (left.name !== right.name) return false;
  if (left.fields.length !== right.fields.length) return false;

  return left.fields.every((field, index) => {
    const other = right.fields[index];
    return (
      field.fieldKey === other?.fieldKey &&
      field.label === other?.label &&
      field.fieldType === other?.fieldType &&
      field.required === other?.required &&
      field.minNumber === other?.minNumber &&
      field.maxNumber === other?.maxNumber &&
      field.minDate === other?.minDate &&
      field.maxDate === other?.maxDate &&
      field.minLength === other?.minLength &&
      field.maxLength === other?.maxLength &&
      field.pattern === other?.pattern &&
      field.optionsText === other?.optionsText
    );
  });
}

function defaultFieldConfiguration(): Omit<EditableField, 'clientId' | 'fieldKey' | 'label'> {
  return {
    fieldType: 'Text',
    required: false,
    minNumber: '',
    maxNumber: '',
    minDate: '',
    maxDate: '',
    minLength: '',
    maxLength: '',
    pattern: '',
    optionsText: '',
  };
}

function configurationFromRules(
  fieldType: ObjectFieldType,
  rules: readonly ObjectFieldRuleContract[],
): Omit<EditableField, 'clientId' | 'fieldKey' | 'label'> {
  const base = fieldTypePatch(fieldType);
  const numericRange = findRule(rules, fieldRuleDefinitionKeys.numericRange);
  const dateRange = findRule(rules, fieldRuleDefinitionKeys.dateRange);
  const textLength = findRule(rules, fieldRuleDefinitionKeys.textLength);
  const textPattern = findRule(rules, fieldRuleDefinitionKeys.textPattern);
  const singleSelectOptions = findRule(rules, fieldRuleDefinitionKeys.singleSelectOptions);

  return {
    ...base,
    required: Boolean(findRule(rules, fieldRuleDefinitionKeys.required)),
    minNumber: ruleParameterValue(numericRange, 'min'),
    maxNumber: ruleParameterValue(numericRange, 'max'),
    minDate: ruleParameterValue(dateRange, 'min'),
    maxDate: ruleParameterValue(dateRange, 'max'),
    minLength: ruleParameterValue(textLength, 'min'),
    maxLength: ruleParameterValue(textLength, 'max'),
    pattern: ruleParameterValue(textPattern, 'pattern'),
    optionsText: (singleSelectOptions?.parameters?.options ?? []).join('\n'),
  };
}

function findRule(rules: readonly ObjectFieldRuleContract[], definitionKey: string) {
  return rules.find((rule) => rule.definitionKey === definitionKey);
}

function ruleParameterValue(rule: ObjectFieldRuleContract | undefined, key: string): string {
  return rule?.parameters?.[key]?.[0] ?? '';
}

function fieldTypePatch(fieldType: ObjectFieldType): Omit<
  EditableField,
  'clientId' | 'fieldKey' | 'label' | 'required'
> & {
  required?: boolean;
} {
  return {
    fieldType,
    minNumber: '',
    maxNumber: '',
    minDate: '',
    maxDate: '',
    minLength: '',
    maxLength: '',
    pattern: '',
    optionsText: '',
  };
}

function toRequestRules(field: DefinitionSnapshotField): ObjectFieldRuleInput[] {
  const rules: ObjectFieldRuleInput[] = [];

  if (field.required) {
    rules.push(fieldRule(fieldRuleDefinitionKeys.required));
  }

  if (field.fieldType === 'Text') {
    const minLength = optionalInteger(field.minLength);
    const maxLength = optionalInteger(field.maxLength);
    if (minLength !== null || maxLength !== null) {
      rules.push(
        fieldRule(
          fieldRuleDefinitionKeys.textLength,
          optionalRuleParameters([
            ['min', minLength],
            ['max', maxLength],
          ]),
        ),
      );
    }

    if (field.pattern.trim()) {
      rules.push(
        fieldRule(fieldRuleDefinitionKeys.textPattern, { pattern: [field.pattern.trim()] }),
      );
    }
  }

  if (field.fieldType === 'Integer' || field.fieldType === 'Decimal') {
    const minNumber = optionalNumber(field.minNumber);
    const maxNumber = optionalNumber(field.maxNumber);
    if (minNumber !== null || maxNumber !== null) {
      rules.push(
        fieldRule(
          fieldRuleDefinitionKeys.numericRange,
          optionalRuleParameters([
            ['min', minNumber],
            ['max', maxNumber],
          ]),
        ),
      );
    }
  }

  if (field.fieldType === 'Date' && (field.minDate || field.maxDate)) {
    rules.push(
      fieldRule(
        fieldRuleDefinitionKeys.dateRange,
        optionalRuleParameters([
          ['min', field.minDate || null],
          ['max', field.maxDate || null],
        ]),
      ),
    );
  }

  if (field.fieldType === 'SingleSelect') {
    rules.push(
      fieldRule(fieldRuleDefinitionKeys.singleSelectOptions, {
        options: optionValues(field.optionsText),
      }),
    );
  }

  return rules;
}

function fieldRule(
  definitionKey: string,
  parameters: NonNullable<ObjectFieldRuleInput['parameters']> = {},
): ObjectFieldRuleInput {
  return { definitionKey, parameters };
}

function optionalRuleParameters(
  values: readonly (readonly [string, string | number | null])[],
): NonNullable<ObjectFieldRuleInput['parameters']> {
  return Object.fromEntries(
    values
      .filter(([, value]) => value !== null)
      .map(([key, value]) => [key, [String(value)] as string[]]),
  );
}

function optionalNumber(value: string): number | null {
  const trimmed = value.trim();
  return trimmed ? Number(trimmed) : null;
}

function optionalInteger(value: string): number | null {
  const parsed = optionalNumber(value);
  return parsed === null ? null : Math.trunc(parsed);
}

function optionValues(value: string): string[] {
  return value
    .split(/\r?\n/)
    .map((option) => option.trim())
    .filter(Boolean);
}

function isEditableFieldInput(key: string): key is EditableFieldInput {
  return [
    'fieldKey',
    'label',
    'fieldType',
    'required',
    'minNumber',
    'maxNumber',
    'minDate',
    'maxDate',
    'minLength',
    'maxLength',
    'pattern',
    'optionsText',
  ].includes(key);
}

function compatibleRuleDefinitions(
  definitions: readonly FieldRuleDefinition[],
  fieldType: ObjectFieldType,
): FieldRuleDefinition[] {
  return definitions
    .filter((definition) => definition.supportedFieldTypes?.includes(fieldType))
    .sort(compareFieldRuleDefinitions);
}

function fallbackRuleDefinitions(fieldType: ObjectFieldType): FieldRuleDefinition[] {
  const keys: string[] = [fieldRuleDefinitionKeys.required];

  if (fieldType === 'Text') {
    keys.push(fieldRuleDefinitionKeys.textLength, fieldRuleDefinitionKeys.textPattern);
  }

  if (fieldType === 'Integer' || fieldType === 'Decimal') {
    keys.push(fieldRuleDefinitionKeys.numericRange);
  }

  if (fieldType === 'Date') {
    keys.push(fieldRuleDefinitionKeys.dateRange);
  }

  if (fieldType === 'SingleSelect') {
    keys.push(fieldRuleDefinitionKeys.singleSelectOptions);
  }

  return keys.map((definitionKey) => ({ definitionKey }));
}

function fieldRuleDisplayName(definition: FieldRuleDefinition, t: (key: string) => string): string {
  const key = fieldRuleNameTranslationKey(definition.definitionKey);
  return key
    ? t(key)
    : (definition.displayName ?? definition.definitionKey ?? t('rules.unknownRule'));
}

function fieldRuleDescription(definition: FieldRuleDefinition, t: (key: string) => string): string {
  const key = fieldRuleDescriptionTranslationKey(definition.definitionKey);
  return key
    ? t(key)
    : (definition.description ?? definition.definitionKey ?? t('rules.unknownRuleDescription'));
}

function configuredFieldRuleCount(field: EditableField): number {
  let count = field.required ? 1 : 0;

  if (field.fieldType === 'Text') {
    if (field.minLength.trim() || field.maxLength.trim()) count += 1;
    if (field.pattern.trim()) count += 1;
  }

  if (
    (field.fieldType === 'Integer' || field.fieldType === 'Decimal') &&
    (field.minNumber.trim() || field.maxNumber.trim())
  ) {
    count += 1;
  }

  if (field.fieldType === 'Date' && (field.minDate || field.maxDate)) {
    count += 1;
  }

  if (field.fieldType === 'SingleSelect') {
    count += 1;
  }

  return count;
}

function omitRecordKey<T>(record: Record<string, T>, keyToOmit: string): Record<string, T> {
  const { [keyToOmit]: _omitted, ...next } = record;
  return next;
}

function validateFields(
  fields: readonly EditableField[],
  t: (key: string) => string,
  options: {
    requireFields: boolean;
    fieldIds?: readonly string[] | null;
    fieldTouches?: TouchedFieldInputs;
  },
): FieldValidationResult {
  const errors: string[] = [];
  const formErrors: string[] = [];
  const fieldErrors: Record<string, EditableFieldErrors> = {};
  const validatedFieldIds = options.fieldIds ? new Set(options.fieldIds) : null;
  const fieldsToValidate = validatedFieldIds
    ? fields.filter((field) => validatedFieldIds.has(field.clientId))
    : fields;
  const keyCounts = new Map<string, number>();

  if (options.requireFields && fieldsToValidate.length === 0) {
    addFormValidationError(errors, formErrors, t('objects.validationFieldsRequired'));
  }

  for (const field of fields) {
    const trimmedKey = field.fieldKey.trim();
    if (!trimmedKey) continue;

    keyCounts.set(trimmedKey, (keyCounts.get(trimmedKey) ?? 0) + 1);
  }

  for (const field of fieldsToValidate) {
    const trimmedKey = field.fieldKey.trim();
    const duplicatedKey = trimmedKey ? (keyCounts.get(trimmedKey) ?? 0) > 1 : false;
    const touchedInputs = options.fieldTouches?.[field.clientId];
    const shouldValidate = (key: EditableFieldInput) =>
      !options.fieldTouches || touchedInputs?.[key];

    if (shouldValidate('fieldKey') && !trimmedKey) {
      addFieldValidationError(
        fieldErrors,
        errors,
        field.clientId,
        'fieldKey',
        t('objects.validationFieldKeyRequired'),
      );
    } else if (shouldValidate('fieldKey') && (!keyPattern.test(trimmedKey) || duplicatedKey)) {
      addFieldValidationError(
        fieldErrors,
        errors,
        field.clientId,
        'fieldKey',
        t('objects.validationFieldKey'),
      );
    }

    if (shouldValidate('label') && !field.label.trim()) {
      addFieldValidationError(
        fieldErrors,
        errors,
        field.clientId,
        'label',
        t('objects.validationFieldLabel'),
      );
    }

    if (shouldValidate('fieldType') && !isSupportedFieldType(field.fieldType)) {
      addFieldValidationError(
        fieldErrors,
        errors,
        field.clientId,
        'fieldType',
        t('objects.validationFieldType'),
      );
    }

    if (field.fieldType === 'Text') {
      validateIntegerBound(
        field.minLength,
        'minLength',
        t('objects.validationLengthInteger'),
        shouldValidate,
        fieldErrors,
        errors,
        field.clientId,
      );
      validateIntegerBound(
        field.maxLength,
        'maxLength',
        t('objects.validationLengthInteger'),
        shouldValidate,
        fieldErrors,
        errors,
        field.clientId,
      );

      const minLength = optionalNumber(field.minLength);
      const maxLength = optionalNumber(field.maxLength);
      if (
        (shouldValidate('minLength') || shouldValidate('maxLength')) &&
        minLength !== null &&
        maxLength !== null &&
        Number.isInteger(minLength) &&
        Number.isInteger(maxLength) &&
        minLength >= 0 &&
        maxLength >= 0 &&
        minLength > maxLength
      ) {
        addFieldValidationError(
          fieldErrors,
          errors,
          field.clientId,
          shouldValidate('minLength') ? 'minLength' : 'maxLength',
          t('objects.validationLengthRange'),
        );
      }

      if (shouldValidate('pattern') && field.pattern.trim()) {
        try {
          new RegExp(field.pattern.trim());
        } catch {
          addFieldValidationError(
            fieldErrors,
            errors,
            field.clientId,
            'pattern',
            t('objects.validationPattern'),
          );
        }
      }
    }

    if (field.fieldType === 'Integer' || field.fieldType === 'Decimal') {
      validateNumberBound(
        field.minNumber,
        'minNumber',
        field.fieldType,
        t,
        shouldValidate,
        fieldErrors,
        errors,
        field.clientId,
      );
      validateNumberBound(
        field.maxNumber,
        'maxNumber',
        field.fieldType,
        t,
        shouldValidate,
        fieldErrors,
        errors,
        field.clientId,
      );

      const minNumber = optionalNumber(field.minNumber);
      const maxNumber = optionalNumber(field.maxNumber);
      if (
        (shouldValidate('minNumber') || shouldValidate('maxNumber')) &&
        minNumber !== null &&
        maxNumber !== null &&
        Number.isFinite(minNumber) &&
        Number.isFinite(maxNumber) &&
        minNumber > maxNumber
      ) {
        addFieldValidationError(
          fieldErrors,
          errors,
          field.clientId,
          shouldValidate('minNumber') ? 'minNumber' : 'maxNumber',
          t('objects.validationNumericRange'),
        );
      }
    }

    if (
      field.fieldType === 'Date' &&
      (shouldValidate('minDate') || shouldValidate('maxDate')) &&
      field.minDate &&
      field.maxDate &&
      field.minDate > field.maxDate
    ) {
      addFieldValidationError(
        fieldErrors,
        errors,
        field.clientId,
        shouldValidate('minDate') ? 'minDate' : 'maxDate',
        t('objects.validationDateRange'),
      );
    }

    if (
      field.fieldType === 'SingleSelect' &&
      (!options.fieldTouches || touchedInputs?.optionsText || touchedInputs?.fieldType)
    ) {
      const optionsList = optionValues(field.optionsText);
      if (optionsList.length === 0) {
        addFieldValidationError(
          fieldErrors,
          errors,
          field.clientId,
          'optionsText',
          t('objects.validationOptionsRequired'),
        );
      } else if (new Set(optionsList).size !== optionsList.length) {
        addFieldValidationError(
          fieldErrors,
          errors,
          field.clientId,
          'optionsText',
          t('objects.validationOptionsUnique'),
        );
      }
    }
  }

  return {
    messages: [...new Set(errors)],
    formMessages: [...new Set(formErrors)],
    fieldErrors,
  };
}

function emptyFieldValidationResult(): FieldValidationResult {
  return {
    messages: [],
    formMessages: [],
    fieldErrors: {},
  };
}

function isSupportedFieldType(fieldType: ObjectFieldType): boolean {
  return supportedFieldTypes.includes(fieldType);
}

function validateIntegerBound(
  value: string,
  key: 'minLength' | 'maxLength',
  message: string,
  shouldValidate: (key: EditableFieldInput) => boolean | undefined,
  fieldErrors: Record<string, EditableFieldErrors>,
  messages: string[],
  clientId: string,
) {
  if (!shouldValidate(key) || !value.trim()) return;

  const parsed = Number(value);
  if (!Number.isInteger(parsed) || parsed < 0) {
    addFieldValidationError(fieldErrors, messages, clientId, key, message);
  }
}

function validateNumberBound(
  value: string,
  key: 'minNumber' | 'maxNumber',
  fieldType: ObjectFieldType,
  t: (key: string) => string,
  shouldValidate: (key: EditableFieldInput) => boolean | undefined,
  fieldErrors: Record<string, EditableFieldErrors>,
  messages: string[],
  clientId: string,
) {
  if (!shouldValidate(key) || !value.trim()) return;

  const parsed = Number(value);
  if (!Number.isFinite(parsed)) {
    addFieldValidationError(fieldErrors, messages, clientId, key, t('objects.validationNumber'));
    return;
  }

  if (fieldType === 'Integer' && !Number.isInteger(parsed)) {
    addFieldValidationError(fieldErrors, messages, clientId, key, t('objects.validationInteger'));
  }
}

function addFormValidationError(messages: string[], formMessages: string[], message: string) {
  messages.push(message);
  formMessages.push(message);
}

function addFieldValidationError(
  fieldErrors: Record<string, EditableFieldErrors>,
  messages: string[],
  clientId: string,
  key: keyof EditableFieldErrors,
  message: string,
) {
  fieldErrors[clientId] = {
    ...fieldErrors[clientId],
    [key]: message,
  };
  messages.push(message);
}

function apiErrorFeedback(
  error: unknown,
  scope: FeedbackScope,
  excludedValidationFields: readonly string[] = [],
): FeedbackState | null {
  if (error instanceof ApiError) {
    const detail = apiErrorDetail(error.data, excludedValidationFields);

    if (!detail) {
      if (excludedValidationFields.length > 0 && apiErrorHasValidationErrors(error.data)) {
        return null;
      }

      return {
        scope,
        variant: 'destructive',
        titleKey: 'auth.genericError',
      };
    }

    return {
      scope,
      variant: 'destructive',
      titleKey:
        error.status === 400 || error.status === 409 || error.status === 422
          ? 'objects.validationTitle'
          : 'objects.requestError',
      detail,
    };
  }

  return {
    scope,
    variant: 'destructive',
    titleKey: 'auth.genericError',
  };
}

function applyIdentityApiErrors(
  error: unknown,
  setError: UseFormSetError<IdentityFormValues>,
): boolean {
  const messagesByField = apiErrorMessagesByField(error);
  const nameMessage = messagesByField.name?.[0];
  if (!nameMessage) return false;

  setError('name', {
    type: 'server',
    message: nameMessage,
  });
  return true;
}

function apiErrorDetail(
  data: unknown,
  excludedValidationFields: readonly string[] = [],
): string | undefined {
  const record = objectRecord(data);
  if (!record) return undefined;

  const validationText = validationErrorsText(record.errors, excludedValidationFields);
  if (validationText) return validationText;
  if (apiErrorHasValidationErrors(data)) return undefined;

  return firstText(record.detail, record.title, record.message);
}

function validationErrorsText(
  errors: unknown,
  excludedValidationFields: readonly string[] = [],
): string | undefined {
  const record = objectRecord(errors);
  if (!record) return undefined;
  const excludedFields = new Set(excludedValidationFields.map((field) => field.toLowerCase()));

  const messages = Object.entries(record)
    .filter(([key]) => !excludedFields.has(key.toLowerCase()))
    .map(([, value]) => value)
    .flatMap((value) => (Array.isArray(value) ? value : [value]))
    .filter(isNonEmptyString);

  return messages.length > 0 ? [...new Set(messages)].join(' ') : undefined;
}

function apiErrorHasValidationErrors(data: unknown): boolean {
  const record = objectRecord(data);
  return Boolean(record && validationErrorsText(record.errors));
}

function apiErrorMessagesByField(error: unknown): Record<string, string[]> {
  if (!(error instanceof ApiError)) return {};

  const record = objectRecord(error.data);
  const errors = record ? objectRecord(record.errors) : undefined;
  if (!errors) return {};

  return Object.fromEntries(
    Object.entries(errors)
      .map(([key, value]) => [key.toLowerCase(), toMessageArray(value)] as const)
      .filter((entry): entry is readonly [string, string[]] => entry[1].length > 0),
  );
}

function toMessageArray(value: unknown): string[] {
  return (Array.isArray(value) ? value : [value]).filter(isNonEmptyString);
}

function firstText(...values: unknown[]): string | undefined {
  return values.find(isNonEmptyString);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function objectRecord(value: unknown): Record<string, unknown> | undefined {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return undefined;
  }

  return value as Record<string, unknown>;
}
